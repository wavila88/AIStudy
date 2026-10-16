using System.Net.Http.Json;
using System.Text.Json;
using RagBot.Core;

namespace RagBot.Pinecone;

/// <summary>
/// Búsqueda semántica: convierte la pregunta en vector con el MISMO modelo de la ingesta
/// (pero con input_type=query) y pide a Pinecone los chunks más cercanos.
/// </summary>
public sealed class PineconeRetriever(HttpClient http, PineconeOptions options) : IRetriever
{
    internal sealed record IndexInfo(string Host);

    private string? _host;

    public async Task<IReadOnlyList<RetrievedChunk>> SearchAsync(string query, int topK, CancellationToken ct = default)
    {
        var vector = await EmbedQueryAsync(query, ct);
        var host = await HostAsync(ct);

        var body = new Dictionary<string, object?>
        {
            ["vector"] = vector,
            ["topK"] = topK,
            ["includeMetadata"] = true
        };
        if (options.Namespace.Length > 0) body["namespace"] = options.Namespace;

        using var response = await http.PostAsJsonAsync($"{host}/query", body, ct);
        await PineconeHttp.EnsureSuccessAsync(response, ct);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var chunks = new List<RetrievedChunk>();
        if (!doc.RootElement.TryGetProperty("matches", out var matches)) return chunks;

        foreach (var match in matches.EnumerateArray())
        {
            var metadata = match.TryGetProperty("metadata", out var m) ? m : default;
            chunks.Add(new RetrievedChunk(
                Id: match.GetProperty("id").GetString() ?? "",
                Score: match.TryGetProperty("score", out var score) ? score.GetDouble() : 0,
                DocId: Text(metadata, "docId"),
                Section: Text(metadata, "section"),
                Type: Text(metadata, "type"),
                Text: Text(metadata, "text")));
        }
        return chunks;
    }

    private static string Text(JsonElement metadata, string name) =>
        metadata.ValueKind == JsonValueKind.Object
        && metadata.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private async Task<float[]> EmbedQueryAsync(string query, CancellationToken ct)
    {
        var request = new
        {
            model = options.EmbeddingModel,
            parameters = new { input_type = "query", truncate = "END", dimension = options.Dimension },
            inputs = new[] { new { text = query } }
        };

        using var response = await http.PostAsJsonAsync($"{options.ControlPlaneUrl}/embed", request, ct);
        await PineconeHttp.EnsureSuccessAsync(response, ct);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.GetArrayLength() == 0)
            throw new InvalidOperationException("Pinecone no devolvió el vector de la pregunta.");

        return data[0].GetProperty("values").EnumerateArray().Select(v => v.GetSingle()).ToArray();
    }

    private async Task<string> HostAsync(CancellationToken ct)
    {
        if (_host is not null) return _host;
        var url = $"{options.ControlPlaneUrl}/indexes/{Uri.EscapeDataString(options.IndexName)}";
        using var response = await http.GetAsync(url, ct);
        await PineconeHttp.EnsureSuccessAsync(response, ct);

        var info = await response.Content.ReadFromJsonAsync<IndexInfo>(ct)
            ?? throw new InvalidOperationException("Pinecone no devolvió información del índice.");
        _host = info.Host.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? info.Host : "https://" + info.Host;
        return _host;
    }
}
