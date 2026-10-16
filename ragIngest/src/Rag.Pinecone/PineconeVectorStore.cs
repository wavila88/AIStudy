using System.Net.Http.Json;
using Rag.Core;

namespace Rag.Pinecone;

public sealed class PineconeVectorStore(HttpClient http, PineconeOptions options) : IVectorStore
{
    public const int ListPageSize = 100;
    public const int DeleteBatchSize = 1000;
    public const int UpsertBatchSize = 100;

    internal sealed record IndexInfo(string Host);
    internal sealed record ListedVector(string Id);
    internal sealed record ListPagination(string? Next);
    internal sealed record ListResponse(List<ListedVector>? Vectors, ListPagination? Pagination);

    private string? _host;

    public async Task<IReadOnlyList<string>> ListIdsAsync(string prefix, CancellationToken ct = default)
    {
        var host = await HostAsync(ct);
        var ids = new List<string>();
        string? token = null;
        do
        {
            var url = $"{host}/vectors/list?prefix={Uri.EscapeDataString(prefix)}&limit={ListPageSize}"
                      + (options.Namespace.Length > 0 ? $"&namespace={Uri.EscapeDataString(options.Namespace)}" : "")
                      + (token is null ? "" : $"&paginationToken={Uri.EscapeDataString(token)}");

            using var response = await http.GetAsync(url, ct);
            await PineconeHttp.EnsureSuccessAsync(response, ct);

            var page = await response.Content.ReadFromJsonAsync<ListResponse>(ct);
            ids.AddRange((page?.Vectors ?? new List<ListedVector>()).Select(v => v.Id));
            token = page?.Pagination?.Next;
        } while (token is not null);
        return ids;
    }

    public async Task DeleteAsync(IReadOnlyList<string> ids, CancellationToken ct = default)
    {
        foreach (var batch in ids.Chunk(DeleteBatchSize))
            await PostAsync("/vectors/delete", WithNamespace(new Dictionary<string, object?> { ["ids"] = batch }), ct);
    }

    public async Task UpsertAsync(IReadOnlyList<VectorRecord> records, CancellationToken ct = default)
    {
        foreach (var batch in records.Chunk(UpsertBatchSize))
        {
            var vectors = batch.Select(r => new Dictionary<string, object?>
            {
                ["id"] = r.Id,
                ["values"] = r.Values,
                ["metadata"] = r.Metadata
            }).ToList();
            await PostAsync("/vectors/upsert", WithNamespace(new Dictionary<string, object?> { ["vectors"] = vectors }), ct);
        }
    }

    private Dictionary<string, object?> WithNamespace(Dictionary<string, object?> body)
    {
        if (options.Namespace.Length > 0) body["namespace"] = options.Namespace;
        return body;
    }

    private async Task PostAsync(string path, object body, CancellationToken ct)
    {
        var host = await HostAsync(ct);
        using var response = await http.PostAsJsonAsync($"{host}{path}", body, ct);
        await PineconeHttp.EnsureSuccessAsync(response, ct);
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
