# Contributing

## Branches and commits

- `main` holds released, accepted work. `develop` is the integration branch and is always green.
- Each story gets `story/<story-id>` from `develop`. Each task gets `feature/<story-id>-<task-slug>` (fixes: `fix/<story-id>-<slug>`) from the story branch and is merged back with `--no-ff`.
- Commit messages follow `<type>(<task-id>): <summary>`, for example `feat(US-201-T2): add HTTP connector`.
- No force push, rebase, amend or `--no-verify`. The commit hooks block secrets, sensitive files, large files and badly formatted messages.

## Dependencies

- Every dependency is pinned to an exact version: `==` in Python requirements files, bare `x.y.z` in `package.json` (`save-exact`), and central `PackageVersion` entries in `api/Directory.Packages.props`.
- New packages go through the dependency guard: the package must exist, not look like a typosquat, be the newest stable release at least 14 days old, carry an allowed licence and have no known vulnerability. Any exception is recorded as a decision with its reason.
- Add a dependency only when the story's plan lists it, and say why.

## Tests first

Each story starts with failing tests that map to its acceptance criteria. Implementation makes them pass without changing, skipping or weakening them. Unit tests inject the clock, seed randomness, and use no real models and no network. A task is done when the build, the story's tests, the full unit suite and the linters all pass.

## Testing-engine boundary

Test content comes only from the suite registry: the pinned test frameworks and the founder's own signed suite packages in `suite-packages/`, which only the founder writes. Code in this repository builds the loader, signature check, registry and wiring around that content. It never contains hand-written attack payloads, exploit chains or bypass techniques, in code, tests or fixtures. The mock agent's weaknesses are simple misconfigurations, fixtures and canary-lite values use neutral marker strings, tests stop at proof, and test traffic goes only to the local mock agent.

## Data

Never commit real client data (evidence, transcripts, exports). Tests use synthetic data only. Secrets never go in code, configuration, logs, fixtures or responses.
