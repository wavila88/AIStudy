# Ingesta de guías a Pinecone — Plan de implementación

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Aplicación de consola en C# que lee guías `.docx`, las divide en chunks, muestra un resumen y, tras confirmación, las guarda como vectores en un índice de Pinecone.

**Architecture:** Cuatro proyectos con una responsabilidad cada uno: `Rag.Core` (modelos e interfaces), `Rag.Ingestion` (lector de `.docx`, chunker y orquestación), `Rag.Pinecone` (cliente REST de Pinecone: embeddings, listar, borrar, insertar) e `Ingest.Cli` (consola y configuración). Todo lo que habla con el exterior está detrás de una interfaz, así que el flujo completo se prueba con fakes.

**Tech Stack:** .NET 10, C#, DocumentFormat.OpenXml, xUnit, `HttpClient` contra la API REST de Pinecone (versión `2025-04`), Microsoft.Extensions.Configuration.

**Spec:** `docs/superpowers/specs/2026-10-14-ingestion-design.md`

## Global Constraints

- Solución en `C:\Nortal\AIStudy\ragIngest`; plataforma `net10.0`, `Nullable` y `ImplicitUsings` activados.
- Índice de Pinecone: `customer-service-kb`, serverless, métrica coseno, dimensión 1024, namespace `kb`.
- Embeddings: endpoint `POST https://api.pinecone.io/embed`, modelo `llama-text-embed-v2`, `input_type=passage`, `truncate=END`, `dimension=1024`. Lotes de 50 textos.
- Cabeceras en toda llamada a Pinecone: `Api-Key` y `X-Pinecone-API-Version: 2025-04`.
- La API key solo viene de `Pinecone:ApiKey` (user secrets) o de la variable de entorno `PINECONE_API_KEY`. Nunca en archivos del repositorio.
- Constantes de chunking: `MaxChars = 2000`, `MinChars = 80`, solapamiento de un bloque. Ids: `<DocId>-s<n>` para guías, `<DocId>-q<n>` para preguntas frecuentes.
- Nada se escribe en Pinecone sin confirmación: `--dry-run` no escribe, `--yes` omite la pregunta, por defecto pregunta `¿Continuar? [s/N]`.
- Se calculan los embeddings **antes** de borrar registros de un documento.
- Mensajes de la consola en español.
- Los pasos "Commit" solo se ejecutan si el usuario pidió commits. Si se hacen, el mensaje termina con la línea `Co-Authored-By: Claude`.

## Review Focus

- **Archivos de bloqueo de Word** (`~$01_...docx`) dentro de la carpeta de origen: deben ignorarse, no tratarse como guías (Task 6).
- **Dos archivos con el mismo `DocId`** (una guía copiada y pegada): el segundo se omite con aviso y no pisa al primero (Task 5).
- **`DocId` ausente o inseguro** (vacío, con espacios o caracteres no ASCII): el documento se rechaza con un mensaje claro (Task 2).
- **`DocId` que es prefijo de otro** (`KB` y `KB-01`): borrar `KB` no debe borrar registros de `KB-01` (Task 5).
- **Más de 100 ids para listar o más de 1.000 para borrar**: paginación y lotes correctos (Task 4).
- **Documento que no produce chunks, o embedder que devuelve menos vectores**: no se borra nada (Task 5).

---

## File Structure

```
ragIngest/
├─ Directory.Build.props
├─ .gitignore
├─ README.md
├─ RagIngest.sln
├─ scripts/search.ps1                       búsqueda manual para verificar
├─ src/
│  ├─ Rag.Core/
│  │    Chunk.cs                            ChunkType, Chunk
│  │    ParsedDocument.cs                   DocSection, ParsedDocument
│  │    Abstractions.cs                     IDocumentLoader, IChunker, IEmbedder, IVectorStore, VectorRecord
│  │    Exceptions.cs                       InvalidDocumentException, IngestInputException, IngestionException
│  ├─ Rag.Ingestion/
│  │    DocxLoader.cs
│  │    SectionChunker.cs
│  │    DocumentDiscovery.cs                ruta -> lista de .docx
│  │    SourceResolver.cs                   --source > appsettings > INGEST_SOURCE
│  │    IngestArguments.cs                  parseo de --source/--dry-run/--yes/--show
│  │    IngestionService.cs                 PlanAsync / ExecuteAsync
│  │    IngestCommand.cs                    resumen, confirmación y ejecución
│  ├─ Rag.Pinecone/
│  │    PineconeOptions.cs
│  │    PineconeHttp.cs                     cliente HTTP y manejo de errores, PineconeException
│  │    PineconeEmbedder.cs
│  │    PineconeVectorStore.cs
│  └─ Ingest.Cli/
│       Program.cs, appsettings.json
└─ tests/
   ├─ Rag.Ingestion.Tests/   (Fixtures/, TestSupport.cs, y un archivo de pruebas por clase)
   └─ Rag.Pinecone.Tests/    (StubHandler.cs, PineconeEmbedderTests.cs, PineconeVectorStoreTests.cs)
```

---

### Task 1: Solución, proyectos y `Rag.Core`

**Files:**
- Create: `Directory.Build.props`, `.gitignore`, `RagIngest.sln` y los 6 proyectos
- Create: `src/Rag.Core/Chunk.cs`, `ParsedDocument.cs`, `Abstractions.cs`, `Exceptions.cs`

**Interfaces:**
- Produces (usado por todas las tareas siguientes):
  - `enum ChunkType { Guide, Faq }`
  - `record Chunk(string Id, string DocId, string DocTitle, string Section, ChunkType Type, int ChunkIndex, string Text)`
  - `record DocSection(string Heading, IReadOnlyList<string> Blocks)`
  - `record ParsedDocument(string DocId, string Title, string SourcePath, IReadOnlyList<DocSection> Sections)`
  - `IDocumentLoader.Load(string path) : ParsedDocument`
  - `IChunker.CreateChunks(ParsedDocument) : IReadOnlyList<Chunk>`
  - `IEmbedder.EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default) : Task<IReadOnlyList<float[]>>`
  - `record VectorRecord(string Id, float[] Values, IReadOnlyDictionary<string, object> Metadata)`
  - `IVectorStore.ListIdsAsync(string prefix, CancellationToken ct = default) : Task<IReadOnlyList<string>>`, `DeleteAsync(IReadOnlyList<string> ids, CancellationToken ct = default)`, `UpsertAsync(IReadOnlyList<VectorRecord> records, CancellationToken ct = default)`
  - `InvalidDocumentException(string sourcePath, string reason)` con `SourcePath` y `Reason`; `IngestInputException(string message)`; `IngestionException(string docId, Exception inner)` con `DocId`

- [ ] **Step 1: Crear la solución y los proyectos**

Desde `C:\Nortal\AIStudy\ragIngest` (la carpeta ya existe con `docs/`):

```bash
cd /c/Nortal/AIStudy/ragIngest
dotnet new sln -n RagIngest --format sln
dotnet new classlib -n Rag.Core -o src/Rag.Core -f net10.0
dotnet new classlib -n Rag.Ingestion -o src/Rag.Ingestion -f net10.0
dotnet new classlib -n Rag.Pinecone -o src/Rag.Pinecone -f net10.0
dotnet new console -n Ingest.Cli -o src/Ingest.Cli -f net10.0
dotnet new xunit -n Rag.Ingestion.Tests -o tests/Rag.Ingestion.Tests -f net10.0
dotnet new xunit -n Rag.Pinecone.Tests -o tests/Rag.Pinecone.Tests -f net10.0
rm -f src/Rag.Core/Class1.cs src/Rag.Ingestion/Class1.cs src/Rag.Pinecone/Class1.cs tests/*/UnitTest1.cs
dotnet sln add src/*/*.csproj tests/*/*.csproj
dotnet add src/Rag.Ingestion reference src/Rag.Core/Rag.Core.csproj
dotnet add src/Rag.Pinecone reference src/Rag.Core/Rag.Core.csproj
dotnet add src/Ingest.Cli reference src/Rag.Ingestion/Rag.Ingestion.csproj src/Rag.Pinecone/Rag.Pinecone.csproj
dotnet add tests/Rag.Ingestion.Tests reference src/Rag.Ingestion/Rag.Ingestion.csproj
dotnet add tests/Rag.Pinecone.Tests reference src/Rag.Pinecone/Rag.Pinecone.csproj
dotnet add src/Rag.Ingestion package DocumentFormat.OpenXml
dotnet add tests/Rag.Ingestion.Tests package DocumentFormat.OpenXml
dotnet add src/Ingest.Cli package Microsoft.Extensions.Configuration.Json
dotnet add src/Ingest.Cli package Microsoft.Extensions.Configuration.EnvironmentVariables
dotnet add src/Ingest.Cli package Microsoft.Extensions.Configuration.UserSecrets
dotnet add src/Ingest.Cli package Microsoft.Extensions.Configuration.Binder
dotnet user-secrets init --project src/Ingest.Cli
```

- [ ] **Step 2: Crear `Directory.Build.props` y `.gitignore`**

`Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>
</Project>
```

`.gitignore`:

```
bin/
obj/
.vs/
*.user
appsettings.Local.json
```

- [ ] **Step 3: Crear `src/Rag.Core/Chunk.cs` y `ParsedDocument.cs`**

`Chunk.cs`:

```csharp
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
```

`ParsedDocument.cs`:

```csharp
namespace Rag.Core;

public sealed record DocSection(string Heading, IReadOnlyList<string> Blocks);

public sealed record ParsedDocument(
    string DocId,
    string Title,
    string SourcePath,
    IReadOnlyList<DocSection> Sections);
```

- [ ] **Step 4: Crear `src/Rag.Core/Abstractions.cs` y `Exceptions.cs`**

`Abstractions.cs`:

```csharp
namespace Rag.Core;

public interface IDocumentLoader
{
    ParsedDocument Load(string path);
}

public interface IChunker
{
    IReadOnlyList<Chunk> CreateChunks(ParsedDocument document);
}

public interface IEmbedder
{
    Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default);
}

public sealed record VectorRecord(string Id, float[] Values, IReadOnlyDictionary<string, object> Metadata);

public interface IVectorStore
{
    Task<IReadOnlyList<string>> ListIdsAsync(string prefix, CancellationToken ct = default);
    Task DeleteAsync(IReadOnlyList<string> ids, CancellationToken ct = default);
    Task UpsertAsync(IReadOnlyList<VectorRecord> records, CancellationToken ct = default);
}
```

`Exceptions.cs`:

