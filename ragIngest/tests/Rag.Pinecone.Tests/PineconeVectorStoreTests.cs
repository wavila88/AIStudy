using System.Text.Json;
using Rag.Core;

namespace Rag.Pinecone.Tests;

public class PineconeVectorStoreTests
{
    private const string Host = "https://idx.svc.pinecone.io";
    private static readonly PineconeOptions Options = new() { ApiKey = "k", IndexName = "test-index", Namespace = "kb" };

    private static HttpResponseMessage Describe() => Json.Ok("{\"host\":\"idx.svc.pinecone.io\"}");

    private static PineconeVectorStore Store(StubHandler handler) => new(new HttpClient(handler), Options);

    [Fact]
    public async Task Lists_ids_following_pagination()
    {
        var handler = new StubHandler((req, _) =>
        {
            var url = req.RequestUri!.ToString();
            if (url == "https://api.pinecone.io/indexes/test-index") return Describe();
            return url.Contains("paginationToken=tok")
                ? Json.Ok("{\"vectors\":[{\"id\":\"KB-01-s3\"}]}")
                : Json.Ok("{\"vectors\":[{\"id\":\"KB-01-s1\"},{\"id\":\"KB-01-s2\"}],\"pagination\":{\"next\":\"tok\"}}");
        });

        var ids = await Store(handler).ListIdsAsync("KB-01-");

        Assert.Equal(new[] { "KB-01-s1", "KB-01-s2", "KB-01-s3" }, ids);
        var urls = handler.Requests.Select(r => r.Url).ToList();
        Assert.Equal("https://api.pinecone.io/indexes/test-index", urls[0]);
        Assert.Equal($"{Host}/vectors/list?prefix=KB-01-&limit=100&namespace=kb", urls[1]);
        Assert.Equal($"{Host}/vectors/list?prefix=KB-01-&limit=100&namespace=kb&paginationToken=tok", urls[2]);
    }

    [Fact]
    public async Task Returns_an_empty_list_when_the_response_has_no_vectors()
    {
        var handler = new StubHandler((req, _) =>
            req.RequestUri!.ToString().Contains("/indexes/") ? Describe() : Json.Ok("{}"));

        var ids = await Store(handler).ListIdsAsync("KB-01-");

        Assert.Empty(ids);
    }

    [Fact]
    public async Task Looks_up_the_index_host_only_once()
    {
        var handler = new StubHandler((req, _) =>
            req.RequestUri!.ToString().Contains("/indexes/") ? Describe() : Json.Ok("{\"vectors\":[]}"));
        var store = Store(handler);

        await store.ListIdsAsync("a-");
        await store.ListIdsAsync("b-");

        Assert.Single(handler.Requests, r => r.Url.StartsWith("https://api.pinecone.io/indexes/"));
    }

    [Fact]
    public async Task Deletes_in_batches_of_one_thousand_inside_the_namespace()
    {
        var handler = new StubHandler((req, _) =>
            req.RequestUri!.ToString().Contains("/indexes/") ? Describe() : Json.Ok("{}"));
        var ids = Enumerable.Range(0, 2500).Select(i => $"KB-01-s{i}").ToList();

        await Store(handler).DeleteAsync(ids);

        var deletes = handler.Requests.Where(r => r.Url == $"{Host}/vectors/delete").ToList();
        Assert.Equal(new[] { 1000, 1000, 500 },
            deletes.Select(d => JsonDocument.Parse(d.Body!).RootElement.GetProperty("ids").GetArrayLength()));
        Assert.All(deletes, d =>
            Assert.Equal("kb", JsonDocument.Parse(d.Body!).RootElement.GetProperty("namespace").GetString()));
    }

    [Fact]
    public async Task Does_not_call_pinecone_when_there_is_nothing_to_delete()
    {
        var handler = new StubHandler((_, _) => Json.Ok("{}"));

        await Store(handler).DeleteAsync(Array.Empty<string>());

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Upserts_in_batches_of_one_hundred_with_metadata()
    {
        var handler = new StubHandler((req, _) =>
            req.RequestUri!.ToString().Contains("/indexes/") ? Describe() : Json.Ok("{\"upsertedCount\":1}"));
        var records = Enumerable.Range(1, 250).Select(i => new VectorRecord(
            $"KB-01-s{i}",
            new[] { 0.5f, 1.5f },
            new Dictionary<string, object> { ["docId"] = "KB-01", ["chunkIndex"] = i, ["text"] = "hola" })).ToList();

        await Store(handler).UpsertAsync(records);

        var upserts = handler.Requests.Where(r => r.Url == $"{Host}/vectors/upsert").ToList();
        var counts = upserts.Select(u => JsonDocument.Parse(u.Body!).RootElement.GetProperty("vectors").GetArrayLength());
        Assert.Equal(new[] { 100, 100, 50 }, counts);

        var first = JsonDocument.Parse(upserts[0].Body!).RootElement;
        Assert.Equal("kb", first.GetProperty("namespace").GetString());
        var vector = first.GetProperty("vectors")[0];
        Assert.Equal("KB-01-s1", vector.GetProperty("id").GetString());
        Assert.Equal(2, vector.GetProperty("values").GetArrayLength());
        Assert.Equal("KB-01", vector.GetProperty("metadata").GetProperty("docId").GetString());
        Assert.Equal(1, vector.GetProperty("metadata").GetProperty("chunkIndex").GetInt32());
    }

    [Fact]
    public async Task Omits_the_namespace_when_it_is_empty()
    {
        var options = new PineconeOptions { ApiKey = "k", IndexName = "test-index", Namespace = "" };
        var handler = new StubHandler((req, _) =>
            req.RequestUri!.ToString().Contains("/indexes/") ? Describe() : Json.Ok("{}"));

        await new PineconeVectorStore(new HttpClient(handler), options).DeleteAsync(new[] { "a" });

        var delete = handler.Requests.Single(r => r.Url.EndsWith("/vectors/delete"));
        Assert.False(JsonDocument.Parse(delete.Body!).RootElement.TryGetProperty("namespace", out _));
    }
}
