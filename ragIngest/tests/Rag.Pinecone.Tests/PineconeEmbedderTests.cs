using System.Net;
using System.Text.Json;

namespace Rag.Pinecone.Tests;

public class PineconeEmbedderTests
{
    private static readonly PineconeOptions Options = new() { ApiKey = "k", IndexName = "idx" };

    // Responde un vector [longitud del texto] por cada input, para poder comprobar el orden.
    private static HttpResponseMessage EchoLengths(HttpRequestMessage req, string? body)
    {
        using var doc = JsonDocument.Parse(body!);
        var items = doc.RootElement.GetProperty("inputs").EnumerateArray()
            .Select(i => $"{{\"values\":[{i.GetProperty("text").GetString()!.Length}.0]}}");
        return Json.Ok($"{{\"data\":[{string.Join(",", items)}]}}");
    }

    [Fact]
    public async Task Embeds_in_batches_of_fifty_and_keeps_the_order()
    {
        var handler = new StubHandler(EchoLengths);
        var embedder = new PineconeEmbedder(new HttpClient(handler), Options);
        var texts = Enumerable.Range(1, 120).Select(i => new string('a', i)).ToList();

        var vectors = await embedder.EmbedAsync(texts);

        Assert.Equal(120, vectors.Count);
        for (var i = 0; i < texts.Count; i++) Assert.Equal(texts[i].Length, vectors[i][0]);

        var sizes = handler.Requests
            .Select(r => JsonDocument.Parse(r.Body!).RootElement.GetProperty("inputs").GetArrayLength())
            .ToList();
        Assert.Equal(new[] { 50, 50, 20 }, sizes);
    }

    [Fact]
    public async Task Sends_model_and_passage_parameters_to_the_embed_endpoint()
    {
        var handler = new StubHandler(EchoLengths);
        var embedder = new PineconeEmbedder(new HttpClient(handler), Options);

        await embedder.EmbedAsync(new[] { "hola" });

        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://api.pinecone.io/embed", request.Url);
        var root = JsonDocument.Parse(request.Body!).RootElement;
        Assert.Equal("llama-text-embed-v2", root.GetProperty("model").GetString());
        var parameters = root.GetProperty("parameters");
        Assert.Equal("passage", parameters.GetProperty("input_type").GetString());
        Assert.Equal("END", parameters.GetProperty("truncate").GetString());
        Assert.Equal(1024, parameters.GetProperty("dimension").GetInt32());
    }

    [Fact]
    public async Task Returns_nothing_and_makes_no_request_for_an_empty_list()
    {
        var handler = new StubHandler(EchoLengths);
        var embedder = new PineconeEmbedder(new HttpClient(handler), Options);

        var vectors = await embedder.EmbedAsync(Array.Empty<string>());

        Assert.Empty(vectors);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Throws_a_pinecone_exception_with_status_and_body_on_failure()
    {
        var handler = new StubHandler((_, _) => Json.Error(HttpStatusCode.Unauthorized, "{\"error\":\"bad key\"}"));
        var embedder = new PineconeEmbedder(new HttpClient(handler), Options);

        var ex = await Assert.ThrowsAsync<PineconeException>(() => embedder.EmbedAsync(new[] { "x" }));

        Assert.Equal(401, ex.StatusCode);
        Assert.Contains("bad key", ex.Body);
    }

    [Fact]
    public async Task Throws_when_pinecone_returns_fewer_vectors_than_inputs()
    {
        var handler = new StubHandler((_, _) => Json.Ok("{\"data\":[{\"values\":[1.0]}]}"));
        var embedder = new PineconeEmbedder(new HttpClient(handler), Options);

        await Assert.ThrowsAsync<InvalidOperationException>(() => embedder.EmbedAsync(new[] { "a", "b" }));
    }

    [Fact]
    public void Client_sends_the_api_key_and_version_headers()
    {
        using var client = PineconeHttp.CreateClient(new PineconeOptions { ApiKey = "secret" });

        Assert.Equal("secret", client.DefaultRequestHeaders.GetValues("Api-Key").Single());
        Assert.Equal("2025-04", client.DefaultRequestHeaders.GetValues("X-Pinecone-API-Version").Single());
    }
}
