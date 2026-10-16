"""Busca en el índice configurado en .env los chunks más parecidos a una pregunta.

Uso:
  python search.py me rechazan la tarjeta aunque tengo saldo
  python search.py -k 6 como cambio los limites
"""
import argparse

parser = argparse.ArgumentParser(description="Búsqueda por similitud.")
parser.add_argument("pregunta", nargs="*", help="la pregunta, sin comillas")
parser.add_argument("-k", "--top", type=int, default=4, help="cuántos resultados traer (por defecto 4)")
args = parser.parse_args()

from connect import pc, index, index_name, namespace  # noqa: E402

MODEL = "llama-text-embed-v2"
CAMPOS_TEXTO = ("text", "texto")

pregunta = " ".join(args.pregunta) or "me rechazan la tarjeta aunque tengo saldo"

# La dimensión debe coincidir con la del índice (384 en 'libros', 1024 en 'customer-service-kb').
dimension = pc.describe_index(index_name).dimension
embedding = pc.inference.embed(
    model=MODEL,
    inputs=[pregunta],
    parameters={"input_type": "query", "truncate": "END", "dimension": dimension},
)[0]
vector = embedding["values"] if isinstance(embedding, dict) else embedding.values

res = index.query(vector=vector, top_k=args.top, include_metadata=True, namespace=namespace)

print(f"Pregunta: {pregunta}   (índice {index_name}, namespace '{namespace or '(default)'}')\n")
for m in res.matches:
    meta = dict(m.metadata or {})
    texto = next((str(meta.pop(c)) for c in CAMPOS_TEXTO if c in meta), "")
    texto = texto.replace("\n", " ")
    if len(texto) > 140:
        texto = texto[:140] + "..."
    print(f"{m.score:.3f}  {m.id}")
    print("       " + " | ".join(f"{k}={v}" for k, v in meta.items()))
    print(f"       {texto}")
