# worker

The test worker: Python 3.12+, one container per run, stateless. It talks to the API only through the queue and the stores. Right now it is a shell with a single liveness endpoint. The pinned test frameworks and Node.js arrive in US-306, and the queue consumer in US-302.

## Layout

- `pyproject.toml`: tool settings (pytest, ruff, mypy strict). Dependencies are not listed here.
- `requirements.txt`: runtime dependencies, pinned with `==`.
- `requirements-dev.txt`: test and lint tools, pinned with `==`. It includes `requirements.txt`.
- `src/worker/main.py`: the FastAPI app with `GET /health`. OpenAPI docs routes are off.
- `tests/`: pytest tests using FastAPI's in-process `TestClient`, with no network.

## Run locally

```sh
cd worker
python -m venv .venv
.venv/Scripts/activate            # Windows; on macOS/Linux: source .venv/bin/activate
pip install -r requirements-dev.txt
ENVIRONMENT=local uvicorn worker.main:app --app-dir src --host 127.0.0.1 --port 8001   # PowerShell: $env:ENVIRONMENT="local"; uvicorn ...
# http://127.0.0.1:8001/health -> {"status":"ok"}
```

The worker needs `ENVIRONMENT` set to `local` or `azure`, and refuses to start without it. `GET /health/ready` checks PostgreSQL, Blob (Azurite), Service Bus (emulator) and Ollama in parallel with a short timeout. It answers `200 {"status":"ready",...}` or `503 {"status":"not-ready",...}`, with `ok` or `unreachable` per check. The addresses default to the Docker Compose service names and can be changed with `DEPENDENCIES__POSTGRES__HOST`, `DEPENDENCIES__POSTGRES__PORT`, `DEPENDENCIES__BLOB__ENDPOINT`, `DEPENDENCIES__SERVICEBUS__ENDPOINT`, `DEPENDENCIES__OLLAMA__ENDPOINT` and `DEPENDENCIES__PROBETIMEOUTSECONDS`.

## Docker

Built from `Dockerfile` (pinned Python image, non-root user, port 8001). Run it as part of the local stack with `python scripts/local_stack.py up` from the repository root. See the root `README.md`.

## Tests and linters

```sh
cd worker
pytest
ruff check .
mypy --strict src tests
```
