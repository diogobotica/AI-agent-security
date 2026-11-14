"""HTTP surface of the worker shell: a liveness endpoint only."""

from fastapi import FastAPI

# OpenAPI docs are off: the worker exposes nothing beyond health checks.
app = FastAPI(title="worker", docs_url=None, redoc_url=None, openapi_url=None)


@app.get("/health")
def health() -> dict[str, str]:
    """Liveness check. Returns status only: no version, environment or host details."""
    return {"status": "ok"}
