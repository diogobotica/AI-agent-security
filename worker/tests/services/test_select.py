"""The worker chooses its service implementations by ENVIRONMENT, like the API (US-103 criterion 8).

Contract:
- ``worker.services.select.select_services(env=None, *, http_transport=None) -> Services`` reads the given mapping
  (``os.environ`` when None) and returns ``.evidence``, ``.secrets`` and ``.model``.
- Local implementations live in ``worker.services.local``, Azure ones in ``worker.services.azure``.
- Building the services makes no network call.
- In azure mode a missing or empty required variable raises ValueError naming it and showing no value.
- ``worker.services.protocols`` defines ``EvidenceStore``, ``SecretStore`` and ``ModelGateway`` as
  ``typing.runtime_checkable`` Protocols.
"""

import pytest

from worker.services.protocols import EvidenceStore, ModelGateway, SecretStore
from worker.services.select import select_services

AZURE_REQUIRED = (
    "SERVICES__BLOB__ACCOUNTURL",
    "SERVICES__KEYVAULT__URI",
    "MODELGATEWAY__ENDPOINT",
    "MODELGATEWAY__MODEL",
)


def test_select_services_local(local_env: dict[str, str]) -> None:
    services = select_services(local_env)

    assert type(services.evidence).__module__ == "worker.services.local"
    assert type(services.secrets).__module__ == "worker.services.local"


def test_select_services_azure(azure_env: dict[str, str]) -> None:
    services = select_services(azure_env)

    assert type(services.evidence).__module__ == "worker.services.azure"
    assert type(services.secrets).__module__ == "worker.services.azure"


@pytest.mark.parametrize("missing", AZURE_REQUIRED)
def test_azure_missing_variable_names_it(azure_env: dict[str, str], missing: str) -> None:
    other_values = [value for name, value in azure_env.items() if name not in (missing, "ENVIRONMENT")]
    azure_env[missing] = ""

    with pytest.raises(ValueError, match=missing) as error:
        select_services(azure_env)

    for value in other_values:
        assert value not in str(error.value)


@pytest.mark.parametrize("value", ["", "staging"])
def test_unknown_environment_is_rejected(local_env: dict[str, str], value: str) -> None:
    local_env["ENVIRONMENT"] = value

    with pytest.raises(ValueError, match="ENVIRONMENT"):
        select_services(local_env)


@pytest.mark.parametrize("environment", ["local", "azure"])
def test_protocols_satisfied(local_env: dict[str, str], azure_env: dict[str, str], environment: str) -> None:
    services = select_services(local_env if environment == "local" else azure_env)

    assert isinstance(services.evidence, EvidenceStore)
    assert isinstance(services.secrets, SecretStore)
    assert isinstance(services.model, ModelGateway)
