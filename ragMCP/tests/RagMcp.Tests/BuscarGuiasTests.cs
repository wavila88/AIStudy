using ModelContextProtocol.Protocol;
using RagMcp.Core;

namespace RagMcp.Tests;

public class BuscarGuiasTests
{
    private static readonly RetrievedChunk Primero = new("KB-01-s2", 0.8123, "KB-01", "Lista de verificación", "guide", "Guía > Lista\n1. Límites");
    private static readonly RetrievedChunk Segundo = new("KB-03-q1", 0.5, "KB-03", "Preguntas frecuentes", "faq", "P: ...\nR: ...");

    private static async Task<string> BuscarAsync(TestServer server, string pregunta)
    {
        var result = await server.Client.CallToolAsync(
            "buscar_guias", new Dictionary<string, object?> { ["pregunta"] = pregunta });
        return Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
    }

    [Fact]
    public async Task Devuelve_id_puntuacion_y_texto_de_cada_fragmento_en_orden()
    {
        await using var server = await TestServer.StartAsync(new FakeRetriever(Primero, Segundo));

        var text = await BuscarAsync(server, "tengo un problema para hacer pagos con la tarjeta");

        Assert.Contains("[KB-01-s2]", text);
        Assert.Contains("0.8123", text);
        Assert.Contains("Guía > Lista\n1. Límites", text);
        Assert.Contains("[KB-03-q1]", text);
        Assert.Contains("0.5000", text);
        Assert.Contains("P: ...\nR: ...", text);
        Assert.True(text.IndexOf("[KB-01-s2]") < text.IndexOf("[KB-03-q1]"));
    }

    [Fact]
    public async Task Consulta_con_la_pregunta_y_el_topk_configurado()
    {
        var retriever = new FakeRetriever(Primero);
        var options = new RagMcpOptions { Search = { TopK = 7 } };
        await using var server = await TestServer.StartAsync(retriever, options);

        await BuscarAsync(server, "no me deja pagar");

        Assert.Equal("no me deja pagar", retriever.LastQuery);
        Assert.Equal(7, retriever.LastTopK);
    }
}
