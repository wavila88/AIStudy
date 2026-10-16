using Rag.Core;

namespace Rag.Ingestion;

public sealed record IngestArguments(string? Source, bool DryRun, bool AssumeYes, bool Show)
{
    public const string Usage =
        "Uso: ingest [--source <archivo|carpeta>] [--dry-run] [--yes] [--show]\n" +
        "  --source   archivo .docx o carpeta con .docx\n" +
        "  --dry-run  muestra el resumen y no escribe nada\n" +
        "  --yes      no pregunta antes de escribir\n" +
        "  --show     imprime el texto completo de cada chunk";

    public static IngestArguments Parse(IReadOnlyList<string> args)
    {
        string? source = null;
        bool dryRun = false, yes = false, show = false;

        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--source":
                    if (i + 1 >= args.Count) throw new IngestInputException("--source requiere una ruta.");
                    source = args[++i];
                    break;
                case "--dry-run": dryRun = true; break;
                case "--yes": yes = true; break;
                case "--show": show = true; break;
                default: throw new IngestInputException($"Argumento desconocido: {args[i]}");
            }
        }
        return new IngestArguments(source, dryRun, yes, show);
    }
}
