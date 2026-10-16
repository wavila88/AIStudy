using Rag.Core;

namespace Rag.Ingestion;

public static class SourceResolver
{
    public static string Resolve(string? argument, string? appSettings, string? environment)
    {
        foreach (var candidate in new[] { argument, appSettings, environment })
            if (!string.IsNullOrWhiteSpace(candidate)) return candidate.Trim();

        throw new IngestInputException(
            "Indica la ruta de origen con --source, con Ingestion:Source en appsettings.json " +
            "o con la variable de entorno INGEST_SOURCE.");
    }
}
