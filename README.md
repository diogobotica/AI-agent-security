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

On macOS or Linux, use `.venv/bin/` instead of `.venv/Scripts/`. The API, worker and mock agent answer `GET /health` with `{"status":"ok"}`. Docker Compose for the whole stack arrives in US-102.

## Tests

```sh
cd api && dotnet build Platform.slnx && dotnet test Platform.slnx
cd worker && pytest && ruff check . && mypy --strict src tests        # same in mock-agent
cd frontend && npm test && npm run lint && npm run build
python -m pytest tests/repo                                          # from the root, with the worker's venv
```

## Licence

Proprietary. All rights reserved. See `LICENSE`.
