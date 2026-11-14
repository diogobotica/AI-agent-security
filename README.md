# AI Agent Security Testing Platform (working name)

Automated subscription testing for AI agents: finds weaknesses, proves them with mechanical evidence (deterministic rule checks and customer replay), fixes them and proves the fix. Tests in English, Portuguese and Spanish. EU-hosted on Azure.

## Folders

- `context/`: project knowledge and rules. Start with `context/README.md`; project rules are in `context/PROJECT.md`, planning documents in `context/docs/`.
- `stories/`: one folder per user story (scope, plan, checkpoints, reports, manual test, log), the board and `tracker.html`.
- `.workflow/`: this project's guard-policy additions (`policy.json`) and the plugin's refreshed copy (`kit/`).
- `.claude/`, `.githooks/`, `.github/`, `start.cmd` and `CLAUDE.md` come from the flow plugin in `Desktop/ai-agentic-workflow` (set up with `/flow:init`); change them there, not here.

## Working with the agents

Double-click `start.cmd` (or run `.\start.cmd` in this folder): it checks Python, Git and Claude Code, then starts the loop, where agents build the stories one after another. You only do the manual test per story (`stories/US-xxx/manual-test.md`), then `/flow:accept US-xxx` or `/flow:reject US-xxx what is wrong`. `/flow:status` shows what waits for you; `/flow:pause` stops the loop after the current step; `/flow:stop` stops it now and saves where every agent left off. In the Claude Code window, use ↑/↓ and Enter to open any agent and type to it, Esc to interrupt it, Ctrl+T for the task list. Keep the small start window open: it restarts the session after context handoffs. `start.cmd ignore-usage` keeps going past the 90% weekly usage reserve.
