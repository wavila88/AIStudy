using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Rag.Ingestion.Tests;

public static class TestFiles
{
    public static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
}

public sealed class TempDir : IDisposable
{
    public string Dir { get; } = Path.Combine(Path.GetTempPath(), "ragingest-" + Guid.NewGuid().ToString("N"));

    public TempDir() => Directory.CreateDirectory(Dir);

    public string Combine(string name) => Path.Combine(Dir, name);

    public void Dispose()
    {
        try { Directory.Delete(Dir, true); } catch { /* limpieza best-effort */ }
    }
}

// Constructor fluido para armar .docx de prueba a medida.
public sealed class DocxBuilder
{
    private readonly List<DocumentFormat.OpenXml.OpenXmlElement> _body = new();
    private readonly List<(string Id, string Name)> _styles = new();
    private bool _numbering;

    public DocxBuilder Style(string id, string name) { _styles.Add((id, name)); return this; }

    public DocxBuilder Para(string? style, string text)
    {
        var p = new Paragraph(new Run(new Text(text)));
        if (style is not null) p.ParagraphProperties = new ParagraphProperties(new ParagraphStyleId { Val = style });
        _body.Add(p);
        return this;
    }

    public DocxBuilder Raw(Paragraph paragraph) { _body.Add(paragraph); return this; }

    // Párrafo de lista numerada (decimal) que usa el numId indicado.
    public DocxBuilder Numbered(int numId, string text)
    {
        _numbering = true;
        _body.Add(new Paragraph(
            new ParagraphProperties(new NumberingProperties(
                new NumberingLevelReference { Val = 0 }, new NumberingId { Val = numId })),
            new Run(new Text(text))));
        return this;
    }

    public DocxBuilder NoteTable(string text)
    {
        _body.Add(new Table(new TableRow(new TableCell(new Paragraph(new Run(new Text(text)))))));
        return this;
    }

    public DocxBuilder Meta(string label, string value)
    {
        _body.Add(new Table(new TableRow(
            new TableCell(new Paragraph(new Run(new Text(label)))),
            new TableCell(new Paragraph(new Run(new Text(value)))))));
        return this;
    }

    public string Save(string directory, string fileName)
    {
        var path = Path.Combine(directory, fileName);
        using (var doc = WordprocessingDocument.Create(path, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new Document(new Body(_body.Select(e => e.CloneNode(true))));

            if (_styles.Count > 0)
            {
                var stylesPart = main.AddNewPart<StyleDefinitionsPart>();
                stylesPart.Styles = new Styles(_styles.Select(s => new Style(new StyleName { Val = s.Name })
                {
                    Type = StyleValues.Paragraph,
                    StyleId = s.Id
                }));
            }

            if (_numbering)
            {
                var numberingPart = main.AddNewPart<NumberingDefinitionsPart>();
                numberingPart.Numbering = new Numbering(
                    new AbstractNum(new Level(new NumberingFormat { Val = NumberFormatValues.Decimal }) { LevelIndex = 0 })
                    { AbstractNumberId = 0 },
                    new NumberingInstance(new AbstractNumId { Val = 0 }) { NumberID = 1 });
            }
        }
        return path;
    }
}

public static class TestDocx
{
    public static string CreateWithDocId(string directory, string fileName, string docId)
        => Create(directory, fileName, docId);

    public static string CreateWithoutDocId(string directory, string fileName)
        => Create(directory, fileName, null);

    private static string Create(string directory, string fileName, string? docId)
    {
        var path = Path.Combine(directory, fileName);
        using (var doc = WordprocessingDocument.Create(path, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            var body = new Body(Para("Title", "Guía de prueba"));
            if (docId is not null)
            {
                body.Append(new Table(new TableRow(
                    new TableCell(Para(null, "ID de documento")),
                    new TableCell(Para(null, docId)))));
            }
            body.Append(Para("Heading2", "Sección"));
            body.Append(Para(null, new string('x', 120)));
            main.Document = new Document(body);
        }
        return path;
    }

    private static Paragraph Para(string? style, string text)
    {
        var p = new Paragraph(new Run(new Text(text)));
        if (style is not null) p.ParagraphProperties = new ParagraphProperties(new ParagraphStyleId { Val = style });
        return p;
    }
}
