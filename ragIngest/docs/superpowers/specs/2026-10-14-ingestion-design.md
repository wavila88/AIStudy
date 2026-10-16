# Ingesta de guías de servicio al cliente a Pinecone — Diseño

Fecha: 2026-10-14
Estado: pendiente de revisión del usuario

## 1. Propósito

Prueba de concepto de RAG para un asistente de servicio al cliente de un neobanco (sin nombre de empresa en los documentos). Esta primera fase construye **solo la ingesta**: leer las guías `.docx`, dividirlas en chunks, mostrar un resumen y, con confirmación explícita, guardarlas en Pinecone.

Éxito de esta fase:
- Las 8 guías de `C:\Nortal\AIStudy\ragCustomerservice` se cargan en Pinecone como unos 60 a 80 chunks con metadatos.
- Reejecutar la ingesta de un documento no deja registros duplicados ni huérfanos.
- Nada se escribe en Pinecone sin que el usuario lo confirme.

## 2. Fuera de alcance

- Consulta y chat (retriever, prompt, LLM). Es la fase 2, con su propio spec.
- Memoria de conversación y reescritura de preguntas.
- Fuentes remotas (URL, SharePoint). La entrada es solo una ruta local.
- Interfaz web o API.

## 3. Decisiones acordadas

| Decisión | Elección |
|---|---|
| Lenguaje | C# (.NET) |
| Forma | Aplicación de consola |
| Entrada | Ruta local de un archivo `.docx` o de una carpeta |
| Embeddings | Los genera Pinecone con un modelo que ellos alojan (Inference API, endpoint `/embed`, modelo `llama-text-embed-v2`, dimensión 1024). Los vectores se insertan con upsert estándar. Sin proveedor adicional. |
| Índice | Uno nuevo, dedicado a este proyecto |
| Escritura | Resumen primero; inserta solo tras confirmación |

## 4. Estructura de la solución

Carpeta: `C:\Nortal\AIStudy\ragIngest`

```
ragIngest/
├─ RagIngest.sln
├─ src/
│  ├─ Rag.Core/          Chunk, IDocumentLoader, IChunker, IVectorStore
│  ├─ Rag.Ingestion/     DocxLoader, SectionChunker
│  ├─ Rag.Pinecone/      PineconeStore
│  └─ Ingest.Cli/        consola, configuración, resumen y confirmación
└─ tests/
   └─ Rag.Ingestion.Tests/
```

Responsabilidades:

- **Rag.Core:** modelos e interfaces, sin dependencias externas. `Chunk` contiene `Id`, `DocId`, `Title`, `Section`, `Type`, `ChunkIndex` y `Text`.
- **DocxLoader:** abre el `.docx` con OpenXml y devuelve el documento como una lista de secciones (título, bloques de contenido). Detecta secciones por el estilo `Heading 2`.
- **SectionChunker:** convierte las secciones en chunks aplicando las reglas de la sección 6.
- **PineconeStore:** implementa `IVectorStore`: cuenta y borra registros por `DocId`, e inserta chunks.
- **Ingest.Cli:** une todo, lee la configuración, imprime el resumen y pide confirmación.

El chunker no conoce Pinecone y Pinecone no conoce Word. Cada uno se prueba por separado.

## 5. Configuración

La ruta de origen se resuelve en este orden de prioridad:

1. Argumento `--source <ruta>`.
2. `appsettings.json`: `Ingestion:Source`.
3. Variable de entorno `INGEST_SOURCE`.

Si la ruta es una carpeta, se procesan todos sus `.docx`.

Parámetros de Pinecone (nombre del índice, namespace) van en `appsettings.json`. La API key va solo en variable de entorno o user secrets, nunca en archivos del repositorio.

## 6. Reglas de chunking

1. Cada sección `Heading 2` es un chunk.
2. Las listas numeradas, notas y recuadros, y el párrafo que los introduce, se mantienen en el chunk de su sección.
3. En la sección de preguntas frecuentes, cada par pregunta/respuesta es un chunk propio con `Type = faq`. El resto tiene `Type = guide`.
4. Cada chunk lleva el prefijo `<título del documento> > <sección>` antes del texto.
5. La tabla de metadatos inicial del documento no genera chunk. La sección "Resumen" sí.
6. Si el texto de un chunk supera 2.000 caracteres, se divide por párrafo o elemento de lista, sin cortar frases, con un solapamiento de un párrafo.
7. Un chunk de una sola línea se une al anterior.
8. Ids estables: `<DocId>-s<n>` para guías y `<DocId>-q<n>` para preguntas. El `DocId` sale del campo "ID de documento" de la tabla inicial (por ejemplo `KB-01`).

