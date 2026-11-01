using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RagMcp.Core;
using RagMcp.Pinecone;
using RagMcp.Server;

// Claude Code lanza el proceso desde su propia carpeta: la configuración se busca junto al ejecutable, no en el cwd.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);

// En stdio la salida estándar es el canal del protocolo: todo el log debe ir a stderr.
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

var options = builder.Configuration.Get<RagMcpOptions>() ?? new RagMcpOptions();
builder.Services.AddSingleton(options);
builder.Services.AddSingleton<IRetriever>(_ =>
    new PineconeRetriever(PineconeHttp.CreateClient(options.Pinecone), options.Pinecone));

builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly(typeof(BuscarGuiasTool).Assembly);

await builder.Build().RunAsync();
