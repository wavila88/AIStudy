using Rag.Core;
using Rag.Ingestion;

namespace Rag.Ingestion.Tests;

public class IngestionServiceTests
{
    // ---- fakes ----
    private sealed class FakeLoader(Dictionary<string, Func<ParsedDocument>> docs) : IDocumentLoader
    {
        public ParsedDocument Load(string path) => docs[path]();
    }

    private sealed class FakeEmbedder(List<string> log, bool fail = false, bool shortAnswer = false, Exception? throwWith = null) : IEmbedder
    {
        public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
        {
            log.Add("embed");
            if (throwWith is not null) throw throwWith;
            if (fail) throw new InvalidOperationException("embedder caído");
            var take = shortAnswer ? texts.Count - 1 : texts.Count;
            return Task.FromResult<IReadOnlyList<float[]>>(texts.Take(take).Select(t => new[] { (float)t.Length }).ToList());
        }
    }

    private sealed class FakeStore(List<string> log) : IVectorStore
    {
        public Dictionary<string, VectorRecord> Records { get; } = new();
        public bool FailUpsert { get; set; }

        public Task<IReadOnlyList<string>> ListIdsAsync(string prefix, CancellationToken ct = default)
        {
            log.Add("list");
            return Task.FromResult<IReadOnlyList<string>>(
                Records.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList());
        }

        public Task DeleteAsync(IReadOnlyList<string> ids, CancellationToken ct = default)
        {
            log.Add("delete");
            foreach (var id in ids) Records.Remove(id);
            return Task.CompletedTask;
        }

        public Task UpsertAsync(IReadOnlyList<VectorRecord> records, CancellationToken ct = default)
        {
            log.Add("upsert");
            if (FailUpsert) throw new InvalidOperationException("upsert rechazado");
            foreach (var r in records) Records[r.Id] = r;
            return Task.CompletedTask;
        }
    }

    // ---- helpers ----
    private static string Long(char c) => new(c, 120);

    private static ParsedDocument Doc(string id, params DocSection[] sections) =>
        new(id, $"Guía {id}", $"{id}.docx", sections);

    private static ParsedDocument ThreeChunkDoc(string id) => Doc(id,
        new DocSection("Uno", new[] { Long('a') }),
        new DocSection("Dos", new[] { Long('b') }),
        new DocSection("Preguntas frecuentes", new[] { "P: q", "R: a" }));

    private static (IngestionService service, FakeStore store, List<string> log) Build(
        Dictionary<string, Func<ParsedDocument>> docs, bool failEmbed = false, bool shortEmbed = false,
        Exception? embedThrows = null)
    {
        var log = new List<string>();
        var store = new FakeStore(log);
        var service = new IngestionService(
            new FakeLoader(docs), new SectionChunker(), new FakeEmbedder(log, failEmbed, shortEmbed, embedThrows), store);
        return (service, store, log);
    }

    private static VectorRecord Seed(string id) => new(id, new[] { 1f }, new Dictionary<string, object>());

    // ---- plan ----
    [Fact]
    public async Task Plan_reads_existing_ids_and_writes_nothing()
    {
        var (service, store, log) = Build(new() { ["a.docx"] = () => ThreeChunkDoc("KB-01") });
        store.Records["KB-01-s1"] = Seed("KB-01-s1");

        var plans = await service.PlanAsync(new[] { "a.docx" });

        var plan = Assert.Single(plans);
        Assert.True(plan.IsValid);
        Assert.Equal("KB-01", plan.DocId);
        Assert.Equal(3, plan.Chunks.Count);
        Assert.Equal(new[] { "KB-01-s1" }, plan.ExistingIds);
        Assert.Equal(new[] { "list" }, log);
    }

    [Fact]
    public async Task Plan_flags_a_document_without_doc_id_and_execute_skips_it()
    {
        var (service, store, log) = Build(new()
        {
            ["bad.docx"] = () => throw new InvalidDocumentException("bad.docx", "Falta 'ID de documento' en la tabla inicial.")
        });

        var plans = await service.PlanAsync(new[] { "bad.docx" });
        var results = await service.ExecuteAsync(plans);

        Assert.False(plans[0].IsValid);
        Assert.Contains("ID de documento", plans[0].Error);
        Assert.Empty(results);
        Assert.Empty(store.Records);
        Assert.DoesNotContain("embed", log);
    }

    [Fact]
    public async Task Plan_rejects_the_second_file_that_reuses_a_doc_id()
    {
        var (service, _, _) = Build(new()
        {
            ["one.docx"] = () => ThreeChunkDoc("KB-01"),
            ["copy.docx"] = () => ThreeChunkDoc("KB-01")
        });

        var plans = await service.PlanAsync(new[] { "one.docx", "copy.docx" });

        Assert.True(plans[0].IsValid);
        Assert.False(plans[1].IsValid);
        Assert.Contains("duplicado", plans[1].Error);
        Assert.Contains("one.docx", plans[1].Error);
    }

