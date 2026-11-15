"""HTTP surface of the mock-agent shell: a liveness endpoint only."""

from fastapi import FastAPI

# OpenAPI docs are off: the shell exposes nothing beyond health checks.
app = FastAPI(title="mock-agent", docs_url=None, redoc_url=None, openapi_url=None)


@app.get("/health")
def health() -> dict[str, str]:
    """Liveness check. Returns status only: no version, environment or host details."""
    return {"status": "ok"}
