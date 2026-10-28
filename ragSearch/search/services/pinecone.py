"""Read-only access to Pinecone: embed the query, then query the existing index."""
from dataclasses import dataclass

import httpx
from django.conf import settings

from .errors import SearchServiceError

CONTROL_PLANE_URL = "https://api.pinecone.io"
TOP_K = 3
TIMEOUT_SECONDS = 10

_host_cache = {}


@dataclass(frozen=True)
class SearchResult:
    id: str
    score: float
    doc_id: str = ""
    section: str = ""
    type: str = ""
    text: str = ""


def _client():
    return httpx.Client(
        timeout=TIMEOUT_SECONDS,
        headers={
            "Api-Key": settings.PINECONE_API_KEY,
            "X-Pinecone-API-Version": settings.PINECONE_API_VERSION,
            "Accept": "application/json",
        },
    )


def _json(response):
    if response.status_code >= 400:
        raise SearchServiceError()
    return response.json()


def resolve_host(client):
    name = settings.PINECONE_INDEX_NAME
    if name in _host_cache:
        return _host_cache[name]
    data = _json(client.get(f"{CONTROL_PLANE_URL}/indexes/{name}"))
    host = data.get("host")
    if not host:
        raise SearchServiceError()
    host = host if host.startswith("http") else f"https://{host}"
    _host_cache[name] = host  # cached only after a successful lookup
    return host


def embed_query(client, text):
    data = _json(
        client.post(
            f"{CONTROL_PLANE_URL}/embed",
            json={
                "model": settings.PINECONE_EMBEDDING_MODEL,
                "parameters": {
                    "input_type": "query",
                    "truncate": "END",
                    "dimension": settings.PINECONE_DIMENSION,
                },
                "inputs": [{"text": text}],
            },
        )
    )
    items = data.get("data") or []
    if not items:
        raise SearchServiceError()
    return items[0]["values"]


def query_index(client, host, vector):
    data = _json(
        client.post(
            f"{host}/query",
            json={
                "vector": vector,
                "topK": TOP_K,
                "includeMetadata": True,
                "namespace": settings.PINECONE_NAMESPACE,
            },
        )
    )
    results = []
    for match in data.get("matches") or []:
        meta = match.get("metadata") or {}
        results.append(
            SearchResult(
                id=match.get("id", ""),
                score=float(match.get("score", 0)),
                doc_id=str(meta.get("docId", "")),
                section=str(meta.get("section", "")),
                type=str(meta.get("type", "")),
                text=str(meta.get("text", "")),
            )
        )
    return results


def search(text):
    """Return the 3 closest results, best score first, with no score threshold."""
    try:
        with _client() as client:
            host = resolve_host(client)
            vector = embed_query(client, text)
            results = query_index(client, host, vector)
    except SearchServiceError:
        raise
    except (httpx.HTTPError, KeyError, ValueError, TypeError):
        raise SearchServiceError() from None
    return sorted(results, key=lambda r: r.score, reverse=True)[:TOP_K]
