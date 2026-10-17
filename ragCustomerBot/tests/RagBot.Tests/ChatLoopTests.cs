using RagBot.Core;

namespace RagBot.Tests;

public class ChatLoopTests
{
    private sealed class FakeRetriever(params RetrievedChunk[] chunks) : IRetriever
    {
        public List<string> Queries { get; } = new();

        public Task<IReadOnlyList<RetrievedChunk>> SearchAsync(string query, int topK, CancellationToken ct = default)
        {
            Queries.Add(query);
            return Task.FromResult<IReadOnlyList<RetrievedChunk>>(chunks);
        }
    }

    // Cada llamada al modelo consume el siguiente paso del guion: un texto, o una excepción.
    private sealed class ScriptedModel(params object[] script) : IChatModel
    {
        private int _next;
        public int Calls => _next;

        public Task<string> CompleteAsync(IReadOnlyList<ChatMessage> messages, CancellationToken ct = default)
        {
            var step = script[_next++];
            if (step is Exception ex) throw ex;
            return Task.FromResult((string)step);
        }
    }

    private static RetrievedChunk Chunk(double score) =>
        new("KB-01-s1", score, "KB-01", "Lista de verificación", "guide", "Guía > Lista\n1. Límites");

    private static (ChatLoop loop, StringWriter output, FakeRetriever retriever, ScriptedModel model) Build(
        string[] typed, RetrievedChunk[] chunks, params object[] modelScript)
    {
        var retriever = new FakeRetriever(chunks);
        var model = new ScriptedModel(modelScript);
        var options = new ChatOptions { MinScore = 0.30, NoAnswerMessage = "SIN INFORMACION" };
        var service = new ChatService(retriever, model, "REGLAS", options);
        var output = new StringWriter();
        var lines = new Queue<string?>(typed);
        var loop = new ChatLoop(service, "REGLAS", options, output, () => lines.Count > 0 ? lines.Dequeue() : null);
        return (loop, output, retriever, model);
    }

    [Fact]
    public async Task Answers_each_typed_question_until_the_user_exits()
    {
        // La segunda pregunta ya tiene conversación previa: antes de buscar, el modelo la reescribe ("segunda reescrita").
        var (loop, output, retriever, _) = Build(
            new[] { "primera pregunta", "segunda pregunta", "/salir", "no debe leerse" },
            new[] { Chunk(0.8) }, "Respuesta uno", "segunda reescrita", "Respuesta dos");

        await loop.RunAsync(debug: false, banner: "BANNER");

        Assert.Equal(new[] { "primera pregunta", "segunda reescrita" }, retriever.Queries);
        var text = output.ToString();
        Assert.Contains("BANNER", text);
        Assert.Contains("Respuesta uno", text);
        Assert.Contains("Respuesta dos", text);
        Assert.Contains("Fuentes: KB-01 (0.80)", text);
    }

    [Theory]
    [InlineData("/salir")]
    [InlineData("salir")]
    [InlineData("exit")]
    [InlineData("/q")]
    [InlineData("  /SALIR  ")]
    public async Task Recognises_the_exit_commands(string command)
    {
        var (loop, _, retriever, _) = Build(new[] { command, "no debe leerse" }, new[] { Chunk(0.8) }, "x");

        await loop.RunAsync(debug: false, banner: "");

        Assert.Empty(retriever.Queries);
    }

    [Fact]
    public async Task Ends_quietly_when_the_input_closes()
    {
        var (loop, _, retriever, _) = Build(Array.Empty<string>(), new[] { Chunk(0.8) }, "x");

        await loop.RunAsync(debug: false, banner: "");

        Assert.Empty(retriever.Queries);
    }

    [Fact]
    public async Task Ignores_blank_lines_without_searching()
    {
        var (loop, _, retriever, model) = Build(new[] { "", "   ", "/salir" }, new[] { Chunk(0.8) }, "x");

        await loop.RunAsync(debug: false, banner: "");

        Assert.Empty(retriever.Queries);
        Assert.Equal(0, model.Calls);
    }

    [Fact]
    public async Task Debug_command_toggles_the_detail_for_the_following_questions()
    {
        var (loop, output, _, _) = Build(
            new[] { "uno", "/debug", "dos", "/debug", "tres", "/salir" },
            new[] { Chunk(0.8) }, "R1", "R2", "R3");

        await loop.RunAsync(debug: false, banner: "");

        var text = output.ToString();
        Assert.Equal(1, text.Split("DEBUG: fragmentos recuperados").Length - 1);   // solo la segunda pregunta
        Assert.Contains("Modo debug: activado", text);
        Assert.Contains("Modo debug: desactivado", text);
    }

    [Fact]
    public async Task Can_start_with_debug_already_on()
    {
        var (loop, output, _, _) = Build(new[] { "uno", "/salir" }, new[] { Chunk(0.8) }, "R1");

        await loop.RunAsync(debug: true, banner: "");

        Assert.Contains("DEBUG: fragmentos recuperados", output.ToString());
        Assert.Contains("KB-01-s1", output.ToString());
    }

    [Fact]
    public async Task A_failing_question_prints_the_error_and_the_session_continues()
    {
        var (loop, output, retriever, _) = Build(
            new[] { "falla", "funciona", "/salir" },
            new[] { Chunk(0.8) },
            new InvalidOperationException("se cayó el modelo"), "Respuesta buena");

        await loop.RunAsync(debug: false, banner: "");

        Assert.Equal(new[] { "falla", "funciona" }, retriever.Queries);
        var text = output.ToString();
        Assert.Contains("Error: se cayó el modelo", text);
        Assert.Contains("Respuesta buena", text);
    }

    [Fact]
    public async Task Low_scores_print_the_no_answer_message_and_do_not_call_the_model()
    {
        var (loop, output, _, model) = Build(new[] { "receta de pizza", "/salir" }, new[] { Chunk(0.12) });

        await loop.RunAsync(debug: false, banner: "");

        Assert.Equal(0, model.Calls);
        var text = output.ToString();
        Assert.Contains("SIN INFORMACION", text);
        // La nota técnica es para depurar: el usuario normal solo ve el mensaje amable.
        Assert.DoesNotContain("No se llamó al modelo", text);
        Assert.DoesNotContain("0.12", text);
    }

    [Fact]
    public async Task Debug_mode_explains_why_the_model_was_not_called()
    {
        var (loop, output, _, model) = Build(new[] { "receta de pizza", "/salir" }, new[] { Chunk(0.12) });

        await loop.RunAsync(debug: true, banner: "");

        Assert.Equal(0, model.Calls);
        var text = output.ToString();
        Assert.Contains("SIN INFORMACION", text);
        Assert.Contains("No se llamó al modelo", text);
        Assert.Contains("0.12", text);
        Assert.Contains("0.30", text);
    }

    [Fact]
    public async Task Prompts_the_user_before_reading_each_line()
    {
        var (loop, output, _, _) = Build(new[] { "/salir" }, new[] { Chunk(0.8) }, "x");

        await loop.RunAsync(debug: false, banner: "");

        Assert.Contains("Tú> ", output.ToString());
    }
}
