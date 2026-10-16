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
