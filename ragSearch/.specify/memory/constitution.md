<!--
Sync Impact Report (temporary; remove before commit)
- Version change: 2.1.0 → 2.2.0
- Modified principles:
  - IV. End-to-End Tests with Playwright (one E2E test for the main flow per feature instead of
    one per user story; secondary scenarios may be checked by hand)
- Added sections: none
- Removed sections: none
- Updated sections: Technology & Security Constraints (removed dev-only/production-credential
  rule)
- Follow-up TODOs: none. Unit/integration tests with mocks are out of scope for now and may
  be added in a future amendment. Embedding provider/model still decided in /speckit-plan.
-->
# ragSearch Constitution

## Core Principles

### I. Django on Python
The backend MUST be a Django application written in Python. Views handle HTTP concerns only
(request parsing, validation, response rendering). Business and integration logic MUST NOT
live in views, templates, or models' request handling.
Rationale: a single, conventional framework keeps the project predictable and reviewable.

### II. Isolated Service Layer
All embedding generation and all Pinecone access MUST live in a dedicated services module
(e.g., `search/services/`). Views MUST call these services and MUST NOT import the Pinecone
client or the embedding provider SDK directly. The application MUST only query Pinecone; it
MUST NOT create, upsert, update, or delete vectors or indexes.
Rationale: isolating external dependencies keeps them replaceable, and the data already in
Pinecone is owned by another process and must not be altered by this app.

### III. Secrets Only in Environment Variables (NON-NEGOTIABLE)
Credentials and secrets (Pinecone API key and index/environment settings, embedding provider
keys, Django `SECRET_KEY`) MUST be read from environment variables and MUST NOT be committed
to the repository, logs, or error messages. The repo MUST ship a `.env.example` containing
variable names without real values, and `.env` MUST be listed in `.gitignore`. The app MUST
fail fast at startup with a clear error when a required variable is missing.
Rationale: leaked credentials are the highest-impact avoidable risk in this project.

### IV. End-to-End Tests with Playwright
The test strategy is end-to-end only, using Playwright (pytest-playwright). E2E tests MUST
run against the running Django app and against the real Pinecone index and embedding provider
that hold the existing data, using credentials from local environment variables. Each feature
MUST have at least one E2E test that covers its main end-to-end flow; covering every
acceptance scenario with automated tests is NOT required, and secondary scenarios MAY be
checked by hand. The test MUST use a query whose expected result is stable over the existing
data (for example, a specific known document appears in the results). Keep the suite small:
slow E2E runs are a cost to avoid.
Unit and integration tests with mocks are out of scope for now and MAY be introduced by a
future amendment.
Rationale: the main risk is the integration between the UI, embeddings, and Pinecone, which
only real end-to-end runs can verify.

### V. Simplicity
Start with the simplest design that satisfies the specification (YAGNI). New dependencies,
layers, or abstractions MUST be justified in the feature's `plan.md`.
Rationale: a small search app does not need speculative infrastructure.

## Technology & Security Constraints

- Language/framework: Python 3.12+ and Django.
- Vector store: Pinecone, read-only access from this app. The existing dev index is the data
  source; the embedding model used for queries MUST match the one used to load that index.
- The embedding provider and model are chosen per feature in `/speckit-plan` and MUST be
  accessed only through the service layer.
- User input MUST be validated and length-limited before being embedded or queried.
- Dependencies MUST be pinned in `requirements.txt` (or equivalent lockfile).
- `DEBUG` MUST be driven by an environment variable and MUST be off by default.

## Development Workflow & Quality Gates

- Every change is made through the Spec Kit flow: specify → plan → tasks → implement.
- E2E tests require network access to Pinecone and valid local credentials; they MUST pass before a
  change is considered complete.
- Code reviews MUST verify compliance with this constitution, including a check that no
  secret or real credential appears in the diff.
- Code MUST follow the project formatter/linter once configured in the plan.

## Governance

This constitution supersedes other practices. Amendments require a documented change via
`/speckit-constitution`, a version bump, and a review of open plans for conflicts.
Versioning follows semantic versioning: MAJOR for removed or redefined principles, MINOR for
added principles or materially expanded guidance, PATCH for wording clarifications.
Every plan and pull request MUST be checked for compliance; deviations MUST be justified in
the plan's complexity notes or the amendment is rejected.

**Version**: 2.2.0 | **Ratified**: 2026-10-28 | **Last Amended**: 2026-10-28
