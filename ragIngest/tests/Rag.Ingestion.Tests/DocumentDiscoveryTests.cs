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
