"""Worker settings read from environment variables (US-102).

ENVIRONMENT chooses the implementations of external services (CLAUDE.md rule 2): only "local" and "azure"
are accepted. The dependency addresses use the same variable names as the API's ``Dependencies`` section
(``DEPENDENCIES__POSTGRES__HOST`` and so on), so the compose file can give both services the same values.
They hold hosts, ports and endpoints only, never credentials.
"""

import os
from collections.abc import Mapping
from dataclasses import dataclass
from enum import StrEnum
from urllib.parse import urlsplit

ENVIRONMENT_VARIABLE = "ENVIRONMENT"


class DeploymentEnvironment(StrEnum):
    """Where the worker runs."""

    LOCAL = "local"
    AZURE = "azure"


@dataclass(frozen=True)
class Settings:
    """Validated worker settings."""

    environment: DeploymentEnvironment
    postgres_host: str
    postgres_port: int
    blob_endpoint: str
    servicebus_endpoint: str
    ollama_endpoint: str
    probe_timeout_seconds: float


# Defaults are the service names and ports of the local Docker Compose stack.
_DEFAULTS: dict[str, str] = {
    "DEPENDENCIES__POSTGRES__HOST": "postgres",
    "DEPENDENCIES__POSTGRES__PORT": "5432",
    "DEPENDENCIES__BLOB__ENDPOINT": "http://azurite:10000/",
    "DEPENDENCIES__SERVICEBUS__ENDPOINT": "http://servicebus:5300/health",
    "DEPENDENCIES__OLLAMA__ENDPOINT": "http://ollama:11434/api/tags",
    "DEPENDENCIES__PROBETIMEOUTSECONDS": "2",
}


def read_environment(env: Mapping[str, str] | None = None) -> DeploymentEnvironment:
    """Return the validated ENVIRONMENT value; raise ValueError if it is missing or unknown."""
    source = os.environ if env is None else env
    value = source.get(ENVIRONMENT_VARIABLE, "")
    try:
        return DeploymentEnvironment(value)
    except ValueError:
        raise ValueError(
            f'The {ENVIRONMENT_VARIABLE} environment variable must be set to "local" or "azure".'
        ) from None


def load_settings(env: Mapping[str, str] | None = None) -> Settings:
    """Read and validate every worker setting; raise ValueError naming the variable that is wrong."""
    source = os.environ if env is None else env

    def get(name: str) -> str:
        return source.get(name) or _DEFAULTS[name]

    return Settings(
        environment=read_environment(source),
        postgres_host=get("DEPENDENCIES__POSTGRES__HOST"),
        postgres_port=_port(get("DEPENDENCIES__POSTGRES__PORT"), "DEPENDENCIES__POSTGRES__PORT"),
        blob_endpoint=_http_url(get("DEPENDENCIES__BLOB__ENDPOINT"), "DEPENDENCIES__BLOB__ENDPOINT"),
        servicebus_endpoint=_http_url(get("DEPENDENCIES__SERVICEBUS__ENDPOINT"), "DEPENDENCIES__SERVICEBUS__ENDPOINT"),
        ollama_endpoint=_http_url(get("DEPENDENCIES__OLLAMA__ENDPOINT"), "DEPENDENCIES__OLLAMA__ENDPOINT"),
        probe_timeout_seconds=_positive_seconds(
            get("DEPENDENCIES__PROBETIMEOUTSECONDS"), "DEPENDENCIES__PROBETIMEOUTSECONDS"
        ),
    )


def _port(value: str, name: str) -> int:
    if not value.isdigit() or not 0 < int(value) < 65536:
        raise ValueError(f"{name} must be a TCP port number.")
    return int(value)


def _http_url(value: str, name: str) -> str:
    parts = urlsplit(value)
    if parts.scheme not in ("http", "https") or not parts.hostname:
        raise ValueError(f"{name} must be an absolute http or https URL.")
    return value


def _positive_seconds(value: str, name: str) -> float:
    try:
        seconds = float(value)
    except ValueError:
        raise ValueError(f"{name} must be a number of seconds.") from None
    if not seconds > 0:
        raise ValueError(f"{name} must be positive.")
    return seconds
