#!/usr/bin/env python3
"""Start, stop or reset the local Docker Compose stack (US-102). Standard library only.

    python scripts/local_stack.py up      # creates .env on first use, then starts everything and waits
    python scripts/local_stack.py down    # stops the stack, keeps the local data volumes
    python scripts/local_stack.py reset   # stops the stack and deletes the local data volumes

`up` creates `.env` from `.env.example` when it is missing. Every empty value whose name contains PASSWORD or
SECRET, or ends in KEY, gets its own random value; everything else is copied unchanged. An existing `.env` is
never changed. `.env` is git-ignored and holds local, throwaway credentials only.
"""

from __future__ import annotations

import argparse
import base64
import os
import secrets
import string
import subprocess
import sys
from pathlib import Path

# Repository root. Read when a command runs, so tests can point it at a temporary folder.
ROOT = Path(__file__).resolve().parents[1]

ENV_FILE = ".env"
ENV_EXAMPLE = ".env.example"
SQL_SERVER_PASSWORD_KEY = "MSSQL_SA_PASSWORD"


def is_secret_key(name: str) -> bool:
    """True for names the script fills with a random value (PASSWORD or SECRET anywhere, or ending in KEY)."""
    upper = name.upper()
    return "PASSWORD" in upper or "SECRET" in upper or upper.endswith("KEY")


def random_value(name: str) -> str:
    """A fresh random value for one secret key, in the format the consuming service expects."""
    if name.upper() == SQL_SERVER_PASSWORD_KEY:
        return sql_server_password()
    if name.upper().endswith("KEY"):
        # Storage account keys (Azurite) must be base64.
        return base64.b64encode(secrets.token_bytes(32)).decode("ascii")
    return secrets.token_urlsafe(24)


def sql_server_password() -> str:
    """A SQL Server SA password: 32 URL-safe characters with upper case, lower case and digits.

    SQL Server needs at least 8 characters from 3 of 4 classes. URL-safe characters ([A-Za-z0-9_-]) never break
    .env files or shell quoting.
    """
    while True:
        candidate = secrets.token_urlsafe(24)
        if (
            any(char in string.ascii_uppercase for char in candidate)
            and any(char in string.ascii_lowercase for char in candidate)
            and any(char in string.digits for char in candidate)
        ):
            return candidate


def create_env_file(root: Path) -> bool:
    """Create root/.env from root/.env.example if .env is missing. Returns True if it was created."""
    target = root / ENV_FILE
    if target.exists():
        return False
    example = root / ENV_EXAMPLE
    if not example.is_file():
        raise FileNotFoundError(f"{ENV_EXAMPLE} is missing in {root}")

    lines: list[str] = []
    for raw in example.read_text(encoding="utf-8").splitlines():
        stripped = raw.strip()
        name, separator, value = stripped.partition("=")
        if stripped and not stripped.startswith("#") and separator and not value.strip() and is_secret_key(name.strip()):
            lines.append(f"{name.strip()}={random_value(name.strip())}")
        else:
            lines.append(raw)

    # Exclusive create: never overwrite a .env that appeared in the meantime.
    with target.open("x", encoding="utf-8", newline="\n") as handle:
        handle.write("\n".join(lines) + "\n")
    if os.name == "posix":
        target.chmod(0o600)
    return True


def compose(*arguments: str) -> int:
    """Run `docker compose <arguments>` in the repository root and return its exit code."""
    command = ["docker", "compose", *arguments]
    try:
        completed = subprocess.run(command, cwd=ROOT, check=False)  # noqa: S603 - fixed argument list, no shell
    except FileNotFoundError:
        print("docker was not found. Install Docker Desktop and make sure `docker` is on PATH.", file=sys.stderr)
        return 1
    return completed.returncode


def up() -> int:
    if create_env_file(ROOT):
        print(f"Created {ENV_FILE} with new random local passwords and keys.")
    else:
        print(f"Using the existing {ENV_FILE}.")
    # --build rebuilds the API, worker and mock agent from the working tree; --wait returns once every service is
    # healthy (or running) and ollama-init has pulled the model.
    code = compose("up", "--detach", "--build", "--wait")
    if code != 0:
        print("The local stack did not start. See `docker compose logs` and the README's Troubleshooting section.",
              file=sys.stderr)
    return code


def down() -> int:
    return compose("down")


def reset() -> int:
    return compose("down", "--volumes")


COMMANDS = {"up": up, "down": down, "reset": reset}


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Start, stop or reset the local Docker Compose stack.")
    parser.add_argument("command", choices=sorted(COMMANDS), help="up: start and wait; down: stop; reset: stop and delete local data")
    arguments = parser.parse_args(argv)
    return COMMANDS[arguments.command]()


if __name__ == "__main__":
    sys.exit(main())
