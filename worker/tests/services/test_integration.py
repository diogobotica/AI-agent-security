"""Worker services against the US-102 local stack (US-103 criteria 4, 5 and 8).

Marked ``integration``: a plain ``pytest`` leaves them out. With the stack up (``python scripts/local_stack.py up``)
run ``pytest -m integration``. Azurite and Ollama are reached on the loopback ports docker-compose.yml publishes;
the Azurite account and key and the model name come from the repository's git-ignored .env. Only neutral marker
content is written.
"""

import uuid
from collections.abc import Callable
from pathlib import Path

import pytest

from worker.services.protocols import ModelRequest
from worker.services.select import select_services

pytestmark = pytest.mark.integration

REPO_ROOT = Path(__file__).resolve().parents[3]
BLOB_CONNECTION_REF = "markerblob"  # read from SECRETS__MARKERBLOB below


def _dotenv() -> dict[str, str]:
    path = REPO_ROOT / ".env"
    assert path.is_file(), "The repository .env is missing: run python scripts/local_stack.py up first."
    values: dict[str, str] = {}
    for raw in path.read_text(encoding="utf-8").splitlines():
        line = raw.strip()
        if line and not line.startswith("#") and "=" in line:
            name, _, value = line.partition("=")
            values[name.strip()] = value.strip().strip('"').strip("'")
    return values


@pytest.fixture
def stack_env(make_blob_connection: Callable[[str, str, str], str]) -> dict[str, str]:
    dotenv = _dotenv()
    account = dotenv["AZURITE_ACCOUNT_NAME"]
    return {
        "ENVIRONMENT": "local",
        "SERVICES__BLOB__CONNECTIONSTRINGSECRET": BLOB_CONNECTION_REF,
        "SECRETS__MARKERBLOB": make_blob_connection(
            f"http://127.0.0.1:10000/{account}", account, dotenv["AZURITE_ACCOUNT_KEY"]
        ),
        "SECRETS__MARKERROUNDTRIP": "marker-round-trip-value",
        "MODELGATEWAY__ENDPOINT": "http://127.0.0.1:11434/v1/",
        "MODELGATEWAY__MODEL": dotenv["OLLAMA_MODEL"],
    }


def test_local_round_trip(stack_env: dict[str, str]) -> None:
    services = select_services(stack_env)
    tenant, run = uuid.uuid4(), uuid.uuid4()
    content = b'{"marker": "neutral evidence"}'

    reference = services.evidence.put(tenant, run, "marker-evidence.json", content, "application/json")

    assert reference.path.startswith(f"{tenant}/{run}/")
    assert services.evidence.get(tenant, reference) == content
    with pytest.raises(PermissionError):
        services.evidence.get(uuid.uuid4(), reference)
    assert services.secrets.get_secret("markerroundtrip") == "marker-round-trip-value"
    request = ModelRequest(prompt="Reply with the single word: ready.", max_output_tokens=16)
    completion = services.model.complete(request)
    assert completion.text.strip()
    assert completion.input_tokens > 0
    assert completion.output_tokens > 0
