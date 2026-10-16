using Rag.Core;

namespace Rag.Ingestion;

public static class DocumentDiscovery
{
    public static IReadOnlyList<string> Find(string source)
    {
        if (File.Exists(source))
        {
            if (!source.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
                throw new IngestInputException($"'{source}' no es un archivo .docx.");
            return new[] { source };
        }

        if (Directory.Exists(source))
        {
            var files = Directory.EnumerateFiles(source, "*.docx", SearchOption.TopDirectoryOnly)
                .Where(f => !Path.GetFileName(f).StartsWith("~$", StringComparison.Ordinal))
                .OrderBy(f => f, StringComparer.Ordinal)
                .ToList();
            if (files.Count == 0)
                throw new IngestInputException($"No hay archivos .docx en '{source}'.");
            return files;
        }

        throw new IngestInputException($"No existe la ruta '{source}'.");
    }
}
