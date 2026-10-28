---

description: "Task list for Semantic Search Page"
---

# Tasks: Semantic Search Page

**Input**: Design documents from `/specs/001-semantic-search-page/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: A single Playwright E2E test covers the full main flow (type a query, submit, see ranked results) against the real Pinecone index, with no mocks. The same test also asserts the loading indicator (US3) while the search is in progress. US2 behavior is checked by hand using quickstart.md; it has no automated test by decision.

**Organization**: Tasks are grouped by user story so each can be implemented and checked independently.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: US1, US2, US3 (maps to spec.md)
- Paths are relative to the repository root `C:\Nortal\AIStudy\ragSearch`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Project initialization and basic structure

- [X] T001 Create the Django project skeleton per plan.md: `manage.py`, `config/__init__.py`, `config/wsgi.py`, `search/__init__.py`, `search/services/__init__.py`, `search/templates/search/`, `search/static/search/`, `tests/e2e/`
- [X] T002 [P] Create `requirements.txt` (Django 5.x, httpx, python-dotenv) and `requirements-dev.txt` (pytest, pytest-playwright), all versions pinned
- [X] T003 [P] Create `.gitignore` (must include `.env`, `.venv/`, `__pycache__/`, `*.pyc`) and `.env.example` listing `DJANGO_SECRET_KEY`, `DEBUG`, `PINECONE_API_KEY`, `PINECONE_INDEX_NAME`, `PINECONE_NAMESPACE`, `PINECONE_EMBEDDING_MODEL`, `PINECONE_DIMENSION`, `PINECONE_API_VERSION` with names only and no real values
- [X] T004 [P] Create `pytest.ini` with `testpaths = tests/e2e`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Config, Pinecone service, form and test harness that every user story depends on

**CRITICAL**: No user story work can begin until this phase is complete

- [X] T005 Implement `config/settings.py`: load `.env` with python-dotenv; read all variables from contracts/pinecone-consumed.md; raise `ImproperlyConfigured` at startup when `DJANGO_SECRET_KEY`, `PINECONE_API_KEY`, `PINECONE_INDEX_NAME` or `PINECONE_NAMESPACE` is missing; `DEBUG` defaults to false; defaults `PINECONE_EMBEDDING_MODEL=llama-text-embed-v2`, `PINECONE_DIMENSION=1024`, `PINECONE_API_VERSION=2025-04`; no `DATABASES`; installed apps only `django.contrib.staticfiles` and `search`; keep `CsrfViewMiddleware`
- [X] T006 [P] Create `search/services/errors.py` with `SearchServiceError` whose message is generic and never includes keys, URLs or response bodies
- [X] T007 Implement `search/services/pinecone.py`: `SearchResult` dataclass (`id`, `score`, `doc_id`, `section`, `type`, `text`; missing metadata defaults to empty string); `resolve_host()` via `GET https://api.pinecone.io/indexes/{name}` cached in memory only after success; `embed_query(text)` via `POST /embed` with `input_type=query`, `truncate=END`, `dimension` from settings; `query_index(vector)` via `POST {host}/query` with `topK=3`, `includeMetadata=true`, `namespace`; `search(text)` returning results sorted by `score` descending with NO score threshold; httpx timeout 10 s per call; wrap every httpx error, timeout, non-2xx and empty `data` in `SearchServiceError`; no write endpoints (depends on T005, T006)
- [X] T008 [P] Create `search/forms.py` with `SearchForm`: field `query`, required, stripped, max 100 characters, whitespace-only counts as empty, with clear error messages for empty and too-long
- [X] T009 Create `tests/e2e/conftest.py`: one session fixture `app_url` that starts `python manage.py runserver 127.0.0.1:<free port> --noreload` as a subprocess with the local environment, waits until it answers, yields the base URL and terminates the subprocess on teardown; skip with a clear message when `PINECONE_API_KEY` is not set locally (depends on T005)

**Checkpoint**: Foundation ready; user story work can begin

---

## Phase 3: User Story 1 - Search with a free-text query (Priority: P1) MVP

**Goal**: A user types a query and sees the 3 most similar results, best first, each with text, document, section and score.

**Independent Test**: Run the single E2E test: it opens the page, searches "me rechazan la tarjeta aunque tengo saldo", and checks the results.

### E2E test (write first, confirm it FAILS)

- [X] T010 [US1] Write `tests/e2e/test_search_flow.py` with ONE test using `app_url`: open the page, fill `query-input` with "me rechazan la tarjeta aunque tengo saldo", click `search-button`, wait for `results`; assert exactly 3 `result-item` entries, each with non-empty `result-text`, `result-doc` and `result-score`; assert the scores are in descending order; assert `KB-01` appears among the `result-doc` values; use `data-testid` selectors from contracts/search-page.md

### Implementation for User Story 1

