# Contract: Search Page (exposed by the app)

## `GET /`

Returns the search page with an empty form. Status `200`, `text/html`.

## `POST /`

Form fields (`application/x-www-form-urlencoded`, CSRF token required):

| Field | Required | Rules |
|-------|----------|-------|
| `query` | yes | Stripped; non-empty; at most 100 characters |

Responses (always `text/html`, status `200` so the page re-renders with a message; `403` only for
a missing/invalid CSRF token):

| Outcome | Page contains |
|---------|---------------|
| Valid, results found | List of up to 3 results ordered by score descending; each shows `text`, `docId`, `section`, score |
| Empty / whitespace-only | Error "enter a query"; no Pinecone call |
| Longer than 100 chars | Error stating the 100-char limit; no Pinecone call |
| Zero items returned | "No results found" message |
| Service failure or timeout | Friendly generic error; no technical detail; textarea keeps the typed query |

## Stable selectors (for E2E tests)

| Element | `data-testid` |
|---------|---------------|
| Query textarea | `query-input` |
| Submit button | `search-button` |
| Loading indicator | `loading` |
| Validation error | `form-error` |
| Service error | `service-error` |
| No-results message | `no-results` |
| Results container | `results` |
| One result item | `result-item` (with children `result-text`, `result-doc`, `result-section`, `result-score`) |

## Behavior

- While a submission is in progress the button is disabled and `loading` is visible.
- All user and result text is HTML-escaped.
