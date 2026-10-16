using System.Globalization;
using System.Text;
using System.Text.Json;

namespace RagBot.Core;

public sealed record EvalQuestion(string Question, IReadOnlyList<string> ExpectedDocs);

public sealed record EvalResult(EvalQuestion Question, IReadOnlyList<RetrievedChunk> Chunks, int? FirstHitRank)
{
    public bool IsHit => FirstHitRank.HasValue;

    /// <summary>Puntuación del mejor resultado (es la que decide la compuerta de "no tengo información").</summary>
    public double TopScore => Chunks.Count > 0 ? Chunks[0].Score : 0;
}

public static class EvalQuestionLoader
{
    private sealed class Raw
    {
        public string? Question { get; set; }
        public List<string>? ExpectedDocs { get; set; }
    }

    public static IReadOnlyList<EvalQuestion> Load(string path) => Parse(File.ReadAllText(path));

    public static IReadOnlyList<EvalQuestion> Parse(string json)
    {
        var raw = JsonSerializer.Deserialize<List<Raw>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("El archivo de preguntas está vacío.");

        var questions = new List<EvalQuestion>();
        foreach (var item in raw)
        {
            if (string.IsNullOrWhiteSpace(item.Question))
                throw new InvalidDataException("Hay una pregunta vacía en el archivo de evaluación.");
            if (item.ExpectedDocs is null || item.ExpectedDocs.Count == 0)
                throw new InvalidDataException($"La pregunta \"{item.Question}\" no indica expectedDocs.");
            questions.Add(new EvalQuestion(item.Question.Trim(), item.ExpectedDocs));
        }
        return questions;
    }
}

/// <summary>Recorre las preguntas solo contra la búsqueda (sin llamar al modelo de chat, así que no gasta tokens).</summary>
public sealed class RetrievalEvaluator(IRetriever retriever)
{
    public async Task<IReadOnlyList<EvalResult>> RunAsync(
        IReadOnlyList<EvalQuestion> questions, int topK, CancellationToken ct = default)
    {
        var results = new List<EvalResult>();
        foreach (var question in questions)
        {
            var chunks = await retriever.SearchAsync(question.Question, topK, ct);
            var rank = FirstHitRank(chunks, question.ExpectedDocs);
            results.Add(new EvalResult(question, chunks, rank));
        }
        return results;
    }

    private static int? FirstHitRank(IReadOnlyList<RetrievedChunk> chunks, IReadOnlyList<string> expected)
    {
        for (var i = 0; i < chunks.Count; i++)
            if (expected.Contains(chunks[i].DocId, StringComparer.Ordinal)) return i + 1;
        return null;
    }
}

public static class EvalReport
{
    private static string F(double value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    public static string Render(IReadOnlyList<EvalResult> results, int topK)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Evaluación de recuperación (top-{topK})");
        sb.AppendLine();
        sb.AppendLine(" #  resultado  rank  mejor  pregunta");

        for (var i = 0; i < results.Count; i++)
        {
            var r = results[i];
            var estado = r.IsHit ? "OK" : "FALLA";
            var rank = r.FirstHitRank?.ToString() ?? "-";
            sb.AppendLine($"{i + 1,2}  {estado,-9}  {rank,4}  {F(r.TopScore),5}  {r.Question.Question}");
            if (!r.IsHit)
            {
                var obtained = r.Chunks.Select(c => c.DocId).Distinct().ToList();
                var shown = obtained.Count > 0 ? string.Join(", ", obtained) : "nada";
                sb.AppendLine($"                              esperaba: {string.Join(", ", r.Question.ExpectedDocs)} | obtuvo: {shown}");
            }
        }

        var hits = results.Count(r => r.IsHit);
        var pct = results.Count == 0 ? 0 : (int)Math.Round(100.0 * hits / results.Count);
        sb.AppendLine();
        sb.AppendLine($"Aciertos: {hits}/{results.Count} ({pct}%)");

        var hitScores = results.Where(r => r.IsHit).Select(r => r.TopScore).ToList();
        var missScores = results.Where(r => !r.IsHit).Select(r => r.TopScore).ToList();
        if (hitScores.Count > 0)
            sb.AppendLine($"Puntuación más baja entre los aciertos: {F(hitScores.Min())}");
        if (missScores.Count > 0)
            sb.AppendLine($"Puntuación más alta entre los fallos:   {F(missScores.Max())}");
        if (hitScores.Count > 0 && missScores.Count > 0)
            sb.AppendLine("Un MinScore entre esos dos valores deja pasar los aciertos y frena los fallos (orientativo, con pocas preguntas).");

        return sb.ToString();
    }
}
