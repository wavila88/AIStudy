"""Borra TODOS los registros del índice configurado en .env (de todos los namespaces).

Es destructivo y no se puede deshacer, por eso:
  - muestra primero qué índice es y cuántos registros hay en cada namespace;
  - pide escribir el nombre exacto del índice para confirmar.

Uso:
  python vaciar.py                       # muestra el resumen y pide confirmación
  python vaciar.py --dry-run             # solo muestra el resumen, no borra nada
  python vaciar.py --namespace kb        # borra solo ese namespace
  python vaciar.py --confirmar NOMBRE    # sin pregunta; NOMBRE debe ser el nombre exacto del índice
"""
import argparse
import sys
import time

parser = argparse.ArgumentParser(description="Borra todos los registros del índice.")
parser.add_argument("--namespace", help="borra solo este namespace (por defecto, todos los que haya)")
parser.add_argument("--dry-run", action="store_true", help="muestra el resumen y termina sin borrar")
parser.add_argument("--confirmar", metavar="NOMBRE", help="nombre exacto del índice; evita la pregunta interactiva")
args = parser.parse_args()

from connect import pc, index, index_name  # noqa: E402  (se importa después para que --help no necesite .env)


def etiqueta(ns: str) -> str:
    return ns or "(default)"


def conteo_por_namespace() -> dict:
    stats = index.describe_index_stats()
    return {name: summary.vector_count for name, summary in stats.namespaces.items()}


namespaces = conteo_por_namespace()
if args.namespace is not None:
    if args.namespace not in namespaces:
        print(f"El namespace '{etiqueta(args.namespace)}' no existe o está vacío en el índice '{index_name}'.")
        print(f"Namespaces con datos: {[etiqueta(n) for n in namespaces] or 'ninguno'}")
        sys.exit(1)
    namespaces = {args.namespace: namespaces[args.namespace]}

total = sum(namespaces.values())
desc = pc.describe_index(index_name)

print(f"Índice : {index_name}  (dimensión {desc.dimension})")
print(f"Host   : {desc.host}")
for ns, count in namespaces.items():
    print(f"  namespace {etiqueta(ns)!r}: {count} registros")
print(f"Total a borrar: {total} registros")

if total == 0:
    print("No hay nada que borrar.")
    sys.exit(0)

if args.dry_run:
    print("Modo --dry-run: no se borró nada.")
    sys.exit(0)

if args.confirmar is not None:
    escrito = args.confirmar
else:
    print("\nEsto borra los registros de forma permanente.")
    escrito = input(f"Para confirmar escribe el nombre del índice ({index_name}): ")

if escrito.strip() != index_name:
    print("El nombre no coincide. Cancelado, no se borró nada.")
    sys.exit(1)

fallos = 0
for ns in namespaces:
    try:
        index.delete(delete_all=True, namespace=ns)
        print(f"Borrado el namespace {etiqueta(ns)!r}.")
    except Exception as exc:  # un namespace que falla no debe impedir borrar los demás
        fallos += 1
        print(f"No se pudo borrar el namespace {etiqueta(ns)!r}: {exc}")

# Las estadísticas de Pinecone se actualizan con retraso: se espera un poco antes de informar.
restantes = None
for _ in range(15):
    restantes = sum(c for n, c in conteo_por_namespace().items() if n in namespaces)
    if restantes == 0:
        break
    time.sleep(2)

if restantes == 0:
    print("Listo: el índice quedó vacío.")
else:
    print(f"Las estadísticas aún muestran {restantes} registros; suelen tardar unos segundos en actualizarse. "
          "Vuelve a correr count.py en un momento.")

sys.exit(1 if fallos else 0)
