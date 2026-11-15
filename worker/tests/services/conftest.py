"""Settings for the worker's external-service tests (US-103).

The worker reads the same setting names as the API, upper-cased as environment variables:

- local: SERVICES__BLOB__CONNECTIONSTRINGSECRET names a secret; the local secret store reads secret ``name`` from
  ``SECRETS__<NAME>`` (upper case, ``-`` becomes ``_``). MODELGATEWAY__ENDPOINT and MODELGATEWAY__MODEL.
- azure: SERVICES__BLOB__ACCOUNTURL, SERVICES__KEYVAULT__URI, MODELGATEWAY__ENDPOINT, MODELGATEWAY__MODEL
  (all required; credentials come from DefaultAzureCredential).

Values are neutral: a closed loopback port, the reserved ``.test`` domain, Azure-style names nobody connects to,
and key material built at run time.
"""

import base64
from collections.abc import Callable

import pytest

BLOB_CONNECTION_REF = "markerblob"  # the name under which the local secret store holds the blob connection string
LOCAL_MODEL_ENDPOINT = "http://model-gateway.test/v1/"
MARKER_MODEL = "marker-model"


def marker_key() -> str:
    return base64.b64encode(b"neutral-marker-key-material-0001").decode("ascii")


def blob_connection(blob_endpoint: str, account: str, key: str) -> str:
    parts = [
        "DefaultEndpointsProtocol=http",
        "AccountName=" + account,
        "AccountKey=" + key,
        "BlobEndpoint=" + blob_endpoint,
    ]
    return ";".join(parts) + ";"


@pytest.fixture
def make_blob_connection() -> Callable[[str, str, str], str]:
    """Builds an Azurite-style connection string: (blob endpoint, account, key)."""
    return blob_connection


@pytest.fixture
def local_env() -> dict[str, str]:
    return {
        "ENVIRONMENT": "local",
        "SERVICES__BLOB__CONNECTIONSTRINGSECRET": BLOB_CONNECTION_REF,
        "SECRETS__" + BLOB_CONNECTION_REF.upper(): blob_connection(
            "http://127.0.0.1:1/markeraccount", "markeraccount", marker_key()
        ),
        "MODELGATEWAY__ENDPOINT": LOCAL_MODEL_ENDPOINT,
        "MODELGATEWAY__MODEL": MARKER_MODEL,
    }


@pytest.fixture
def azure_env() -> dict[str, str]:
    return {
        "ENVIRONMENT": "azure",
        "SERVICES__BLOB__ACCOUNTURL": "https://markeraccount.blob.core.windows.net/",
        "SERVICES__KEYVAULT__URI": "https://marker-vault.vault.azure.net/",
        "MODELGATEWAY__ENDPOINT": "https://marker-foundry.test/v1/",
        "MODELGATEWAY__MODEL": MARKER_MODEL,
    }
