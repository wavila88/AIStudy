using RagBot.Core;

namespace RagBot.Tests;

public class MemoryTests
{
    private const string Rules = "REGLAS";

    private sealed class FakeRetriever(params RetrievedChunk[] chunks) : IRetriever
    {
        public List<string> Queries { get; } = new();

        public Task<IReadOnlyList<RetrievedChunk>> SearchAsync(string query, int topK, CancellationToken ct = default)
        {
            Queries.Add(query);
            return Task.FromResult<IReadOnlyList<RetrievedChunk>>(chunks);
        }
    }

    // Guion: cada llamada al modelo consume el siguiente paso (un texto o una excepción) y guarda lo que recibió.
    private sealed class RecordingModel(params object[] script) : IChatModel
    {
        public List<IReadOnlyList<ChatMessage>> Calls { get; } = new();

        public Task<string> CompleteAsync(IReadOnlyList<ChatMessage> messages, CancellationToken ct = default)
        {
            Calls.Add(messages);
            var step = script[Calls.Count - 1];
            if (step is Exception ex) throw ex;
            return Task.FromResult((string)step);
        }
    }

    private static RetrievedChunk Chunk(double score = 0.8) =>
        new("KB-01-s1", score, "KB-01", "Lista", "guide", "Guía > Lista\ncontenido");

    private static ChatOptions Options(int historyTurns = 6) =>
        new() { MinScore = 0.30, HistoryTurns = historyTurns, NoAnswerMessage = "SIN INFORMACION" };

    private static ChatService Service(FakeRetriever retriever, RecordingModel model, int historyTurns = 6) =>
        new(retriever, model, Rules, Options(historyTurns));

    private static ChatLoop Loop(ChatService service, string[] typed, StringWriter output, int historyTurns = 6)
    {
        var lines = new Queue<string?>(typed);
        return new ChatLoop(service, Rules, Options(historyTurns), output, () => lines.Count > 0 ? lines.Dequeue() : null);
    }

    // ---------------------------------------------------------------- reescritura
    [Fact]
    public void Rewrite_prompt_carries_the_previous_turns_and_the_last_question()
    {
        var prompt = QueryRewriter.BuildPrompt(
            new[] { new ChatTurn("¿puedo usar la tarjeta en Brasil?", "Sí, con PIX") }, "¿y en dólares?");

        Assert.Equal(2, prompt.Count);
        Assert.Equal("system", prompt[0].Role);
        Assert.Equal(QueryRewriter.Instructions, prompt[0].Content);
        var user = prompt[1].Content;
        Assert.Contains("Cliente: ¿puedo usar la tarjeta en Brasil?", user);
        Assert.Contains("Asistente: Sí, con PIX", user);
        Assert.True(user.IndexOf("¿y en dólares?", StringComparison.Ordinal) > user.IndexOf("Sí, con PIX", StringComparison.Ordinal));
    }

    [Fact]
    public void Rewrite_prompt_shortens_long_assistant_replies_to_save_tokens()
    {
        var prompt = QueryRewriter.BuildPrompt(new[] { new ChatTurn("hola", new string('x', 2000)) }, "y ahora?");

        var user = prompt[1].Content;
        Assert.True(user.Length < 1200);
        Assert.Contains("…", user);
    }

    [Theory]
    [InlineData("  \"¿cómo pago en dólares en Brasil?\"  ", "¿cómo pago en dólares en Brasil?")]
    [InlineData("¿cómo pago en dólares?\nNota: reformulada", "¿cómo pago en dólares?")]
    [InlineData("\n\n  ¿cómo pago?  \n", "¿cómo pago?")]
    [InlineData("", "ORIGINAL")]
    [InlineData("   ", "ORIGINAL")]
    [InlineData(null, "ORIGINAL")]
    public void Clean_keeps_a_single_plain_line_or_falls_back_to_the_original(string? raw, string expected) =>
        Assert.Equal(expected, QueryRewriter.Clean(raw, "ORIGINAL"));

