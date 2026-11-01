using System.IO.Pipelines;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using RagMcp.Core;
using RagMcp.Server;

namespace RagMcp.Tests;

/// <summary>Retriever de mentira: devuelve los chunks dados y recuerda con qué lo llamaron.</summary>
public sealed class FakeRetriever(params RetrievedChunk[] chunks) : IRetriever
{
    public string? LastQuery { get; private set; }
    public int? LastTopK { get; private set; }

    public Task<IReadOnlyList<RetrievedChunk>> SearchAsync(string query, int topK, CancellationToken ct = default)
    {
        LastQuery = query;
        LastTopK = topK;
        return Task.FromResult<IReadOnlyList<RetrievedChunk>>(chunks);
    }
}

/// <summary>Levanta el servidor MCP en memoria (tuberías en lugar de stdio) con un cliente MCP real conectado.</summary>
public sealed class TestServer : IAsyncDisposable
{
    private readonly IHost _host;
    public McpClient Client { get; }

    private TestServer(IHost host, McpClient client) => (_host, Client) = (host, client);

    public static async Task<TestServer> StartAsync(IRetriever retriever, RagMcpOptions? options = null)
    {
        var clientToServer = new Pipe();
        var serverToClient = new Pipe();

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton(retriever);
        builder.Services.AddSingleton(options ?? new RagMcpOptions());
        builder.Services.AddMcpServer()
            .WithStreamServerTransport(clientToServer.Reader.AsStream(), serverToClient.Writer.AsStream())
            .WithToolsFromAssembly(typeof(BuscarGuiasTool).Assembly);
        var host = builder.Build();
        await host.StartAsync();

        var client = await McpClient.CreateAsync(
            new StreamClientTransport(clientToServer.Writer.AsStream(), serverToClient.Reader.AsStream()));
        return new TestServer(host, client);
    }

    public async ValueTask DisposeAsync()
    {
        await Client.DisposeAsync();
        await _host.StopAsync();
        _host.Dispose();
    }
}
