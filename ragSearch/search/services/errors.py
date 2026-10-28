class SearchServiceError(Exception):
    """The search could not be completed.

    The message is deliberately generic: it never carries keys, URLs or response bodies.
    """

    def __init__(self, message="La búsqueda no está disponible en este momento."):
        super().__init__(message)
