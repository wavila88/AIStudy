namespace Rag.Core;

public sealed record DocSection(string Heading, IReadOnlyList<string> Blocks);

public sealed record ParsedDocument(
    string DocId,
    string Title,
    string SourcePath,
    IReadOnlyList<DocSection> Sections);
