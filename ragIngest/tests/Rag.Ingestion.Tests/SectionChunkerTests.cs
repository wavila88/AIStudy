using Rag.Core;
using Rag.Ingestion;

namespace Rag.Ingestion.Tests;

public class SectionChunkerTests
{
    private static readonly SectionChunker Chunker = new();

    private static ParsedDocument Doc(params DocSection[] sections) =>
        new("KB-99", "Guía de prueba", "x.docx", sections);

    private static DocSection Section(string heading, params string[] blocks) => new(heading, blocks);

    private static string Body(Chunk c) => c.Text[(c.Text.IndexOf('\n') + 1)..];

    [Fact]
    public void Splits_the_real_guide_into_twelve_chunks()
    {
        var doc = new DocxLoader().Load(TestFiles.Fixture("01_rechazos_de_pago_con_tarjeta.docx"));
        var chunks = Chunker.CreateChunks(doc);

        Assert.Equal(12, chunks.Count);
        Assert.Equal(6, chunks.Count(c => c.Type == ChunkType.Guide));
        Assert.Equal(6, chunks.Count(c => c.Type == ChunkType.Faq));
        Assert.Equal(Enumerable.Range(0, 12), chunks.Select(c => c.ChunkIndex));
        Assert.Equal(
            new[] { "KB-01-s1", "KB-01-s2", "KB-01-s3", "KB-01-s4", "KB-01-s5", "KB-01-s6" },
            chunks.Where(c => c.Type == ChunkType.Guide).Select(c => c.Id));
        Assert.Equal(
            new[] { "KB-01-q1", "KB-01-q2", "KB-01-q3", "KB-01-q4", "KB-01-q5", "KB-01-q6" },
            chunks.Where(c => c.Type == ChunkType.Faq).Select(c => c.Id));
    }

    [Fact]
    public void Keeps_the_eight_step_checklist_in_a_single_chunk()
    {
        var doc = new DocxLoader().Load(TestFiles.Fixture("01_rechazos_de_pago_con_tarjeta.docx"));
        var checklist = Chunker.CreateChunks(doc).Single(c => c.Section.StartsWith("Lista de verificación"));

        for (var i = 1; i <= 8; i++) Assert.Contains($"{i}. ", checklist.Text);
        Assert.StartsWith(
            "Guía: Rechazos de pago con tarjeta > Lista de verificación (en este orden)\n1. ",
            checklist.Text);
    }

    [Fact]
    public void Every_chunk_has_the_context_prefix_and_none_contains_the_metadata_table()
    {
        var doc = new DocxLoader().Load(TestFiles.Fixture("01_rechazos_de_pago_con_tarjeta.docx"));
        foreach (var chunk in Chunker.CreateChunks(doc))
        {
            Assert.StartsWith($"Guía: Rechazos de pago con tarjeta > {chunk.Section}\n", chunk.Text);
            Assert.DoesNotContain("ID de documento", chunk.Text);
        }
    }

    [Fact]
    public void Pairs_each_question_with_its_answer()
    {
        var chunks = Chunker.CreateChunks(Doc(Section("Preguntas frecuentes", "P: a", "R: b", "P: c", "R: d")));

        Assert.Equal(2, chunks.Count);
        Assert.All(chunks, c => Assert.Equal(ChunkType.Faq, c.Type));
        Assert.Equal("KB-99-q1", chunks[0].Id);
        Assert.Equal("KB-99-q2", chunks[1].Id);
        Assert.Equal("Guía de prueba > Preguntas frecuentes\nP: a\nR: b", chunks[0].Text);
    }

    [Fact]
    public void Blocks_before_the_first_question_become_a_guide_chunk()
    {
        var intro = new string('i', 150);
        var chunks = Chunker.CreateChunks(Doc(Section("Preguntas frecuentes", intro, "P: a", "R: b")));

        Assert.Equal(2, chunks.Count);
        Assert.Equal(ChunkType.Guide, chunks[0].Type);
        Assert.Equal("KB-99-s1", chunks[0].Id);
        Assert.Equal(ChunkType.Faq, chunks[1].Type);
    }

    [Fact]
    public void Splits_a_long_section_by_block_with_one_block_of_overlap()
    {
        var blocks = Enumerable.Range(0, 30).Select(i => $"{i:D2}" + new string('x', 198)).ToArray();
        var chunks = Chunker.CreateChunks(Doc(Section("Larga", blocks)));

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, c => Assert.True(Body(c).Length <= SectionChunker.MaxChars));

        var covered = chunks.SelectMany(c => Body(c).Split('\n')).ToHashSet();
        Assert.All(blocks, b => Assert.Contains(b, covered));

        for (var i = 0; i < chunks.Count - 1; i++)
        {
            var current = Body(chunks[i]).Split('\n');
            var next = Body(chunks[i + 1]).Split('\n');
            Assert.Equal(current[^1], next[0]);
        }
    }

    [Fact]
    public void Never_cuts_a_single_oversized_block()
    {
        var huge = new string('y', 2500);
        var chunks = Chunker.CreateChunks(Doc(Section("Enorme", huge)));

        var chunk = Assert.Single(chunks);
        Assert.Equal(huge, Body(chunk));
    }

    [Fact]
    public void Merges_a_very_short_section_into_the_previous_guide_chunk()
    {
        var chunks = Chunker.CreateChunks(Doc(
            Section("Larga", new string('a', 300)),
            Section("Corta", "ok")));

        var chunk = Assert.Single(chunks);
        Assert.Contains("Corta:\nok", chunk.Text);
    }

    [Fact]
    public void Keeps_a_short_first_section_because_there_is_nothing_to_merge_into()
    {
        var chunks = Chunker.CreateChunks(Doc(Section("Corta", "ok")));

        var chunk = Assert.Single(chunks);
        Assert.Equal("Corta", chunk.Section);
    }

    [Fact]
    public void Skips_sections_without_content()
    {
        var chunks = Chunker.CreateChunks(Doc(Section("Vacía"), Section("Con texto", new string('a', 120))));

        var chunk = Assert.Single(chunks);
        Assert.Equal("Con texto", chunk.Section);
    }
}
