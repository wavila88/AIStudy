namespace RagMcp.Core;

public class RagMcpOptions
{
    public PineconeOptions Pinecone { get; set; } = new();
    public SearchOptions Search { get; set; } = new();
}

public class PineconeOptions
{
    public string? ApiKey { get; set; }
    public string IndexName { get; set; } = "";
    public string Namespace { get; set; } = "";
    public string EmbeddingModel { get; set; } = "";
    public int Dimension { get; set; }
}

public class SearchOptions
{
    public int TopK { get; set; }
    public double MinScore { get; set; }
}
