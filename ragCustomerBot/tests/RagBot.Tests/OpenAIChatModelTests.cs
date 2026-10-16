using System.Net;
using System.Text.Json;
using RagBot.Core;
using RagBot.OpenAI;

namespace RagBot.Tests;

public class OpenAIChatModelTests
{
    private static readonly OpenAIOptions Options = new() { ApiKey = "sk-test", Model = "gpt-4o-mini" };

    private static readonly ChatMessage[] Messages =
    {
        new("system", "REGLAS"),
        new("user", "CONTEXTO ... PREGUNTA")
    };

    private const string Reply = """{"choices":[{"index":0,"message":{"role":"assistant","content":"Hola, cliente"},"finish_reason":"stop"}]}""";

    private static OpenAIChatModel Model(StubHandler handler, OpenAIOptions? options = null) =>
        new(new HttpClient(handler), options ?? Options);

    [Fact]
    public async Task Posts_the_messages_to_chat_completions_and_returns_the_reply()
    {
        var handler = new StubHandler((_, _) => Json.Ok(Reply));

        var text = await Model(handler).CompleteAsync(Messages);

        Assert.Equal("Hola, cliente", text);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://api.openai.com/v1/chat/completions", request.Url);

        var root = JsonDocument.Parse(request.Body!).RootElement;
        Assert.Equal("gpt-4o-mini", root.GetProperty("model").GetString());
        var sent = root.GetProperty("messages");
        Assert.Equal(2, sent.GetArrayLength());
        Assert.Equal("system", sent[0].GetProperty("role").GetString());
        Assert.Equal("REGLAS", sent[0].GetProperty("content").GetString());
        Assert.Equal("user", sent[1].GetProperty("role").GetString());
    }

    [Fact]
    public async Task Sends_the_configured_temperature()
    {
        var handler = new StubHandler((_, _) => Json.Ok(Reply));

        await Model(handler, new OpenAIOptions { ApiKey = "k", Temperature = 0.2 }).CompleteAsync(Messages);

        var root = JsonDocument.Parse(handler.Requests.Single().Body!).RootElement;
        Assert.Equal(0.2, root.GetProperty("temperature").GetDouble(), 3);
    }

    [Fact]
    public async Task Omits_the_temperature_when_it_is_null_for_models_that_do_not_accept_it()
    {
        var handler = new StubHandler((_, _) => Json.Ok(Reply));

        await Model(handler, new OpenAIOptions { ApiKey = "k", Temperature = null }).CompleteAsync(Messages);

        var root = JsonDocument.Parse(handler.Requests.Single().Body!).RootElement;
        Assert.False(root.TryGetProperty("temperature", out _));
    }

    [Fact]
    public async Task Honors_a_custom_base_url()
    {
        var handler = new StubHandler((_, _) => Json.Ok(Reply));

        await Model(handler, new OpenAIOptions { ApiKey = "k", BaseUrl = "https://proxy.local/v1/" }).CompleteAsync(Messages);

        Assert.Equal("https://proxy.local/v1/chat/completions", handler.Requests.Single().Url);
    }

    [Fact]
    public async Task Throws_an_openai_exception_with_status_and_body_on_failure()
    {
        var handler = new StubHandler((_, _) => Json.Error(HttpStatusCode.TooManyRequests, """{"error":{"message":"quota"}}"""));

        var ex = await Assert.ThrowsAsync<OpenAIException>(() => Model(handler).CompleteAsync(Messages));

        Assert.Equal(429, ex.StatusCode);
        Assert.Contains("quota", ex.Body);
    }

    [Fact]
    public async Task Throws_when_the_response_has_no_message_content()
    {
        var handler = new StubHandler((_, _) => Json.Ok("""{"choices":[]}"""));

        await Assert.ThrowsAsync<InvalidOperationException>(() => Model(handler).CompleteAsync(Messages));
    }

    [Fact]
    public async Task Throws_when_the_content_is_null()
    {
        var handler = new StubHandler((_, _) => Json.Ok("""{"choices":[{"message":{"role":"assistant","content":null,"refusal":"no puedo"}}]}"""));

        await Assert.ThrowsAsync<InvalidOperationException>(() => Model(handler).CompleteAsync(Messages));
    }

    [Fact]
    public void Client_authenticates_with_a_bearer_token()
    {
        using var client = OpenAIHttp.CreateClient(new OpenAIOptions { ApiKey = "sk-test" });

        Assert.Equal("Bearer", client.DefaultRequestHeaders.Authorization!.Scheme);
        Assert.Equal("sk-test", client.DefaultRequestHeaders.Authorization!.Parameter);
    }
}
