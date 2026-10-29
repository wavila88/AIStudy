using System.ComponentModel;
using ModelContextProtocol.Server;

namespace RagMcp.Server;

[McpServerToolType]
public static class BuscarGuiasTool
{
    [McpServerTool(Name = "buscar_guias")]
    [Description("Busca en las guías de servicio al cliente del Banco William la información relacionada con el problema del cliente.")]
    public static string BuscarGuias(
        [Description("El problema o la pregunta del cliente, tal como lo escribió.")] string pregunta)
        => $"Esqueleto Banco William: recibí tu pregunta \"{pregunta}\". La búsqueda real llega en la Fase 3.";
}
