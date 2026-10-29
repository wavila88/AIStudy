---

description: "Task list for Clear Search Results"
---

# Tasks: Clear Search Results

**Input**: `specs/002-clear-search/spec.md`, `specs/002-clear-search/plan.md`

**Prerequisites**: plan.md, spec.md

**Tests**: TDD with Playwright (Constitution IV). In every phase the order is: write the E2E test first, run it and confirm it FAILS (red), make the change, run it again and confirm it PASSES (green). A phase is done only when its green run passes. The single existing test is extended; no new test file.

**Scope**: Only the two files below may change. No new files, dependencies, views, forms or services (see plan.md "Structure Decision").

## Format: `[ID] [P?] [Story] Description`

## Path Conventions

- Template: `search/templates/search/index.html`
- E2E test: `tests/e2e/test_search_flow.py`

---

## Phase 1: User Story 1 - Clear the results after a search (Priority: P1) ðŸŽ¯ MVP

**Goal**: A **Limpiar** link appears with results and returns the page to its initial state.

**Independent Test**: Run a search with results, confirm **Limpiar** is visible, press it, and confirm results are gone, the query box is empty and **Limpiar** is gone.

### Red

- [x] T001 [US1] In `tests/e2e/test_search_flow.py`, extend the existing test: after a search with results, assert **Limpiar** is visible, click it, then assert the results are gone, the query box is empty and **Limpiar** is gone.
- [x] T002 [US1] Run `pytest tests/e2e/test_search_flow.py` and confirm the test FAILS because **Limpiar** does not exist yet. Do not continue if it passes or fails for another reason.

### Green

- [x] T003 [US1] In `search/templates/search/index.html`, add a **Limpiar** link whose `href` is the page's own URL, rendered only when the existing `results` context is non-empty (FR-001, FR-002, FR-003, FR-005). Do not change `views.py`.
- [x] T004 [US1] In `search/templates/search/index.html`, add the few CSS lines needed to style the **Limpiar** link consistently with the existing page.
- [x] T005 [US1] Run `pytest tests/e2e/test_search_flow.py` and confirm the test PASSES.

**Checkpoint**: User Story 1 works and is verified by a test that failed first.

---

## Phase 2: User Story 2 - Button only when it makes sense (Priority: P2)

**Goal**: **Limpiar** is never shown in the initial, in-progress, validation-error, service-error or no-results states.

**Independent Test**: Load the initial page, submit an empty query and an over-limit query, and observe a search in progress; **Limpiar** is never visible.

### Red

- [x] T006 [US2] In `tests/e2e/test_search_flow.py`, extend the same test: assert **Limpiar** is not visible on the initial page, after an empty query, after an over-limit query, and while a new search is in progress.
- [x] T007 [US2] Run `pytest tests/e2e/test_search_flow.py` and confirm the test FAILS on the in-progress assertion (the initial, empty and over-limit states may already pass after Phase 1; that is expected). Do not continue if everything passes.

### Green

- [x] T008 [US2] In `search/templates/search/index.html`, extend the existing inline script so **Limpiar** is hidden while a new search is in progress (FR-004).
- [x] T009 [US2] Run `pytest tests/e2e/test_search_flow.py` and confirm the test PASSES.

**Checkpoint**: Both stories work and each was verified by a test that failed first.

---

## Phase 3: Validation

- [x] T010 Run the full E2E suite (`pytest tests/e2e`) and confirm everything passes.

---

## Dependencies & Execution Order

- Within each phase the order is strict: Red tasks â†’ confirm failure â†’ Green tasks â†’ confirm pass.
- Phase 1: T001 â†’ T002 â†’ T003 â†’ T004 â†’ T005.
- Phase 2: starts after T005. T006 â†’ T007 â†’ T008 â†’ T009.
- T010 runs last.
- No tasks are marked [P]: every task touches one of the two files already in use by another task.

## Implementation Strategy

- **Execute ONE phase per run.** When `/speckit-implement` is invoked, execute ONLY the first phase that still has unchecked tasks, then STOP. Do not start the next phase, do not run Phase 3, and wait for the user to review the code changes and ask for the next phase.
- Mark each task `[x]` as it is completed, so the next run knows which phase is next.
- MVP = Phase 1 (T001-T005). Stop and validate, then add Phase 2.
- Report the output of every test run (the red one and the green one) before moving to the next task.
- Do not implement anything outside the tasks above. If something extra seems useful, propose it instead of creating it.
