# Contract: Pinecone calls consumed by the app

The app is read-only. It MUST NOT call any upsert, update or delete endpoint. All requests send
headers `Api-Key: $PINECONE_API_KEY`, `X-Pinecone-API-Version: $PINECONE_API_VERSION`,
`Accept: application/json`. Timeout: 10 s per call.

## 1. Resolve index host

`GET https://api.pinecone.io/indexes/{PINECONE_INDEX_NAME}` → JSON with `host`. The app prefixes
`https://` if absent and caches the host in memory after the first success.

## 2. Embed the query

`POST https://api.pinecone.io/embed`

```json
{
  "model": "llama-text-embed-v2",
  "parameters": { "input_type": "query", "truncate": "END", "dimension": 1024 },
  "inputs": [ { "text": "<query>" } ]
}
```

Response: `data[0].values` = list of 1024 floats. An empty `data` is treated as a service error.

## 3. Query the index

`POST https://{host}/query`

```json
{
  "vector": [ "...1024 floats..." ],
  "topK": 3,
  "includeMetadata": true,
  "namespace": "kb"
}
```

Response: `matches[]` each with `id`, `score`, `metadata { docId, section, type, text }`. The
app sorts by `score` descending and maps to `SearchResult` ([data-model.md](../data-model.md)).

## Error mapping

| Condition | App behavior |
|-----------|--------------|
| HTTP 401/403 (bad key) | `SearchServiceError`; user sees generic error |
| HTTP 4xx/5xx, network error, timeout | `SearchServiceError`; user sees generic error |
| Zero `matches` | Outcome `no_results` |

Error messages shown to users never include response bodies, keys or URLs.

## Environment variables

| Variable | Required | Default |
|----------|----------|---------|
| `DJANGO_SECRET_KEY` | yes | none |
| `DEBUG` | no | `false` |
| `PINECONE_API_KEY` | yes | none |
| `PINECONE_INDEX_NAME` | yes | none (dev value: `customer-service-kb`) |
| `PINECONE_NAMESPACE` | yes | none (dev value: `kb`) |
| `PINECONE_EMBEDDING_MODEL` | no | `llama-text-embed-v2` |
| `PINECONE_DIMENSION` | no | `1024` |
| `PINECONE_API_VERSION` | no | `2025-04` |
