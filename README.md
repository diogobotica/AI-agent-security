# AI agent security testing platform

The platform tests companies' AI agents and chatbots across the prompt, data and tools layers. It proves each weakness with mechanical evidence (deterministic rule checks and canary-lite values), gives an agent-specific fix, and proves the fix with a retest. Reports map to the OWASP LLM and Agentic Top 10, the EU AI Act and DORA. It is hosted in the EU on Azure (Sweden Central).

## Folders

| Folder | What it holds |
| --- | --- |
| `api/` | .NET (LTS) backend: ASP.NET Core controllers, EF Core and PostgreSQL later. Owns all state and business logic. |
| `worker/` | Python test worker, one stateless container per run. Talks to the API only through the queue and the stores. |
| `frontend/` | Next.js App Router web app (TypeScript strict, Tailwind CSS, shadcn/ui). No business logic. |
| `infra/` | Terraform (azurerm) for our own Azure infrastructure in Sweden Central. |
| `mock-agent/` | Deterministic local test agent. The only target tests and end-to-end runs send traffic to. |
| `docs/` | Developer documentation for this repository (contributing rules). |
| `e2e/` | Playwright end-to-end tests against the local stack (added in US-101-T7). |
| `suite-packages/` | Founder-only: the founder's own signed test suites. Never written or edited by anyone else. |
| `tests/` | Repository-level checks (layout, exact pins, licence, ignore rules). |

Each component folder has its own `README.md` with details.

## Prerequisites

- .NET 10 SDK (the version is pinned in `api/global.json`)
- Python 3.12 or newer
- Node.js LTS (22.13 or newer)

## Run locally

Each part runs on its own port. From the repository root:

| Part | Port | Commands |
| --- | --- | --- |
| API | 5080 | `cd api && dotnet run --project src/Platform.Api` |
| Worker | 8001 | `cd worker && python -m venv .venv && .venv/Scripts/pip install -r requirements-dev.txt && .venv/Scripts/uvicorn worker.main:app --app-dir src --host 127.0.0.1 --port 8001` |
| Mock agent | 8002 | `cd mock-agent && python -m venv .venv && .venv/Scripts/pip install -r requirements-dev.txt && .venv/Scripts/uvicorn mock_agent.main:app --app-dir src --host 127.0.0.1 --port 8002` |
| Frontend | 3000 | `cd frontend && npm ci && npm run dev` (or `npm run build && npm run start`) |

On macOS or Linux, use `.venv/bin/` instead of `.venv/Scripts/`. The API, worker and mock agent answer `GET /health` with `{"status":"ok"}`.

## Local stack (Docker Compose)

The whole backend, with its local stand-ins for the Azure services, starts with one command. You need Docker Desktop (or Docker Engine with Compose v2) and Python 3.12 or newer. From the repository root:

```sh
python scripts/local_stack.py up
```

The first time, this creates `.env` from the template `infra/local/env.template` and fills every password and key with a new random value. `.env` is git-ignored and only ever holds throwaway local credentials. The command then builds the API, worker and mock agent, starts everything and waits until every service is healthy. On the first run it also downloads the local model (about 400 MB), which takes a few minutes. Later runs reuse it.

| Service | Address on your machine | What it is |
| --- | --- | --- |
| API | http://127.0.0.1:5080 (`/health`, `/health/ready`) | The platform API (`ENVIRONMENT=local`) |
| Worker | http://127.0.0.1:8001 (`/health`, `/health/ready`) | The test worker (`ENVIRONMENT=local`) |
| Mock agent | http://127.0.0.1:8002 | The local test agent |
| PostgreSQL | 127.0.0.1:**55432** | Database `platform`, app role `platform_app` (not a superuser, cannot bypass row-level security) |
| Azurite (Blob) | http://127.0.0.1:10000 | Local stand-in for Azure Blob Storage |
| Service Bus emulator | 127.0.0.1:5672 (AMQP), http://127.0.0.1:5300/health | Queues `run-jobs` and `run-events` |
| Ollama | http://127.0.0.1:11434 | Local model server; the model is set by `OLLAMA_MODEL` in `.env` |

Every port listens on 127.0.0.1 only. PostgreSQL uses host port 55432 so it never clashes with a PostgreSQL installed on your machine. Inside the stack, services still reach it at `postgres:5432`.

`GET /health/ready` on the API and the worker answers `200 {"status":"ready","checks":{...}}` when PostgreSQL, Blob, Service Bus and Ollama all answer, and `503 {"status":"not-ready",...}` otherwise. Each check shows `ok` or `unreachable`.

```sh
python scripts/local_stack.py down    # stop the stack, keep the data (database, blobs, model)
python scripts/local_stack.py reset   # stop the stack and delete the local data volumes
```

The frontend runs outside Docker: `cd frontend && npm run dev`. Its home page shows "API reachable" or "API unreachable" for the API at `API_BASE_URL`. When `API_BASE_URL` is not set, development (`npm run dev`) uses http://127.0.0.1:5080; a production build must set it.

### Troubleshooting

- **SQL Server or the Service Bus emulator does not start, or the machine runs out of memory.** SQL Server (which the emulator needs) wants at least 2 GB of memory. Give Docker Desktop at least 6 GB (Settings → Resources) and close other heavy programs. `docker compose logs mssql servicebus` shows the cause.
- **"ports are not available" or "port is already allocated".** Another program uses one of the ports above. Stop that program, or find it with `netstat -ano | findstr :<port>` (Windows) or `lsof -i :<port>` (macOS and Linux).
- **The Service Bus emulator restarts or reports errors at first.** It waits for SQL Server to be healthy, and SQL Server can take a minute on a cold start. The API and worker readiness checks show `servicebus: unreachable` until it is up. Wait, or run `python scripts/local_stack.py up` again.
- **The model download fails or is slow.** Run `python scripts/local_stack.py up` again; it continues where it stopped. `docker compose logs ollama-init` shows progress.
- **"required variable … is missing a value" after an update.** A newer `infra/local/env.template` added keys that your existing `.env` does not have (the script never changes an existing `.env`). Copy the new lines from the template to the end of `.env` and run `python scripts/local_stack.py up` again.
- **Need new local passwords, or `.env` is broken.** Run `python scripts/local_stack.py reset`, delete `.env`, then run `python scripts/local_stack.py up`. It creates a new `.env`. The reset is needed because the database keeps the passwords it was first created with.
- **An image download fails with "connection reset" or "EOF".** It's a network problem; run the command again.

## Tests

```sh
cd api && dotnet build Platform.slnx && dotnet test Platform.slnx
cd worker && pytest && ruff check . && mypy --strict src tests        # same in mock-agent
cd frontend && npm test && npm run lint && npm run build
python -m pytest tests/repo                                          # from the root, with the worker's venv
```

## Licence

Proprietary. All rights reserved. See `LICENSE`.
