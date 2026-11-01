using Microsoft.Extensions.Configuration;
using RagMcp.Core;

namespace RagMcp.Tests;

/// <summary>Carga la configuración real (appsettings.json + appsettings.Local.json + variables de entorno) para las pruebas de integración.</summary>
public static class IntegrationConfig
{
    public static RagMcpOptions Load() =>
        new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Local.json", optional: true)
            .AddEnvironmentVariables()
            .Build()
            .Get<RagMcpOptions>()!;

    public static bool TieneClavePinecone
    {
        get
        {
            var key = Load().Pinecone.ApiKey;
            return !string.IsNullOrWhiteSpace(key) && !key.StartsWith("pega-aqui", StringComparison.OrdinalIgnoreCase);
        }
    }
}

/// <summary>Como [Fact], pero se salta sola cuando no hay clave de Pinecone configurada.</summary>
public sealed class RequiereClavePineconeFactAttribute : FactAttribute
{
    public RequiereClavePineconeFactAttribute()
    {
        if (!IntegrationConfig.TieneClavePinecone)
            Skip = "Falta Pinecone:ApiKey (appsettings.Local.json o variable de entorno Pinecone__ApiKey).";
    }
}
