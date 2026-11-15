"""HTTP surface of the worker shell: liveness and readiness endpoints only."""

from collections.abc import AsyncIterator
from contextlib import asynccontextmanager
from typing import Annotated

from fastapi import Depends, FastAPI
from fastapi.responses import JSONResponse

from worker.readiness import DependencyProbe, get_probes, run_checks
from worker.settings import load_settings, read_environment


@asynccontextmanager
async def lifespan(_: FastAPI) -> AsyncIterator[None]:
    """Stop startup unless ENVIRONMENT is local or azure, and unless the dependency settings are valid."""
    read_environment()
    load_settings()
    yield


# OpenAPI docs are off: the worker exposes nothing beyond health checks.
app = FastAPI(title="worker", docs_url=None, redoc_url=None, openapi_url=None, lifespan=lifespan)


@app.get("/health")
def health() -> dict[str, str]:
    """Liveness check. Returns status only: no version, environment or host details."""
    return {"status": "ok"}


@app.get("/health/ready", response_model=None)
def ready(probes: Annotated[list[DependencyProbe], Depends(get_probes)]) -> JSONResponse:
    """Readiness: 200 when every dependency answers, otherwise 503 naming the unreachable ones."""
    checks = run_checks(probes, load_settings().probe_timeout_seconds)
    is_ready = all(value == "ok" for value in checks.values())
    return JSONResponse(
        status_code=200 if is_ready else 503,
        content={"status": "ready" if is_ready else "not-ready", "checks": checks},
    )
