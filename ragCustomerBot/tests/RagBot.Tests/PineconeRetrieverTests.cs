using System.Net;
using System.Text.Json;
using RagBot.Pinecone;

namespace RagBot.Tests;

public class PineconeRetrieverTests
{
    private const string Host = "https://idx.svc.pinecone.io";
    private static readonly PineconeOptions Options = new() { ApiKey = "k", IndexName = "test-index", Namespace = "kb" };

    private const string QueryResponse = """
        {"matches":[
          {"id":"KB-01-s2","score":0.8123,"metadata":{"docId":"KB-01","section":"Lista de verificación","type":"guide","chunkIndex":1,"text":"Guía > Lista\n1. Límites"}},
          {"id":"KB-03-q1","score":0.5,"metadata":{"docId":"KB-03","section":"Preguntas frecuentes","type":"faq","text":"P: ...\nR: ..."}}
        ],"namespace":"kb"}
        """;

    // Responde según la URL: describe del índice, embed y query.
    private static StubHandler Handler(string queryJson = QueryResponse) => new((req, _) =>
    {
        var url = req.RequestUri!.ToString();
        if (url == "https://api.pinecone.io/indexes/test-index") return Json.Ok("{\"host\":\"idx.svc.pinecone.io\"}");
        if (url == "https://api.pinecone.io/embed") return Json.Ok("{\"data\":[{\"values\":[0.1,0.2,0.3]}]}");
        if (url == $"{Host}/query") return Json.Ok(queryJson);
        return Json.Error(HttpStatusCode.NotFound, "ruta inesperada: " + url);
    });

    private static PineconeRetriever Retriever(StubHandler handler) => new(new HttpClient(handler), Options);

    [Fact]
    public async Task Embeds_the_question_as_a_query_with_the_ingestion_model_and_dimension()
    {
        var handler = Handler();

        await Retriever(handler).SearchAsync("me rechazan la tarjeta", 5);

        var embed = handler.Requests.Single(r => r.Url == "https://api.pinecone.io/embed");
        var root = JsonDocument.Parse(embed.Body!).RootElement;
        Assert.Equal("llama-text-embed-v2", root.GetProperty("model").GetString());
        var parameters = root.GetProperty("parameters");
        Assert.Equal("query", parameters.GetProperty("input_type").GetString());
        Assert.Equal("END", parameters.GetProperty("truncate").GetString());
        Assert.Equal(1024, parameters.GetProperty("dimension").GetInt32());
        Assert.Equal("me rechazan la tarjeta", root.GetProperty("inputs")[0].GetProperty("text").GetString());
    }

    [Fact]
    public async Task Queries_the_index_host_with_vector_topk_namespace_and_metadata()
    {
        var handler = Handler();

        await Retriever(handler).SearchAsync("hola", 4);

        var query = handler.Requests.Single(r => r.Url == $"{Host}/query");
        var root = JsonDocument.Parse(query.Body!).RootElement;
        Assert.Equal(3, root.GetProperty("vector").GetArrayLength());
        Assert.Equal(4, root.GetProperty("topK").GetInt32());
        Assert.True(root.GetProperty("includeMetadata").GetBoolean());
        Assert.Equal("kb", root.GetProperty("namespace").GetString());
    }

    [Fact]
    public async Task Maps_matches_to_chunks_in_the_order_pinecone_returns_them()
    {
        var chunks = await Retriever(Handler()).SearchAsync("hola", 5);

        Assert.Equal(2, chunks.Count);
        Assert.Equal("KB-01-s2", chunks[0].Id);
        Assert.Equal(0.8123, chunks[0].Score, 4);
        Assert.Equal("KB-01", chunks[0].DocId);
        Assert.Equal("Lista de verificación", chunks[0].Section);
        Assert.Equal("guide", chunks[0].Type);
        Assert.Equal("Guía > Lista\n1. Límites", chunks[0].Text);
        Assert.Equal("faq", chunks[1].Type);
    }

    [Fact]
    public async Task Tolerates_matches_without_metadata()
    {
        var handler = Handler("{\"matches\":[{\"id\":\"X-1\",\"score\":0.4}]}");

        var chunk = Assert.Single(await Retriever(handler).SearchAsync("hola", 5));

        Assert.Equal("X-1", chunk.Id);
        Assert.Equal("", chunk.DocId);
        Assert.Equal("", chunk.Text);
    }

    [Fact]
    public async Task Returns_nothing_when_pinecone_finds_no_matches()
    {
        var handler = Handler("{\"matches\":[],\"namespace\":\"kb\"}");

        Assert.Empty(await Retriever(handler).SearchAsync("hola", 5));
    }

    [Fact]
    public async Task Looks_up_the_index_host_only_once()
    {
        var handler = Handler();
        var retriever = Retriever(handler);

        await retriever.SearchAsync("uno", 5);
        await retriever.SearchAsync("dos", 5);

        Assert.Single(handler.Requests, r => r.Url == "https://api.pinecone.io/indexes/test-index");
    }

    [Fact]
    public async Task Omits_the_namespace_when_it_is_empty()
    {
        var options = new PineconeOptions { ApiKey = "k", IndexName = "test-index", Namespace = "" };
        var handler = Handler();

        await new PineconeRetriever(new HttpClient(handler), options).SearchAsync("hola", 5);

        var query = handler.Requests.Single(r => r.Url == $"{Host}/query");
        Assert.False(JsonDocument.Parse(query.Body!).RootElement.TryGetProperty("namespace", out _));
    }

    [Fact]
    public async Task Throws_a_pinecone_exception_with_the_status_and_body_on_failure()
    {
        var handler = new StubHandler((_, _) => Json.Error(HttpStatusCode.Unauthorized, "{\"error\":\"bad key\"}"));

        var ex = await Assert.ThrowsAsync<PineconeException>(() => Retriever(handler).SearchAsync("hola", 5));

        Assert.Equal(401, ex.StatusCode);
        Assert.Contains("bad key", ex.Body);
    }

    [Fact]
    public async Task Throws_when_the_embedding_response_has_no_vector()
    {
        var handler = new StubHandler((req, _) =>
            req.RequestUri!.ToString().EndsWith("/embed") ? Json.Ok("{\"data\":[]}") : Json.Ok("{}"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => Retriever(handler).SearchAsync("hola", 5));
    }

    [Fact]
    public void Client_sends_the_api_key_and_version_headers()
    {
        using var client = PineconeHttp.CreateClient(new PineconeOptions { ApiKey = "secret" });

        Assert.Equal("secret", client.DefaultRequestHeaders.GetValues("Api-Key").Single());
        Assert.Equal("2025-04", client.DefaultRequestHeaders.GetValues("X-Pinecone-API-Version").Single());
    }
}
