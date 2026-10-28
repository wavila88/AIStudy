# Quickstart: Semantic Search Page

Validation guide for the feature. Runs against the existing Pinecone index `customer-service-kb`.

## Prerequisites

- Python 3.12+
- A Pinecone API key with read access to the dev index `customer-service-kb` (namespace `kb`)
- Network access to Pinecone

## Setup

```powershell
python -m venv .venv
.venv\Scripts\Activate.ps1
pip install -r requirements.txt -r requirements-dev.txt
playwright install chromium
copy .env.example .env     # then fill in values locally; .env is gitignored
```

Required variables are listed in [contracts/pinecone-consumed.md](contracts/pinecone-consumed.md).

## Run the app

```powershell
python manage.py runserver
```

Open http://127.0.0.1:8000/.

## Manual validation

| Step | Expected |
|------|----------|
| Type "me rechazan la tarjeta aunque tengo saldo" and submit | 3 results, highest score first, one from `KB-01` |
| Submit an empty box | "enter a query" message, no results |
| Paste more than 100 characters | Limit message |
| Start with a wrong `PINECONE_API_KEY` and search | Friendly error, typed text still in the box |
| Missing `PINECONE_API_KEY` at startup | App refuses to start with a clear message |

## Automated validation (E2E)

```powershell
pytest tests/e2e
```

There is a single E2E test, `tests/e2e/test_search_flow.py`. It starts the app itself and
drives it with Playwright against the real Pinecone index: it types "me rechazan la tarjeta
aunque tengo saldo", submits, and checks that 3 results appear in descending score order with
`KB-01` among them. It skips with a clear message when `PINECONE_API_KEY` is not set.

The same test also checks the loading indicator (US3): at the moment the search is submitted,
"Buscando…" is visible and the button disabled, and the indicator is hidden again when results
appear. The empty, too-long and service-failure scenarios (US2) are checked by hand with the
table above.
