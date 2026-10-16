namespace Rag.Pinecone;

public sealed class PineconeException(int statusCode, string body)
    : Exception($"Pinecone respondió {statusCode}: {body}")
{
    public int StatusCode { get; } = statusCode;
    public string Body { get; } = body;
}

public static class PineconeHttp
{
    public static HttpClient CreateClient(PineconeOptions options)
    {
        var http = new HttpClient();
        http.DefaultRequestHeaders.Add("Api-Key", options.ApiKey);
        http.DefaultRequestHeaders.Add("X-Pinecone-API-Version", options.ApiVersion);
        http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return http;
    }

    internal static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync(ct);
        throw new PineconeException((int)response.StatusCode, body);
    }
}