- [X] T011 [US1] Create `search/views.py` with an `index` view: GET renders the empty form; POST validates `SearchForm`, calls `search.services.pinecone.search`, passes results to the template (views import only the service, never `httpx` or Pinecone details)
- [X] T012 [P] [US1] Create `search/urls.py` mapping `""` to `index`, and `config/urls.py` including it
- [X] T013 [US1] Create `search/templates/search/index.html`: large `<textarea data-testid="query-input">` with `{% csrf_token %}`, submit button `data-testid="search-button"`, results container `data-testid="results"` with up to 3 `data-testid="result-item"` entries containing `result-text`, `result-doc`, `result-section`, `result-score` (score with 2 decimals); rely on Django autoescaping, no `|safe`; omit empty section/type fields; Spanish UI text; responsive layout for mobile and desktop
- [X] T014 [US1] Run `pytest` and fix until the E2E test passes against the real index

**Checkpoint**: User Story 1 is fully functional (MVP)

---

## Phase 4: User Story 2 - Clear feedback for empty, invalid or failed searches (Priority: P2)

**Goal**: Empty, too-long and failed searches show a friendly message and never a blank or broken page.

**Independent Test (manual)**: Submit an empty query, a 101-character query, and a query with an invalid `PINECONE_API_KEY`; each shows its message and the typed text is preserved when a search fails.

- [X] T015 [US2] Update `search/views.py`: on invalid form re-render with errors and without calling the service; catch `SearchServiceError` and re-render with a generic service error flag while preserving the typed query; pass a `no_results` flag when the service returns zero items; never log the query or exceptions' response bodies
- [X] T016 [US2] Update `search/templates/search/index.html`: add `data-testid="form-error"`, `data-testid="service-error"` (friendly generic Spanish message), and `data-testid="no-results"` blocks; keep the textarea value from the submitted query
- [X] T017 [US2] Check by hand using the steps in `specs/001-semantic-search-page/quickstart.md` (empty, 101 characters, wrong API key, a `<script>` string rendered as text)

**Checkpoint**: User Stories 1 and 2 both work

---

## Phase 5: User Story 3 - Progress indication while searching (Priority: P3)

**Goal**: While a search runs, the user sees a loading indicator and cannot double-submit.

**Independent Test (manual)**: Submit a query and observe the "Buscando…" indicator and the disabled button until the response renders.

- [X] T018 [US3] Add an inline script to `search/templates/search/index.html` (not a static file, because static files are not served with `DEBUG` off): on form submit, disable the button and show the element `data-testid="loading"` with the text "Buscando…"
- [X] T019 [US3] Add the hidden `data-testid="loading"` element with a small spinner to `search/templates/search/index.html`; page must still work without JavaScript
- [X] T020 [US3] Extend the E2E test in `tests/e2e/test_search_flow.py` (same single test, no new test, no artificial delays): register a `page.add_init_script` that on `beforeunload` stores in `sessionStorage` whether `loading` was visible and `search-button` disabled at that moment; after the results appear, assert that value is `true` and that `loading` is hidden again (spec US3 scenario 3)

**Checkpoint**: All user stories work

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T021 [P] Verify startup fails fast with a clear message when each required variable is missing (run `python manage.py check` with one variable unset at a time)
- [X] T022 [P] Review the diff for secrets: confirm no real key in `.env.example`, logs, templates or error messages, and that `.env` is ignored by git (`git check-ignore .env`)
- [X] T023 [P] Confirm no code path writes to Pinecone (search `search/` for `upsert`, `delete`, `update`) and that no view imports `httpx`; confirm FR-007 (`no-results`) by code review since the real index always returns 3 items
- [ ] T024 Run `pytest` and the manual steps in `specs/001-semantic-search-page/quickstart.md`; fix any gap against spec.md FR-001 to FR-011

---

## Dependencies & Execution Order

- **Phase 1 Setup**: no dependencies; T002, T003, T004 run in parallel after T001.
- **Phase 2 Foundational**: depends on Phase 1; T005 first; then T006 and T008 in parallel; T007 after T005 and T006; T009 after T005. Blocks all user stories.
- **User Stories**: all depend on Phase 2. US1 is the MVP. US2 and US3 modify the same files as US1 (`views.py`, `index.html`), so implement them after US1, in priority order.
- **Polish**: after the desired stories are complete.

### Parallel opportunities

- Phase 1: T002, T003, T004.
- Phase 2: T006 and T008 together.
- T012 and T018 touch files separate from their neighbors.
- Polish: T021, T022, T023.

## Implementation Strategy

### MVP first

Complete Phase 1, Phase 2, and Phase 3 (US1); `pytest` passing means the full flow works against Pinecone.

### Incremental delivery

Add US2 (feedback), checked by hand, then US3 (loading), covered by the same E2E test.

### Notes

- The E2E test calls the real Pinecone index and needs `PINECONE_API_KEY` set locally; it skips without it.
- Never write to Pinecone; the app is read-only.
- Commit after each task or logical group.
