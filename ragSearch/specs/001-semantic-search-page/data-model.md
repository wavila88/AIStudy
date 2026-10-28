# Data Model: Semantic Search Page

Nothing is persisted by this app. These are in-memory structures used during a request.

## SearchQuery (form input)

| Field | Type | Rules |
|-------|------|-------|
| `query` | string | Required; surrounding whitespace stripped; whitespace-only counts as empty (FR-005); max 100 characters (FR-006) |

Lifecycle: exists only for the request. It is echoed back into the textarea when the search
fails or is invalid (FR-008). Never stored or logged (FR-011).

## SearchResult (service output)

| Field | Source in Pinecone | Notes |
|-------|--------------------|-------|
| `id` | `matches[].id` | Vector id |
| `score` | `matches[].score` | Float; shown with 2 decimals as the relevance indicator |
| `doc_id` | `metadata.docId` | Source document, e.g. `KB-01` |
| `section` | `metadata.section` | Section within the document |
| `type` | `metadata.type` | Content type of the chunk; informational |
| `text` | `metadata.text` | Matched content shown to the user |

Rules:
- The list has at most 3 items, sorted by `score` descending (FR-003, FR-004).
- Missing metadata fields default to an empty string; the page omits empty fields.
- No filtering by score.

## SearchOutcome (view state)

| State | Shown to the user |
|-------|-------------------|
| `initial` | Empty form |
| `invalid` | Form error message (empty or too long), query preserved |
| `results` | Up to 3 `SearchResult` items |
| `no_results` | "No results found" message (service returned zero items) |
| `error` | Friendly generic error message, query preserved |

Transitions: `initial` → (submit) → `invalid` | `results` | `no_results` | `error`; any state →
(submit) → any outcome.

## Configuration (environment)

See [contracts/pinecone-consumed.md](contracts/pinecone-consumed.md) and `.env.example`. Required
at startup: `DJANGO_SECRET_KEY`, `PINECONE_API_KEY`, `PINECONE_INDEX_NAME`,
`PINECONE_NAMESPACE`. Optional with defaults: `DEBUG` (false), `PINECONE_EMBEDDING_MODEL`
(`llama-text-embed-v2`), `PINECONE_DIMENSION` (1024), `PINECONE_API_VERSION` (`2025-04`).
