using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using RagBot.Core;

namespace RagBot.OpenAI;

public sealed class OpenAIOptions
{
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "gpt-4o-mini";
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";

    /// <summary>Baja = respuestas más estables. Ponla en null (omitirla) para modelos que no aceptan este parámetro.</summary>
    public double? Temperature { get; set; } = 0.2;
}

public sealed class OpenAIException(int statusCode, string body)
    : Exception($"OpenAI respondió {statusCode}: {body}")
{
    public int StatusCode { get; } = statusCode;
    public string Body { get; } = body;
}

public static class OpenAIHttp
{
    public static HttpClient CreateClient(OpenAIOptions options)
    {
        var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return http;
    }
}

public sealed class OpenAIChatModel(HttpClient http, OpenAIOptions options) : IChatModel
{
    public async Task<string> CompleteAsync(IReadOnlyList<ChatMessage> messages, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["model"] = options.Model,
            ["messages"] = messages.Select(m => new { role = m.Role, content = m.Content }).ToList()
        };
        if (options.Temperature is double temperature) body["temperature"] = temperature;

        var url = $"{options.BaseUrl.TrimEnd('/')}/chat/completions";
        using var response = await http.PostAsJsonAsync(url, body, ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode) throw new OpenAIException((int)response.StatusCode, json);

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("choices", out var choices)
            && choices.GetArrayLength() > 0
            && choices[0].TryGetProperty("message", out var message)
            && message.TryGetProperty("content", out var content)
            && content.ValueKind == JsonValueKind.String)
        {
            return content.GetString() ?? "";
        }
        throw new InvalidOperationException("OpenAI no devolvió texto en la respuesta.");
    }
}
