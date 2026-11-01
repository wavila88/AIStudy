namespace RagMcp.Tests;

public class ServerSmokeTests
{
    [Fact]
    public async Task Expone_la_herramienta_buscar_guias()
    {
        await using var server = await TestServer.StartAsync(new FakeRetriever());

        var tools = await server.Client.ListToolsAsync();

        Assert.Contains(tools, t => t.Name == "buscar_guias");
    }
}
