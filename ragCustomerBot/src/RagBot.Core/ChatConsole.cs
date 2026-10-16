using System.Globalization;

namespace RagBot.Core;

/// <summary>Cómo se muestra una respuesta en la consola (respuesta, fuentes y, si se pide, el detalle de depuración).</summary>
public static class AnswerPrinter
{
    private static string F(double v) => v.ToString("0.00", CultureInfo.InvariantCulture);

    public static void Write(
        TextWriter output, ChatAnswer answer, string question, string rules, ChatOptions options, bool debug,
        IReadOnlyList<ChatTurn>? history = null)
    {
        output.WriteLine(answer.Text);
        output.WriteLine();

        if (answer.UsedModel)
        {
            var sources = answer.Context
                .GroupBy(c => c.DocId)
                .Select(g => $"{g.Key} ({F(g.Max(c => c.Score))})");
            output.WriteLine($"Fuentes: {string.Join(", ", sources)}");
        }
        else
        {
            var best = answer.Retrieved.Count > 0 ? F(answer.Retrieved[0].Score) : "sin resultados";
            output.WriteLine($"(No se llamó al modelo: la mejor puntuación fue {best}, por debajo del mínimo {F(options.MinScore)}.)");
        }

        if (!debug) return;

        if (answer.SearchQuery is not null && !answer.SearchQuery.Equals(question.Trim(), StringComparison.OrdinalIgnoreCase))
            output.WriteLine($"\n===== DEBUG =====\nConsulta de búsqueda: {answer.SearchQuery}");
        output.WriteLine("\n===== DEBUG: fragmentos recuperados =====");
        foreach (var c in answer.Retrieved)
        {
            var state = answer.Context.Any(x => x.Id == c.Id) ? "en el prompt" : "descartado (bajo el mínimo)";
            output.WriteLine($"  {F(c.Score)}  {c.Id,-9} {c.Section}  [{state}]");
        }
        if (answer.UsedModel)
        {
            output.WriteLine("\n===== DEBUG: mensaje del usuario enviado al modelo =====");
            var messages = PromptBuilder.Build(rules, question, answer.Context, history, answer.SearchQuery);
            output.WriteLine(messages[^1].Content);
        }
    }
}

/// <summary>
/// Chat por consola: lee preguntas hasta que el usuario sale. Recuerda los últimos <see cref="ChatOptions.HistoryTurns"/>
/// turnos para entender las preguntas de seguimiento. Un error en una pregunta no cierra la sesión ni se recuerda.
/// </summary>
public sealed class ChatLoop(ChatService service, string rules, ChatOptions options, TextWriter output, Func<string?> readLine)
{
    private static readonly string[] ExitCommands = { "/salir", "salir", "exit", "/q" };
    private static readonly string[] ResetCommands = { "/nuevo", "/reset" };

    public async Task RunAsync(bool debug, string banner, CancellationToken ct = default)
    {
        if (!string.IsNullOrEmpty(banner)) output.WriteLine(banner);
        var history = new List<ChatTurn>();

        while (!ct.IsCancellationRequested)
        {
            output.Write("\nTú> ");
            var line = readLine();
            if (line is null) break;                       // la entrada se cerró (Ctrl+Z / Ctrl+D)

            var question = line.Trim();
            if (question.Length == 0) continue;
            if (ExitCommands.Contains(question, StringComparer.OrdinalIgnoreCase)) break;

            if (question.Equals("/debug", StringComparison.OrdinalIgnoreCase))
            {
                debug = !debug;
                output.WriteLine($"Modo debug: {(debug ? "activado" : "desactivado")}");
                continue;
            }

            if (ResetCommands.Contains(question, StringComparer.OrdinalIgnoreCase))
            {
                history.Clear();
                output.WriteLine("Conversación reiniciada.");
                continue;
            }

            try
            {
                output.WriteLine();
                var past = history.ToList();                // copia: lo que el bot recuerda ANTES de esta pregunta
                var answer = await service.AskAsync(question, past, ct);
                AnswerPrinter.Write(output, answer, question, rules, options, debug, past);

                if (options.HistoryTurns > 0)
                {
                    history.Add(new ChatTurn(question, answer.Text));
                    while (history.Count > options.HistoryTurns) history.RemoveAt(0);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                output.WriteLine($"Error: {ex.Message}");
            }
        }
    }
}