```csharp
namespace Rag.Core;

public sealed class InvalidDocumentException(string sourcePath, string reason)
    : Exception($"{System.IO.Path.GetFileName(sourcePath)}: {reason}")
{
    public string SourcePath { get; } = sourcePath;
    public string Reason { get; } = reason;
}

public sealed class IngestInputException(string message) : Exception(message);

public sealed class IngestionException(string docId, Exception inner)
    : Exception($"Falló la ingesta de {docId}: {inner.Message}", inner)
{
    public string DocId { get; } = docId;
}
```

- [ ] **Step 5: Compilar la solución**

Run: `dotnet build RagIngest.sln`
Expected: `Build succeeded` con 0 errores.

- [ ] **Step 6: Checkpoint (solo si el usuario pidió commits)**

```bash
git init
git add -A
git commit -m "chore: scaffold solution and Rag.Core" -m "Co-Authored-By: Claude"
```

---

### Task 2: `DocxLoader`

**Files:**
- Create: `src/Rag.Ingestion/DocxLoader.cs`
- Create: `tests/Rag.Ingestion.Tests/TestSupport.cs`, `tests/Rag.Ingestion.Tests/DocxLoaderTests.cs`
- Copy: `tests/Rag.Ingestion.Tests/Fixtures/01_rechazos_de_pago_con_tarjeta.docx`
- Modify: `tests/Rag.Ingestion.Tests/Rag.Ingestion.Tests.csproj`

**Interfaces:**
- Consumes: `IDocumentLoader`, `ParsedDocument`, `DocSection`, `InvalidDocumentException` (Task 1)
- Produces: `DocxLoader : IDocumentLoader`. Convención de bloques: párrafo normal → texto; viñeta → `- texto`; lista numerada → `1. texto`, `2. texto`…; tabla de una celda (recuadro) → un solo bloque con su texto; pregunta frecuente → bloques `P: …` y `R: …` separados. Las secciones empiezan en `Heading1` o `Heading2`. La tabla inicial de metadatos no genera bloques. Produce también en tests: `TestFiles.Fixture(name)`, `TempDir` (`Dir`), `TestDocx.CreateWithDocId(dir, fileName, docId)`, `TestDocx.CreateWithoutDocId(dir, fileName)`.

- [ ] **Step 1: Copiar el fixture y registrarlo en el proyecto de pruebas**

```bash
mkdir -p tests/Rag.Ingestion.Tests/Fixtures
cp /c/Nortal/AIStudy/ragCustomerservice/01_rechazos_de_pago_con_tarjeta.docx tests/Rag.Ingestion.Tests/Fixtures/
```

En `tests/Rag.Ingestion.Tests/Rag.Ingestion.Tests.csproj`, antes de `</Project>`, añadir:

```xml
  <ItemGroup>
    <None Include="Fixtures\**\*.docx" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
```

- [ ] **Step 2: Crear `TestSupport.cs`**

```csharp
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
```

- [ ] **Step 3: Escribir las pruebas que fallan, `DocxLoaderTests.cs`**

```csharp
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
        Assert.StartsWith("P: ¿Por qué me rechazan si tengo saldo?", faq[0]);
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
    public void Accepts_a_plain_ascii_doc_id()
    {
        using var temp = new TempDir();
        var path = TestDocx.CreateWithDocId(temp.Dir, "ok.docx", "KB-77");
        var doc = new DocxLoader().Load(path);
        Assert.Equal("KB-77", doc.DocId);
        Assert.Single(doc.Sections);
    }
}
```

- [ ] **Step 4: Ejecutar para verificar que falla**

Run: `dotnet test tests/Rag.Ingestion.Tests --filter "FullyQualifiedName~DocxLoaderTests"`
Expected: FAIL, error de compilación `The type or namespace name 'DocxLoader' could not be found`.

- [ ] **Step 5: Implementar `src/Rag.Ingestion/DocxLoader.cs`**

```csharp
using System.Text.RegularExpressions;
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

        string? title = null, docId = null, heading = null;
        var sections = new List<DocSection>();
        var blocks = new List<string>();
        var metadataRead = false;
        int? listNumId = null;
        var listCount = 0;

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
                var style = para.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
                var text = para.InnerText.Trim();

                if (style == "Title") { title = text; listNumId = null; continue; }
                if (style is "Heading1" or "Heading2") { CloseSection(); heading = text; listNumId = null; continue; }
                if (text.Length == 0) { listNumId = null; continue; }

                heading ??= "Introducción";
                var numId = para.ParagraphProperties?.NumberingProperties?.NumberingId?.Val?.Value;
                if (numId is int id)
                {
                    if (IsBullet(numbering, id))
                    {
                        listNumId = null;
                        blocks.Add("- " + text);
                    }
                    else
                    {
                        listCount = listNumId == id ? listCount + 1 : 1;
                        listNumId = id;
                        blocks.Add($"{listCount}. {text}");
                    }
                }
                else
                {
                    listNumId = null;
                    blocks.Add(text);
                }
            }
            else if (element is Table table)
            {
                listNumId = null;
                if (!metadataRead && heading is null)
                {
                    docId = ReadDocId(table);
                    metadataRead = true;
                    continue;
                }
                var cellText = string.Join(" ", table.Descendants<Paragraph>()
                    .Select(p => p.InnerText.Trim())
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

    private static string? ReadDocId(Table table)
    {
        foreach (var row in table.Elements<TableRow>())
        {
            var cells = row.Elements<TableCell>().ToList();
            if (cells.Count >= 2 && cells[0].InnerText.Trim() == DocIdLabel)
                return cells[1].InnerText.Trim();
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
```

Nota: si la versión instalada de DocumentFormat.OpenXml expone alguna de estas propiedades con otro tipo (por ejemplo `Val.Value` ya no anulable), ajustar solo esa expresión para que compile; el comportamiento es el descrito arriba.

- [ ] **Step 6: Ejecutar para verificar que pasa**

Run: `dotnet test tests/Rag.Ingestion.Tests --filter "FullyQualifiedName~DocxLoaderTests"`
Expected: PASS, 11 pruebas (6 del fixture + 1 sin id + 3 theory + 1 ok).

- [ ] **Step 7: Checkpoint (solo si el usuario pidió commits)**

```bash
git add -A
git commit -m "feat: add DocxLoader" -m "Co-Authored-By: Claude"
```

---

### Task 3: `SectionChunker`

**Files:**
- Create: `src/Rag.Ingestion/SectionChunker.cs`
- Test: `tests/Rag.Ingestion.Tests/SectionChunkerTests.cs`

**Interfaces:**
- Consumes: `IChunker`, `Chunk`, `ChunkType`, `ParsedDocument`, `DocSection` (Task 1); `DocxLoader` (Task 2, solo en una prueba)
- Produces: `SectionChunker : IChunker` con `public const int MaxChars = 2000` y `public const int MinChars = 80`. El texto de cada chunk es `"<título> > <sección>\n<cuerpo>"`.

- [ ] **Step 1: Escribir las pruebas que fallan, `SectionChunkerTests.cs`**

```csharp
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
```

- [ ] **Step 2: Ejecutar para verificar que falla**

Run: `dotnet test tests/Rag.Ingestion.Tests --filter "FullyQualifiedName~SectionChunkerTests"`
Expected: FAIL, error de compilación `The type or namespace name 'SectionChunker' could not be found`.

- [ ] **Step 3: Implementar `src/Rag.Ingestion/SectionChunker.cs`**

```csharp
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
```

- [ ] **Step 4: Ejecutar para verificar que pasa**

Run: `dotnet test tests/Rag.Ingestion.Tests --filter "FullyQualifiedName~SectionChunkerTests"`
Expected: PASS, 10 pruebas.

- [ ] **Step 5: Checkpoint (solo si el usuario pidió commits)**

```bash
git add -A
git commit -m "feat: add SectionChunker" -m "Co-Authored-By: Claude"
```

---

### Task 4: Cliente REST de Pinecone (`Rag.Pinecone`)

**Files:**
- Create: `src/Rag.Pinecone/PineconeOptions.cs`, `PineconeHttp.cs`, `PineconeEmbedder.cs`, `PineconeVectorStore.cs`
- Create: `tests/Rag.Pinecone.Tests/StubHandler.cs`, `PineconeEmbedderTests.cs`, `PineconeVectorStoreTests.cs`

**Interfaces:**
- Consumes: `IEmbedder`, `IVectorStore`, `VectorRecord` (Task 1)
- Produces:
  - `PineconeOptions { string ApiKey; string IndexName; string Namespace = ""; string EmbeddingModel = "llama-text-embed-v2"; int Dimension = 1024; string ApiVersion = "2025-04"; string ControlPlaneUrl = "https://api.pinecone.io" }`
  - `PineconeHttp.CreateClient(PineconeOptions) : HttpClient` (público)
  - `PineconeException(int statusCode, string body)` con `StatusCode` y `Body`
  - `PineconeEmbedder(HttpClient, PineconeOptions) : IEmbedder` con `public const int BatchSize = 50`
  - `PineconeVectorStore(HttpClient, PineconeOptions) : IVectorStore` con `ListPageSize = 100`, `DeleteBatchSize = 1000`, `UpsertBatchSize = 100`

- [ ] **Step 1: Crear `StubHandler.cs` (soporte de pruebas)**

```csharp
using System.Net;
using System.Text;

namespace Rag.Pinecone.Tests;

public sealed class StubHandler(Func<HttpRequestMessage, string?, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<(HttpMethod Method, string Url, string? Body)> Requests { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
        Requests.Add((request.Method, request.RequestUri!.ToString(), body));
        return respond(request, body);
    }
}

public static class Json
{
    public static HttpResponseMessage Ok(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    public static HttpResponseMessage Error(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };
}
```

- [ ] **Step 2: Escribir las pruebas del embedder, `PineconeEmbedderTests.cs`**