Resultado esperado para `01_rechazos_de_pago_con_tarjeta.docx`: 12 chunks (resumen, lista de verificación, rechazos que no dependen del banco, notificaciones automáticas, información a pedir, cuándo escalar, y 6 preguntas frecuentes). La lista de verificación de 8 pasos queda en un solo chunk.

## 7. Flujo de ejecución

1. Resolver la ruta y validar que existe y que los archivos son `.docx`.
2. Cargar y dividir en chunks cada documento.
3. Consultar a Pinecone cuántos registros existen ya con ese `DocId`.
4. Imprimir el resumen: por cada documento, la tabla de chunks (id, sección, caracteres, tipo), la cantidad a borrar, la cantidad a insertar, el índice y el namespace. Marcar con advertencia los chunks demasiado cortos o que superan el máximo. La opción `--show` imprime el texto completo de cada chunk.
5. Según el modo:
   - Sin flags: preguntar `¿Continuar? [s/N]`. Cualquier respuesta distinta de `s` termina sin escribir.
   - `--dry-run`: terminar tras el resumen.
   - `--yes`: omitir la pregunta.
6. Si se confirma: borrar los registros del `DocId` y luego insertar los nuevos. Hacerlo documento por documento.
7. Imprimir el resultado final por documento (por ejemplo `KB-01: 12 insertados, 0 omitidos`).

El borrado seguido de inserción hace la ingesta repetible: si un documento baja de 12 a 11 chunks, no queda un registro viejo.

## 8. Metadatos por registro en Pinecone

`docId`, `title`, `section`, `type` (`guide` o `faq`), `chunkIndex` y `text` (el texto del chunk con su prefijo). Sirven para citar la fuente en la fase 2, filtrar por documento y actualizar un documento sin tocar los demás.

## 9. Errores

- Ruta inexistente, archivo que no es `.docx` o API key ausente: terminar antes de llamar a Pinecone, con un mensaje claro.
- Documento sin tabla de metadatos o sin `DocId`: omitirlo y avisar en el resumen.
- Fallo de Pinecone durante el borrado o la inserción: informar el documento afectado y detenerse. Volver a ejecutar lo repara, porque se borra y se vuelve a insertar.

## 10. Pruebas

- **Chunker (xUnit):** con `01_rechazos_de_pago_con_tarjeta.docx` produce 12 chunks, la lista de 8 pasos está en un único chunk, cada pregunta frecuente es un chunk, la tabla de metadatos no genera chunk y todos llevan el prefijo.
- **Chunker, reglas de tamaño:** una sección de más de 2.000 caracteres se divide por párrafo y nunca a mitad de frase.
- **Flujo de confirmación:** con un `IVectorStore` falso, responder "n" no escribe nada, `--dry-run` no escribe, `--yes` escribe sin preguntar, y reejecutar no duplica.
- **Prueba manual con Pinecone real:** ingestar el documento 01 y comprobar con 3 o 4 búsquedas que los chunks recuperados son los esperados.

## 11. Decisión sobre el cliente de Pinecone (verificada 2026-10-14)

El paquete oficial `Pinecone.Client` 4.0.2 (repositorio archivado) expone `Inference.EmbedAsync` y el upsert de vectores, pero no el upsert de texto para índices con embeddings integrados, ni borrado por filtro de metadatos. Por eso:

- `Rag.Pinecone` usa la **API REST de Pinecone con `HttpClient`** (sin SDK), versión de API `2025-04`: `POST /embed`, `GET /indexes/{name}` (para el host), `GET /vectors/list?prefix=`, `POST /vectors/delete` y `POST /vectors/upsert`.
- Hay un `IEmbedder` en `Rag.Core` (implementado por `PineconeEmbedder`) además de `IVectorStore`.
- El borrado de un documento es por prefijo de id (`<DocId>-`): se listan los ids con ese prefijo y se borran por id.
- El índice es **nuevo**: `customer-service-kb`, serverless, métrica coseno, dimensión 1024. El índice de `pinneconeDemo` (dimensión 384) no es reutilizable.
- La ingesta calcula los embeddings **antes** de borrar, para que un fallo del embedder no deje el documento sin registros.
- Los metadatos incluyen además `text` (el texto del chunk), necesario para la fase 2.
- Plataforma: .NET 10 (LTS vigente). Se añade el proyecto `tests/Rag.Pinecone.Tests` para probar el cliente REST con un `HttpMessageHandler` falso.
