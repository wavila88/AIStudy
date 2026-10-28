from django.shortcuts import render

from .forms import SearchForm
from .services.errors import SearchServiceError
from .services.pinecone import search


def index(request):
    context = {"results": None, "service_error": False, "no_results": False, "query": ""}

    if request.method == "POST":
        form = SearchForm(request.POST)
        context["query"] = request.POST.get("query", "")
        if form.is_valid():
            try:
                results = search(form.cleaned_data["query"])
            except SearchServiceError:
                context["service_error"] = True
            else:
                context["results"] = results
                context["no_results"] = not results
    else:
        form = SearchForm()

    context["form"] = form
    return render(request, "search/index.html", context)