```csharp
using System.Net;
using System.Text.Json;

namespace Rag.Pinecone.Tests;

public class PineconeEmbedderTests
{
    private static readonly PineconeOptions Options = new() { ApiKey = "k", IndexName = "idx" };

    // Responde un vector [longitud del texto] por cada input, para poder comprobar el orden.
    private static HttpResponseMessage EchoLengths(HttpRequestMessage req, string? body)
    {
        using var doc = JsonDocument.Parse(body!);
        var items = doc.RootElement.GetProperty("inputs").EnumerateArray()
            .Select(i => $"{{\"values\":[{i.GetProperty("text").GetString()!.Length}.0]}}");
        return Json.Ok($"{{\"data\":[{string.Join(",", items)}]}}");
    }

    [Fact]
    public async Task Embeds_in_batches_of_fifty_and_keeps_the_order()
    {
        var handler = new StubHandler(EchoLengths);
        var embedder = new PineconeEmbedder(new HttpClient(handler), Options);
        var texts = Enumerable.Range(1, 120).Select(i => new string('a', i)).ToList();

        var vectors = await embedder.EmbedAsync(texts);

        Assert.Equal(120, vectors.Count);
        for (var i = 0; i < texts.Count; i++) Assert.Equal(texts[i].Length, vectors[i][0]);

        var sizes = handler.Requests
            .Select(r => JsonDocument.Parse(r.Body!).RootElement.GetProperty("inputs").GetArrayLength())
            .ToList();
        Assert.Equal(new[] { 50, 50, 20 }, sizes);
    }

    [Fact]
    public async Task Sends_model_and_passage_parameters_to_the_embed_endpoint()
    {
        var handler = new StubHandler(EchoLengths);
        var embedder = new PineconeEmbedder(new HttpClient(handler), Options);

        await embedder.EmbedAsync(new[] { "hola" });

        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://api.pinecone.io/embed", request.Url);
        var root = JsonDocument.Parse(request.Body!).RootElement;
        Assert.Equal("llama-text-embed-v2", root.GetProperty("model").GetString());
        var parameters = root.GetProperty("parameters");
        Assert.Equal("passage", parameters.GetProperty("input_type").GetString());
        Assert.Equal("END", parameters.GetProperty("truncate").GetString());
        Assert.Equal(1024, parameters.GetProperty("dimension").GetInt32());
    }

    [Fact]
    public async Task Returns_nothing_and_makes_no_request_for_an_empty_list()
    {
        var handler = new StubHandler(EchoLengths);
        var embedder = new PineconeEmbedder(new HttpClient(handler), Options);

        var vectors = await embedder.EmbedAsync(Array.Empty<string>());

        Assert.Empty(vectors);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Throws_a_pinecone_exception_with_status_and_body_on_failure()
    {
        var handler = new StubHandler((_, _) => Json.Error(HttpStatusCode.Unauthorized, "{\"error\":\"bad key\"}"));
        var embedder = new PineconeEmbedder(new HttpClient(handler), Options);

        var ex = await Assert.ThrowsAsync<PineconeException>(() => embedder.EmbedAsync(new[] { "x" }));

        Assert.Equal(401, ex.StatusCode);
        Assert.Contains("bad key", ex.Body);
    }

    [Fact]
    public async Task Throws_when_pinecone_returns_fewer_vectors_than_inputs()
    {
        var handler = new StubHandler((_, _) => Json.Ok("{\"data\":[{\"values\":[1.0]}]}"));
        var embedder = new PineconeEmbedder(new HttpClient(handler), Options);

        await Assert.ThrowsAsync<InvalidOperationException>(() => embedder.EmbedAsync(new[] { "a", "b" }));
    }

    [Fact]
    public void Client_sends_the_api_key_and_version_headers()
    {
        using var client = PineconeHttp.CreateClient(new PineconeOptions { ApiKey = "secret" });

        Assert.Equal("secret", client.DefaultRequestHeaders.GetValues("Api-Key").Single());
        Assert.Equal("2025-04", client.DefaultRequestHeaders.GetValues("X-Pinecone-API-Version").Single());
    }
}
```

- [ ] **Step 3: Escribir las pruebas del almacén, `PineconeVectorStoreTests.cs`**

```csharp
using System.Text.Json;
using Rag.Core;

namespace Rag.Pinecone.Tests;

public class PineconeVectorStoreTests
{
    private const string Host = "https://idx.svc.pinecone.io";
    private static readonly PineconeOptions Options = new() { ApiKey = "k", IndexName = "test-index", Namespace = "kb" };

    private static HttpResponseMessage Describe() => Json.Ok("{\"host\":\"idx.svc.pinecone.io\"}");

    private static PineconeVectorStore Store(StubHandler handler) => new(new HttpClient(handler), Options);

    [Fact]
    public async Task Lists_ids_following_pagination()
    {
        var handler = new StubHandler((req, _) =>
        {
            var url = req.RequestUri!.ToString();
            if (url == "https://api.pinecone.io/indexes/test-index") return Describe();
            return url.Contains("paginationToken=tok")
                ? Json.Ok("{\"vectors\":[{\"id\":\"KB-01-s3\"}]}")
                : Json.Ok("{\"vectors\":[{\"id\":\"KB-01-s1\"},{\"id\":\"KB-01-s2\"}],\"pagination\":{\"next\":\"tok\"}}");
        });

        var ids = await Store(handler).ListIdsAsync("KB-01-");

        Assert.Equal(new[] { "KB-01-s1", "KB-01-s2", "KB-01-s3" }, ids);
        var urls = handler.Requests.Select(r => r.Url).ToList();
        Assert.Equal("https://api.pinecone.io/indexes/test-index", urls[0]);
        Assert.Equal($"{Host}/vectors/list?prefix=KB-01-&limit=100&namespace=kb", urls[1]);
        Assert.Equal($"{Host}/vectors/list?prefix=KB-01-&limit=100&namespace=kb&paginationToken=tok", urls[2]);
    }

    [Fact]
    public async Task Returns_an_empty_list_when_the_response_has_no_vectors()
    {
        var handler = new StubHandler((req, _) =>
            req.RequestUri!.ToString().Contains("/indexes/") ? Describe() : Json.Ok("{}"));

        var ids = await Store(handler).ListIdsAsync("KB-01-");

        Assert.Empty(ids);
    }

    [Fact]
    public async Task Looks_up_the_index_host_only_once()
    {
        var handler = new StubHandler((req, _) =>
            req.RequestUri!.ToString().Contains("/indexes/") ? Describe() : Json.Ok("{\"vectors\":[]}"));
        var store = Store(handler);

        await store.ListIdsAsync("a-");
        await store.ListIdsAsync("b-");

        Assert.Single(handler.Requests, r => r.Url.StartsWith("https://api.pinecone.io/indexes/"));
    }

    [Fact]
    public async Task Deletes_in_batches_of_one_thousand_inside_the_namespace()
    {
        var handler = new StubHandler((req, _) =>
            req.RequestUri!.ToString().Contains("/indexes/") ? Describe() : Json.Ok("{}"));
        var ids = Enumerable.Range(0, 2500).Select(i => $"KB-01-s{i}").ToList();

        await Store(handler).DeleteAsync(ids);

        var deletes = handler.Requests.Where(r => r.Url == $"{Host}/vectors/delete").ToList();
        Assert.Equal(new[] { 1000, 1000, 500 },
            deletes.Select(d => JsonDocument.Parse(d.Body!).RootElement.GetProperty("ids").GetArrayLength()));
        Assert.All(deletes, d =>
            Assert.Equal("kb", JsonDocument.Parse(d.Body!).RootElement.GetProperty("namespace").GetString()));
    }

    [Fact]
    public async Task Does_not_call_pinecone_when_there_is_nothing_to_delete()
    {
        var handler = new StubHandler((_, _) => Json.Ok("{}"));

        await Store(handler).DeleteAsync(Array.Empty<string>());

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Upserts_in_batches_of_one_hundred_with_metadata()
    {
        var handler = new StubHandler((req, _) =>
            req.RequestUri!.ToString().Contains("/indexes/") ? Describe() : Json.Ok("{\"upsertedCount\":1}"));
        var records = Enumerable.Range(1, 250).Select(i => new VectorRecord(
            $"KB-01-s{i}",
            new[] { 0.5f, 1.5f },
            new Dictionary<string, object> { ["docId"] = "KB-01", ["chunkIndex"] = i, ["text"] = "hola" })).ToList();

        await Store(handler).UpsertAsync(records);

        var upserts = handler.Requests.Where(r => r.Url == $"{Host}/vectors/upsert").ToList();
        var counts = upserts.Select(u => JsonDocument.Parse(u.Body!).RootElement.GetProperty("vectors").GetArrayLength());
        Assert.Equal(new[] { 100, 100, 50 }, counts);

        var first = JsonDocument.Parse(upserts[0].Body!).RootElement;
        Assert.Equal("kb", first.GetProperty("namespace").GetString());
        var vector = first.GetProperty("vectors")[0];
        Assert.Equal("KB-01-s1", vector.GetProperty("id").GetString());
        Assert.Equal(2, vector.GetProperty("values").GetArrayLength());
        Assert.Equal("KB-01", vector.GetProperty("metadata").GetProperty("docId").GetString());
        Assert.Equal(1, vector.GetProperty("metadata").GetProperty("chunkIndex").GetInt32());
    }

    [Fact]
    public async Task Omits_the_namespace_when_it_is_empty()
    {
        var options = new PineconeOptions { ApiKey = "k", IndexName = "test-index", Namespace = "" };
        var handler = new StubHandler((req, _) =>
            req.RequestUri!.ToString().Contains("/indexes/") ? Describe() : Json.Ok("{}"));

        await new PineconeVectorStore(new HttpClient(handler), options).DeleteAsync(new[] { "a" });

        var delete = handler.Requests.Single(r => r.Url.EndsWith("/vectors/delete"));
        Assert.False(JsonDocument.Parse(delete.Body!).RootElement.TryGetProperty("namespace", out _));
    }
}
```

- [ ] **Step 4: Ejecutar para verificar que falla**

Run: `dotnet test tests/Rag.Pinecone.Tests`
Expected: FAIL, errores de compilación por `PineconeEmbedder`, `PineconeVectorStore`, `PineconeOptions`, `PineconeHttp` y `PineconeException` inexistentes.

- [ ] **Step 5: Implementar `PineconeOptions.cs` y `PineconeHttp.cs`**

`src/Rag.Pinecone/PineconeOptions.cs`:

```csharp
namespace Rag.Pinecone;

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
```

`src/Rag.Pinecone/PineconeHttp.cs`:

```csharp
namespace Rag.Pinecone;

public sealed class PineconeException(int statusCode, string body)
    : Exception($"Pinecone respondió {statusCode}: {body}")
{
    public int StatusCode { get; } = statusCode;
    public string Body { get; } = body;
}

public static class PineconeHttp
{
    public static HttpClient CreateClient(PineconeOptions options)
    {
        var http = new HttpClient();
        http.DefaultRequestHeaders.Add("Api-Key", options.ApiKey);
        http.DefaultRequestHeaders.Add("X-Pinecone-API-Version", options.ApiVersion);
        http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return http;
    }

    internal static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync(ct);
        throw new PineconeException((int)response.StatusCode, body);
    }
}
```

