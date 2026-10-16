using System.Text.RegularExpressions;
using Rag.Core;

namespace Rag.Ingestion;

public sealed record DocumentPlan(
    string SourcePath,
    string? DocId,
    string? Title,
    IReadOnlyList<Chunk> Chunks,
    IReadOnlyList<string> ExistingIds,
    string? Error)
{
    public bool IsValid => Error is null;
}

public sealed record DocumentResult(string DocId, int Deleted, int Inserted);

public sealed class IngestionService(
    IDocumentLoader loader,
    IChunker chunker,
    IEmbedder embedder,
    IVectorStore store)
{
    public async Task<IReadOnlyList<DocumentPlan>> PlanAsync(IReadOnlyList<string> files, CancellationToken ct = default)
    {
        var plans = new List<DocumentPlan>();
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var file in files)
        {
            ParsedDocument parsed;
            try
            {
                parsed = loader.Load(file);
            }
            catch (InvalidDocumentException ex)
            {
                plans.Add(Failed(file, ex.Reason));
                continue;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                // Archivo en uso por Word, dañado, renombrado, etc.: se omite y se sigue con los demás.
                plans.Add(Failed(file, $"No se pudo leer el archivo: {ex.Message}"));
                continue;
            }

            if (seen.TryGetValue(parsed.DocId, out var firstFile))
            {
                plans.Add(Failed(file, $"DocId duplicado '{parsed.DocId}', ya usado por {Path.GetFileName(firstFile)}.", parsed));
                continue;
            }
            seen[parsed.DocId] = file;

            var chunks = chunker.CreateChunks(parsed);
            if (chunks.Count == 0)
            {
                plans.Add(Failed(file, "El documento no produjo chunks (no tiene secciones con contenido).", parsed));
                continue;
            }

            var listed = await store.ListIdsAsync(parsed.DocId + "-", ct);
            var chunkId = new Regex($"^{Regex.Escape(parsed.DocId)}-[sq][0-9]+$", RegexOptions.CultureInvariant);
            var existing = listed.Where(id => chunkId.IsMatch(id)).ToList();

            plans.Add(new DocumentPlan(file, parsed.DocId, parsed.Title, chunks, existing, null));
        }
        return plans;
    }

    public async Task<IReadOnlyList<DocumentResult>> ExecuteAsync(
        IReadOnlyList<DocumentPlan> plans,
        Action<DocumentResult>? onDocumentDone = null,
        CancellationToken ct = default)
    {
        var results = new List<DocumentResult>();
        foreach (var plan in plans.Where(p => p.IsValid))
        {
            try
            {
                var vectors = await embedder.EmbedAsync(plan.Chunks.Select(c => c.Text).ToList(), ct);
                if (vectors.Count != plan.Chunks.Count)
                    throw new InvalidOperationException(
                        $"El embedder devolvió {vectors.Count} vectores para {plan.Chunks.Count} chunks.");

                // Primero se escribe lo nuevo (los ids repetidos se sobrescriben) y solo después se borra lo que
                // sobró: si el upsert falla, el documento conserva sus registros anteriores.
                var records = plan.Chunks
                    .Select((chunk, i) => new VectorRecord(chunk.Id, vectors[i], Metadata(chunk)))
                    .ToList();
                await store.UpsertAsync(records, ct);

                var newIds = records.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
                var stale = plan.ExistingIds.Where(id => !newIds.Contains(id)).ToList();
                if (stale.Count > 0) await store.DeleteAsync(stale, ct);

                var result = new DocumentResult(plan.DocId!, plan.ExistingIds.Count, records.Count);
                results.Add(result);
                onDocumentDone?.Invoke(result);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                throw new IngestionException(plan.DocId!, ex);
            }
        }
        return results;
    }

    private static DocumentPlan Failed(string file, string error, ParsedDocument? parsed = null) =>
        new(file, parsed?.DocId, parsed?.Title, Array.Empty<Chunk>(), Array.Empty<string>(), error);

    private static Dictionary<string, object> Metadata(Chunk chunk) => new()
    {
        ["docId"] = chunk.DocId,
        ["title"] = chunk.DocTitle,
        ["section"] = chunk.Section,
        ["type"] = chunk.Type == ChunkType.Faq ? "faq" : "guide",
        ["chunkIndex"] = chunk.ChunkIndex,
        ["text"] = chunk.Text
    };
}
