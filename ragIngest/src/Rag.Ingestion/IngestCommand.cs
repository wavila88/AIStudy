using Rag.Core;

namespace Rag.Ingestion;

public sealed record IngestRunOptions(
    string Source, bool DryRun, bool AssumeYes, bool Show, string IndexName, string Namespace);

public sealed class IngestCommand(IngestionService service, TextWriter output, Func<string?> readLine)
{
    public async Task<int> RunAsync(IngestRunOptions options, CancellationToken ct = default)
    {
        IReadOnlyList<string> files;
        try
        {
            files = DocumentDiscovery.Find(options.Source);
        }
        catch (IngestInputException ex)
        {
            output.WriteLine($"Error: {ex.Message}");
            return 1;
        }

        var plans = await service.PlanAsync(files, ct);
        WriteSummary(plans, options);

        var valid = plans.Where(p => p.IsValid).ToList();
        var skippedExitCode = valid.Count == plans.Count ? 0 : 2;

        if (valid.Count == 0)
        {
            output.WriteLine("No hay documentos válidos para ingestar.");
            return 1;
        }

        if (options.DryRun)
        {
            output.WriteLine("Modo --dry-run: no se escribió nada.");
            return skippedExitCode;
        }

        if (!options.AssumeYes)
        {
            output.Write("¿Continuar? [s/N] ");
            var answer = readLine();
            if (!string.Equals(answer?.Trim(), "s", StringComparison.OrdinalIgnoreCase))
            {
                output.WriteLine("Cancelado. No se escribió nada.");
                return 0;
            }
        }

        try
        {
            await service.ExecuteAsync(
                plans,
                r => output.WriteLine($"{r.DocId}: {r.Deleted} borrados, {r.Inserted} insertados"),
                ct);
        }
        catch (IngestionException ex)
        {
            output.WriteLine($"Error: {ex.Message}");
            return 3;
        }
        return skippedExitCode;
    }

    private void WriteSummary(IReadOnlyList<DocumentPlan> plans, IngestRunOptions options)
    {
        var ns = options.Namespace.Length == 0 ? "(default)" : options.Namespace;
        output.WriteLine($"Origen: {options.Source}");
        output.WriteLine($"Índice: {options.IndexName} (namespace: {ns})");

        foreach (var plan in plans)
        {
            output.WriteLine();
            if (!plan.IsValid)
            {
                output.WriteLine($"OMITIDO {Path.GetFileName(plan.SourcePath)}: {plan.Error}");
                continue;
            }

            output.WriteLine($"Documento: {plan.DocId} · {plan.Title}");
            output.WriteLine($"Chunks a crear: {plan.Chunks.Count}");
            output.WriteLine(" #  id          tipo   chars  sección");
            for (var i = 0; i < plan.Chunks.Count; i++)
            {
                var chunk = plan.Chunks[i];
                var body = chunk.Text.Length - chunk.Text.IndexOf('\n') - 1;
                var warning = chunk.Type == ChunkType.Guide && body < SectionChunker.MinChars ? " (corto)"
                    : body > SectionChunker.MaxChars ? " (largo)" : "";
                var type = chunk.Type == ChunkType.Faq ? "faq" : "guide";
                output.WriteLine($"{i + 1,2}  {chunk.Id,-10}  {type,-5}  {body,5}  {chunk.Section}{warning}");
            }
            output.WriteLine($"Acción: BORRAR {plan.ExistingIds.Count} registros existentes · INSERTAR {plan.Chunks.Count}");

            if (options.Show)
                foreach (var chunk in plan.Chunks)
                {
                    output.WriteLine($"--- {chunk.Id} ---");
                    output.WriteLine(chunk.Text);
                }
        }
    }
}
