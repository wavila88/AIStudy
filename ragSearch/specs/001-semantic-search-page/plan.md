# Implementation Plan: Semantic Search Page

**Branch**: `001-semantic-search-page` | **Date**: 2026-10-28 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/001-semantic-search-page/spec.md`

## Summary

A single Django page with a large textarea. On submit, the server validates the query, asks
Pinecone to embed it (`llama-text-embed-v2`, `input_type=query`), queries the existing
`customer-service-kb` index (namespace `kb`) for the 3 closest vectors with no score threshold,
sorts them by score descending and renders them server-side with text, source document
(`docId`), section and score. All Pinecone/embedding logic lives in `search/services/`; views
only handle HTTP. Configuration and credentials come from environment variables. The app is
read-only against Pinecone. Quality is verified only by Playwright E2E tests against the running
app and the real dev Pinecone index.

## Technical Context

**Language/Version**: Python 3.12+

**Primary Dependencies**: Django 5.x, httpx, python-dotenv (load local `.env`); dev/test: pytest, pytest-playwright

**Storage**: None. No Django database; queries are not stored (FR-011). Source data lives in the existing Pinecone index (read-only).

**Testing**: pytest + pytest-playwright, E2E only, against the app started as a subprocess and the real dev Pinecone index (constitution IV)

**Target Platform**: Developer machine / dev environment, desktop and mobile browsers

**Project Type**: Web application (server-rendered Django, single project)

**Performance Goals**: Results within 3 s for 95% of searches (SC-002); two sequential Pinecone calls per search

**Constraints**: Query max 100 chars; httpx timeout 10 s per call; no secrets in repo/logs/errors; Pinecone read-only

**Scale/Scope**: One page, one view, one service; 3 results per search; ~8 documents currently in the index

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | How the plan satisfies it |
|-----------|--------|---------------------------|
| I. Django on Python | PASS | Django project, views only parse/validate/render |
| II. Isolated Service Layer | PASS | `search/services/pinecone.py` owns embed + query; view imports only the service. No upsert/delete method exists |
| III. Secrets in env vars | PASS | `PINECONE_API_KEY`, `DJANGO_SECRET_KEY` etc. from env; `.env.example`; `.env` gitignored; settings fail fast when missing |
| IV. E2E with Playwright | PASS | `tests/e2e/` with pytest-playwright against real dev Pinecone; one E2E test for the main flow; secondary scenarios checked by hand; no mocks |
| V. Simplicity | PASS | No DB, no JS framework, no cache layer; one extra dependency set (httpx, python-dotenv) justified in research.md |
| Tech constraints | PASS | Embedding model matches the ingestion (`llama-text-embed-v2`, dim 1024); input validated and length-limited; deps pinned; DEBUG env-driven and off by default |

Post-design re-check: PASS (no violations; Complexity Tracking not needed).

## Project Structure

### Documentation (this feature)

```text
specs/001-semantic-search-page/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── search-page.md        # UI contract exposed by the app
│   └── pinecone-consumed.md  # external Pinecone calls the app depends on
└── tasks.md                  # created later by /speckit-tasks
```

### Source Code (repository root)

```text
manage.py
requirements.txt              # pinned runtime deps
requirements-dev.txt          # pinned test deps (pytest, pytest-playwright)
pytest.ini
.env.example                  # variable names only, no values
.gitignore                    # includes .env
config/
├── settings.py               # env-driven, fail-fast on missing vars, no DATABASES
├── urls.py
└── wsgi.py
search/
├── forms.py                  # SearchForm: required, stripped, max 100 chars
├── views.py                  # GET/POST handling, calls services only
├── urls.py
├── services/
│   ├── __init__.py
│   ├── pinecone.py           # embed_query(), query_index(), search() -> list[SearchResult]
│   └── errors.py             # SearchServiceError (no secrets in message)
├── templates/search/
│   └── index.html            # textarea, errors, results, loading state (inline script)
tests/
└── e2e/
    ├── conftest.py           # starts the app as a subprocess with the local environment
    └── test_search_flow.py   # the single E2E: query -> 3 ranked results incl. KB-01
```

**Structure Decision**: Single Django project with one app (`search`). Settings in `config/`.
No database is configured because nothing is persisted; sessions/auth/admin apps are left out.
E2E tests start the real app with `manage.py runserver` in a subprocess so the same code path
used in dev is exercised.

## Complexity Tracking

No constitution violations; nothing to justify.
