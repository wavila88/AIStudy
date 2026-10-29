# Project: ragMCP
Sources read: ragCustomerBot/README.md, ragCustomerBot/src/*/*.csproj, ragCustomerBot/tests/RagBot.Tests/RagBot.Tests.csproj, `dotnet --list-sdks`
Last scan: 2026-10-29
Note: `ragMCP/` is empty (only git). Everything below describes the sibling RAG project `ragCustomerBot` as the reference to follow; nothing is implemented in ragMCP yet.

## Stack
- C# / .NET 10 in the reference project (← ragCustomerBot/README.md "Decisiones"); ragMCP target requested by the developer: .NET 8
- .NET SDKs installed: 8.0.426 and 10.0.401 (← `dotnet --list-sdks`)
- Vector DB: Pinecone, index `customer-service-kb`, namespace `kb`, dim 1024, embeddings `llama-text-embed-v2` (← ragCustomerBot/README.md)
- Chat model: OpenAI via plain HTTP, model configurable (← ragCustomerBot/src/RagBot.OpenAI)

## Architecture
- Pattern: Core + adapters (ports and interfaces) (← ragCustomerBot/README.md "Estructura")
- Layers: `Core` → pure logic, models, `IRetriever`, `IChatModel`, no network; `Pinecone` → retriever; `OpenAI` → chat client; `Cli` → startup and config (← ragCustomerBot/README.md)
- Dependency rules: Core references nothing; adapters and Cli reference Core (← ragCustomerBot/src/RagBot.Core/RagBot.Core.csproj, RagBot.Pinecone.csproj)

## Conventions
- `Nullable` and `ImplicitUsings` enabled in every project (← ragCustomerBot/src/RagBot.Core/RagBot.Core.csproj)
- Config in `appsettings.json` (versioned, no keys) + `appsettings.Local.json` (git-ignored, API keys) (← ragCustomerBot/README.md "Claves")
- Network clients tested with stubbed HttpMessageHandler (← ragCustomerBot/tests/RagBot.Tests/StubHandler.cs)
- RAG flow: retrieve top-K → score gate (`MinScore`) → prompt with chunks → answer with citations (← ragCustomerBot/README.md "Flujo")
- README and docs are written in Spanish (← ragCustomerBot/README.md)

## Frontend
- N/A

## Tests
- Frameworks: xUnit 2.9.3, Microsoft.NET.Test.Sdk (← ragCustomerBot/tests/RagBot.Tests/RagBot.Tests.csproj)
- Location and naming: `tests/<Project>.Tests/`, one file per class, `*Tests.cs` (← ragCustomerBot/tests/RagBot.Tests/)
- Command: `dotnet test <solution>.sln` (← ragCustomerBot/README.md "Comandos")

## Run the app
- `dotnet run --project src/<Name>.Cli` (← ragCustomerBot/README.md "Comandos")

## Not detected / questions
- MCP transport: stdio or HTTP/SSE?
- MCP SDK: official `ModelContextProtocol` NuGet package?
- Knowledge source: reuse Pinecone index `customer-service-kb` (already loaded by ragIngest) or a new one?
- Embeddings and generation: Pinecone embeddings as in ragCustomerBot? Should the MCP expose only retrieval tools (the client LLM generates the answer) or also call OpenAI?
- Solution and project names (suggested: `RagMcp.sln`, `RagMcp.Core`, `RagMcp.Pinecone`, `RagMcp.Server`, `RagMcp.Tests`)
- Docs language: Spanish as in sibling projects?