- [ ] **Step 6: Implementar `PineconeEmbedder.cs`**

```csharp
using System.Net.Http.Json;
using Rag.Core;

namespace Rag.Pinecone;

public sealed class PineconeEmbedder(HttpClient http, PineconeOptions options) : IEmbedder
{
    public const int BatchSize = 50;

    internal sealed record EmbedResponse(List<EmbedItem>? Data);
    internal sealed record EmbedItem(float[] Values);

    public async Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        var vectors = new List<float[]>(texts.Count);
        foreach (var batch in texts.Chunk(BatchSize))
        {
            var request = new
            {
                model = options.EmbeddingModel,
                parameters = new { input_type = "passage", truncate = "END", dimension = options.Dimension },
                inputs = batch.Select(t => new { text = t }).ToArray()
            };

            using var response = await http.PostAsJsonAsync($"{options.ControlPlaneUrl}/embed", request, ct);
            await PineconeHttp.EnsureSuccessAsync(response, ct);

            var parsed = await response.Content.ReadFromJsonAsync<EmbedResponse>(ct);
            var items = parsed?.Data ?? new List<EmbedItem>();
            if (items.Count != batch.Length)
                throw new InvalidOperationException(
                    $"Pinecone devolvió {items.Count} vectores para {batch.Length} textos.");

            vectors.AddRange(items.Select(i => i.Values));
        }
        return vectors;
    }
}
```

- [ ] **Step 7: Implementar `PineconeVectorStore.cs`**

```csharp
using System.Net.Http.Json;
using Rag.Core;

namespace Rag.Pinecone;

public sealed class PineconeVectorStore(HttpClient http, PineconeOptions options) : IVectorStore
{
    public const int ListPageSize = 100;
    public const int DeleteBatchSize = 1000;
    public const int UpsertBatchSize = 100;

    internal sealed record IndexInfo(string Host);
    internal sealed record ListedVector(string Id);
    internal sealed record ListPagination(string? Next);
    internal sealed record ListResponse(List<ListedVector>? Vectors, ListPagination? Pagination);

    private string? _host;

    public async Task<IReadOnlyList<string>> ListIdsAsync(string prefix, CancellationToken ct = default)
    {
        var host = await HostAsync(ct);
        var ids = new List<string>();
        string? token = null;
        do
        {
            var url = $"{host}/vectors/list?prefix={Uri.EscapeDataString(prefix)}&limit={ListPageSize}"
                      + (options.Namespace.Length > 0 ? $"&namespace={Uri.EscapeDataString(options.Namespace)}" : "")
                      + (token is null ? "" : $"&paginationToken={Uri.EscapeDataString(token)}");

            using var response = await http.GetAsync(url, ct);
            await PineconeHttp.EnsureSuccessAsync(response, ct);

            var page = await response.Content.ReadFromJsonAsync<ListResponse>(ct);
            ids.AddRange((page?.Vectors ?? new List<ListedVector>()).Select(v => v.Id));
            token = page?.Pagination?.Next;
        } while (token is not null);
        return ids;
    }

    public async Task DeleteAsync(IReadOnlyList<string> ids, CancellationToken ct = default)
    {
        foreach (var batch in ids.Chunk(DeleteBatchSize))
            await PostAsync("/vectors/delete", WithNamespace(new Dictionary<string, object?> { ["ids"] = batch }), ct);
    }

    public async Task UpsertAsync(IReadOnlyList<VectorRecord> records, CancellationToken ct = default)
    {
        foreach (var batch in records.Chunk(UpsertBatchSize))
        {
            var vectors = batch.Select(r => new Dictionary<string, object?>
            {
                ["id"] = r.Id,
                ["values"] = r.Values,
                ["metadata"] = r.Metadata
            }).ToList();
            await PostAsync("/vectors/upsert", WithNamespace(new Dictionary<string, object?> { ["vectors"] = vectors }), ct);
        }
    }

    private Dictionary<string, object?> WithNamespace(Dictionary<string, object?> body)
    {
        if (options.Namespace.Length > 0) body["namespace"] = options.Namespace;
        return body;
    }

    private async Task PostAsync(string path, object body, CancellationToken ct)
    {
        var host = await HostAsync(ct);
        using var response = await http.PostAsJsonAsync($"{host}{path}", body, ct);
        await PineconeHttp.EnsureSuccessAsync(response, ct);
    }

    private async Task<string> HostAsync(CancellationToken ct)
    {
        if (_host is not null) return _host;
        var url = $"{options.ControlPlaneUrl}/indexes/{Uri.EscapeDataString(options.IndexName)}";
        using var response = await http.GetAsync(url, ct);
        await PineconeHttp.EnsureSuccessAsync(response, ct);

        var info = await response.Content.ReadFromJsonAsync<IndexInfo>(ct)
            ?? throw new InvalidOperationException("Pinecone no devolvió información del índice.");
        _host = info.Host.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? info.Host : "https://" + info.Host;
        return _host;
    }
}
```

- [ ] **Step 8: Ejecutar para verificar que pasa**

Run: `dotnet test tests/Rag.Pinecone.Tests`
Expected: PASS, 13 pruebas (6 del embedder + 7 del almacén).

- [ ] **Step 9: Checkpoint (solo si el usuario pidió commits)**

```bash
git add -A
git commit -m "feat: add Pinecone REST client" -m "Co-Authored-By: Claude"
```

---

### Task 5: `IngestionService` (plan y ejecución)

**Files:**
- Create: `src/Rag.Ingestion/IngestionService.cs`
- Test: `tests/Rag.Ingestion.Tests/IngestionServiceTests.cs`

**Interfaces:**
- Consumes: `IDocumentLoader`, `IChunker`, `IEmbedder`, `IVectorStore`, `VectorRecord`, `Chunk`, `ParsedDocument`, `InvalidDocumentException`, `IngestionException` (Task 1); `SectionChunker` (Task 3, en las pruebas)
- Produces:
  - `record DocumentPlan(string SourcePath, string? DocId, string? Title, IReadOnlyList<Chunk> Chunks, IReadOnlyList<string> ExistingIds, string? Error)` con `bool IsValid => Error is null`
  - `record DocumentResult(string DocId, int Deleted, int Inserted)`
  - `IngestionService(IDocumentLoader, IChunker, IEmbedder, IVectorStore)` con `Task<IReadOnlyList<DocumentPlan>> PlanAsync(IReadOnlyList<string> files, CancellationToken ct = default)` y `Task<IReadOnlyList<DocumentResult>> ExecuteAsync(IReadOnlyList<DocumentPlan> plans, Action<DocumentResult>? onDocumentDone = null, CancellationToken ct = default)`
  - Metadatos de cada registro: `docId`, `title`, `section`, `type` (`"guide"` o `"faq"`), `chunkIndex` (int) y `text`.

- [ ] **Step 1: Escribir las pruebas que fallan, `IngestionServiceTests.cs`**

