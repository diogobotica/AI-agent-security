"""Local worker services, offline (US-103 criteria 5, 6 and 8).

Contract (``worker.services.protocols``):
- ``EvidenceRef(tenant_id: UUID, run_id: UUID, name: str)`` with ``.path == f"{tenant_id}/{run_id}/{name}"``.
- ``EvidenceStore.put(tenant_id, run_id, name, content: bytes, content_type: str) -> EvidenceRef`` and
  ``EvidenceStore.get(tenant_id, ref) -> bytes``. ``get`` with another tenant's reference raises PermissionError;
  ``put`` with an empty name or one containing ``/``, ``\\`` or ``..`` raises ValueError; both before any request.
- ``SecretStore.get_secret(name) -> str``; a missing secret raises KeyError naming the secret, never a value.
- ``ModelGateway.complete(ModelRequest(prompt, max_output_tokens=None)) -> ModelResponse(text, input_tokens,
  output_tokens)``, POST to MODELGATEWAY__ENDPOINT + "chat/completions" with the configured model, through an
  httpx client that uses ``select_services(..., http_transport=...)`` when given. Error statuses raise.

The evidence store points at a closed loopback port; the model gateway at an httpx.MockTransport. No network.
"""

import json
import logging
import uuid

import httpx
import pytest

from worker.services.protocols import EvidenceRef, ModelRequest
from worker.services.select import select_services

TENANT_A = uuid.UUID("0000000a-0000-4000-8000-00000000000a")
TENANT_B = uuid.UUID("0000000b-0000-4000-8000-00000000000b")
RUN = uuid.UUID("00000001-0000-4000-8000-000000000001")
NEUTRAL_PROMPT = "neutral marker prompt"
COMPLETION_REPLY = {
    "id": "marker-completion",
    "object": "chat.completion",
    "model": "marker-model",
    "choices": [
        {"index": 0, "message": {"role": "assistant", "content": "marker completion text"}, "finish_reason": "stop"}
    ],
    "usage": {"prompt_tokens": 7, "completion_tokens": 3, "total_tokens": 10},
}


def test_evidence_tenant_prefix(local_env: dict[str, str]) -> None:
    reference = EvidenceRef(tenant_id=TENANT_A, run_id=RUN, name="transcript.json")

    assert reference.path == f"{TENANT_A}/{RUN}/transcript.json"
    assert reference.path.startswith(str(TENANT_A))


def test_evidence_get_with_other_tenant_fails(local_env: dict[str, str]) -> None:
    store = select_services(local_env).evidence
    belongs_to_a = EvidenceRef(tenant_id=TENANT_A, run_id=RUN, name="transcript.json")

    with pytest.raises(PermissionError):
        store.get(TENANT_B, belongs_to_a)


@pytest.mark.parametrize("name", ["", "../transcript.json", "nested/transcript.json", "nested\\transcript.json", ".."])
def test_evidence_put_with_name_outside_run_folder_fails(local_env: dict[str, str], name: str) -> None:
    store = select_services(local_env).evidence

    with pytest.raises(ValueError):
        store.put(TENANT_A, RUN, name, b'{"marker": "neutral evidence"}', "application/json")


def test_secret_not_in_repr_or_logs(local_env: dict[str, str], caplog: pytest.LogCaptureFixture) -> None:
    stored = "marker-" + uuid.uuid4().hex
    local_env["SECRETS__MARKERMASKED"] = stored
    caplog.set_level(logging.DEBUG)
    services = select_services(local_env)

    assert services.secrets.get_secret("markermasked") == stored
    with pytest.raises(KeyError) as missing:
        services.secrets.get_secret("markermissing")

    assert "markermissing" in str(missing.value)
    shown = [caplog.text, str(missing.value), repr(services.secrets), str(services.secrets), repr(services)]
    for text in shown:
        assert stored not in text
    connection = local_env["SECRETS__MARKERBLOB"]
    for text in shown:
        assert connection not in text


def test_model_gateway_parses_tokens(local_env: dict[str, str]) -> None:
    requests: list[httpx.Request] = []

    def answer(request: httpx.Request) -> httpx.Response:
        requests.append(request)
        return httpx.Response(200, json=COMPLETION_REPLY)

    gateway = select_services(local_env, http_transport=httpx.MockTransport(answer)).model

    response = gateway.complete(ModelRequest(prompt=NEUTRAL_PROMPT))

    assert response.text == "marker completion text"
    assert response.input_tokens == 7
    assert response.output_tokens == 3
    assert len(requests) == 1
    request = requests[0]
    assert request.method == "POST"
    assert str(request.url) == local_env["MODELGATEWAY__ENDPOINT"] + "chat/completions"
    assert "authorization" not in request.headers
    body = json.loads(request.content)
    assert body["model"] == local_env["MODELGATEWAY__MODEL"]
    assert {"role": "user", "content": NEUTRAL_PROMPT} in body["messages"]


@pytest.mark.parametrize("status", [400, 500, 503])
def test_model_gateway_fails_on_error_status(local_env: dict[str, str], status: int) -> None:
    def answer(_: httpx.Request) -> httpx.Response:
        return httpx.Response(status, json={"error": {"message": "marker failure"}})

    gateway = select_services(local_env, http_transport=httpx.MockTransport(answer)).model

    with pytest.raises(Exception):  # noqa: B017 - any error type, as long as it does not return a response
        gateway.complete(ModelRequest(prompt=NEUTRAL_PROMPT))
