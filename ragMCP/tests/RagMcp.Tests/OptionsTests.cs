using Microsoft.Extensions.Configuration;
using RagMcp.Core;

namespace RagMcp.Tests;

public class OptionsTests
{
    private static RagMcpOptions Load()
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();
        return config.Get<RagMcpOptions>()!;
    }

    [Fact]
    public void Pinecone_se_carga_desde_appsettings()
    {
        var pinecone = Load().Pinecone;

        Assert.Equal("customer-service-kb", pinecone.IndexName);
        Assert.Equal("kb", pinecone.Namespace);
        Assert.Equal("llama-text-embed-v2", pinecone.EmbeddingModel);
        Assert.Equal(1024, pinecone.Dimension);
    }

    [Fact]
    public void Busqueda_se_carga_desde_appsettings()
    {
        var search = Load().Search;

        Assert.Equal(5, search.TopK);
        Assert.Equal(0.30, search.MinScore);
    }
}
