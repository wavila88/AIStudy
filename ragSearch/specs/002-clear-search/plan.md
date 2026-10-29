# Implementation Plan: Clear Search Results

**Branch**: `002-clear-search` | **Date**: 2026-10-28 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/002-clear-search/spec.md`

## Summary

Add a **Limpiar** control to the existing search page. It is shown only when the server rendered
a non-empty result list, and it takes the user back to the initial page (empty query box, no
results, no messages) without contacting Pinecone. The simplest design is a plain link to the
page's own URL: a GET returns the empty form, so no new view logic, no service call and no
JavaScript are needed for clearing. The existing inline script also hides the control while a new
search is in progress. Verification extends the single existing Playwright E2E.

## Technical Context

**Language/Version**: Python 3.12+ (project runs 3.13), Django 5.2

**Primary Dependencies**: Unchanged from feature 001 (Django, httpx, python-dotenv); no new dependencies

**Storage**: None

**Testing**: Extend `tests/e2e/test_search_flow.py` (pytest-playwright, real Pinecone, no mocks)

**Target Platform**: Same as 001 (desktop and mobile browsers)

**Project Type**: Web application (server-rendered Django, single project)

**Performance Goals**: Clearing is a local page load with no external calls; completes in under 2 s (SC-001)

**Constraints**: No call to Pinecone when clearing (FR-005); nothing stored or logged

**Scale/Scope**: One template change (plus a few CSS lines); no change to views, forms or services

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | How the plan satisfies it |
|-----------|--------|---------------------------|
| I. Django on Python | PASS | The template decides visibility from the existing `results` context; no logic added to the view |
| II. Isolated Service Layer | PASS | Untouched; clearing never calls the service layer or Pinecone |
| III. Secrets in env vars | PASS | No new configuration or secrets |
| IV. E2E with Playwright | PASS | The existing single E2E is extended with the Limpiar steps; no new test file, no new run |
| V. Simplicity | PASS | A plain link instead of JavaScript state handling; no new dependency, layer or file |
| Tech constraints | PASS | Read-only Pinecone, input validation and `DEBUG` rules are unaffected |

Post-design re-check: PASS. Complexity Tracking not needed.

## Project Structure

### Documentation (this feature)

```text
specs/002-clear-search/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── search-page.md        # delta to the 001 page contract
├── checklists/
│   └── requirements.md
└── tasks.md                  # created later by /speckit-tasks
```

### Source Code (repository root)

```text
search/
└── templates/search/
    └── index.html            # CHANGED: Limpiar link (shown only with results) + hide it on submit
tests/
└── e2e/
    └── test_search_flow.py   # CHANGED: extended with the Limpiar steps (same single test)
```

**Structure Decision**: No new files in the application. `views.py`, `forms.py`, `services/`,
`config/` and `urls.py` stay as they are, because the existing GET on the page's URL already
returns the initial state.

## Complexity Tracking

No constitution violations; nothing to justify.
