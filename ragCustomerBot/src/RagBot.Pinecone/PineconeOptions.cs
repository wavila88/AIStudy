namespace RagBot.Pinecone;

public sealed class PineconeOptions
{
    public string ApiKey { get; set; } = "";
    public string IndexName { get; set; } = "";
    public string Namespace { get; set; } = "";
    public string EmbeddingModel { get; set; } = "llama-text-embed-v2";
    public int Dimension { get; set; } = 1024;
    public string ApiVersion { get; set; } = "2025-04";
    public string ControlPlaneUrl { get; set; } = "https://api.pinecone.io";
}
