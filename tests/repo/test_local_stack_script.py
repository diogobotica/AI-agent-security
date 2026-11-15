"""Tests for scripts/local_stack.py, the one command that brings the local stack up (US-102).

The script is loaded from its file and run in a temporary folder: its module-level `ROOT` is replaced,
and `subprocess.run` is replaced by a recorder, so no Docker command ever runs and the real `.env` is
never touched. Fixture values are neutral markers.

Contract the tests rely on:
- `local_stack.ROOT` is the repository root (a `Path`), read when a command runs.
- `local_stack.main(argv)` takes the arguments after the script name and returns the exit code.
- Docker is called through `subprocess.run([...])` with an argument list starting `docker compose`.
- `up` creates `ROOT/.env` from the template `ROOT/infra/local/env.template` (D-019) when `.env` is missing:
  every key whose name contains PASSWORD or SECRET or ends in KEY (or _KEY) and is empty in the template gets
  a random value; other values are copied. An existing `.env` is never changed.
"""

from __future__ import annotations

import importlib.util
import re
import string
import subprocess
from collections.abc import Callable, Iterator, Sequence
from pathlib import Path
from types import ModuleType
from typing import Any

import pytest

REPO_ROOT = Path(__file__).resolve().parents[2]
SCRIPT = REPO_ROOT / "scripts" / "local_stack.py"

ENV_TEMPLATE = Path("infra") / "local" / "env.template"
ENV_TEMPLATE_FIXTURE = """\
# Local stack settings (fixture)
OLLAMA_MODEL=neutral-model-marker
POSTGRES_USER=marker_user
POSTGRES_PASSWORD=
PLATFORM_APP_PASSWORD=
MSSQL_SA_PASSWORD=
AZURITE_ACCOUNT_KEY=
"""
SECRET_KEYS = ("POSTGRES_PASSWORD", "PLATFORM_APP_PASSWORD", "MSSQL_SA_PASSWORD", "AZURITE_ACCOUNT_KEY")


class CommandRecorder:
    """Stands in for subprocess.run and records each command instead of running it."""

    def __init__(self, returncode: int = 0) -> None:
        self.returncode = returncode
        self.calls: list[list[str]] = []

    def __call__(self, args: Sequence[str], *_: Any, **__: Any) -> subprocess.CompletedProcess[str]:
        command = [str(part) for part in args]
        self.calls.append(command)
        return subprocess.CompletedProcess(command, self.returncode, stdout="", stderr="")

    def compose_calls(self, subcommand: str) -> list[list[str]]:
        return [call for call in self.calls if call[:2] == ["docker", "compose"] and subcommand in call[2:]]


@pytest.fixture
def local_stack() -> Iterator[ModuleType]:
    assert SCRIPT.is_file(), "scripts/local_stack.py is missing"
    spec = importlib.util.spec_from_file_location("local_stack_under_test", SCRIPT)
    assert spec is not None and spec.loader is not None
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    yield module


@pytest.fixture
def run_in(local_stack: ModuleType, monkeypatch: pytest.MonkeyPatch) -> Callable[..., tuple[int, CommandRecorder]]:
    """Run the script's main() against a temporary root with Docker replaced by a recorder."""

    def run(root: Path, argv: list[str], returncode: int = 0) -> tuple[int, CommandRecorder]:
        recorder = CommandRecorder(returncode)
        monkeypatch.setattr(local_stack, "ROOT", root)
        monkeypatch.setattr(subprocess, "run", recorder)
        return local_stack.main(argv), recorder

    return run


def _fresh_root(tmp_path: Path, name: str = "repo") -> Path:
    root = tmp_path / name
    template = root / ENV_TEMPLATE
    template.parent.mkdir(parents=True)
    template.write_text(ENV_TEMPLATE_FIXTURE, encoding="utf-8")
    return root


def _read_env(path: Path) -> dict[str, str]:
    values: dict[str, str] = {}
    for raw in path.read_text(encoding="utf-8").splitlines():
        line = raw.strip()
        if line and not line.startswith("#"):
            name, _, value = line.partition("=")
            values[name.strip()] = value.strip().strip('"').strip("'")
    return values


def test_env_file_generated_with_random_secrets(
    tmp_path: Path, run_in: Callable[..., tuple[int, CommandRecorder]]
) -> None:
    first_root = _fresh_root(tmp_path, "first")
    second_root = _fresh_root(tmp_path, "second")

    run_in(first_root, ["up"])
    run_in(second_root, ["up"])

    assert (first_root / ".env").is_file(), "up did not create .env from infra/local/env.template"
    first = _read_env(first_root / ".env")
    second = _read_env(second_root / ".env")
    assert first["OLLAMA_MODEL"] == "neutral-model-marker", "non-secret values must be copied unchanged"
    assert first["POSTGRES_USER"] == "marker_user", "non-secret values must be copied unchanged"
    for key in SECRET_KEYS:
        assert len(first.get(key, "")) >= 16, f"{key} must get a random value of at least 16 characters"
        assert first[key] != second[key], f"{key} is the same in two fresh .env files, so it is not random"
    first_secrets = [first[key] for key in SECRET_KEYS]
    assert len(set(first_secrets)) == len(first_secrets), "each secret must get its own value"


