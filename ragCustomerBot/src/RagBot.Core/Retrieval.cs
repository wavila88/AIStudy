namespace RagBot.Core;

/// <summary>Un fragmento de guía devuelto por la búsqueda, con su puntuación de similitud.</summary>
public sealed record RetrievedChunk(
    string Id,
    double Score,
    string DocId,
    string Section,
    string Type,
    string Text);

public interface IRetriever
{
    /// <summary>Devuelve los <paramref name="topK"/> chunks más parecidos a la consulta, del más al menos parecido.</summary>
    Task<IReadOnlyList<RetrievedChunk>> SearchAsync(string query, int topK, CancellationToken ct = default);
}
