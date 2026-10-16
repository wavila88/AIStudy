from connect import pc, index, namespace

MODEL = "llama-text-embed-v2"  # soporta dimension=384, igual que el index

libros = [
    {"id": "libro-1", "titulo": "Cien años de soledad", "autor": "Gabriel García Márquez",
     "texto": "Saga de la familia Buendía en el pueblo ficticio de Macondo."},
    {"id": "libro-2", "titulo": "El principito", "autor": "Antoine de Saint-Exupéry",
     "texto": "Un niño de otro planeta recorre el universo y aprende sobre la amistad."},
    {"id": "libro-3", "titulo": "Clean Code", "autor": "Robert C. Martin",
     "texto": "Guía de buenas prácticas para escribir código limpio y mantenible."},
]

# 1. Texto -> embeddings
embeddings = pc.inference.embed(
    model=MODEL,
    inputs=[l["texto"] for l in libros],
    parameters={"input_type": "passage", "truncate": "END", "dimension": 384},
)

# 2. Insertar (upsert): id + vector + metadata
vectors = [
    {"id": l["id"], "values": e["values"],
     "metadata": {"titulo": l["titulo"], "autor": l["autor"], "texto": l["texto"]}}
    for l, e in zip(libros, embeddings)
]
index.upsert(vectors=vectors, namespace=namespace)
print(f"Insertados {len(vectors)} vectores")
