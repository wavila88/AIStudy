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
