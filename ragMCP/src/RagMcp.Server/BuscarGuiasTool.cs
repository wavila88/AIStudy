using System.ComponentModel;
using System.Globalization;
using ModelContextProtocol.Server;
using RagMcp.Core;

namespace RagMcp.Server;

[McpServerToolType]
public static class BuscarGuiasTool
{
    [McpServerTool(Name = "buscar_guias")]
    [Description("Busca en las guías de servicio al cliente del Banco William la información relacionada con el problema del cliente. Devuelve los fragmentos más parecidos, cada uno con el id de su vector.")]
    public static async Task<string> BuscarGuias(
        [Description("El problema o la pregunta del cliente, tal como lo escribió.")] string pregunta,
        IRetriever retriever,
        RagMcpOptions options,
        CancellationToken ct)
    {
        var chunks = await retriever.SearchAsync(pregunta, options.Search.TopK, ct);

        return string.Join("\n\n", chunks.Select(c =>
            $"[{c.Id}] (puntuación {c.Score.ToString("0.0000", CultureInfo.InvariantCulture)})\n{c.Text}"));
    }
}
