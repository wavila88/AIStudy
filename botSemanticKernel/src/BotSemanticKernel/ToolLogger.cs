using Microsoft.SemanticKernel;

namespace BotSemanticKernel;

/// <summary>
/// Un "filtro" de Semantic Kernel: código que se ejecuta alrededor de cada herramienta que llama el modelo.
/// Aquí solo lo usamos para IMPRIMIR qué herramienta se llamó, con qué argumentos y qué devolvió.
/// Sirve para ver el ciclo: cliente -> modelo -> herramienta -> modelo -> respuesta.
/// </summary>
public sealed class ToolLogger : IFunctionInvocationFilter
{
    public bool Enabled { get; set; }

    public async Task OnFunctionInvocationAsync(
        FunctionInvocationContext context,
        Func<FunctionInvocationContext, Task> next)
    {
        if (Enabled)
        {
            var arguments = string.Join(", ", context.Arguments.Select(a => $"{a.Key}={a.Value}"));
            Print($"  [herramienta] {context.Function.Name}({arguments})");
        }

        await next(context);   // aquí se ejecuta de verdad el método de OrderPlugin

        if (Enabled)
            Print($"  [resultado]   {context.Result.ToString().ReplaceLineEndings(" | ")}");
    }

    private static void Print(string text)
    {
        var previous = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine(text);
        Console.ForegroundColor = previous;
    }
}
