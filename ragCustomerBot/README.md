# ragCustomerBot

Bot de servicio al cliente que responde **solo con la información de las guías** cargadas en Pinecone (índice `customer-service-kb`) y siguiendo unas reglas fijas de la empresa. Es la fase 2 de la prueba de concepto de RAG; la fase 1 (cargar las guías) es [`ragIngest`](../ragIngest).

> **Estado: pasos 1 a 4 de 5 terminados** (75 pruebas). Ejecutas el programa, escribes tus preguntas en la consola y el bot responde usando las guías, **recordando la conversación**: "¿y en dólares?" se entiende por lo que hablaban antes. Falta el paso 5 (calibrar con las 8 guías cargadas). Este README describe el diseño y el orden en que se construye.

### Memoria de la conversación
- En `ChatLoop` se guardan los últimos `Chat:HistoryTurns` turnos (por defecto 6; 0 = sin memoria). `/nuevo` los borra.
- **Para buscar:** si hay turnos previos, el modelo reescribe la pregunta para que se entienda sola (`QueryRewriter`) y esa versión es la que se busca en Pinecone. Cuesta una llamada pequeña extra por cada pregunta de seguimiento; la primera no la necesita.
- **Para responder:** los turnos previos se reenvían como conversación real (mensajes `user`/`assistant`) antes del mensaje con el contexto (`PromptBuilder.Build`).
- Una pregunta que falla no se recuerda. La memoria vive solo en la sesión: al cerrar el programa se pierde.

## Decisiones

| Tema | Decisión |
|---|---|
| Lenguaje y plataforma | C#, .NET 10 |
| Interfaz | Consola de chat |
| Cuándo busca | RAG clásico: cada mensaje dispara una búsqueda |
| Modelo de respuestas | OpenAI (API de chat, modelo configurable) |
| Base vectorial | Pinecone, índice `customer-service-kb`, namespace `kb`, dimensión 1024 |
| Embeddings | `llama-text-embed-v2` de Pinecone, igual que en la ingesta (con `input_type=query` al buscar) |
| Independencia | Solución autocontenida: no depende del código de `ragIngest` |

## Estructura

```
ragCustomerBot/
├─ RagCustomerBot.sln
├─ Directory.Build.props         net10.0, Nullable, ImplicitUsings
├─ README.md
├─ prompts/
│  └─ system.es.md               reglas fijas de la empresa (se edita sin recompilar)
├─ eval/
│  └─ questions.json             preguntas de prueba con la guía que debería aparecer
├─ docs/                         specs y planes de diseño (vacío por ahora)
├─ src/
│  ├─ RagBot.Core/               lógica pura, sin red: modelos, interfaces, ChatService, armado del prompt
│  ├─ RagBot.Pinecone/           Retriever: pregunta -> embedding -> búsqueda en Pinecone
│  ├─ RagBot.OpenAI/             cliente de chat contra la API de OpenAI
│  └─ RagBot.Cli/                consola, configuración (appsettings*.json) y arranque
└─ tests/
   └─ RagBot.Tests/              pruebas de todo lo anterior (clientes con respuestas simuladas)
```

Dependencias entre proyectos (las flechas indican "usa a"):

```
RagBot.Cli ──> RagBot.Core <── RagBot.Pinecone
     │             ▲
     └──────> RagBot.OpenAI
RagBot.Tests ──> los cuatro
```

`RagBot.Core` no conoce Pinecone ni OpenAI: solo define lo que necesita (un *retriever* y un modelo de chat) y los demás proyectos lo implementan. Así la lógica se prueba sin red.

## Qué hará cada pieza (por implementar)

