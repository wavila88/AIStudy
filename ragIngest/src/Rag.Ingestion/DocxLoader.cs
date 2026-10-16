using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Rag.Core;

namespace Rag.Ingestion;

public sealed class DocxLoader : IDocumentLoader
{
    private const string DocIdLabel = "ID de documento";
    private static readonly Regex SafeDocId = new("^[A-Za-z0-9_.-]+$", RegexOptions.Compiled);

    public ParsedDocument Load(string path)
    {
        using var doc = WordprocessingDocument.Open(path, false);
        var main = doc.MainDocumentPart;
        var body = main?.Document?.Body
            ?? throw new InvalidDocumentException(path, "El documento no tiene contenido.");
        var numbering = main!.NumberingDefinitionsPart?.Numbering;
        var styleNames = ReadStyleNames(main);

        string? title = null, docId = null, heading = null;
        var sections = new List<DocSection>();
        var blocks = new List<string>();
        var seenHeading = false;
        var counters = new Dictionary<int, int>();

        void CloseSection()
        {
            if (heading is not null && blocks.Count > 0)
                sections.Add(new DocSection(heading, blocks.ToArray()));
            blocks.Clear();
        }

        foreach (var element in body.ChildElements)
        {
            if (element is Paragraph para)
            {
                var styleId = para.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
                var kind = StyleKind(styleId, styleNames);
                var text = TextOf(para);

                if (kind == "title") { title = text; continue; }
                if (kind == "heading") { CloseSection(); heading = text; seenHeading = true; continue; }
                if (text.Length == 0) continue;

                heading ??= "Introducción";
                var numId = para.ParagraphProperties?.NumberingProperties?.NumberingId?.Val?.Value;
                if (numId is int id && id != 0)
                {
                    if (IsBullet(numbering, id))
                    {
                        blocks.Add("- " + text);
                    }
                    else
                    {
                        // Word numera por lista (numId) aunque algo se interponga entre los elementos.
                        counters[id] = counters.GetValueOrDefault(id) + 1;
                        blocks.Add($"{counters[id]}. {text}");
                    }
                }
                else
                {
                    blocks.Add(text);
                }
            }
            else if (element is Table table)
            {
                if (!seenHeading)
                {
                    var found = ReadDocId(table);
                    if (found is not null)
                    {
                        docId ??= found;
                        continue;
                    }
                }
                var cellText = string.Join(" ", table.Descendants<Paragraph>()
                    .Select(TextOf)
                    .Where(t => t.Length > 0));
                if (cellText.Length > 0)
                {
                    heading ??= "Introducción";
                    blocks.Add(cellText);
                }
            }
        }
        CloseSection();

        if (string.IsNullOrWhiteSpace(docId))
            throw new InvalidDocumentException(path, $"Falta '{DocIdLabel}' en la tabla inicial.");
        if (!SafeDocId.IsMatch(docId))
            throw new InvalidDocumentException(path,
                $"El ID de documento '{docId}' solo puede usar letras ASCII, números, '-', '_' y '.'.");

        var documentTitle = string.IsNullOrWhiteSpace(title) ? Path.GetFileNameWithoutExtension(path) : title;
        return new ParsedDocument(docId, documentTitle, path, sections);
    }

    // Texto visible del elemento: solo <w:t>; las tabulaciones y saltos son espacios.
    // Se ignoran el texto borrado con control de cambios y los códigos de campo.
    private static string TextOf(OpenXmlElement element)
    {
        var sb = new StringBuilder();
        foreach (var node in element.Descendants())
        {
            switch (node)
            {
                case Text t: sb.Append(t.Text); break;
                case TabChar: sb.Append(' '); break;
                case Break: sb.Append(' '); break;
            }
        }
        return sb.ToString().Trim();
    }

    // Word en otros idiomas traduce los styleId ("Ttulo2"), pero el nombre interno sigue siendo "heading 2".
    private static Dictionary<string, string> ReadStyleNames(MainDocumentPart main)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        var styles = main.StyleDefinitionsPart?.Styles;
        if (styles is null) return names;
        foreach (var style in styles.Elements<Style>())
        {
            var id = style.StyleId?.Value;
            var name = style.StyleName?.Val?.Value;
            if (id is not null && name is not null) names[id] = name;
        }
        return names;
    }

    private static string? StyleKind(string? styleId, Dictionary<string, string> names)
    {
        if (styleId is null) return null;
        var name = names.TryGetValue(styleId, out var n) ? n : styleId;
        var key = name.Replace(" ", "").ToLowerInvariant();
        return key switch
        {
            "title" => "title",
            "heading1" or "heading2" => "heading",
            _ => null
        };
    }

    private static string? ReadDocId(Table table)
    {
        foreach (var row in table.Elements<TableRow>())
        {
            var cells = row.Elements<TableCell>().ToList();
            if (cells.Count < 2) continue;
            var label = TextOf(cells[0]).TrimEnd(':').Trim();
            if (string.Equals(label, DocIdLabel, StringComparison.OrdinalIgnoreCase))
                return TextOf(cells[1]);
        }
        return null;
    }

    private static bool IsBullet(Numbering? numbering, int numId)
    {
        if (numbering is null) return false;
        var instance = numbering.Elements<NumberingInstance>().FirstOrDefault(n => n.NumberID?.Value == numId);
        var abstractId = instance?.AbstractNumId?.Val?.Value;
        if (abstractId is null) return false;
        var abstractNum = numbering.Elements<AbstractNum>().FirstOrDefault(a => a.AbstractNumberId?.Value == abstractId);
        var level = abstractNum?.Elements<Level>().FirstOrDefault(l => l.LevelIndex?.Value == 0);
        return level?.NumberingFormat?.Val?.Value == NumberFormatValues.Bullet;
    }
}
