using System.Globalization;
using System.Text;
using Microsoft.Extensions.Configuration;
using RagBot.Core;
using RagBot.OpenAI;
using RagBot.Pinecone;

const string Usage = """
    Uso:
      ragbot                       chat interactivo: escribes tus preguntas en la consola (igual que "ragbot chat")
      ragbot chat [--debug] [--top N]
      ragbot ask "<pregunta>" [--debug] [--top N]   responde una sola pregunta y termina
      ragbot eval [--top N] [--questions <archivo.json>]
          Recorre las preguntas de evaluación solo contra Pinecone (no llama a OpenAI, no gasta tokens).

      --debug  muestra los fragmentos recuperados, cuáles entraron al prompt y el prompt completo
      --top N  cuántos fragmentos pedir (por defecto Chat:TopK de appsettings.json)

    Dentro del chat:  /debug  activa o desactiva el detalle   |   /salir  termina
    """;

static string F(double v) => v.ToString("0.00", CultureInfo.InvariantCulture);

Console.OutputEncoding = Encoding.UTF8;
try { Console.InputEncoding = Encoding.UTF8; } catch (IOException) { /* entrada redirigida: se deja como está */ }

try
{
    // Sin comando (o solo con opciones, como "--debug") se entra al chat.
    var implicitChat = args.Length == 0 || args[0].StartsWith("--");
    var command = implicitChat ? "chat" : args[0];
    var first = implicitChat ? 0 : 1;

    if (command is "help" or "-h" || (implicitChat && args.Length > 0 && args[0] == "--help"))
    {
        Console.WriteLine(Usage);
        return 0;
    }
    if (command is not ("chat" or "ask" or "eval"))
    {
        Console.WriteLine(Usage);
        return 1;
    }

    var config = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: true)
        .AddJsonFile("appsettings.Local.json", optional: true)   // local, ignorado por git: aquí van las claves
        .AddEnvironmentVariables()
        .Build();

    var pinecone = config.GetSection("Pinecone").Get<PineconeOptions>() ?? new PineconeOptions();
    if (string.IsNullOrWhiteSpace(pinecone.ApiKey))
        pinecone.ApiKey = Environment.GetEnvironmentVariable("PINECONE_API_KEY") ?? "";
    if (string.IsNullOrWhiteSpace(pinecone.ApiKey))
        throw new ArgumentException("Falta la API key de Pinecone: ponla en src/RagBot.Cli/appsettings.Local.json (Pinecone:ApiKey).");
    if (string.IsNullOrWhiteSpace(pinecone.IndexName))
        throw new ArgumentException("Falta Pinecone:IndexName en appsettings.json.");

    var chat = config.GetSection("Chat").Get<ChatOptions>() ?? new ChatOptions();
    var debug = false;
    string? questionsPath = null;
    var words = new List<string>();
    for (var i = first; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--debug" when command is "ask" or "chat":
                debug = true; break;
            case "--top" when i + 1 < args.Length && int.TryParse(args[i + 1], out var n) && n > 0:
                chat.TopK = n; i++; break;
            case "--questions" when command == "eval" && i + 1 < args.Length:
                questionsPath = args[++i]; break;
            case var other when command == "ask" && !other.StartsWith("--"):
                words.Add(other); break;
            default:
                throw new ArgumentException($"Argumento no válido: {args[i]}\n{Usage}");
        }
    }

    using var pineconeHttp = PineconeHttp.CreateClient(pinecone);
    var retriever = new PineconeRetriever(pineconeHttp, pinecone);
    var ns = pinecone.Namespace.Length == 0 ? "(default)" : pinecone.Namespace;

    // ---------------------------------------------------------------- eval
    if (command == "eval")
    {
        questionsPath ??= Path.Combine(AppContext.BaseDirectory, "eval", "questions.json");
        if (!File.Exists(questionsPath))
            throw new ArgumentException($"No existe el archivo de preguntas: {questionsPath}");
        var questions = EvalQuestionLoader.Load(questionsPath);

        Console.WriteLine($"Índice: {pinecone.IndexName} (namespace: {ns})");
        Console.WriteLine($"Preguntas: {questions.Count} de {questionsPath}\n");
        var results = await new RetrievalEvaluator(retriever).RunAsync(questions, chat.TopK);
        Console.WriteLine(EvalReport.Render(results, chat.TopK));
        return results.All(r => r.IsHit) ? 0 : 2;
    }

    // ------------------------------------------------------- chat y ask
    var openai = config.GetSection("OpenAI").Get<OpenAIOptions>() ?? new OpenAIOptions();
    if (string.IsNullOrWhiteSpace(openai.ApiKey))
        openai.ApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? "";
    if (string.IsNullOrWhiteSpace(openai.ApiKey))
        throw new ArgumentException("Falta la API key de OpenAI: ponla en src/RagBot.Cli/appsettings.Local.json (OpenAI:ApiKey).");

    var promptPath = Path.Combine(AppContext.BaseDirectory, config["Chat:SystemPromptPath"] ?? "prompts/system.es.md");
    if (!File.Exists(promptPath))
        throw new ArgumentException($"No existe el archivo de reglas del bot: {promptPath}");
    var rules = File.ReadAllText(promptPath);

    using var openaiHttp = OpenAIHttp.CreateClient(openai);
    var chatModel = new OpenAIChatModel(openaiHttp, openai);
    var service = new ChatService(retriever, chatModel, rules, chat, new LanguageDetector(chatModel));
    var info = $"({pinecone.IndexName}/{ns} · modelo {openai.Model} · top {chat.TopK} · mínimo {F(chat.MinScore)})";

    if (command == "chat")
    {
        var banner = $"ragCustomerBot {info}\nEscribe tu pregunta y pulsa Enter.  /debug = ver detalle   /nuevo = olvidar la conversación   /salir = terminar";
        await new ChatLoop(service, rules, chat, Console.Out, Console.ReadLine).RunAsync(debug, banner);
        return 0;
    }

    var question = string.Join(" ", words).Trim();
    if (question.Length == 0)
        throw new ArgumentException($"Falta la pregunta.\n{Usage}");

    Console.WriteLine($"Pregunta: {question}");
    Console.WriteLine($"{info}\n");
    var answer = await service.AskAsync(question);
    AnswerPrinter.Write(Console.Out, answer, question, rules, chat, debug);
    return 0;
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    return 1;
}
catch (InvalidDataException ex)
{
    Console.Error.WriteLine($"Error en el archivo de preguntas: {ex.Message}");
    return 1;
}
catch (PineconeException ex)
{
    Console.Error.WriteLine($"Error de Pinecone: {ex.Message}");
    return 3;
}
catch (OpenAIException ex)
{
    Console.Error.WriteLine($"Error de OpenAI: {ex.Message}");
    return 3;
}
catch (HttpRequestException ex)
{
    Console.Error.WriteLine($"Error de red: {ex.Message}");
    return 3;
}
