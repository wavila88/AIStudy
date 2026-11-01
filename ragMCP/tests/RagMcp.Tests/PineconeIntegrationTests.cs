using ModelContextProtocol.Protocol;
using RagMcp.Pinecone;
using Xunit.Abstractions;

namespace RagMcp.Tests;

// Llaman al Pinecone real: necesitan la clave. Para excluirlas: dotnet test --filter "Category!=Integration"
[Trait("Category", "Integration")]
public class PineconeIntegrationTests(ITestOutputHelper output)
{
    private const string Pregunta = "tengo un problema para hacer pagos con la tarjeta";

    [RequiereClavePineconeFact]
    public async Task Buscar_guias_con_pinecone_real_devuelve_fragmentos_con_su_id()
    {
        var options = IntegrationConfig.Load();
        var retriever = new PineconeRetriever(PineconeHttp.CreateClient(options.Pinecone), options.Pinecone);

        // 1) Directo contra Pinecone: si falla, aquí se ve el error real (401, índice inexistente, red...).
        //    El MCP lo escondería detrás de "An error occurred invoking 'buscar_guias'".
        var chunks = await retriever.SearchAsync(Pregunta, options.Search.TopK);
        output.WriteLine($"Pinecone devolvió {chunks.Count} fragmentos para: {Pregunta}");
        foreach (var c in chunks)
            output.WriteLine($"  {c.Id}  {c.Score:0.0000}  {c.DocId} / {c.Section}");
        Assert.NotEmpty(chunks);

        // 2) Por el servidor MCP completo: es lo que recibiría Claude.
        await using var server = await TestServer.StartAsync(retriever, options);
        var result = await server.Client.CallToolAsync(
            "buscar_guias", new Dictionary<string, object?> { ["pregunta"] = Pregunta });

        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        output.WriteLine("--- Respuesta de la herramienta ---");
        output.WriteLine(text);

        Assert.True(result.IsError != true, text);
        Assert.Matches(@"\[[^\]]+\] \(puntuación \d\.\d{4}\)", text);
    }
}
