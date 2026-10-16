# ragIngest

Ingesta de guías de servicio al cliente (`.docx`) a un índice de Pinecone. Fase 1 de una prueba de concepto de RAG.

## Requisitos
- .NET 10 SDK
- Un índice de Pinecone `customer-service-kb` (serverless, coseno, dimensión 1024)
- La variable de entorno `PINECONE_API_KEY` (o user secret `Pinecone:ApiKey`)

## Uso
```bash
# resumen sin escribir nada
dotnet run --project src/Ingest.Cli -- --source "<archivo o carpeta>" --dry-run

# resumen + pregunta + escritura
dotnet run --project src/Ingest.Cli -- --source "<archivo o carpeta>"

# sin pregunta; mostrar el texto completo de cada chunk
dotnet run --project src/Ingest.Cli -- --source "<archivo o carpeta>" --yes --show
```

La ruta de origen sale de `--source`, luego de `Ingestion:Source` en `appsettings.json`, luego de `INGEST_SOURCE`.
Códigos de salida: 0 correcto, 1 error de entrada o sin documentos válidos, 2 se omitió algún documento, 3 falló la ingesta.

## Búsqueda manual
`./scripts/search.ps1 "tu pregunta"` devuelve los chunks más cercanos.

## Pruebas
`dotnet test RagIngest.sln`
