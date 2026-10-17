using System.Text;
using BotSemanticKernel;
using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

Console.OutputEncoding = Encoding.UTF8;
try { Console.InputEncoding = Encoding.UTF8; } catch (IOException) { /* entrada redirigida */ }

// ───────────────────────── 1. Configuración ─────────────────────────
// La clave de OpenAI va en appsettings.Local.json (ignorado por git) o en la variable OPENAI_API_KEY.
var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true)
    .AddJsonFile("appsettings.Local.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

var apiKey = config["OpenAI:ApiKey"];
if (string.IsNullOrWhiteSpace(apiKey)) apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.Error.WriteLine("Falta la API key de OpenAI. Cópiala en src/BotSemanticKernel/appsettings.Local.json (mira appsettings.Local.example.json).");
    return 1;
}
var model = config["OpenAI:Model"] ?? "gpt-4o-mini";

// ───────────────────────── 2. El Kernel ─────────────────────────
// El Kernel es el "centro" de Semantic Kernel: junta el modelo de chat y los plugins (herramientas).
var order = new Order();

var builder = Kernel.CreateBuilder();
builder.AddOpenAIChatCompletion(modelId: model, apiKey: apiKey);   // 2.1 conecta con OpenAI
var kernel = builder.Build();

kernel.Plugins.AddFromObject(new OrderPlugin(order), "Pedido");    // 2.2 registra las herramientas

var toolLog = new ToolLogger { Enabled = args.Contains("--debug") };
kernel.FunctionInvocationFilters.Add(toolLog);                      // 2.3 (opcional) imprime las herramientas llamadas

// ───────────────────────── 3. Memoria de la conversación ─────────────────────────
// ChatHistory es la lista de mensajes (system / user / assistant / herramientas).
// Se envía COMPLETA al modelo en cada turno: así "recuerda" lo que se habló.
var chat = kernel.GetRequiredService<IChatCompletionService>();
var history = new ChatHistory(SystemPrompt.Text);

// Auto = el modelo decide solo cuándo llamar a una herramienta y Semantic Kernel la ejecuta.
var settings = new OpenAIPromptExecutionSettings { FunctionChoiceBehavior = FunctionChoiceBehavior.Auto() };

// ───────────────────────── 4. Bucle de chat ─────────────────────────
Console.WriteLine($"Burger Kernel · modelo {model}");
Console.WriteLine("Escribe tu pedido.  /resumen = ver pedido   /nuevo = empezar de cero   /debug = ver herramientas   /salir = terminar");

while (true)
{
    Console.Write("\nTú> ");
    var line = Console.ReadLine();
    if (line is null) break;                                  // la entrada se cerró

    var text = line.Trim();
    if (text.Length == 0) continue;

    switch (text.ToLowerInvariant())
    {
        case "/salir" or "salir" or "exit":
            return 0;
        case "/resumen":                                      // sin modelo: lo imprime el código directamente
            Console.WriteLine(order.Summary());
            continue;
        case "/nuevo":
            order.Reset();
            history = new ChatHistory(SystemPrompt.Text);
            Console.WriteLine("Pedido y conversación reiniciados.");
            continue;
        case "/debug":
            toolLog.Enabled = !toolLog.Enabled;
            Console.WriteLine($"Modo debug: {(toolLog.Enabled ? "activado" : "desactivado")}");
            continue;
    }

    var countBefore = history.Count;
    try
    {
        history.AddUserMessage(text);                                                  // 4.1 guarda lo que dijo el cliente
        var reply = await chat.GetChatMessageContentAsync(history, settings, kernel);  // 4.2 modelo (+ herramientas si hace falta)
        history.AddAssistantMessage(reply.Content ?? "");                              // 4.3 guarda lo que respondió el bot
        Console.WriteLine($"\n{reply.Content}");
    }
    catch (Exception ex)
    {
        // Si algo falla, se deshace este turno para que la memoria no quede a medias.
        while (history.Count > countBefore) history.RemoveAt(history.Count - 1);
        Console.WriteLine($"Error: {ex.Message}");
    }
}

return 0;
