using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RagMcp.Server;

var builder = Host.CreateApplicationBuilder(args);

// En stdio la salida estándar es el canal del protocolo: todo el log debe ir a stderr.
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly(typeof(BuscarGuiasTool).Assembly);

await builder.Build().RunAsync();