def test_env_file_never_overwritten(tmp_path: Path, run_in: Callable[..., tuple[int, CommandRecorder]]) -> None:
    root = _fresh_root(tmp_path)
    existing = "POSTGRES_PASSWORD=marker-existing-value\nOLLAMA_MODEL=marker-existing-model\n"
    (root / ".env").write_text(existing, encoding="utf-8")

    run_in(root, ["up"])
    run_in(root, ["up"])

    assert (root / ".env").read_text(encoding="utf-8") == existing


def test_sql_password_meets_complexity(tmp_path: Path, run_in: Callable[..., tuple[int, CommandRecorder]]) -> None:
    # SQL Server refuses SA passwords shorter than 8 characters or using fewer than 3 of the 4 character classes.
    classes = (string.ascii_uppercase, string.ascii_lowercase, string.digits)
    for attempt in range(20):
        root = _fresh_root(tmp_path, f"attempt-{attempt}")
        run_in(root, ["up"])
        password = _read_env(root / ".env").get("MSSQL_SA_PASSWORD", "")

        assert len(password) >= 8, f"MSSQL_SA_PASSWORD too short: {len(password)} characters"
        used = sum(any(char in group for char in password) for group in classes)
        used += any(not char.isalnum() for char in password)
        assert used >= 3, "MSSQL_SA_PASSWORD must use at least 3 of: upper case, lower case, digits, symbols"
        assert not re.search(r"[\s'\"$`\\#=]", password), (
            "MSSQL_SA_PASSWORD must not contain characters that break .env or shell quoting"
        )


def test_up_runs_compose_up_wait(tmp_path: Path, run_in: Callable[..., tuple[int, CommandRecorder]]) -> None:
    root = _fresh_root(tmp_path)

    exit_code, recorder = run_in(root, ["up"])

    assert exit_code == 0
    up_calls = recorder.compose_calls("up")
    assert len(up_calls) == 1, f"expected one `docker compose ... up` call, got {recorder.calls}"
    up = up_calls[0]
    assert "-d" in up or "--detach" in up, f"compose up must run detached: {up}"
    assert "--wait" in up, f"compose up must wait until services are healthy: {up}"
    assert (root / ".env").is_file(), ".env must exist before compose starts"


def test_up_returns_nonzero_when_compose_fails(
    tmp_path: Path, run_in: Callable[..., tuple[int, CommandRecorder]]
) -> None:
    root = _fresh_root(tmp_path)

    exit_code, recorder = run_in(root, ["up"], returncode=1)

    assert recorder.compose_calls("up"), "compose up was not called"
    assert exit_code != 0, "a failing `docker compose up` must make the script fail"


def test_down_stops_stack_and_keeps_volumes(
    tmp_path: Path, run_in: Callable[..., tuple[int, CommandRecorder]]
) -> None:
    root = _fresh_root(tmp_path)

    exit_code, recorder = run_in(root, ["down"])

    assert exit_code == 0
    down_calls = recorder.compose_calls("down")
    assert len(down_calls) == 1, f"expected one `docker compose ... down` call, got {recorder.calls}"
    assert "-v" not in down_calls[0] and "--volumes" not in down_calls[0], "down must keep the local volumes"


def test_reset_removes_local_volumes(tmp_path: Path, run_in: Callable[..., tuple[int, CommandRecorder]]) -> None:
    root = _fresh_root(tmp_path)

    exit_code, recorder = run_in(root, ["reset"])

    assert exit_code == 0
    down_calls = recorder.compose_calls("down")
    assert len(down_calls) == 1, f"expected one `docker compose ... down` call, got {recorder.calls}"
    assert "-v" in down_calls[0] or "--volumes" in down_calls[0], "reset must remove the local volumes"


def test_unknown_command_fails_without_running_docker(
    tmp_path: Path, local_stack: ModuleType, monkeypatch: pytest.MonkeyPatch
) -> None:
    root = _fresh_root(tmp_path)
    recorder = CommandRecorder()
    monkeypatch.setattr(local_stack, "ROOT", root)
    monkeypatch.setattr(subprocess, "run", recorder)

    try:
        exit_code = local_stack.main(["unknown-marker-command"])
    except SystemExit as stop:  # argparse exits on an unknown choice
        exit_code = stop.code if isinstance(stop.code, int) else 1

    assert exit_code != 0
    assert recorder.calls == [], "an unknown command must not run Docker"
    assert not (root / ".env").exists(), "an unknown command must not create .env"
