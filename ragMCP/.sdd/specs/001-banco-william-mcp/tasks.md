# Tasks: MCP Banco William: consulta RAG de servicio al cliente
Spec: ./spec.md
Test command: `dotnet test RagMcp.sln`

## Phase 1 — Estructura de la solución y configuración (prerequisito de todo el spec)
Test: `tests/RagMcp.Tests/OptionsTests.cs` — `RagMcpOptions` se carga desde `appsettings.json` con `TopK=5`, `MinScore=0.30`, índice `customer-service-kb`, namespace `kb`.
Manual check: `dotnet build RagMcp.sln` y `dotnet test RagMcp.sln` en verde; `appsettings.Local.json` aparece ignorado por git.

- [x] 1.1 Failing test — result: FAILED: `error CS0246: RagMcpOptions could not be found` (compilación roja, el tipo no existía)
- [x] 1.2 Implementation — files: `RagMcp.sln`, `Directory.Build.props` (net8.0), `src/RagMcp.Core/{RagMcp.Core.csproj,RagMcpOptions.cs}`, `src/RagMcp.Server/{RagMcp.Server.csproj,Program.cs,appsettings.json,appsettings.Local.example.json}`, `tests/RagMcp.Tests/{RagMcp.Tests.csproj,OptionsTests.cs}`. El `.gitignore` de la raíz (`C:\Nortal\AIStudy`) ya cubre `appsettings.Local*`, `bin/` y `obj/`.
- [x] 1.3 Passing test — result: `Passed! Failed: 0, Passed: 2, Total: 2` (net8.0)
- [x] 1.4 Developer approval

## Phase 2 — El servidor MCP expone `buscar_guias` y Claude Code se conecta (prerequisito de HP-1)
Test: `tests/RagMcp.Tests/ServerSmokeTests.cs` — el servidor registra la herramienta `buscar_guias` y al invocarla con una pregunta devuelve la respuesta fija del esqueleto.
Manual check: `claude mcp add banco-william -- dotnet run --project src/RagMcp.Server`; `/mcp` en Claude Code muestra el servidor conectado con la herramienta `buscar_guias`, y al pedírselo la invoca.

- [ ] 2.1 Failing test — result: ________
- [ ] 2.2 Implementation — files: ________ (paquete `ModelContextProtocol`, host con transporte stdio, herramienta `buscar_guias` con respuesta fija)
- [ ] 2.3 Passing test — result: ________
- [ ] 2.4 Developer approval

## Phase 3 — `buscar_guias` consulta Pinecone y devuelve fragmentos con su id (covers HP-1)
Test: `tests/RagMcp.Tests/PineconeRetrieverTests.cs` y `BuscarGuiasTests.cs` — con Pinecone simulado, se hace el embedding (`input_type=query`) y la consulta con `topK` y `namespace`; la herramienta devuelve id, puntuación y texto de cada fragmento.
Manual check: con claves reales en `appsettings.Local.json`, "tengo un problema para hacer pagos con la tarjeta" devuelve fragmentos con ids `KB-xx`.

- [ ] 3.1 Failing test — result: ________
- [ ] 3.2 Implementation — files: ________ (`IRetriever`, `RetrievedChunk`, `RagMcp.Pinecone`)
- [ ] 3.3 Passing test — result: ________
- [ ] 3.4 Developer approval

## Phase 4 — Sin resultados relevantes pasa a un agente humano (covers SP-1)
Test: `tests/RagMcp.Tests/MinScoreTests.cs` — si ninguna puntuación llega a `MinScore`, la herramienta devuelve "sin resultados relevantes" y ningún fragmento.
Manual check: pregunta fuera de tema ("¿cuál es la capital de Francia?") y Claude ofrece pasar el caso a un agente humano.

- [ ] 4.1 Failing test — result: ________
- [ ] 4.2 Implementation — files: ________
- [ ] 4.3 Passing test — result: ________
- [ ] 4.4 Developer approval

## Phase 5 — Consulta vacía se rechaza sin llamar a Pinecone (covers SP-2)
Test: `tests/RagMcp.Tests/BuscarGuiasTests.cs` — consulta vacía o con espacios devuelve error de entrada inválida y el Pinecone simulado no recibe ninguna llamada.
Manual check: invocar la herramienta con una cadena vacía y ver el mensaje de error.

- [ ] 5.1 Failing test — result: ________
- [ ] 5.2 Implementation — files: ________
- [ ] 5.3 Passing test — result: ________
- [ ] 5.4 Developer approval

## Phase 6 — Errores de Pinecone y configuración faltante (covers SP-3, SP-4)
Test: `tests/RagMcp.Tests/ErrorTests.cs` — Pinecone con 401, 500 o excepción de red devuelve error legible sin la clave; configuración sin `Pinecone:ApiKey` falla al arrancar nombrando la clave que falta.
Manual check: arrancar sin `appsettings.Local.json` y ver el mensaje; poner una clave inválida y ver el error de la herramienta.

- [ ] 6.1 Failing test — result: ________
- [ ] 6.2 Implementation — files: ________
- [ ] 6.3 Passing test — result: ________
- [ ] 6.4 Developer approval

## Phase 7 — Respuesta como agente del Banco William, con citas e idioma (covers HP-2, HP-3)
Test: `tests/RagMcp.Tests/BuscarGuiasTests.cs` — la instrucción devuelta incluye responder como agente del Banco William, en el idioma del usuario, citando cada id usado.
Manual check: escribir "tengo un problema para hacer pagos con la tarjeta" (y la misma frase en inglés) y ver que Claude responde como agente del banco, citando `[KB-xx]`.

- [ ] 7.1 Failing test — result: ________
- [ ] 7.2 Implementation — files: ________ (instrucción de la herramienta, README en español)
- [ ] 7.3 Passing test — result: ________
- [ ] 7.4 Developer approval