    // ---------------------------------------------------------------- ChatService
    [Fact]
    public async Task The_first_question_has_no_history_so_it_is_searched_as_typed_with_one_model_call()
    {
        var retriever = new FakeRetriever(Chunk());
        var model = new RecordingModel("respuesta");

        var answer = await Service(retriever, model).AskAsync("¿puedo usar la tarjeta en Brasil?");

        Assert.Equal(new[] { "¿puedo usar la tarjeta en Brasil?" }, retriever.Queries);
        Assert.Single(model.Calls);
        Assert.Equal("¿puedo usar la tarjeta en Brasil?", answer.SearchQuery);
    }

    [Fact]
    public async Task A_follow_up_is_rewritten_and_the_rewritten_text_is_what_gets_searched()
    {
        var retriever = new FakeRetriever(Chunk());
        var model = new RecordingModel("¿cómo pago en dólares con la tarjeta en Brasil?", "respuesta final");
        var history = new[] { new ChatTurn("¿puedo usar la tarjeta en Brasil?", "Sí, con PIX") };

        var answer = await Service(retriever, model).AskAsync("¿y en dólares?", history);

        Assert.Equal(2, model.Calls.Count);
        Assert.Equal(QueryRewriter.Instructions, model.Calls[0][0].Content);          // 1.ª llamada: reescritura
        Assert.Equal(new[] { "¿cómo pago en dólares con la tarjeta en Brasil?" }, retriever.Queries);
        Assert.Equal("¿cómo pago en dólares con la tarjeta en Brasil?", answer.SearchQuery);
        Assert.Equal("respuesta final", answer.Text);
    }

    [Fact]
    public async Task The_answer_prompt_replays_the_previous_turns_as_real_conversation()
    {
        var model = new RecordingModel("¿cómo pago en dólares en Brasil?", "respuesta final");
        var history = new[] { new ChatTurn("pregunta previa", "respuesta previa") };

        await Service(new FakeRetriever(Chunk()), model).AskAsync("¿y en dólares?", history);

        var messages = model.Calls[1];
        Assert.Equal(new[] { "system", "user", "assistant", "user" }, messages.Select(m => m.Role));
        Assert.Equal(Rules, messages[0].Content);
        Assert.Equal("pregunta previa", messages[1].Content);
        Assert.Equal("respuesta previa", messages[2].Content);
        Assert.Contains("¿y en dólares?", messages[3].Content);
        Assert.Contains("contenido", messages[3].Content);                              // el contexto sigue yendo
        Assert.EndsWith(PromptBuilder.LanguageReminder, messages[3].Content.TrimEnd());
    }

    [Fact]
    public async Task The_final_message_tells_the_model_how_the_follow_up_was_interpreted()
    {
        var model = new RecordingModel("¿cómo pago en dólares en Brasil?", "ok");

        await Service(new FakeRetriever(Chunk()), model)
            .AskAsync("¿y en dólares?", new[] { new ChatTurn("a", "b") });

        var user = model.Calls[1][^1].Content;
        Assert.Contains("interpretada como", user);
        Assert.Contains("¿cómo pago en dólares en Brasil?", user);
    }

    [Fact]
    public async Task No_interpretation_note_when_the_question_already_stood_on_its_own()
    {
        var model = new RecordingModel("¿puedo usar la tarjeta en Chile?", "ok");

        await Service(new FakeRetriever(Chunk()), model)
            .AskAsync("¿puedo usar la tarjeta en Chile?", new[] { new ChatTurn("a", "b") });

        Assert.DoesNotContain("interpretada como", model.Calls[1][^1].Content);
    }

    [Fact]
    public async Task The_score_gate_still_applies_to_follow_ups_and_skips_the_answer_call()
    {
        var model = new RecordingModel("pregunta reescrita");

        var answer = await Service(new FakeRetriever(Chunk(score: 0.10)), model)
            .AskAsync("¿y eso?", new[] { new ChatTurn("a", "b") });

        Assert.False(answer.UsedModel);
        Assert.Equal("SIN INFORMACION", answer.Text);
        Assert.Single(model.Calls);                                                    // solo la reescritura
        Assert.Equal("pregunta reescrita", answer.SearchQuery);
    }

