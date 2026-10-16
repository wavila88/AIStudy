using System.Net.Http.Json;
using Rag.Core;

namespace Rag.Pinecone;

public sealed class PineconeEmbedder(HttpClient http, PineconeOptions options) : IEmbedder
{
    public const int BatchSize = 50;

    internal sealed record EmbedResponse(List<EmbedItem>? Data);
    internal sealed record EmbedItem(float[] Values);

    public async Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        var vectors = new List<float[]>(texts.Count);
        foreach (var batch in texts.Chunk(BatchSize))
        {
            var request = new
            {
                model = options.EmbeddingModel,
                parameters = new { input_type = "passage", truncate = "END", dimension = options.Dimension },
                inputs = batch.Select(t => new { text = t }).ToArray()
            };

            using var response = await http.PostAsJsonAsync($"{options.ControlPlaneUrl}/embed", request, ct);
            await PineconeHttp.EnsureSuccessAsync(response, ct);

            var parsed = await response.Content.ReadFromJsonAsync<EmbedResponse>(ct);
            var items = parsed?.Data ?? new List<EmbedItem>();
            if (items.Count != batch.Length)
                throw new InvalidOperationException(
                    $"Pinecone devolvió {items.Count} vectores para {batch.Length} textos.");

            vectors.AddRange(items.Select(i => i.Values));
        }
        return vectors;
    }
}