    [Fact]
    public async Task Plan_ignores_ids_that_belong_to_another_document_with_a_longer_doc_id()
    {
        var (service, store, _) = Build(new() { ["kb.docx"] = () => ThreeChunkDoc("KB") });
        foreach (var id in new[] { "KB-01-s1", "KB-s1", "KB-q9" })
            store.Records[id] = Seed(id);

        var plans = await service.PlanAsync(new[] { "kb.docx" });

        Assert.Equal(new[] { "KB-q9", "KB-s1" }, plans[0].ExistingIds.OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Plan_rejects_a_document_that_produces_no_chunks()
    {
        var (service, _, _) = Build(new() { ["empty.docx"] = () => Doc("KB-05") });

        var plans = await service.PlanAsync(new[] { "empty.docx" });

        Assert.False(plans[0].IsValid);
        Assert.Contains("chunks", plans[0].Error);
    }

    // ---- execute ----
    [Fact]
    public async Task Execute_embeds_then_upserts_then_deletes_only_the_stale_records_and_stores_metadata()
    {
        var (service, store, log) = Build(new() { ["a.docx"] = () => ThreeChunkDoc("KB-01") });
        store.Records["KB-01-s1"] = Seed("KB-01-s1");
        store.Records["KB-01-s99"] = Seed("KB-01-s99");
        var plans = await service.PlanAsync(new[] { "a.docx" });
        log.Clear();

        var done = new List<DocumentResult>();
        var results = await service.ExecuteAsync(plans, done.Add);

        Assert.Equal(new[] { "embed", "upsert", "delete" }, log);
        Assert.DoesNotContain("KB-01-s99", store.Records.Keys);
        var result = Assert.Single(results);
        Assert.Equal(new DocumentResult("KB-01", 2, 3), result);
        Assert.Equal(results, done);

        var faq = store.Records["KB-01-q1"];
        Assert.Equal("KB-01", faq.Metadata["docId"]);
        Assert.Equal("Guía KB-01", faq.Metadata["title"]);
        Assert.Equal("Preguntas frecuentes", faq.Metadata["section"]);
        Assert.Equal("faq", faq.Metadata["type"]);
        Assert.Equal(2, faq.Metadata["chunkIndex"]);
        Assert.StartsWith("Guía KB-01 > Preguntas frecuentes\nP: q", (string)faq.Metadata["text"]);
        Assert.Equal("guide", store.Records["KB-01-s1"].Metadata["type"]);
    }

    [Fact]
    public async Task Running_twice_does_not_duplicate_and_removes_stale_records()
    {
        var (service, store, _) = Build(new() { ["a.docx"] = () => ThreeChunkDoc("KB-01") });
        store.Records["KB-01-s99"] = Seed("KB-01-s99");

        for (var i = 0; i < 2; i++)
            await service.ExecuteAsync(await service.PlanAsync(new[] { "a.docx" }));

        Assert.Equal(
            new[] { "KB-01-q1", "KB-01-s1", "KB-01-s2" },
            store.Records.Keys.OrderBy(k => k, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Embedding_failure_deletes_nothing_and_reports_the_document()
    {
        var (service, store, log) = Build(new() { ["a.docx"] = () => ThreeChunkDoc("KB-01") }, failEmbed: true);
        store.Records["KB-01-s1"] = Seed("KB-01-s1");
        var plans = await service.PlanAsync(new[] { "a.docx" });

        var ex = await Assert.ThrowsAsync<IngestionException>(() => service.ExecuteAsync(plans));

        Assert.Equal("KB-01", ex.DocId);
        Assert.DoesNotContain("delete", log);
        Assert.Contains("KB-01-s1", store.Records.Keys);
    }

    [Fact]
    public async Task Embedder_returning_fewer_vectors_deletes_nothing()
    {
        var (service, store, log) = Build(new() { ["a.docx"] = () => ThreeChunkDoc("KB-01") }, shortEmbed: true);
        store.Records["KB-01-s1"] = Seed("KB-01-s1");
        var plans = await service.PlanAsync(new[] { "a.docx" });

        await Assert.ThrowsAsync<IngestionException>(() => service.ExecuteAsync(plans));

        Assert.DoesNotContain("delete", log);
        Assert.DoesNotContain("upsert", log);
    }

    [Fact]
    public async Task A_failed_upsert_leaves_the_existing_records_untouched()
    {
        var (service, store, log) = Build(new() { ["a.docx"] = () => ThreeChunkDoc("KB-01") });
        store.Records["KB-01-s1"] = Seed("KB-01-s1");
        store.Records["KB-01-s99"] = Seed("KB-01-s99");
        var plans = await service.PlanAsync(new[] { "a.docx" });
        store.FailUpsert = true;

        var ex = await Assert.ThrowsAsync<IngestionException>(() => service.ExecuteAsync(plans));

        Assert.Equal("KB-01", ex.DocId);
        Assert.DoesNotContain("delete", log);
        Assert.Equal(new[] { "KB-01-s1", "KB-01-s99" }, store.Records.Keys.OrderBy(k => k, StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_timeout_while_embedding_is_reported_for_the_document_not_crashed()
    {
        var (service, _, _) = Build(
            new() { ["a.docx"] = () => ThreeChunkDoc("KB-01") },
            embedThrows: new TaskCanceledException("HttpClient.Timeout"));
        var plans = await service.PlanAsync(new[] { "a.docx" });

        var ex = await Assert.ThrowsAsync<IngestionException>(() => service.ExecuteAsync(plans));

        Assert.Equal("KB-01", ex.DocId);
    }

    [Fact]
    public async Task Plan_turns_an_unreadable_file_into_a_failed_plan_and_keeps_going()
    {
        var (service, _, _) = Build(new()
        {
            ["locked.docx"] = () => throw new IOException("El archivo está en uso por otro proceso."),
            ["ok.docx"] = () => ThreeChunkDoc("KB-01")
        });

        var plans = await service.PlanAsync(new[] { "locked.docx", "ok.docx" });

        Assert.False(plans[0].IsValid);
        Assert.Contains("en uso", plans[0].Error);
        Assert.True(plans[1].IsValid);
    }
}
