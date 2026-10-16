using Rag.Core;
using Rag.Ingestion;

namespace Rag.Ingestion.Tests;

public class IngestCommandTests : IDisposable
{
    private sealed class FakeEmbedder : IEmbedder
    {
        public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<float[]>>(texts.Select(t => new[] { (float)t.Length }).ToList());
    }

    private sealed class FakeStore : IVectorStore
    {
        public Dictionary<string, VectorRecord> Records { get; } = new();
        public int Writes { get; private set; }

        public Task<IReadOnlyList<string>> ListIdsAsync(string prefix, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<string>>(Records.Keys.Where(k => k.StartsWith(prefix)).ToList());

        public Task DeleteAsync(IReadOnlyList<string> ids, CancellationToken ct = default)
        {
            Writes++;
            foreach (var id in ids) Records.Remove(id);
            return Task.CompletedTask;
        }

        public Task UpsertAsync(IReadOnlyList<VectorRecord> records, CancellationToken ct = default)
        {
            Writes++;
            foreach (var r in records) Records[r.Id] = r;
            return Task.CompletedTask;
        }
    }

    private readonly TempDir _temp = new();
    private readonly FakeStore _store = new();
    private readonly StringWriter _output = new();
    private readonly Queue<string?> _answers = new();

    public IngestCommandTests() =>
        File.Copy(TestFiles.Fixture("01_rechazos_de_pago_con_tarjeta.docx"), _temp.Combine("01_rechazos.docx"));

    public void Dispose() => _temp.Dispose();

    private IngestCommand Command() => new(
        new IngestionService(new DocxLoader(), new SectionChunker(), new FakeEmbedder(), _store),
        _output,
        () => _answers.Count > 0 ? _answers.Dequeue() : throw new InvalidOperationException("No se debía preguntar."));

    private IngestRunOptions Options(bool dryRun = false, bool yes = false, bool show = false, string? source = null) =>
        new(source ?? _temp.Dir, dryRun, yes, show, "customer-service-kb", "kb");

    [Fact]
    public async Task Dry_run_prints_the_summary_and_writes_nothing()
    {
        var code = await Command().RunAsync(Options(dryRun: true));

        Assert.Equal(0, code);
        Assert.Equal(0, _store.Writes);
        var text = _output.ToString();
        Assert.Contains("customer-service-kb (namespace: kb)", text);
        Assert.Contains("Documento: KB-01", text);
        Assert.Contains("Chunks a crear: 12", text);
        Assert.Contains("BORRAR 0 registros existentes · INSERTAR 12", text);
        Assert.Contains("KB-01-s2", text);
        Assert.Contains("KB-01-q6", text);
    }

    [Fact]
    public async Task Answering_no_cancels_without_writing()
    {
        _answers.Enqueue("n");

        var code = await Command().RunAsync(Options());

        Assert.Equal(0, code);
        Assert.Equal(0, _store.Writes);
        Assert.Contains("¿Continuar? [s/N]", _output.ToString());
        Assert.Contains("Cancelado. No se escribió nada.", _output.ToString());
    }

    [Fact]
    public async Task An_empty_answer_counts_as_no()
    {
        _answers.Enqueue("");

        await Command().RunAsync(Options());

        Assert.Equal(0, _store.Writes);
    }

    [Fact]
    public async Task Answering_yes_writes_the_chunks()
    {
        _answers.Enqueue("s");

        var code = await Command().RunAsync(Options());

        Assert.Equal(0, code);
        Assert.Equal(12, _store.Records.Count);
        Assert.Contains("KB-01: 0 borrados, 12 insertados", _output.ToString());
    }

    [Fact]
    public async Task Yes_flag_writes_without_asking()
    {
        var code = await Command().RunAsync(Options(yes: true));

        Assert.Equal(0, code);
        Assert.Equal(12, _store.Records.Count);
        Assert.DoesNotContain("¿Continuar?", _output.ToString());
    }

    [Fact]
    public async Task Running_again_reports_the_records_it_replaced()
    {
        await Command().RunAsync(Options(yes: true));
        _output.GetStringBuilder().Clear();

        await Command().RunAsync(Options(yes: true));

        Assert.Equal(12, _store.Records.Count);
        Assert.Contains("KB-01: 12 borrados, 12 insertados", _output.ToString());
    }

    [Fact]
    public async Task Show_prints_the_full_text_of_each_chunk()
    {
        await Command().RunAsync(Options(dryRun: true, show: true));

        var text = _output.ToString();
        Assert.Contains("--- KB-01-s2 ---", text);
        Assert.Contains("Guía: Rechazos de pago con tarjeta > Lista de verificación (en este orden)", text);
    }

    [Fact]
    public async Task A_missing_source_returns_1_with_a_message()
    {
        var code = await Command().RunAsync(Options(source: _temp.Combine("no-existe")));

        Assert.Equal(1, code);
        Assert.Contains("Error:", _output.ToString());
    }

    [Fact]
    public async Task A_skipped_document_is_reported_and_returns_2()
    {
        TestDocx.CreateWithoutDocId(_temp.Dir, "sin-id.docx");

        var code = await Command().RunAsync(Options(yes: true));

        Assert.Equal(2, code);
        Assert.Equal(12, _store.Records.Count);
        Assert.Contains("OMITIDO sin-id.docx", _output.ToString());
    }

    [Fact]
    public async Task A_folder_with_only_invalid_documents_returns_1_and_writes_nothing()
    {
        using var other = new TempDir();
        TestDocx.CreateWithoutDocId(other.Dir, "sin-id.docx");

        var code = await Command().RunAsync(Options(yes: true, source: other.Dir));

        Assert.Equal(1, code);
        Assert.Equal(0, _store.Writes);
    }

    [Fact]
    public async Task Word_lock_files_in_the_folder_are_ignored()
    {
        File.WriteAllBytes(_temp.Combine("~$01_rechazos.docx"), new byte[] { 1, 2, 3 });

        var code = await Command().RunAsync(Options(dryRun: true));

        Assert.Equal(0, code);
        Assert.DoesNotContain("OMITIDO", _output.ToString());
    }
}
