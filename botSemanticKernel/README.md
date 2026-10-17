# botSemanticKernel

Un bot de consola, **básico**, para aprender Semantic Kernel (SK). Toma pedidos de **hamburguesas con gaseosa** en una hamburguesería inventada ("Burger Kernel"):

- recuerda la conversación,
- si le preguntas algo que no es tomar el pedido, explica que solo se encarga de pedidos,
- da un resumen del pedido cuando se lo pides.

## Cómo ejecutarlo
1. Copia la plantilla y pega tu clave de OpenAI:
   ```powershell
   cd src\BotSemanticKernel
   copy appsettings.Local.example.json appsettings.Local.json
   ```
   `appsettings.Local.json` está ignorado por git. También sirve la variable de entorno `OPENAI_API_KEY`.
2. Ejecuta:
   ```powershell
   dotnet run --project src/BotSemanticKernel              # chat
   dotnet run --project src/BotSemanticKernel -- --debug   # chat mostrando qué herramientas llama el modelo
   dotnet test                                             # 28 pruebas de la lógica (sin OpenAI, sin costo)
   ```

Dentro del chat: `/resumen` (imprime el pedido, sin llamar al modelo), `/nuevo` (empezar de cero), `/debug` (ver u ocultar las herramientas), `/salir`.

## Qué leer y en qué orden
| Archivo | Qué contiene |
|---|---|
| `Program.cs` | **Todo Semantic Kernel está aquí**, en 4 pasos numerados |
| `OrderPlugin.cs` | Las *herramientas* que el modelo puede llamar (`[KernelFunction]`) |
| `SystemPrompt.cs` | Las instrucciones del bot: qué hace y qué responde si le piden otra cosa |
| `Order.cs` y `Menu.cs` | El pedido y la carta: C# normal, sin nada de IA |
| `ToolLogger.cs` | Un *filtro* que imprime cada herramienta llamada (para ver el ciclo) |

## La idea central
El modelo **habla**; el código **sabe**. El modelo no guarda el pedido ni conoce los precios: cuando el cliente pide algo, el modelo *decide llamar a una función* y el código hace el trabajo real.

```
Cliente: "quiero 2 clásicas y una cola"
   │
   ▼
Semantic Kernel envía al modelo: [reglas] + [historial] + [mensaje] + [lista de herramientas disponibles]
   │
   ▼
El modelo responde: "llama a AddItem(name=Clásica, quantity=2)"      ← no escribe texto todavía
   │
   ▼
Semantic Kernel ejecuta OrderPlugin.AddItem(...)  →  modifica el objeto Order  →  devuelve "Agregado: 2 x Clásica..."
   │
   ▼
Semantic Kernel vuelve a llamar al modelo con ese resultado
   │
   ▼
El modelo ahora sí redacta: "Listo, agregué 2 clásicas. ¿Quieres una gaseosa?"
```

Ese ir y venir es lo que ves en pantalla con `--debug`, en gris:
```
  [herramienta] AddItem(name=Clásica, quantity=2)
  [resultado]   Agregado: 2 x Clásica. | Pedido actual: | 2 x Clásica — $36.000 | Total: $36.000
```

## Conceptos de Semantic Kernel que aparecen
| Concepto | Dónde | Qué es |
|---|---|---|
| **Kernel** | `Program.cs` paso 2 | El objeto central: junta el modelo de chat y los plugins |
| **`AddOpenAIChatCompletion`** | paso 2.1 | Le dice al Kernel que use OpenAI (modelo y clave) |
| **Plugin** | `OrderPlugin.cs`, `AddFromObject` | Una clase cuyos métodos pueden ser llamados por el modelo |
| **`[KernelFunction]` + `[Description]`** | `OrderPlugin.cs` | Marca un método como herramienta. El texto de `Description` es **lo único que el modelo sabe** de ella: se envía a OpenAI para que decida cuándo usarla |
| **`ChatHistory`** | `Program.cs` paso 3 | La lista de mensajes de la conversación. Se reenvía completa en cada turno: **eso es la memoria** |
| **`FunctionChoiceBehavior.Auto()`** | paso 3 | El modelo decide solo cuándo llamar a una herramienta y Semantic Kernel la ejecuta automáticamente |
| **Filtro** | `ToolLogger.cs` | Código que se ejecuta alrededor de cada herramienta (aquí, para imprimir) |

## Cómo cumple lo que pediste
- **Solo hamburguesas con gaseosa:** `SystemPrompt.cs` lo define, y además el código lo hace cumplir: `ConfirmOrder` rechaza un pedido sin hamburguesa o sin gaseosa.
- **Otros temas:** el prompt indica explicar amablemente que solo toma pedidos. Es una instrucción al modelo, no un bloqueo duro; un modelo puede equivocarse alguna vez.
- **Recuerda la conversación:** `ChatHistory` (se guarda `history.AddUserMessage` / `AddAssistantMessage` en cada turno).
- **Resumen del pedido:** la herramienta `GetOrderSummary` lo arma el **código**, así los números siempre cuadran. Lo que el modelo muestra es lo que esa función devuelve.

## Lo que este bot NO hace (a propósito, para mantenerlo simple)
- **La historia crece sin límite** durante la sesión. En una conversación larga cada mensaje cuesta más. Semantic Kernel trae *reducers* (recortar o resumir el historial) para eso.
- **No guarda nada al cerrar:** el pedido y la conversación viven en memoria.
- **El menú es fijo** en `Menu.cs`.
- **Sin pagos, direcciones ni nombre del cliente.**

## Ideas para seguir aprendiendo
1. Añade una herramienta nueva en `OrderPlugin.cs` (por ejemplo `SetDeliveryAddress`) y mira cómo el modelo la descubre sin tocar nada más.
2. Cambia el menú y observa que el bot lo respeta sin cambiar el prompt.
3. Prueba un *reducer* para limitar el historial.
4. Cambia `FunctionChoiceBehavior.Auto()` por `None()` y verás qué pasa si el modelo no puede usar herramientas.
