"""Muestra los registros del índice configurado en .env, ordenados por docId y chunkIndex.

Uso:
  python ver.py                 # todos, con el texto recortado
  python ver.py --doc KB-01     # solo los chunks de ese documento
  python ver.py --full          # texto completo
  python ver.py --limit 5       # solo los primeros 5
"""
import argparse

parser = argparse.ArgumentParser(description="Lista los registros del índice.")
parser.add_argument("--doc", help="muestra solo este docId (ej. KB-01)")
parser.add_argument("--full", action="store_true", help="imprime el texto completo")
parser.add_argument("--limit", type=int, default=0, help="máximo de registros (0 = todos)")
args = parser.parse_args()

from connect import index, namespace  # noqa: E402

PREVIEW = 160
CAMPOS_TEXTO = ("text", "texto")


def listar_ids(prefix=None):
    ids = []
    for page in index.list(prefix=prefix, namespace=namespace):
        ids.extend(item.id for item in page.vectors)
    return ids


def traer(ids, tamano=100):
    registros = {}
    for i in range(0, len(ids), tamano):
        registros.update(index.fetch(ids=ids[i:i + tamano], namespace=namespace).vectors)
    return registros


ids = listar_ids(prefix=f"{args.doc}-" if args.doc else None)
if not ids:
    print(f"No hay registros en el namespace '{namespace or '(default)'}'.")
    raise SystemExit(0)

registros = traer(ids)


def orden(item):
    id_, v = item
    meta = v.metadata or {}
    return (str(meta.get("docId", "")), meta.get("chunkIndex", 0), id_)


ordenados = sorted(registros.items(), key=orden)
if args.limit:
    ordenados = ordenados[:args.limit]

for id_, v in ordenados:
    meta = dict(v.metadata or {})
    texto = next((str(meta.pop(c)) for c in CAMPOS_TEXTO if c in meta), "")
    print(f"\nID: {id_}")
    print("  " + " | ".join(f"{k}={val}" for k, val in meta.items()))
    if not args.full and len(texto) > PREVIEW:
        texto = texto[:PREVIEW].replace("\n", " ") + "..."
    print(f"  texto: {texto if args.full else texto.replace(chr(10), ' ')}")
    print(f"  vector: {len(v.values)} números, primeros 5 = {[round(x, 3) for x in v.values[:5]]}")

print(f"\n{len(ordenados)} de {len(ids)} registros mostrados (namespace '{namespace or '(default)'}').")
