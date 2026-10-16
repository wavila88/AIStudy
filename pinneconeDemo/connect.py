import os

from dotenv import load_dotenv
from pinecone import Pinecone

load_dotenv()

api_key = os.environ["PINECONE_API_KEY"]
index_name = os.environ["PINECONE_INDEX_NAME"]
host = os.getenv("PINECONE_HOST")
namespace = os.getenv("PINECONE_NAMESPACE", "")

pc = Pinecone(api_key=api_key)


def _limpiar(h: str) -> str:
    return h.replace("https://", "").replace("http://", "").rstrip("/")


# El host se resuelve SIEMPRE a partir del nombre del índice, así `index` y lo que mostramos
# (nombre, host) son el mismo índice. Si PINECONE_HOST está definido y no corresponde a ese
# nombre, se aborta: antes ese host mandaba en silencio y se operaba sobre otro índice.
_desc = pc.describe_index(index_name)
if host and _limpiar(host) != _limpiar(_desc.host):
    raise SystemExit(
        f"PINECONE_HOST ({_limpiar(host)}) no corresponde al índice '{index_name}' ({_limpiar(_desc.host)}).\n"
        "Borra la línea PINECONE_HOST del .env (se resuelve sola) o corrígela."
    )
index = pc.Index(host=_desc.host)

if __name__ == "__main__":
    print(f"Index: {_desc.name} | dimension: {_desc.dimension} | metric: {_desc.metric} | host: {_desc.host}")
    print(index.describe_index_stats())
