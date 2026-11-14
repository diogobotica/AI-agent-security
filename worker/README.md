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
uvicorn worker.main:app --app-dir src --host 127.0.0.1 --port 8001
# http://127.0.0.1:8001/health -> {"status":"ok"}
```

## Tests and linters

```sh
cd worker
pytest
ruff check .
mypy --strict src tests
```