```csharp
using Rag.Core;
using Rag.Ingestion;

namespace Rag.Ingestion.Tests;

public class IngestionServiceTests
{
    // ---- fakes ----
    private sealed class FakeLoader(Dictionary<string, Func<ParsedDocument>> docs) : IDocumentLoader
    {
        public ParsedDocument Load(string path) => docs[path]();
    }

    private sealed class FakeEmbedder(List<string> log, bool fail = false, bool shortAnswer = false) : IEmbedder
    {
        public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
        {
            log.Add("embed");
            if (fail) throw new InvalidOperationException("embedder caído");
            var take = shortAnswer ? texts.Count - 1 : texts.Count;
            return Task.FromResult<IReadOnlyList<float[]>>(texts.Take(take).Select(t => new[] { (float)t.Length }).ToList());
        }
    }

    private sealed class FakeStore(List<string> log) : IVectorStore
    {
        public Dictionary<string, VectorRecord> Records { get; } = new();

        public Task<IReadOnlyList<string>> ListIdsAsync(string prefix, CancellationToken ct = default)
        {
            log.Add("list");
            return Task.FromResult<IReadOnlyList<string>>(
                Records.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList());
        }

        public Task DeleteAsync(IReadOnlyList<string> ids, CancellationToken ct = default)
        {
            log.Add("delete");
            foreach (var id in ids) Records.Remove(id);
            return Task.CompletedTask;
        }

        public Task UpsertAsync(IReadOnlyList<VectorRecord> records, CancellationToken ct = default)
        {
            log.Add("upsert");
            foreach (var r in records) Records[r.Id] = r;
            return Task.CompletedTask;
        }
    }

    // ---- helpers ----
    private static string Long(char c) => new(c, 120);

    private static ParsedDocument Doc(string id, params DocSection[] sections) =>
        new(id, $"Guía {id}", $"{id}.docx", sections);

    private static ParsedDocument ThreeChunkDoc(string id) => Doc(id,
        new DocSection("Uno", new[] { Long('a') }),
        new DocSection("Dos", new[] { Long('b') }),
        new DocSection("Preguntas frecuentes", new[] { "P: q", "R: a" }));

    private static (IngestionService service, FakeStore store, List<string> log) Build(
        Dictionary<string, Func<ParsedDocument>> docs, bool failEmbed = false, bool shortEmbed = false)
    {
        var log = new List<string>();
        var store = new FakeStore(log);
        var service = new IngestionService(
            new FakeLoader(docs), new SectionChunker(), new FakeEmbedder(log, failEmbed, shortEmbed), store);
        return (service, store, log);
    }

    // ---- plan ----
    [Fact]
    public async Task Plan_reads_existing_ids_and_writes_nothing()
    {
        var (service, store, log) = Build(new() { ["a.docx"] = () => ThreeChunkDoc("KB-01") });
        store.Records["KB-01-s1"] = new VectorRecord("KB-01-s1", new[] { 1f }, new Dictionary<string, object>());

        var plans = await service.PlanAsync(new[] { "a.docx" });

        var plan = Assert.Single(plans);
        Assert.True(plan.IsValid);
        Assert.Equal("KB-01", plan.DocId);
        Assert.Equal(3, plan.Chunks.Count);
        Assert.Equal(new[] { "KB-01-s1" }, plan.ExistingIds);
        Assert.Equal(new[] { "list" }, log);
    }

    [Fact]
    public async Task Plan_flags_a_document_without_doc_id_and_execute_skips_it()
    {
        var (service, store, log) = Build(new()
        {
            ["bad.docx"] = () => throw new InvalidDocumentException("bad.docx", "Falta 'ID de documento' en la tabla inicial.")
        });

        var plans = await service.PlanAsync(new[] { "bad.docx" });
        var results = await service.ExecuteAsync(plans);

        Assert.False(plans[0].IsValid);
        Assert.Contains("ID de documento", plans[0].Error);
        Assert.Empty(results);
        Assert.Empty(store.Records);
        Assert.DoesNotContain("embed", log);
    }

    [Fact]
    public async Task Plan_rejects_the_second_file_that_reuses_a_doc_id()
    {
        var (service, _, _) = Build(new()
        {
            ["one.docx"] = () => ThreeChunkDoc("KB-01"),
            ["copy.docx"] = () => ThreeChunkDoc("KB-01")
        });

        var plans = await service.PlanAsync(new[] { "one.docx", "copy.docx" });

        Assert.True(plans[0].IsValid);
        Assert.False(plans[1].IsValid);
        Assert.Contains("duplicado", plans[1].Error);
        Assert.Contains("one.docx", plans[1].Error);
    }

    [Fact]
    public async Task Plan_ignores_ids_that_belong_to_another_document_with_a_longer_doc_id()
    {
        var (service, store, _) = Build(new() { ["kb.docx"] = () => ThreeChunkDoc("KB") });
        foreach (var id in new[] { "KB-01-s1", "KB-s1", "KB-q9" })
            store.Records[id] = new VectorRecord(id, new[] { 1f }, new Dictionary<string, object>());

        var plans = await service.PlanAsync(new[] { "kb.docx" });

        Assert.Equal(new[] { "KB-s1", "KB-q9" }.OrderBy(x => x), plans[0].ExistingIds.OrderBy(x => x));
    }

    [Fact]
    public async Task Plan_rejects_a_document_that_produces_no_chunks()
    {
        var (service, _, _) = Build(new() { ["empty.docx"] = () => Doc("KB-05") });

        var plans = await service.PlanAsync(new[] { "empty.docx" });

        Assert.False(plans[0].IsValid);
        Assert.Contains("chunks", plans[0].Error);
    }

    // ---- execute ----
    [Fact]
    public async Task Execute_embeds_then_deletes_then_upserts_with_metadata()
    {
        var (service, store, log) = Build(new() { ["a.docx"] = () => ThreeChunkDoc("KB-01") });
        store.Records["KB-01-s1"] = new VectorRecord("KB-01-s1", new[] { 1f }, new Dictionary<string, object>());
        var plans = await service.PlanAsync(new[] { "a.docx" });
        log.Clear();

        var done = new List<DocumentResult>();
        var results = await service.ExecuteAsync(plans, done.Add);

        Assert.Equal(new[] { "embed", "delete", "upsert" }, log);
        var result = Assert.Single(results);
        Assert.Equal(new DocumentResult("KB-01", 1, 3), result);
        Assert.Equal(results, done);

        var faq = store.Records["KB-01-q1"];
        Assert.Equal("KB-01", faq.Metadata["docId"]);
        Assert.Equal("Guía KB-01", faq.Metadata["title"]);
        Assert.Equal("Preguntas frecuentes", faq.Metadata["section"]);
        Assert.Equal("faq", faq.Metadata["type"]);
        Assert.Equal(2, faq.Metadata["chunkIndex"]);
        Assert.StartsWith("Guía KB-01 > Preguntas frecuentes\nP: q", (string)faq.Metadata["text"]);
        Assert.Equal("guide", store.Records["KB-01-s1"].Metadata["type"]);
    }

    [Fact]
    public async Task Running_twice_does_not_duplicate_and_removes_stale_records()
    {
        var (service, store, _) = Build(new() { ["a.docx"] = () => ThreeChunkDoc("KB-01") });
        store.Records["KB-01-s99"] = new VectorRecord("KB-01-s99", new[] { 1f }, new Dictionary<string, object>());

        for (var i = 0; i < 2; i++)
            await service.ExecuteAsync(await service.PlanAsync(new[] { "a.docx" }));

        Assert.Equal(
            new[] { "KB-01-q1", "KB-01-s1", "KB-01-s2" },
            store.Records.Keys.OrderBy(k => k, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Embedding_failure_deletes_nothing_and_reports_the_document()
    {
        var (service, store, log) = Build(new() { ["a.docx"] = () => ThreeChunkDoc("KB-01") }, failEmbed: true);
        store.Records["KB-01-s1"] = new VectorRecord("KB-01-s1", new[] { 1f }, new Dictionary<string, object>());
        var plans = await service.PlanAsync(new[] { "a.docx" });

        var ex = await Assert.ThrowsAsync<IngestionException>(() => service.ExecuteAsync(plans));

        Assert.Equal("KB-01", ex.DocId);
        Assert.DoesNotContain("delete", log);
        Assert.Contains("KB-01-s1", store.Records.Keys);
    }

    [Fact]
    public async Task Embedder_returning_fewer_vectors_deletes_nothing()
    {
        var (service, store, log) = Build(new() { ["a.docx"] = () => ThreeChunkDoc("KB-01") }, shortEmbed: true);
        store.Records["KB-01-s1"] = new VectorRecord("KB-01-s1", new[] { 1f }, new Dictionary<string, object>());
        var plans = await service.PlanAsync(new[] { "a.docx" });

        await Assert.ThrowsAsync<IngestionException>(() => service.ExecuteAsync(plans));

        Assert.DoesNotContain("delete", log);
        Assert.DoesNotContain("upsert", log);
    }
}
```

- [ ] **Step 2: Ejecutar para verificar que falla**

Run: `dotnet test tests/Rag.Ingestion.Tests --filter "FullyQualifiedName~IngestionServiceTests"`
Expected: FAIL, error de compilación `The type or namespace name 'IngestionService' could not be found`.

- [ ] **Step 3: Implementar `src/Rag.Ingestion/IngestionService.cs`**

```csharp
using System.Text.RegularExpressions;
using Rag.Core;

namespace Rag.Ingestion;

public sealed record DocumentPlan(
    string SourcePath,
    string? DocId,
    string? Title,
    IReadOnlyList<Chunk> Chunks,
    IReadOnlyList<string> ExistingIds,
    string? Error)
{
    public bool IsValid => Error is null;
}

public sealed record DocumentResult(string DocId, int Deleted, int Inserted);

public sealed class IngestionService(
    IDocumentLoader loader,
    IChunker chunker,
    IEmbedder embedder,
    IVectorStore store)
{
    public async Task<IReadOnlyList<DocumentPlan>> PlanAsync(IReadOnlyList<string> files, CancellationToken ct = default)
    {
        var plans = new List<DocumentPlan>();
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var file in files)
        {
            ParsedDocument parsed;
            try
            {
                parsed = loader.Load(file);
            }
            catch (InvalidDocumentException ex)
            {
                plans.Add(Failed(file, ex.Reason));
                continue;
            }

            if (seen.TryGetValue(parsed.DocId, out var firstFile))
            {
                plans.Add(Failed(file, $"DocId duplicado '{parsed.DocId}', ya usado por {Path.GetFileName(firstFile)}.", parsed));
                continue;
            }
            seen[parsed.DocId] = file;

            var chunks = chunker.CreateChunks(parsed);
            if (chunks.Count == 0)
            {
                plans.Add(Failed(file, "El documento no produjo chunks (no tiene secciones con contenido).", parsed));
                continue;
            }

            var listed = await store.ListIdsAsync(parsed.DocId + "-", ct);
            var chunkId = new Regex($"^{Regex.Escape(parsed.DocId)}-[sq][0-9]+$", RegexOptions.CultureInvariant);
            var existing = listed.Where(id => chunkId.IsMatch(id)).ToList();

            plans.Add(new DocumentPlan(file, parsed.DocId, parsed.Title, chunks, existing, null));
        }
        return plans;
    }

    public async Task<IReadOnlyList<DocumentResult>> ExecuteAsync(
        IReadOnlyList<DocumentPlan> plans,
        Action<DocumentResult>? onDocumentDone = null,
        CancellationToken ct = default)
    {
        var results = new List<DocumentResult>();
        foreach (var plan in plans.Where(p => p.IsValid))
        {
            try
            {
                var vectors = await embedder.EmbedAsync(plan.Chunks.Select(c => c.Text).ToList(), ct);
                if (vectors.Count != plan.Chunks.Count)
                    throw new InvalidOperationException(
                        $"El embedder devolvió {vectors.Count} vectores para {plan.Chunks.Count} chunks.");

                if (plan.ExistingIds.Count > 0) await store.DeleteAsync(plan.ExistingIds, ct);

                var records = plan.Chunks
                    .Select((chunk, i) => new VectorRecord(chunk.Id, vectors[i], Metadata(chunk)))
                    .ToList();
                await store.UpsertAsync(records, ct);

                var result = new DocumentResult(plan.DocId!, plan.ExistingIds.Count, records.Count);
                results.Add(result);
                onDocumentDone?.Invoke(result);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new IngestionException(plan.DocId!, ex);
            }
        }
        return results;
    }

    private static DocumentPlan Failed(string file, string error, ParsedDocument? parsed = null) =>
        new(file, parsed?.DocId, parsed?.Title, Array.Empty<Chunk>(), Array.Empty<string>(), error);

    private static Dictionary<string, object> Metadata(Chunk chunk) => new()
    {
        ["docId"] = chunk.DocId,
        ["title"] = chunk.DocTitle,
        ["section"] = chunk.Section,
        ["type"] = chunk.Type == ChunkType.Faq ? "faq" : "guide",
        ["chunkIndex"] = chunk.ChunkIndex,
        ["text"] = chunk.Text
    };
}
```

- [ ] **Step 4: Ejecutar para verificar que pasa**

Run: `dotnet test tests/Rag.Ingestion.Tests --filter "FullyQualifiedName~IngestionServiceTests"`
Expected: PASS, 9 pruebas.

- [ ] **Step 5: Checkpoint (solo si el usuario pidió commits)**

```bash
git add -A
git commit -m "feat: add IngestionService" -m "Co-Authored-By: Claude"
```

---

### Task 6: Descubrimiento, argumentos y `IngestCommand`

**Files:**
- Create: `src/Rag.Ingestion/DocumentDiscovery.cs`, `SourceResolver.cs`, `IngestArguments.cs`, `IngestCommand.cs`
- Test: `tests/Rag.Ingestion.Tests/DocumentDiscoveryTests.cs`, `SourceResolverTests.cs`, `IngestArgumentsTests.cs`, `IngestCommandTests.cs`

