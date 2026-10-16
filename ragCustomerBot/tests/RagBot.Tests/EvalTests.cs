using RagBot.Core;

namespace RagBot.Tests;

public class EvalTests
{
    private sealed class FakeRetriever(Dictionary<string, RetrievedChunk[]> answers) : IRetriever
    {
        public List<(string Query, int TopK)> Calls { get; } = new();

        public Task<IReadOnlyList<RetrievedChunk>> SearchAsync(string query, int topK, CancellationToken ct = default)
        {
            Calls.Add((query, topK));
            return Task.FromResult<IReadOnlyList<RetrievedChunk>>(answers[query]);
        }
    }

    private static RetrievedChunk Chunk(string docId, double score) =>
        new($"{docId}-s1", score, docId, "Sección", "guide", "texto");

    private static EvalQuestion Q(string text, params string[] expected) => new(text, expected);

    // ---- evaluador ----
    [Fact]
    public async Task Reports_the_rank_of_the_first_chunk_from_an_expected_document()
    {
        var retriever = new FakeRetriever(new()
        {
            ["a"] = new[] { Chunk("KB-03", 0.7), Chunk("KB-01", 0.6), Chunk("KB-01", 0.5) }
        });

        var results = await new RetrievalEvaluator(retriever).RunAsync(new[] { Q("a", "KB-01") }, topK: 3);

        var result = Assert.Single(results);
        Assert.Equal(2, result.FirstHitRank);
        Assert.True(result.IsHit);
        Assert.Equal(0.7, result.TopScore, 3);
    }

    [Fact]
    public async Task Reports_a_miss_when_no_expected_document_is_retrieved()
    {
        var retriever = new FakeRetriever(new() { ["a"] = new[] { Chunk("KB-03", 0.4) } });

        var result = Assert.Single(await new RetrievalEvaluator(retriever).RunAsync(new[] { Q("a", "KB-01") }, 5));

        Assert.Null(result.FirstHitRank);
        Assert.False(result.IsHit);
    }

    [Fact]
    public async Task Accepts_any_of_several_expected_documents()
    {
        var retriever = new FakeRetriever(new() { ["a"] = new[] { Chunk("KB-05", 0.6) } });

        var result = Assert.Single(await new RetrievalEvaluator(retriever).RunAsync(new[] { Q("a", "KB-01", "KB-05") }, 5));

        Assert.Equal(1, result.FirstHitRank);
    }

    [Fact]
    public async Task Handles_a_question_with_no_results()
    {
        var retriever = new FakeRetriever(new() { ["a"] = Array.Empty<RetrievedChunk>() });

        var result = Assert.Single(await new RetrievalEvaluator(retriever).RunAsync(new[] { Q("a", "KB-01") }, 5));

        Assert.False(result.IsHit);
        Assert.Equal(0, result.TopScore);
    }

    [Fact]
    public async Task Asks_the_retriever_with_the_requested_topk_for_every_question_in_order()
    {
        var retriever = new FakeRetriever(new()
        {
            ["a"] = new[] { Chunk("KB-01", 0.9) },
            ["b"] = new[] { Chunk("KB-02", 0.8) }
        });

        await new RetrievalEvaluator(retriever).RunAsync(new[] { Q("a", "KB-01"), Q("b", "KB-02") }, topK: 7);

        Assert.Equal(new[] { ("a", 7), ("b", 7) }, retriever.Calls);
    }

    // ---- informe ----
    private static EvalResult Result(string question, string expected, params RetrievedChunk[] chunks)
    {
        var q = Q(question, expected);
        var rank = Array.FindIndex(chunks, c => c.DocId == expected);
        return new EvalResult(q, chunks, rank < 0 ? null : rank + 1);
    }

    [Fact]
    public void Report_counts_hits_and_lists_each_question()
    {
        var report = EvalReport.Render(new[]
        {
            Result("me rechazan la tarjeta", "KB-01", Chunk("KB-01", 0.82)),
            Result("pregunta rara", "KB-02", Chunk("KB-05", 0.31), Chunk("KB-03", 0.20))
        }, topK: 5);

        Assert.Contains("top-5", report);
        Assert.Contains("Aciertos: 1/2 (50%)", report);
        Assert.Contains("OK", report);
        Assert.Contains("FALLA", report);
        Assert.Contains("me rechazan la tarjeta", report);
        Assert.Contains("0.82", report);
        Assert.Contains("obtuvo: KB-05, KB-03", report);
    }

    [Fact]
    public void Report_suggests_a_threshold_range_from_hit_and_miss_scores()
    {
        var report = EvalReport.Render(new[]
        {
            Result("a", "KB-01", Chunk("KB-01", 0.70)),
            Result("b", "KB-02", Chunk("KB-02", 0.52)),
            Result("c", "KB-03", Chunk("KB-09", 0.38))
        }, topK: 5);

        Assert.Contains("0.52", report);   // la puntuación más baja entre los aciertos
        Assert.Contains("0.38", report);   // la más alta entre los fallos
    }

    [Fact]
    public void Report_says_so_when_there_are_no_questions()
    {
        Assert.Contains("0/0", EvalReport.Render(Array.Empty<EvalResult>(), topK: 5));
    }

    // ---- carga de preguntas ----
    [Fact]
    public void Loads_questions_from_json_ignoring_case()
    {
        var questions = EvalQuestionLoader.Parse("""[{"Question":"hola","expectedDocs":["KB-01","KB-02"]}]""");

        var q = Assert.Single(questions);
        Assert.Equal("hola", q.Question);
        Assert.Equal(new[] { "KB-01", "KB-02" }, q.ExpectedDocs);
    }

    [Fact]
    public void Rejects_a_question_without_expected_documents()
    {
        var ex = Assert.Throws<InvalidDataException>(() => EvalQuestionLoader.Parse("""[{"question":"hola","expectedDocs":[]}]"""));
        Assert.Contains("hola", ex.Message);
    }

    [Fact]
    public void Rejects_a_blank_question()
    {
        Assert.Throws<InvalidDataException>(() => EvalQuestionLoader.Parse("""[{"question":" ","expectedDocs":["KB-01"]}]"""));
    }

    [Fact]
    public void The_repo_questions_file_has_one_question_per_guide()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "eval", "questions.json");

        var questions = EvalQuestionLoader.Load(path);

        Assert.Equal(8, questions.Count);
        Assert.Equal(
            Enumerable.Range(1, 8).Select(i => $"KB-{i:D2}"),
            questions.Select(q => Assert.Single(q.ExpectedDocs)));
    }
}