| Pieza | Responsabilidad |
|---|---|
| `RagBot.Core` | `ChatService` (orquesta el flujo), armado del prompt, recorte del historial, compuerta por puntuación mínima. Interfaces previstas: `IRetriever`, `IChatModel`. Modelos: `ChatMessage`, `RetrievedChunk` |
| `RagBot.Pinecone` | Convierte la pregunta en vector (`POST /embed`, `input_type=query`) y consulta (`POST {host}/query`) con `topK`, `namespace` y `includeMetadata` |
| `RagBot.OpenAI` | `POST /chat/completions` con los mensajes; devuelve el texto |
| `RagBot.Cli` | Lee configuración y claves, bucle de chat, opciones `--debug` y `eval` |

## Flujo de cada mensaje

```
Mensaje del cliente
   │
   ├─ 1. Reescritura (solo si hay historial): "¿y en dólares?" -> "¿cómo pago con la tarjeta en dólares en Brasil?"
   ├─ 2. Búsqueda: embedding de la pregunta -> top-K de Pinecone con sus puntuaciones
   ├─ 3. Compuerta: si la mejor puntuación < MinScore, NO se llama al modelo y se responde NoAnswerMessage
   ├─ 4. Prompt: reglas (prompts/system.es.md) + chunks con su id + últimos turnos + pregunta
   └─ 5. Respuesta del modelo, con citas [KB-xx], y las fuentes (ids y puntuaciones)
```

Las **reglas** van siempre en el prompt de sistema y no se recuperan por búsqueda. El **conocimiento** (pasos, causas, plazos) sí se recupera.

## Configuración

`src/RagBot.Cli/appsettings.json` (se versiona, sin claves):

| Clave | Valor inicial | Para qué |
|---|---|---|
| `Pinecone:IndexName` / `Namespace` | `customer-service-kb` / `kb` | Dónde buscar |
| `Pinecone:EmbeddingModel` / `Dimension` | `llama-text-embed-v2` / `1024` | Deben coincidir con la ingesta |
| `OpenAI:Model` | `gpt-4o-mini` | Modelo de chat, configurable |
| `OpenAI:BaseUrl` | `https://api.openai.com/v1` | Endpoint |
| `Chat:TopK` | `5` | Chunks por consulta |
| `Chat:MinScore` | `0.30` | **Provisional.** Se calibra con el modo `eval` |
| `Chat:HistoryTurns` | `6` | Turnos de historial que se envían |
| `Chat:SystemPromptPath` | `prompts/system.es.md` | Reglas de la empresa |
| `Chat:NoAnswerMessage` | texto fijo | Respuesta cuando no hay información suficiente |

### Claves (nunca en git)
Copia la plantilla y pega tus claves:

```powershell
cd src\RagBot.Cli
copy appsettings.Local.example.json appsettings.Local.json
```

`appsettings.Local.json` está ignorado por el `.gitignore` de la raíz (`C:\Nortal\AIStudy`). Necesita dos claves: `Pinecone:ApiKey` y `OpenAI:ApiKey`. Nunca pegues una clave en `appsettings.json` ni en la plantilla `.example`, porque ambos se versionan.

## Cómo decide si responde (compuerta)
`ask` pide `Chat:TopK` fragmentos a Pinecone y **solo envía al modelo los que llegan a `Chat:MinScore`**. Si ninguno llega, no llama a OpenAI y responde `Chat:NoAnswerMessage`. Así, una pregunta fuera de tema no gasta tokens ni da pie a que el modelo invente. Con `--debug` ves cada fragmento con su puntuación y si entró al prompt o se descartó.

## Idioma
El bot responde en el idioma en que escribe el cliente, aunque las guías estén en español: la búsqueda es multilingüe y el modelo traduce. La instrucción va en dos sitios: en las reglas (`prompts/system.es.md`) y repetida al final del mensaje del usuario (`PromptBuilder.LanguageReminder`), porque solo con las reglas el modelo se dejaba arrastrar por el contexto en español y contestaba siempre en español.

Dos límites actuales: los nombres de menús y botones se dejan en español (así aparecen en la app), y el mensaje fijo de "no tengo información" (`Chat:NoAnswerMessage`) sale siempre en español, porque en ese caso no se llama al modelo.