**Interfaces:**
- Consumes: `IngestionService`, `DocumentPlan`, `DocumentResult` (Task 5); `IngestInputException`, `IngestionException` (Task 1); `SectionChunker.MinChars/MaxChars` (Task 3)
- Produces:
  - `DocumentDiscovery.Find(string source) : IReadOnlyList<string>`
  - `SourceResolver.Resolve(string? argument, string? appSettings, string? environment) : string`
  - `record IngestArguments(string? Source, bool DryRun, bool AssumeYes, bool Show)` con `static IngestArguments Parse(IReadOnlyList<string> args)` y `const string Usage`
  - `record IngestRunOptions(string Source, bool DryRun, bool AssumeYes, bool Show, string IndexName, string Namespace)`
  - `IngestCommand(IngestionService service, TextWriter output, Func<string?> readLine)` con `Task<int> RunAsync(IngestRunOptions options, CancellationToken ct = default)`. Códigos de salida: 0 correcto; 1 error de entrada o ningún documento válido; 2 se omitió algún documento; 3 falló la ingesta.

- [ ] **Step 1: Escribir las pruebas de descubrimiento, resolución y argumentos**

`DocumentDiscoveryTests.cs`:

```csharp
using Rag.Core;
using Rag.Ingestion;

namespace Rag.Ingestion.Tests;

public class DocumentDiscoveryTests
{
    [Fact]
    public void Returns_a_single_docx_file()
    {
        using var temp = new TempDir();
        var file = temp.Combine("a.docx");
        File.WriteAllBytes(file, new byte[] { 1 });

        Assert.Equal(new[] { file }, DocumentDiscovery.Find(file));
    }

    [Fact]
    public void Lists_docx_files_of_a_folder_sorted_and_ignores_word_lock_files_and_other_types()
    {
        using var temp = new TempDir();
        foreach (var name in new[] { "b.docx", "a.docx", "~$a.docx", "notas.txt" })
            File.WriteAllBytes(temp.Combine(name), new byte[] { 1 });

        var found = DocumentDiscovery.Find(temp.Dir);

        Assert.Equal(new[] { temp.Combine("a.docx"), temp.Combine("b.docx") }, found);
    }

    [Fact]
    public void Rejects_a_file_that_is_not_docx()
    {
        using var temp = new TempDir();
        var file = temp.Combine("notas.txt");
        File.WriteAllBytes(file, new byte[] { 1 });

        Assert.Throws<IngestInputException>(() => DocumentDiscovery.Find(file));
    }

    [Fact]
    public void Rejects_a_path_that_does_not_exist()
    {
        using var temp = new TempDir();
        Assert.Throws<IngestInputException>(() => DocumentDiscovery.Find(temp.Combine("nada")));
    }

    [Fact]
    public void Rejects_a_folder_without_docx_files()
    {
        using var temp = new TempDir();
        File.WriteAllBytes(temp.Combine("~$a.docx"), new byte[] { 1 });

        Assert.Throws<IngestInputException>(() => DocumentDiscovery.Find(temp.Dir));
    }
}
```

`SourceResolverTests.cs`:

```csharp
using Rag.Core;
using Rag.Ingestion;

namespace Rag.Ingestion.Tests;

public class SourceResolverTests
{
    [Fact]
    public void The_argument_wins() =>
        Assert.Equal("arg", SourceResolver.Resolve("arg", "settings", "env"));

    [Fact]
    public void Then_appsettings() =>
        Assert.Equal("settings", SourceResolver.Resolve(null, "settings", "env"));

    [Fact]
    public void Then_the_environment_variable() =>
        Assert.Equal("env", SourceResolver.Resolve(" ", null, "env"));

    [Fact]
    public void Fails_with_a_helpful_message_when_nothing_is_set()
    {
        var ex = Assert.Throws<IngestInputException>(() => SourceResolver.Resolve(null, "", null));
        Assert.Contains("--source", ex.Message);
    }
}
```

`IngestArgumentsTests.cs`:

```csharp
using Rag.Core;
using Rag.Ingestion;

namespace Rag.Ingestion.Tests;

public class IngestArgumentsTests
{
    [Fact]
    public void Parses_all_flags()
    {
        var args = IngestArguments.Parse(new[] { "--source", @"C:\docs", "--dry-run", "--yes", "--show" });

        Assert.Equal(@"C:\docs", args.Source);
        Assert.True(args.DryRun);
        Assert.True(args.AssumeYes);
        Assert.True(args.Show);
    }

    [Fact]
    public void Defaults_to_no_flags_and_no_source()
    {
        var args = IngestArguments.Parse(Array.Empty<string>());

        Assert.Null(args.Source);
        Assert.False(args.DryRun);
        Assert.False(args.AssumeYes);
        Assert.False(args.Show);
    }

    [Fact]
    public void Rejects_unknown_arguments() =>
        Assert.Throws<IngestInputException>(() => IngestArguments.Parse(new[] { "--borrar-todo" }));

    [Fact]
    public void Rejects_source_without_a_value() =>
        Assert.Throws<IngestInputException>(() => IngestArguments.Parse(new[] { "--source" }));
}
```

- [ ] **Step 2: Escribir las pruebas del comando, `IngestCommandTests.cs`**

```csharp
using Rag.Core;
using Rag.Ingestion;

namespace Rag.Ingestion.Tests;

public class IngestCommandTests : IDisposable
{
    private sealed class FakeEmbedder : IEmbedder
    {
        public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<float[]>>(texts.Select(t => new[] { (float)t.Length }).ToList());
    }

    private sealed class FakeStore : IVectorStore
    {
        public Dictionary<string, VectorRecord> Records { get; } = new();
        public int Writes { get; private set; }

        public Task<IReadOnlyList<string>> ListIdsAsync(string prefix, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<string>>(Records.Keys.Where(k => k.StartsWith(prefix)).ToList());

        public Task DeleteAsync(IReadOnlyList<string> ids, CancellationToken ct = default)
        {
            Writes++;
            foreach (var id in ids) Records.Remove(id);
            return Task.CompletedTask;
        }

        public Task UpsertAsync(IReadOnlyList<VectorRecord> records, CancellationToken ct = default)
        {
            Writes++;
            foreach (var r in records) Records[r.Id] = r;
            return Task.CompletedTask;
        }
    }

    private readonly TempDir _temp = new();
    private readonly FakeStore _store = new();
    private readonly StringWriter _output = new();
    private readonly Queue<string?> _answers = new();

    public IngestCommandTests() =>
        File.Copy(TestFiles.Fixture("01_rechazos_de_pago_con_tarjeta.docx"), _temp.Combine("01_rechazos.docx"));

    public void Dispose() => _temp.Dispose();

    private IngestCommand Command() => new(
        new IngestionService(new DocxLoader(), new SectionChunker(), new FakeEmbedder(), _store),
        _output,
        () => _answers.Count > 0 ? _answers.Dequeue() : throw new InvalidOperationException("No se debía preguntar."));

    private IngestRunOptions Options(bool dryRun = false, bool yes = false, bool show = false, string? source = null) =>
        new(source ?? _temp.Dir, dryRun, yes, show, "customer-service-kb", "kb");

    [Fact]
    public async Task Dry_run_prints_the_summary_and_writes_nothing()
    {
        var code = await Command().RunAsync(Options(dryRun: true));

        Assert.Equal(0, code);
        Assert.Equal(0, _store.Writes);
        var text = _output.ToString();
        Assert.Contains("customer-service-kb (namespace: kb)", text);
        Assert.Contains("Documento: KB-01", text);
        Assert.Contains("Chunks a crear: 12", text);
        Assert.Contains("BORRAR 0 registros existentes · INSERTAR 12", text);
        Assert.Contains("KB-01-s2", text);
        Assert.Contains("KB-01-q6", text);
    }

    [Fact]
    public async Task Answering_no_cancels_without_writing()
    {
        _answers.Enqueue("n");

        var code = await Command().RunAsync(Options());

        Assert.Equal(0, code);
        Assert.Equal(0, _store.Writes);
        Assert.Contains("¿Continuar? [s/N]", _output.ToString());
        Assert.Contains("Cancelado. No se escribió nada.", _output.ToString());
    }

    [Fact]
    public async Task An_empty_answer_counts_as_no()
    {
        _answers.Enqueue("");

        await Command().RunAsync(Options());

        Assert.Equal(0, _store.Writes);
    }

    [Fact]
    public async Task Answering_yes_writes_the_chunks()
    {
        _answers.Enqueue("s");

        var code = await Command().RunAsync(Options());

        Assert.Equal(0, code);
        Assert.Equal(12, _store.Records.Count);
        Assert.Contains("KB-01: 0 borrados, 12 insertados", _output.ToString());
    }

    [Fact]
    public async Task Yes_flag_writes_without_asking()
    {
        var code = await Command().RunAsync(Options(yes: true));

        Assert.Equal(0, code);
        Assert.Equal(12, _store.Records.Count);
        Assert.DoesNotContain("¿Continuar?", _output.ToString());
    }

    [Fact]
    public async Task Running_again_reports_the_records_it_replaced()
    {
        await Command().RunAsync(Options(yes: true));
        _output.GetStringBuilder().Clear();

        await Command().RunAsync(Options(yes: true));

        Assert.Equal(12, _store.Records.Count);
        Assert.Contains("KB-01: 12 borrados, 12 insertados", _output.ToString());
    }

    [Fact]
    public async Task Show_prints_the_full_text_of_each_chunk()
    {
        await Command().RunAsync(Options(dryRun: true, show: true));

        var text = _output.ToString();
        Assert.Contains("--- KB-01-s2 ---", text);
        Assert.Contains("Guía: Rechazos de pago con tarjeta > Lista de verificación (en este orden)", text);
    }

    [Fact]
    public async Task A_missing_source_returns_1_with_a_message()
    {
        var code = await Command().RunAsync(Options(source: _temp.Combine("no-existe")));

        Assert.Equal(1, code);
        Assert.Contains("Error:", _output.ToString());
    }

    [Fact]
    public async Task A_skipped_document_is_reported_and_returns_2()
    {
        TestDocx.CreateWithoutDocId(_temp.Dir, "sin-id.docx");

        var code = await Command().RunAsync(Options(yes: true));

        Assert.Equal(2, code);
        Assert.Equal(12, _store.Records.Count);
        Assert.Contains("OMITIDO sin-id.docx", _output.ToString());
    }

    [Fact]
    public async Task A_folder_with_only_invalid_documents_returns_1_and_writes_nothing()
    {
        using var other = new TempDir();
        TestDocx.CreateWithoutDocId(other.Dir, "sin-id.docx");

        var code = await Command().RunAsync(Options(yes: true, source: other.Dir));

        Assert.Equal(1, code);
        Assert.Equal(0, _store.Writes);
    }

    [Fact]
    public async Task Word_lock_files_in_the_folder_are_ignored()
    {
        File.WriteAllBytes(_temp.Combine("~$01_rechazos.docx"), new byte[] { 1, 2, 3 });

        var code = await Command().RunAsync(Options(dryRun: true));

        Assert.Equal(0, code);
        Assert.DoesNotContain("OMITIDO", _output.ToString());
    }
}
```