    // ---------------------------------------------------------------- ChatLoop
    [Fact]
    public async Task The_loop_remembers_previous_turns_for_the_next_question()
    {
        var retriever = new FakeRetriever(Chunk());
        var model = new RecordingModel("respuesta uno", "consulta reescrita", "respuesta dos");
        var output = new StringWriter();

        await Loop(Service(retriever, model), new[] { "pregunta uno", "y eso?", "/salir" }, output).RunAsync(false, "");

        Assert.Equal(new[] { "pregunta uno", "consulta reescrita" }, retriever.Queries);
        Assert.Contains("respuesta uno", model.Calls[1][1].Content);                   // la reescritura vio el turno anterior
        Assert.Equal(new[] { "system", "user", "assistant", "user" }, model.Calls[2].Select(m => m.Role));
        Assert.Equal("respuesta uno", model.Calls[2][2].Content);
    }

    [Theory]
    [InlineData("/nuevo")]
    [InlineData("/reset")]
    public async Task The_new_command_forgets_the_conversation(string command)
    {
        var retriever = new FakeRetriever(Chunk());
        var model = new RecordingModel("respuesta uno", "respuesta dos");
        var output = new StringWriter();

        await Loop(Service(retriever, model), new[] { "pregunta uno", command, "pregunta dos", "/salir" }, output).RunAsync(false, "");

        Assert.Equal(2, model.Calls.Count);                                            // sin reescritura
        Assert.Equal(new[] { "pregunta uno", "pregunta dos" }, retriever.Queries);
        Assert.Contains("Conversación reiniciada", output.ToString());
    }

    [Fact]
    public async Task Only_the_most_recent_turns_are_kept()
    {
        var retriever = new FakeRetriever(Chunk());
        // q1: a1 | q2: reescritura, a2 | q3: reescritura, a3 | q4: reescritura, a4
        var model = new RecordingModel("a1", "r2", "a2", "r3", "a3", "r4", "a4");
        var output = new StringWriter();

        await Loop(Service(retriever, model, historyTurns: 2),
            new[] { "pregunta-1", "pregunta-2", "pregunta-3", "pregunta-4", "/salir" }, output, historyTurns: 2).RunAsync(false, "");

        var rewriteForQ4 = model.Calls[5][1].Content;
        Assert.DoesNotContain("pregunta-1", rewriteForQ4);
        Assert.Contains("pregunta-2", rewriteForQ4);
        Assert.Contains("pregunta-3", rewriteForQ4);
    }

    [Fact]
    public async Task With_zero_history_turns_the_bot_has_no_memory()
    {
        var retriever = new FakeRetriever(Chunk());
        var model = new RecordingModel("a1", "a2");
        var output = new StringWriter();

        await Loop(Service(retriever, model, historyTurns: 0), new[] { "uno", "dos", "/salir" }, output, historyTurns: 0).RunAsync(false, "");

        Assert.Equal(2, model.Calls.Count);
        Assert.Equal(new[] { "uno", "dos" }, retriever.Queries);
    }

    [Fact]
    public async Task A_failed_question_is_not_remembered()
    {
        var retriever = new FakeRetriever(Chunk());
        // q1: a1 | q2: la reescritura falla | q3: reescritura (r3) y respuesta (a3)
        var model = new RecordingModel("a1", new InvalidOperationException("se cayó"), "r3", "a3");
        var output = new StringWriter();

        // q1 funciona, q2 falla en el modelo, q3 debe reescribirse viendo solo q1
        await Loop(Service(retriever, model), new[] { "pregunta-1", "pregunta-2", "pregunta-3", "/salir" }, output).RunAsync(false, "");

        Assert.Contains("Error: se cayó", output.ToString());
        Assert.Equal(4, model.Calls.Count);
        var thirdCall = model.Calls[2][1].Content;                                    // reescritura de pregunta-3
        Assert.Contains("pregunta-1", thirdCall);
        Assert.DoesNotContain("pregunta-2", thirdCall.Replace("pregunta-3", ""));
    }

    [Fact]
    public async Task Debug_shows_the_search_query_when_it_was_rewritten()
    {
        var model = new RecordingModel("respuesta uno", "consulta reescrita", "respuesta dos");
        var output = new StringWriter();

        await Loop(Service(new FakeRetriever(Chunk()), model), new[] { "uno", "y eso?", "/salir" }, output).RunAsync(debug: true, "");

        Assert.Contains("Consulta de búsqueda: consulta reescrita", output.ToString());
    }
}
