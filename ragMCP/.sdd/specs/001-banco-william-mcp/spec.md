# MCP Banco William: consulta RAG de servicio al cliente
Size: complex — servidor nuevo, dependencia nueva (SDK MCP), integración con Pinecone y con Claude Code.

## Goal
Conectar a Claude Code un servidor MCP llamado "Banco William" que, cuando el usuario escribe un problema como cliente del banco, consulte las guías en Pinecone (RAG) y devuelva los fragmentos relevantes con el id de su vector, para que Claude responda como agente del banco citando esos ids.

## Happy path
- HP-1: Given el MCP registrado en Claude Code y el índice con las guías cargadas, when el usuario escribe "tengo un problema para hacer pagos con la tarjeta", then Claude invoca la herramienta de búsqueda del MCP y recibe los top-K fragmentos con id del vector, texto y puntuación.
- HP-2: Given fragmentos con puntuación >= `MinScore`, when Claude redacta la respuesta, then responde como agente del Banco William usando solo esos fragmentos y cita el id de cada vector usado (ej. `[KB-01]`).
- HP-3: Given que el usuario escribe en otro idioma, when se hace la consulta, then la búsqueda sigue funcionando (embeddings multilingües) y Claude responde en el idioma del usuario.

## Sad paths
- SP-1: Given que ningún fragmento llega a `MinScore`, when se invoca la búsqueda, then la herramienta devuelve "sin resultados relevantes" sin fragmentos, y Claude indica que pasará el caso a un agente humano sin inventar información.
- SP-2: Given la consulta vacía o solo espacios, when se invoca la herramienta, then devuelve un error claro de entrada inválida y no llama a Pinecone.
- SP-3: Given Pinecone caído, sin red o clave inválida, when se invoca la herramienta, then devuelve un error de la herramienta con mensaje legible (sin exponer la clave) y el servidor sigue vivo.
- SP-4: Given que falta `Pinecone:ApiKey` al arrancar, when se inicia el servidor, then falla con un mensaje que dice qué clave falta.

## Out of scope
- Llamar a OpenAI u otro LLM desde el MCP: la redacción de la respuesta la hace Claude.
- Ingesta de guías (ya la hace `ragIngest`), reranking, búsqueda híbrida, historial entre sesiones, autenticación de clientes.

## Touches
- `RagMcp.Core` → `IRetriever`, modelo `RetrievedChunk`, compuerta por `MinScore` (patrón de ragCustomerBot).
- `RagMcp.Pinecone` → retriever: embedding (`input_type=query`) y consulta al índice.
- `RagMcp.Server` → host MCP, herramienta de búsqueda, configuración `appsettings*.json`.
- `RagMcp.Tests` → xUnit con `HttpMessageHandler` simulado.

## Decisions and open questions
- Plataforma .NET 8, paquete oficial `ModelContextProtocol`, transporte stdio (Claude Code lanza el proceso).
- Índice existente `customer-service-kb`, namespace `kb`, `llama-text-embed-v2`, dim 1024; `TopK=5`, `MinScore=0.30` provisional (← ragCustomerBot).
- Una sola herramienta, `buscar_guias(pregunta)`, que devuelve id, puntuación y texto de cada fragmento más una instrucción breve: responder como agente del Banco William y citar el id.
- Registro en Claude Code con `claude mcp add banco-william -- dotnet run --project src/RagMcp.Server`; la clave va en `appsettings.Local.json` o variable de entorno, nunca en git.
- Documentación en español.