- [ ] **Step 3: Ejecutar para verificar que falla**

Run: `dotnet test tests/Rag.Ingestion.Tests --filter "FullyQualifiedName~DocumentDiscoveryTests|FullyQualifiedName~SourceResolverTests|FullyQualifiedName~IngestArgumentsTests|FullyQualifiedName~IngestCommandTests"`
Expected: FAIL, errores de compilación por `DocumentDiscovery`, `SourceResolver`, `IngestArguments`, `IngestCommand` e `IngestRunOptions` inexistentes.

- [ ] **Step 4: Implementar `DocumentDiscovery.cs`, `SourceResolver.cs` e `IngestArguments.cs`**

`DocumentDiscovery.cs`:

```csharp
using Rag.Core;

namespace Rag.Ingestion;

public static class DocumentDiscovery
{
    public static IReadOnlyList<string> Find(string source)
    {
        if (File.Exists(source))
        {
            if (!source.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
                throw new IngestInputException($"'{source}' no es un archivo .docx.");
            return new[] { source };
        }

        if (Directory.Exists(source))
        {
            var files = Directory.EnumerateFiles(source, "*.docx", SearchOption.TopDirectoryOnly)
                .Where(f => !Path.GetFileName(f).StartsWith("~$", StringComparison.Ordinal))
                .OrderBy(f => f, StringComparer.Ordinal)
                .ToList();
            if (files.Count == 0)
                throw new IngestInputException($"No hay archivos .docx en '{source}'.");
            return files;
        }

        throw new IngestInputException($"No existe la ruta '{source}'.");
    }
}
```

`SourceResolver.cs`:

```csharp
using Rag.Core;

namespace Rag.Ingestion;

public static class SourceResolver
{
    public static string Resolve(string? argument, string? appSettings, string? environment)
    {
        foreach (var candidate in new[] { argument, appSettings, environment })
            if (!string.IsNullOrWhiteSpace(candidate)) return candidate.Trim();

        throw new IngestInputException(
            "Indica la ruta de origen con --source, con Ingestion:Source en appsettings.json " +
            "o con la variable de entorno INGEST_SOURCE.");
    }
}
```

`IngestArguments.cs`:

```csharp
using Rag.Core;

namespace Rag.Ingestion;

public sealed record IngestArguments(string? Source, bool DryRun, bool AssumeYes, bool Show)
{
    public const string Usage =
        "Uso: ingest [--source <archivo|carpeta>] [--dry-run] [--yes] [--show]\n" +
        "  --source   archivo .docx o carpeta con .docx\n" +
        "  --dry-run  muestra el resumen y no escribe nada\n" +
        "  --yes      no pregunta antes de escribir\n" +
        "  --show     imprime el texto completo de cada chunk";

    public static IngestArguments Parse(IReadOnlyList<string> args)
    {
        string? source = null;
        bool dryRun = false, yes = false, show = false;

        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--source":
                    if (i + 1 >= args.Count) throw new IngestInputException("--source requiere una ruta.");
                    source = args[++i];
                    break;
                case "--dry-run": dryRun = true; break;
                case "--yes": yes = true; break;
                case "--show": show = true; break;
                default: throw new IngestInputException($"Argumento desconocido: {args[i]}");
            }
        }
        return new IngestArguments(source, dryRun, yes, show);
    }
}
```

- [ ] **Step 5: Implementar `IngestCommand.cs`**

```csharp
using Rag.Core;

namespace Rag.Ingestion;

public sealed record IngestRunOptions(
    string Source, bool DryRun, bool AssumeYes, bool Show, string IndexName, string Namespace);

public sealed class IngestCommand(IngestionService service, TextWriter output, Func<string?> readLine)
{
    public async Task<int> RunAsync(IngestRunOptions options, CancellationToken ct = default)
    {
        IReadOnlyList<string> files;
        try
        {
            files = DocumentDiscovery.Find(options.Source);
        }
        catch (IngestInputException ex)
        {
            output.WriteLine($"Error: {ex.Message}");
            return 1;
        }

        var plans = await service.PlanAsync(files, ct);
        WriteSummary(plans, options);

        var valid = plans.Where(p => p.IsValid).ToList();
        var skippedExitCode = valid.Count == plans.Count ? 0 : 2;

        if (valid.Count == 0)
        {
            output.WriteLine("No hay documentos válidos para ingestar.");
            return 1;
        }

        if (options.DryRun)
        {
            output.WriteLine("Modo --dry-run: no se escribió nada.");
            return skippedExitCode;
        }

        if (!options.AssumeYes)
        {
            output.Write("¿Continuar? [s/N] ");
            var answer = readLine();
            if (!string.Equals(answer?.Trim(), "s", StringComparison.OrdinalIgnoreCase))
            {
                output.WriteLine("Cancelado. No se escribió nada.");
                return 0;
            }
        }

        try
        {
            await service.ExecuteAsync(
                plans,
                r => output.WriteLine($"{r.DocId}: {r.Deleted} borrados, {r.Inserted} insertados"),
                ct);
        }
        catch (IngestionException ex)
        {
            output.WriteLine($"Error: {ex.Message}");
            return 3;
        }
        return skippedExitCode;
    }

    private void WriteSummary(IReadOnlyList<DocumentPlan> plans, IngestRunOptions options)
    {
        var ns = options.Namespace.Length == 0 ? "(default)" : options.Namespace;
        output.WriteLine($"Origen: {options.Source}");
        output.WriteLine($"Índice: {options.IndexName} (namespace: {ns})");

        foreach (var plan in plans)
        {
            output.WriteLine();
            if (!plan.IsValid)
            {
                output.WriteLine($"OMITIDO {Path.GetFileName(plan.SourcePath)}: {plan.Error}");
                continue;
            }

            output.WriteLine($"Documento: {plan.DocId} · {plan.Title}");
            output.WriteLine($"Chunks a crear: {plan.Chunks.Count}");
            output.WriteLine(" #  id          tipo   chars  sección");
            for (var i = 0; i < plan.Chunks.Count; i++)
            {
                var chunk = plan.Chunks[i];
                var body = chunk.Text.Length - chunk.Text.IndexOf('\n') - 1;
                var warning = chunk.Type == ChunkType.Guide && body < SectionChunker.MinChars ? " (corto)"
                    : body > SectionChunker.MaxChars ? " (largo)" : "";
                var type = chunk.Type == ChunkType.Faq ? "faq" : "guide";
                output.WriteLine($"{i + 1,2}  {chunk.Id,-10}  {type,-5}  {body,5}  {chunk.Section}{warning}");
            }
            output.WriteLine($"Acción: BORRAR {plan.ExistingIds.Count} registros existentes · INSERTAR {plan.Chunks.Count}");

            if (options.Show)
                foreach (var chunk in plan.Chunks)
                {
                    output.WriteLine($"--- {chunk.Id} ---");
                    output.WriteLine(chunk.Text);
                }
        }
    }
}
```

- [ ] **Step 6: Ejecutar para verificar que pasa**

Run: `dotnet test tests/Rag.Ingestion.Tests`
Expected: PASS, todas las pruebas del proyecto (DocxLoader 11, SectionChunker 10, IngestionService 9, DocumentDiscovery 5, SourceResolver 4, IngestArguments 4, IngestCommand 11 = 54).

- [ ] **Step 7: Checkpoint (solo si el usuario pidió commits)**

```bash
git add -A
git commit -m "feat: add ingest command, discovery and argument parsing" -m "Co-Authored-By: Claude"
```

---

### Task 7: Consola `Ingest.Cli`, configuración y verificación real

**Files:**
- Create: `src/Ingest.Cli/Program.cs`, `src/Ingest.Cli/appsettings.json`, `scripts/search.ps1`, `README.md`
- Modify: `src/Ingest.Cli/Ingest.Cli.csproj`

**Interfaces:**
- Consumes: todo lo anterior (`IngestArguments`, `SourceResolver`, `IngestCommand`, `IngestRunOptions`, `IngestionService`, `DocxLoader`, `SectionChunker`, `PineconeOptions`, `PineconeHttp`, `PineconeEmbedder`, `PineconeVectorStore`, `PineconeException`, `IngestInputException`)
- Produces: el ejecutable `dotnet run --project src/Ingest.Cli -- [opciones]`.

- [ ] **Step 1: Crear `src/Ingest.Cli/appsettings.json` y copiarlo a la salida**

`appsettings.json`:

```json
{
  "Ingestion": {
    "Source": "C:\\Nortal\\AIStudy\\ragCustomerservice"
  },
  "Pinecone": {
    "IndexName": "customer-service-kb",
    "Namespace": "kb",
    "EmbeddingModel": "llama-text-embed-v2",
    "Dimension": 1024
  }
}
```

En `src/Ingest.Cli/Ingest.Cli.csproj`, antes de `</Project>`, añadir:

```xml
  <ItemGroup>
    <None Update="appsettings.json" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
```

- [ ] **Step 2: Crear `src/Ingest.Cli/Program.cs`**

```csharp
using System.Text;
using Microsoft.Extensions.Configuration;
using Rag.Core;
using Rag.Ingestion;
using Rag.Pinecone;

Console.OutputEncoding = Encoding.UTF8;

try
{
    var arguments = IngestArguments.Parse(args);

    var config = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: true)
        .AddUserSecrets<Program>(optional: true)
        .AddEnvironmentVariables()
        .Build();

    var source = SourceResolver.Resolve(
        arguments.Source,
        config["Ingestion:Source"],
        Environment.GetEnvironmentVariable("INGEST_SOURCE"));

    var pinecone = config.GetSection("Pinecone").Get<PineconeOptions>() ?? new PineconeOptions();
    if (string.IsNullOrWhiteSpace(pinecone.ApiKey))
        pinecone.ApiKey = Environment.GetEnvironmentVariable("PINECONE_API_KEY") ?? "";
    if (string.IsNullOrWhiteSpace(pinecone.ApiKey))
        throw new IngestInputException(
            "Falta la API key de Pinecone. Defínela en la variable de entorno PINECONE_API_KEY " +
            "o con: dotnet user-secrets set \"Pinecone:ApiKey\" \"<clave>\" --project src/Ingest.Cli");
    if (string.IsNullOrWhiteSpace(pinecone.IndexName))
        throw new IngestInputException("Falta Pinecone:IndexName en appsettings.json.");

    using var http = PineconeHttp.CreateClient(pinecone);
    var service = new IngestionService(
        new DocxLoader(),
        new SectionChunker(),
        new PineconeEmbedder(http, pinecone),
        new PineconeVectorStore(http, pinecone));
    var command = new IngestCommand(service, Console.Out, Console.ReadLine);

    return await command.RunAsync(new IngestRunOptions(
        source, arguments.DryRun, arguments.AssumeYes, arguments.Show, pinecone.IndexName, pinecone.Namespace));
}
catch (IngestInputException ex)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    Console.Error.WriteLine(IngestArguments.Usage);
    return 1;
}
catch (PineconeException ex)
{
    Console.Error.WriteLine($"Error de Pinecone: {ex.Message}");
    return 3;
}
```

