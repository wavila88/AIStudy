import os
from pathlib import Path

from django.core.exceptions import ImproperlyConfigured
from dotenv import load_dotenv

BASE_DIR = Path(__file__).resolve().parent.parent

load_dotenv(BASE_DIR / ".env")


def _env(name, default=None, required=False):
    """Read an environment variable; empty values count as missing."""
    value = os.environ.get(name, "").strip()
    if value:
        return value
    if required:
        raise ImproperlyConfigured(
            f"Missing required environment variable {name}. See .env.example."
        )
    return default


SECRET_KEY = _env("DJANGO_SECRET_KEY", required=True)
DEBUG = _env("DEBUG", "false").lower() in ("1", "true", "yes")
ALLOWED_HOSTS = ["127.0.0.1", "localhost"]

PINECONE_API_KEY = _env("PINECONE_API_KEY", required=True)
PINECONE_INDEX_NAME = _env("PINECONE_INDEX_NAME", required=True)
PINECONE_NAMESPACE = _env("PINECONE_NAMESPACE", required=True)
PINECONE_EMBEDDING_MODEL = _env("PINECONE_EMBEDDING_MODEL", "llama-text-embed-v2")
PINECONE_DIMENSION = int(_env("PINECONE_DIMENSION", "1024"))
PINECONE_API_VERSION = _env("PINECONE_API_VERSION", "2025-04")

INSTALLED_APPS = [
    "django.contrib.staticfiles",
    "search",
]

MIDDLEWARE = [
    "django.middleware.security.SecurityMiddleware",
    "django.middleware.common.CommonMiddleware",
    "django.middleware.csrf.CsrfViewMiddleware",
]

ROOT_URLCONF = "config.urls"

TEMPLATES = [
    {
        "BACKEND": "django.template.backends.django.DjangoTemplates",
        "DIRS": [],
        "APP_DIRS": True,
        "OPTIONS": {"context_processors": ["django.template.context_processors.request"]},
    }
]

WSGI_APPLICATION = "config.wsgi.application"

# Nothing is persisted (FR-011): no database is configured.
DATABASES = {}

LANGUAGE_CODE = "es"
USE_I18N = False
USE_TZ = True

STATIC_URL = "static/"
DEFAULT_AUTO_FIELD = "django.db.models.BigAutoField"
