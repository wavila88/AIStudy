import os
import socket
import subprocess
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path

import pytest
from dotenv import load_dotenv

ROOT = Path(__file__).resolve().parents[2]


def _free_port():
    with socket.socket() as s:
        s.bind(("127.0.0.1", 0))
        return s.getsockname()[1]


@pytest.fixture(scope="session")
def app_url():
    load_dotenv(ROOT / ".env")
    if not os.environ.get("PINECONE_API_KEY"):
        pytest.skip("PINECONE_API_KEY is not set locally; the E2E test needs the real Pinecone index.")

    port = _free_port()
    proc = subprocess.Popen(
        [sys.executable, "manage.py", "runserver", f"127.0.0.1:{port}", "--noreload"],
        cwd=ROOT,
        env=os.environ.copy(),
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
    )
    url = f"http://127.0.0.1:{port}/"
    try:
        deadline = time.time() + 30
        while time.time() < deadline:
            if proc.poll() is not None:
                pytest.fail("The Django app exited at startup; check the environment variables in .env.")
            try:
                urllib.request.urlopen(url, timeout=2)
                break
            except (urllib.error.URLError, OSError):
                time.sleep(0.5)
        else:
            pytest.fail("The Django app did not start in time.")
        yield url
    finally:
        proc.terminate()
        try:
            proc.wait(timeout=10)
        except subprocess.TimeoutExpired:
            proc.kill()
