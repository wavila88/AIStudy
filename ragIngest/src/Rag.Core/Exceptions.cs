namespace Rag.Core;

public sealed class InvalidDocumentException(string sourcePath, string reason)
    : Exception($"{System.IO.Path.GetFileName(sourcePath)}: {reason}")
{
    public string SourcePath { get; } = sourcePath;
    public string Reason { get; } = reason;
}

public sealed class IngestInputException(string message) : Exception(message);

public sealed class IngestionException(string docId, Exception inner)
    : Exception($"Falló la ingesta de {docId}: {inner.Message}", inner)
{
    public string DocId { get; } = docId;
}
