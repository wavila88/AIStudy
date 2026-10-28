from django import forms

MAX_QUERY_LENGTH = 100


class SearchForm(forms.Form):
    query = forms.CharField(
        strip=True,
        max_length=MAX_QUERY_LENGTH,
        error_messages={
            "required": "Escribe una consulta para buscar.",
            "max_length": f"La consulta no puede superar los {MAX_QUERY_LENGTH} caracteres.",
        },
    )
