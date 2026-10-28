# Research: Semantic Search Page

No `NEEDS CLARIFICATION` remained in the Technical Context. Findings below come from the
existing `ragCustomerBot` project (read-only review; no secrets read).

## R1. Embedding and query path

- **Decision**: Two HTTP calls with `httpx`: `POST https://api.pinecone.io/embed` (model
  `llama-text-embed-v2`, `parameters: {input_type: "query", truncate: "END", dimension: 1024}`),
  then `POST {index_host}/query` with `vector`, `topK=3`, `includeMetadata=true`, `namespace=kb`.
  The index host comes from `GET https://api.pinecone.io/indexes/{name}` and is cached in memory
  after the first success.
- **Rationale**: Same model and dimension as the ingestion, so vectors are comparable. Pinecone
  hosts the embedding model, so one credential (`PINECONE_API_KEY`) covers everything; no OpenAI.
- **Alternatives**: Official `pinecone` Python SDK (rejected: extra dependency for three simple
  calls, and `ragCustomerBot` already proved the REST path); a different embedding model (rejected:
  vectors would not match the stored ones).

## R2. Result shape and ordering

- **Decision**: Read `matches[].id`, `score`, `metadata.{docId, section, type, text}`. Sort by
  `score` descending in the service even though Pinecone already returns them ordered. No score
  threshold; always show the 3 closest.
- **Rationale**: Matches the spec (FR-003, FR-004) and the user's decision. Defensive sort makes
  the ordering guarantee independent of the API.
- **Alternatives**: Minimum score like `ragCustomerBot` (`MinScore 0.30`): rejected by the user.

## R3. Configuration and secrets

- **Decision**: Environment variables read in `config/settings.py`; `python-dotenv` loads a local
  `.env` for convenience. Variables: `DJANGO_SECRET_KEY`, `DEBUG` (default off),
  `PINECONE_API_KEY`, `PINECONE_INDEX_NAME`, `PINECONE_NAMESPACE`, `PINECONE_EMBEDDING_MODEL`
  (default `llama-text-embed-v2`), `PINECONE_DIMENSION` (default 1024), `PINECONE_API_VERSION`
  (default `2025-04`). Missing required ones raise `ImproperlyConfigured` at startup.
- **Rationale**: Constitution III. Fail-fast avoids confusing runtime errors.
- **Alternatives**: `django-environ` (rejected: one more dependency for what `os.environ` does).

## R4. No database

- **Decision**: Do not configure `DATABASES`; omit auth, admin, sessions and messages apps. CSRF
  protection stays (cookie-based, no DB).
- **Rationale**: Nothing is persisted (FR-011) and Principle V (Simplicity).
- **Alternatives**: SQLite default (rejected: unused state and migrations).

## R5. Error handling and failure simulation without mocks

- **Decision**: The service wraps `httpx` errors, timeouts (10 s) and non-2xx responses in
  `SearchServiceError` with a generic message; the view shows a friendly text and keeps the typed
  query. The failure path (US2) is checked by hand by starting the app with an invalid
  `PINECONE_API_KEY`; Pinecone returns 401, exercising the real error path without mocks.
- **Rationale**: Constitution IV forbids mocks; a single E2E test covers the main flow and
  secondary scenarios are checked manually to keep the suite fast.
- **Alternatives**: Automated bad-key E2E test (dropped: extra server instance and slower runs);
  mock transport (rejected by constitution).

## R6. E2E harness

- **Decision**: `pytest-playwright` with a session-scoped fixture that launches
  `python manage.py runserver 127.0.0.1:<free port> --noreload` as a subprocess with the needed
  environment, waits for it to answer, and yields the base URL. The single test skips with a clear
  message if `PINECONE_API_KEY` is not set locally. Its stable query comes from
  `ragCustomerBot/eval/questions.json`: "me rechazan la tarjeta aunque tengo saldo" must include
  `KB-01` among the 3 results.
- **Rationale**: Subprocess avoids pytest-django's database-bound `live_server` fixture (the app
  has no DB) and lets the environment differ per instance.
- **Alternatives**: `live_server` fixture (rejected: requires DB setup and cannot vary env).

## R7. Loading state and duplicate submissions

- **Decision**: A tiny vanilla script on form submit disables the button and shows "Buscando…".
  Server-side rendering remains the source of truth; the page works without JS except for the
  loading indicator.
- **Rationale**: Covers US3 with minimal code; no frontend framework.
- **Alternatives**: HTMX/fetch (rejected: more moving parts than needed).

## R8. Safe rendering and logging

- **Decision**: Django template autoescaping renders query and result text; no `|safe` filters.
  The app does not log queries, API keys or response bodies; error messages shown to users are
  generic.
- **Rationale**: FR-010, FR-011, constitution III.
