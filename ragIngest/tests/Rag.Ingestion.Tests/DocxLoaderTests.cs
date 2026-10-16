using DocumentFormat.OpenXml.Wordprocessing;
using Rag.Core;
using Rag.Ingestion;

namespace Rag.Ingestion.Tests;

public class DocxLoaderTests
{
    private static ParsedDocument LoadFixture() =>
        new DocxLoader().Load(TestFiles.Fixture("01_rechazos_de_pago_con_tarjeta.docx"));

    [Fact]
    public void Reads_doc_id_and_title()
    {
        var doc = LoadFixture();
        Assert.Equal("KB-01", doc.DocId);
        Assert.Equal("Guía: Rechazos de pago con tarjeta", doc.Title);
    }

    [Fact]
    public void Splits_sections_on_headings()
    {
        var doc = LoadFixture();
        Assert.Equal(7, doc.Sections.Count);
        Assert.Equal("Resumen", doc.Sections[0].Heading);
        Assert.StartsWith("Lista de verificación", doc.Sections[1].Heading);
        Assert.Equal("Preguntas frecuentes", doc.Sections[^1].Heading);
    }

    [Fact]
    public void Renders_numbered_lists_with_their_numbers()
    {
        var list = LoadFixture().Sections[1].Blocks;
        Assert.Equal(8, list.Count);
        for (var i = 0; i < list.Count; i++)
            Assert.StartsWith($"{i + 1}. ", list[i]);
    }

    [Fact]
    public void Renders_bullets_and_note_tables_as_blocks()
    {
        var section = LoadFixture().Sections.Single(s => s.Heading.StartsWith("Información a pedir"));
        Assert.Contains("- Fecha y hora aproximada del intento.", section.Blocks);
        Assert.Contains(section.Blocks, b => b.StartsWith("Regla para el asistente:"));
    }

    [Fact]
    public void Leaves_the_metadata_table_out_of_the_sections()
    {
        var blocks = LoadFixture().Sections.SelectMany(s => s.Blocks);
        Assert.DoesNotContain(blocks, b => b.Contains("ID de documento"));
    }

    [Fact]
    public void Keeps_questions_and_answers_as_separate_blocks()
    {
        var faq = LoadFixture().Sections[^1].Blocks;
        Assert.Equal(12, faq.Count);
        Assert.StartsWith("P: ¿Por qué me rechazan la tarjeta si tengo saldo?", faq[0]);
        Assert.StartsWith("R: ", faq[1]);
    }

    [Fact]
    public void Throws_when_the_doc_id_is_missing()
    {
        using var temp = new TempDir();
        var path = TestDocx.CreateWithoutDocId(temp.Dir, "sin-id.docx");
        var ex = Assert.Throws<InvalidDocumentException>(() => new DocxLoader().Load(path));
        Assert.Contains("ID de documento", ex.Reason);
    }

    [Theory]
    [InlineData("KB 01")]
    [InlineData("KB-ñ1")]
    [InlineData("KB/01")]
    public void Throws_when_the_doc_id_is_not_safe_for_a_vector_id(string docId)
    {
        using var temp = new TempDir();
        var path = TestDocx.CreateWithDocId(temp.Dir, "raro.docx", docId);
        Assert.Throws<InvalidDocumentException>(() => new DocxLoader().Load(path));
    }

    [Fact]
    public void Numbering_keeps_counting_after_a_note_table_interrupts_the_list()
    {
        using var temp = new TempDir();
        var path = new DocxBuilder()
            .Meta("ID de documento", "KB-70")
            .Para("Heading2", "Pasos")
            .Numbered(1, "Primero").Numbered(1, "Segundo")
            .NoteTable("Nota: ojo con esto")
            .Numbered(1, "Tercero")
            .Save(temp.Dir, "lista.docx");

        var blocks = new DocxLoader().Load(path).Sections.Single().Blocks;

        Assert.Equal(new[] { "1. Primero", "2. Segundo", "Nota: ojo con esto", "3. Tercero" }, blocks);
    }

    [Fact]
    public void Numbering_with_numId_zero_means_no_list()
    {
        using var temp = new TempDir();
        var path = new DocxBuilder()
            .Meta("ID de documento", "KB-71")
            .Para("Heading2", "Texto")
            .Numbered(0, "Sin lista")
            .Save(temp.Dir, "cero.docx");

        var blocks = new DocxLoader().Load(path).Sections.Single().Blocks;

        Assert.Equal(new[] { "Sin lista" }, blocks);
    }

    [Fact]
    public void Text_keeps_tabs_as_spaces_and_drops_deleted_text()
    {
        using var temp = new TempDir();
        var paragraph = new Paragraph(
            new Run(new Text("Paso 1:") { Space = DocumentFormat.OpenXml.SpaceProcessingModeValues.Preserve }),
            new Run(new TabChar()),
            new Run(new Text("Llamar")),
            new DeletedRun(new Run(new DeletedText("borrado"))));
        var path = new DocxBuilder()
            .Meta("ID de documento", "KB-72")
            .Para("Heading2", "Texto")
            .Raw(paragraph)
            .Save(temp.Dir, "tab.docx");

        var blocks = new DocxLoader().Load(path).Sections.Single().Blocks;

        Assert.Equal(new[] { "Paso 1: Llamar" }, blocks);
    }

    [Fact]
    public void Recognises_headings_by_style_name_when_word_localizes_the_style_ids()
    {
        using var temp = new TempDir();
        var path = new DocxBuilder()
            .Style("Ttulo", "Title").Style("Ttulo2", "heading 2")
            .Para("Ttulo", "Guía en español")
            .Meta("ID de documento", "KB-73")
            .Para("Ttulo2", "Primera sección")
            .Para(null, "Contenido")
            .Save(temp.Dir, "es.docx");

        var doc = new DocxLoader().Load(path);

        Assert.Equal("Guía en español", doc.Title);
        var section = Assert.Single(doc.Sections);
        Assert.Equal("Primera sección", section.Heading);
        Assert.Equal(new[] { "Contenido" }, section.Blocks);
    }

    [Fact]
    public void Finds_the_metadata_table_even_after_a_subtitle_and_with_a_loose_label()
    {
        using var temp = new TempDir();
        var path = new DocxBuilder()
            .Para("Title", "Guía con subtítulo")
            .Para(null, "Versión 2 · octubre")
            .Meta("id de documento:", "KB-74")
            .Para("Heading2", "Sección")
            .Para(null, "Texto")
            .Save(temp.Dir, "meta.docx");

        var doc = new DocxLoader().Load(path);

        Assert.Equal("KB-74", doc.DocId);
        Assert.DoesNotContain(doc.Sections.SelectMany(s => s.Blocks), b => b.Contains("KB-74"));
    }

    [Fact]
    public void Accepts_a_plain_ascii_doc_id()
    {
        using var temp = new TempDir();
        var path = TestDocx.CreateWithDocId(temp.Dir, "ok.docx", "KB-77");
        var doc = new DocxLoader().Load(path);
        Assert.Equal("KB-77", doc.DocId);
        Assert.Single(doc.Sections);
    }
}