- [ ] **Step 3: Compilar y ejecutar toda la suite**

Run: `dotnet build RagIngest.sln && dotnet test RagIngest.sln`
Expected: `Build succeeded` y todas las pruebas en verde (54 de `Rag.Ingestion.Tests` + 13 de `Rag.Pinecone.Tests`).

- [ ] **Step 4: Comprobar los errores de entrada sin tocar Pinecone**

Run: `dotnet run --project src/Ingest.Cli -- --borrar-todo`
Expected: `Error: Argumento desconocido: --borrar-todo`, la línea de uso y código de salida 1.

Run (sin `PINECONE_API_KEY` definida): `dotnet run --project src/Ingest.Cli -- --dry-run`
Expected: `Error: Falta la API key de Pinecone...` y código de salida 1.

- [ ] **Step 5: Crear el índice `customer-service-kb` en Pinecone**

Define la API key en la sesión (no se guarda en ningún archivo) y crea el índice. El plan gratuito solo admite serverless en `aws` / `us-east-1`.

```powershell
$env:PINECONE_API_KEY = "<tu clave>"
$h = @{ "Api-Key" = $env:PINECONE_API_KEY; "X-Pinecone-API-Version" = "2025-04"; "Content-Type" = "application/json" }
$body = @{ name = "customer-service-kb"; dimension = 1024; metric = "cosine"; spec = @{ serverless = @{ cloud = "aws"; region = "us-east-1" } } } | ConvertTo-Json -Depth 5
Invoke-RestMethod -Method Post -Uri https://api.pinecone.io/indexes -Headers $h -Body $body
(Invoke-RestMethod -Uri https://api.pinecone.io/indexes/customer-service-kb -Headers $h).status.ready
```

Expected: el último comando imprime `True` (si imprime `False`, esperar unos segundos y repetirlo).

- [ ] **Step 6: Crear `scripts/search.ps1` (búsqueda manual)**

```powershell
param(
    [Parameter(Mandatory)][string]$Query,
    [int]$TopK = 4,
    [string]$Index = "customer-service-kb",
    [string]$Namespace = "kb"
)

$headers = @{ "Api-Key" = $env:PINECONE_API_KEY; "X-Pinecone-API-Version" = "2025-04" }
$utf8 = [Text.Encoding]::UTF8

$embedJson = @{
    model      = "llama-text-embed-v2"
    parameters = @{ input_type = "query"; truncate = "END"; dimension = 1024 }
    inputs     = @(@{ text = $Query })
} | ConvertTo-Json -Depth 6
$embedding = Invoke-RestMethod -Method Post -Uri "https://api.pinecone.io/embed" -Headers $headers `
    -ContentType "application/json; charset=utf-8" -Body $utf8.GetBytes($embedJson)

$indexHost = (Invoke-RestMethod -Uri "https://api.pinecone.io/indexes/$Index" -Headers $headers).host

$queryJson = @{
    vector          = $embedding.data[0].values
    topK            = $TopK
    namespace       = $Namespace
    includeMetadata = $true
} | ConvertTo-Json -Depth 6 -Compress
$result = Invoke-RestMethod -Method Post -Uri "https://$indexHost/query" -Headers $headers `
    -ContentType "application/json; charset=utf-8" -Body $utf8.GetBytes($queryJson)

foreach ($m in $result.matches) {
    $preview = ($m.metadata.text -replace "\s+", " ")
    if ($preview.Length -gt 140) { $preview = $preview.Substring(0, 140) + "..." }
    "{0:N3}  {1,-9}  {2}" -f $m.score, $m.id, $preview
}
```

- [ ] **Step 7: Ensayo en seco con el documento 01**

Run:
```bash
dotnet run --project src/Ingest.Cli -- --source "C:\Nortal\AIStudy\ragCustomerservice\01_rechazos_de_pago_con_tarjeta.docx" --dry-run
```
Expected: el resumen con `Chunks a crear: 12`, la tabla con `KB-01-s1`…`KB-01-s6` y `KB-01-q1`…`KB-01-q6`, `BORRAR 0 registros existentes · INSERTAR 12` y la línea `Modo --dry-run: no se escribió nada.` En Pinecone no cambia nada. Revisar con `--show` que ningún chunk quede cortado a mitad.

- [ ] **Step 8: Ingestar el documento 01 con confirmación**

Run: el mismo comando sin `--dry-run`.
Expected: muestra el resumen, pregunta `¿Continuar? [s/N]`; al responder `s` imprime `KB-01: 0 borrados, 12 insertados`. Responder `n` termina con `Cancelado. No se escribió nada.`

- [ ] **Step 9: Comprobar que reejecutar no duplica**

Run: repetir el Step 8 respondiendo `s`.
Expected: `BORRAR 12 registros existentes · INSERTAR 12` y al final `KB-01: 12 borrados, 12 insertados`. En la consola de Pinecone el índice sigue teniendo 12 registros en el namespace `kb`.

- [ ] **Step 10: Verificar la recuperación con búsquedas reales**

```powershell
./scripts/search.ps1 "me rechazan la tarjeta aunque tengo saldo"
./scripts/search.ps1 "cómo cambio los límites de la tarjeta"
./scripts/search.ps1 "no me deja pagar sin contacto en la terminal"
./scripts/search.ps1 "la app dice que las compras online están deshabilitadas"
```

Expected: en cada búsqueda, el chunk esperado aparece entre los 3 primeros resultados:
1. `KB-01-q1` o `KB-01-s2`.
2. `KB-01-q2`.
3. `KB-01-q5` o `KB-01-s2`.
4. `KB-01-q6` o `KB-01-s2`.

Si alguno no aparece, anotar la pregunta y el resultado: es material para ajustar las reglas de chunking antes de seguir con los otros documentos.

- [ ] **Step 11: Ingestar toda la carpeta**

Run: `dotnet run --project src/Ingest.Cli` (usa `Ingestion:Source` de `appsettings.json`) y confirmar con `s`.
Expected: el resumen lista los 8 documentos (`KB-01` a `KB-08`), `OMITIDO` solo si algún archivo es inválido, y al final una línea `KB-0N: 0 borrados, X insertados` por documento (KB-01 aparece con 12 borrados, 12 insertados). En total, entre 60 y 80 registros en Pinecone.

- [ ] **Step 12: Crear `README.md`**

````markdown
# ragIngest

Ingesta de guías de servicio al cliente (`.docx`) a un índice de Pinecone. Fase 1 de una prueba de concepto de RAG.

## Requisitos
- .NET 10 SDK
- Un índice de Pinecone `customer-service-kb` (serverless, coseno, dimensión 1024)
- La variable de entorno `PINECONE_API_KEY` (o user secret `Pinecone:ApiKey`)

## Uso
```bash
# resumen sin escribir nada
dotnet run --project src/Ingest.Cli -- --source "<archivo o carpeta>" --dry-run

# resumen + pregunta + escritura
dotnet run --project src/Ingest.Cli -- --source "<archivo o carpeta>"

# sin pregunta; mostrar el texto completo de cada chunk
dotnet run --project src/Ingest.Cli -- --source "<archivo o carpeta>" --yes --show
```

La ruta de origen sale de `--source`, luego de `Ingestion:Source` en `appsettings.json`, luego de `INGEST_SOURCE`.
Códigos de salida: 0 correcto, 1 error de entrada o sin documentos válidos, 2 se omitió algún documento, 3 falló la ingesta.

## Búsqueda manual
`./scripts/search.ps1 "tu pregunta"` devuelve los chunks más cercanos.

## Pruebas
`dotnet test RagIngest.sln`
````

- [ ] **Step 13: Checkpoint (solo si el usuario pidió commits)**

```bash
git add -A
git commit -m "feat: add console app, config and search script" -m "Co-Authored-By: Claude"
```

---

## Self-Review

**Cobertura del spec:**

| Sección del spec | Tarea |
|---|---|
| §4 Estructura (Core, Ingestion, Pinecone, Cli, tests) | 1, 2–7 |
| §5 Configuración de la ruta y secretos | 6 (`SourceResolver`), 7 (`Program.cs`) |
| §6 Reglas de chunking 1–8 | 3 |
| §7 Flujo: validar, cargar, resumen, modos, borrar+insertar, resultado | 5, 6 |
| §8 Metadatos (`docId`, `title`, `section`, `type`, `chunkIndex`, `text`) | 5 |
| §9 Errores (ruta, no `.docx`, API key, sin `DocId`, fallo de Pinecone) | 2, 5, 6, 7 |
| §10 Pruebas (chunker con el 01, tamaños, confirmación, manual con Pinecone) | 3, 6, 7 |
| §11 Cliente REST y decisiones de Pinecone | 4 |

**Escaneo de placeholders:** no hay TBD, TODO ni pasos sin código. La única nota condicional es la del Step 5 de la Task 2, sobre diferencias menores de tipos en `DocumentFormat.OpenXml`.

**Consistencia de tipos:** `CreateChunks`, `EmbedAsync`, `ListIdsAsync`, `DeleteAsync`, `UpsertAsync`, `DocumentPlan.SourcePath`, `DocumentResult(DocId, Deleted, Inserted)`, `ExecuteAsync(plans, onDocumentDone, ct)` y `IngestRunOptions` se usan con las mismas firmas en todas las tareas. Las constantes `SectionChunker.MinChars/MaxChars` las usa `IngestCommand`.

**Review Focus:** cada punto tiene su prueba: archivos `~$` (Task 6, discovery y comando), `DocId` duplicado (Task 5), `DocId` ausente o inseguro (Task 2), prefijo ambiguo `KB`/`KB-01` (Task 5), paginación y lotes (Task 4), documento sin chunks y embedder defectuoso (Task 5).
