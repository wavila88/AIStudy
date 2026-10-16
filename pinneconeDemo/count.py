from connect import index

stats = index.describe_index_stats()
print(f"Total de vectores: {stats.total_vector_count}")
for name, ns in stats.namespaces.items():
    print(f"  namespace '{name or '(default)'}': {ns.vector_count}")