## Evaluación
`eval/questions.json` tiene una pregunta de ejemplo por guía, con la guía que debería aparecer entre los resultados:

```json
{ "question": "me rechazan la tarjeta aunque tengo saldo", "expectedDocs": ["KB-01"] }
```

El modo `eval` recorre esas preguntas **solo contra Pinecone, sin llamar a OpenAI**, y dice en cuántas la guía esperada está entre los K resultados, con la puntuación de cada una. Sirve para calibrar `MinScore`, `TopK` y el tamaño de los chunks sin gastar tokens. Conviene ampliarlo a 15 o 20 preguntas reales de los chats.

```powershell
dotnet run --project src/RagBot.Cli -- eval              # usa Chat:TopK de appsettings.json
dotnet run --project src/RagBot.Cli -- eval --top 3      # otro K
dotnet run --project src/RagBot.Cli -- eval --questions otro.json
```

Salida: una línea por pregunta (`OK` / `FALLA`, posición de la primera guía correcta, puntuación del mejor resultado), el total de aciertos y el rango de puntuaciones útil para elegir `MinScore`. Código de salida: `0` todo acertado, `2` algún fallo, `1` error de uso o configuración, `3` error de Pinecone o de red.

> Para que la evaluación tenga sentido, el índice debe tener cargadas **todas** las guías que nombran las preguntas (`ragIngest`). Si falta una, sus preguntas fallan aunque el buscador esté bien.

## Orden de construcción
1. ✅ **Recuperar:** `RagBot.Pinecone` + `eval`. Comprobar que cada pregunta trae la guía correcta.
2. ✅ **Generar:** `RagBot.OpenAI` + armado del prompt + compuerta en `RagBot.Core`. Comando `ask`.
3. **Conversación:** historial y reescritura de la pregunta.
4. ✅ **Consola:** bucle de chat, `--debug`.
5. **Ajustar:** calibrar con `eval` y revisar conversaciones reales.

Cada paso se desarrolla con pruebas primero; los clientes de red se prueban con respuestas simuladas.

## Fuera de alcance por ahora
Que el modelo decida cuándo buscar (herramientas), reordenar resultados (*reranking*), búsqueda híbrida, API web, respuesta en streaming, memoria guardada entre sesiones y respuestas en portugués.

## Comandos
```powershell
dotnet build RagCustomerBot.sln
dotnet test  RagCustomerBot.sln
dotnet run --project src/RagBot.Cli                        # chat interactivo: escribes tus preguntas
dotnet run --project src/RagBot.Cli -- chat --debug        # chat con el detalle de fragmentos y prompt
dotnet run --project src/RagBot.Cli -- ask "me rechazan la tarjeta aunque tengo saldo"   # una sola pregunta
dotnet run --project src/RagBot.Cli -- eval                # evalúa la recuperación contra Pinecone (sin OpenAI)
dotnet run --project src/RagBot.Cli -- --help              # muestra el uso
```

Dentro del chat: escribe la pregunta y Enter. `/debug` activa o desactiva el detalle, `/salir` (o `salir`, `exit`, `/q`) termina. Un error en una pregunta (por ejemplo, sin saldo en OpenAI) se muestra y la sesión continúa.

En Visual Studio, elige el perfil **chat** en el desplegable de arranque y ejecuta con **Ctrl+F5**; la ventana de consola te deja escribir.

## Relación con los otros proyectos de la carpeta
| Carpeta | Qué es |
|---|---|
| `ragCustomerservice/` | Las 8 guías `.docx` (la base de conocimiento) |
| `ragIngest/` | Carga las guías a Pinecone (fase 1) |
| `pinneconeDemo/` | Scripts en Python para ver, buscar y vaciar el índice |
| `ragCustomerBot/` | Este bot (fase 2) |
