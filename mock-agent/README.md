# mock-agent

A deterministic local test agent: the only target the platform's tests and end-to-end runs send traffic to. Right now it is a shell with a single liveness endpoint, with no tools, no profiles, no instructions and no outbound calls. The `weak-*`, `fixed` and `safe` profiles (simple misconfigurations such as a disallowed tool or leaky instructions, with neutral marker values only) arrive in US-902.

## Layout

- `pyproject.toml`: tool settings (pytest, ruff, mypy strict). Dependencies are not listed here.
- `requirements.txt`: runtime dependencies, pinned with `==`.
- `requirements-dev.txt`: test and lint tools, pinned with `==`. It includes `requirements.txt`.
- `src/mock_agent/main.py`: the FastAPI app with `GET /health`. OpenAPI docs routes are off.
- `tests/`: pytest tests using FastAPI's in-process `TestClient`, with no network.

## Run locally

```sh
cd mock-agent
python -m venv .venv
.venv/Scripts/activate            # Windows; on macOS/Linux: source .venv/bin/activate
pip install -r requirements-dev.txt
uvicorn mock_agent.main:app --app-dir src --host 127.0.0.1 --port 8002
# http://127.0.0.1:8002/health -> {"status":"ok"}
```

## Tests and linters

```sh
cd mock-agent
pytest
ruff check .
mypy --strict src tests
```
