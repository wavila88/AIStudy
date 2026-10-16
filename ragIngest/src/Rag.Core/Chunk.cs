namespace Rag.Core;

public enum ChunkType { Guide, Faq }

public sealed record Chunk(
    string Id,
    string DocId,
    string DocTitle,
    string Section,
    ChunkType Type,
    int ChunkIndex,
    string Text);
