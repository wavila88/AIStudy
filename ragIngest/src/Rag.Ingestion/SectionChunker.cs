using Rag.Core;

namespace Rag.Ingestion;

public sealed class SectionChunker : IChunker
{
    public const int MaxChars = 2000;
    public const int MinChars = 80;

    private const string QuestionPrefix = "P: ";

    private sealed class Draft(string section, ChunkType type, string body)
    {
        public string Section { get; } = section;
        public ChunkType Type { get; } = type;
        public string Body { get; set; } = body;
    }

    public IReadOnlyList<Chunk> CreateChunks(ParsedDocument document)
    {
        var drafts = new List<Draft>();
        foreach (var section in document.Sections)
        {
            if (section.Blocks.Count == 0) continue;

            var firstQuestion = IndexOfQuestion(section.Blocks);
            var guideBlocks = firstQuestion < 0 ? section.Blocks : section.Blocks.Take(firstQuestion).ToList();
            if (guideBlocks.Count > 0) AddGuide(drafts, section.Heading, guideBlocks);
            if (firstQuestion >= 0) AddFaq(drafts, section.Heading, section.Blocks.Skip(firstQuestion).ToList());
        }

        var chunks = new List<Chunk>();
        int guides = 0, questions = 0;
        foreach (var draft in drafts)
        {
            var isFaq = draft.Type == ChunkType.Faq;
            var id = isFaq ? $"{document.DocId}-q{++questions}" : $"{document.DocId}-s{++guides}";
            var text = $"{document.Title} > {draft.Section}\n{draft.Body}";
            chunks.Add(new Chunk(id, document.DocId, document.Title, draft.Section, draft.Type, chunks.Count, text));
        }
        return chunks;
    }

    private static int IndexOfQuestion(IReadOnlyList<string> blocks)
    {
        for (var i = 0; i < blocks.Count; i++)
            if (blocks[i].StartsWith(QuestionPrefix, StringComparison.Ordinal)) return i;
        return -1;
    }

    private static void AddGuide(List<Draft> drafts, string heading, IReadOnlyList<string> blocks)
    {
        foreach (var piece in Split(blocks))
        {
            var previous = drafts.LastOrDefault(d => d.Type == ChunkType.Guide);
            if (piece.Length < MinChars && previous is not null)
                previous.Body += $"\n\n{heading}:\n{piece}";
            else
                drafts.Add(new Draft(heading, ChunkType.Guide, piece));
        }
    }

    private static void AddFaq(List<Draft> drafts, string heading, IReadOnlyList<string> blocks)
    {
        string? question = null;
        var answer = new List<string>();

        void Flush()
        {
            if (question is not null)
                drafts.Add(new Draft(heading, ChunkType.Faq, question + "\n" + string.Join("\n", answer)));
        }

        foreach (var block in blocks)
        {
            if (block.StartsWith(QuestionPrefix, StringComparison.Ordinal))
            {
                Flush();
                question = block;
                answer = new List<string>();
            }
            else
            {
                answer.Add(block);
            }
        }
        Flush();
    }

    // Agrupa bloques hasta MaxChars. Al abrir un chunk nuevo repite el último bloque del anterior
    // (solapamiento). Un bloque que por sí solo supera MaxChars se emite entero.
    private static List<string> Split(IReadOnlyList<string> blocks)
    {
        var result = new List<string>();
        var current = new List<string>();
        var length = 0;

        foreach (var block in blocks)
        {
            var added = block.Length + (current.Count > 0 ? 1 : 0);
            if (current.Count > 0 && length + added > MaxChars)
            {
                result.Add(string.Join("\n", current));
                var overlap = current[^1];
                current = new List<string>();
                length = 0;
                if (overlap.Length + 1 + block.Length <= MaxChars)
                {
                    current.Add(overlap);
                    length = overlap.Length;
                }
                added = block.Length + (current.Count > 0 ? 1 : 0);
            }
            current.Add(block);
            length += added;
        }

        if (current.Count > 0) result.Add(string.Join("\n", current));
        return result;
    }
}
