"""Health endpoint of the worker shell (US-101).

Uses FastAPI's in-process TestClient only: no network, no models.
"""

from collections.abc import Iterator

import pytest
from fastapi.testclient import TestClient

from worker.main import app


@pytest.fixture
def client() -> Iterator[TestClient]:
    with TestClient(app) as test_client:
        yield test_client


def test_health_returns_200_with_status_ok_only(client: TestClient) -> None:
    response = client.get("/health")

    assert response.status_code == 200
    assert response.headers["content-type"].startswith("application/json")
    assert response.json() == {"status": "ok"}


@pytest.mark.parametrize("path", ["/docs", "/redoc", "/openapi.json"])
def test_openapi_docs_are_disabled(client: TestClient, path: str) -> None:
    response = client.get(path)

    assert response.status_code == 404
