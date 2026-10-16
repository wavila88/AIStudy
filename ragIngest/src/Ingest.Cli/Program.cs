using System.Text;
using Microsoft.Extensions.Configuration;
using Rag.Core;
using Rag.Ingestion;
using Rag.Pinecone;

Console.OutputEncoding = Encoding.UTF8;

try
{
    var arguments = IngestArguments.Parse(args);

    var config = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: true)
        .AddJsonFile("appsettings.Local.json", optional: true)   // local, ignorado por git: aquí va la API key
        .AddUserSecrets<Program>(optional: true)
        .AddEnvironmentVariables()
        .Build();

    var source = SourceResolver.Resolve(
        arguments.Source,
        config["Ingestion:Source"],
        Environment.GetEnvironmentVariable("INGEST_SOURCE"));

    var pinecone = config.GetSection("Pinecone").Get<PineconeOptions>() ?? new PineconeOptions();
    if (string.IsNullOrWhiteSpace(pinecone.ApiKey))
        pinecone.ApiKey = Environment.GetEnvironmentVariable("PINECONE_API_KEY") ?? "";
    if (string.IsNullOrWhiteSpace(pinecone.ApiKey))
        throw new IngestInputException(
            "Falta la API key de Pinecone. Defínela en la variable de entorno PINECONE_API_KEY " +
            "o con: dotnet user-secrets set \"Pinecone:ApiKey\" \"<clave>\" --project src/Ingest.Cli");
    if (string.IsNullOrWhiteSpace(pinecone.IndexName))
        throw new IngestInputException("Falta Pinecone:IndexName en appsettings.json.");

    using var http = PineconeHttp.CreateClient(pinecone);
    var service = new IngestionService(
        new DocxLoader(),
        new SectionChunker(),
        new PineconeEmbedder(http, pinecone),
        new PineconeVectorStore(http, pinecone));
    var command = new IngestCommand(service, Console.Out, Console.ReadLine);

    return await command.RunAsync(new IngestRunOptions(
        source, arguments.DryRun, arguments.AssumeYes, arguments.Show, pinecone.IndexName, pinecone.Namespace));
}
catch (IngestInputException ex)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    Console.Error.WriteLine(IngestArguments.Usage);
    return 1;
}
catch (PineconeException ex)
{
    Console.Error.WriteLine($"Error de Pinecone: {ex.Message}");
    return 3;
}
catch (HttpRequestException ex)
{
    Console.Error.WriteLine($"Error de red al hablar con Pinecone: {ex.Message}");
    return 3;
}
catch (Exception ex) when (ex is not OperationCanceledException)
{
    Console.Error.WriteLine($"Error inesperado: {ex.Message}");
    return 3;
}
