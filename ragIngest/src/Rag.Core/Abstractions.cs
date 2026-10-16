namespace Rag.Core;

public interface IDocumentLoader
{
    ParsedDocument Load(string path);
}

public interface IChunker
{
    IReadOnlyList<Chunk> CreateChunks(ParsedDocument document);
}

public interface IEmbedder
{
    Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default);
}

public sealed record VectorRecord(string Id, float[] Values, IReadOnlyDictionary<string, object> Metadata);

public interface IVectorStore
{
    Task<IReadOnlyList<string>> ListIdsAsync(string prefix, CancellationToken ct = default);
    Task DeleteAsync(IReadOnlyList<string> ids, CancellationToken ct = default);
    Task UpsertAsync(IReadOnlyList<VectorRecord> records, CancellationToken ct = default);
}
