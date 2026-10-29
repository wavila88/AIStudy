using System.IO.Pipelines;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using RagMcp.Server;

namespace RagMcp.Tests;

// Levanta el servidor MCP en memoria (tuberías en lugar de stdio) y lo consulta con un cliente MCP real.
public class ServerSmokeTests : IAsyncLifetime
{
    private IHost _host = null!;
    private McpClient _client = null!;

    public async Task InitializeAsync()
    {
        var clientToServer = new Pipe();
        var serverToClient = new Pipe();

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddMcpServer()
            .WithStreamServerTransport(clientToServer.Reader.AsStream(), serverToClient.Writer.AsStream())
            .WithToolsFromAssembly(typeof(BuscarGuiasTool).Assembly);
        _host = builder.Build();
        await _host.StartAsync();

        _client = await McpClient.CreateAsync(
            new StreamClientTransport(clientToServer.Writer.AsStream(), serverToClient.Reader.AsStream()));
    }

    public async Task DisposeAsync()
    {
        await _client.DisposeAsync();
        await _host.StopAsync();
        _host.Dispose();
    }

    [Fact]
    public async Task Expone_la_herramienta_buscar_guias()
    {
        var tools = await _client.ListToolsAsync();

        Assert.Contains(tools, t => t.Name == "buscar_guias");
    }

    [Fact]
    public async Task Buscar_guias_devuelve_la_respuesta_del_esqueleto()
    {
        var result = await _client.CallToolAsync(
            "buscar_guias",
            new Dictionary<string, object?> { ["pregunta"] = "tengo un problema para hacer pagos con la tarjeta" });

        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Contains("Esqueleto", text);
        Assert.Contains("tengo un problema para hacer pagos con la tarjeta", text);
    }
}
