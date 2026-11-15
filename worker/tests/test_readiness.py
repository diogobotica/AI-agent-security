"""Readiness endpoint of the worker (US-102).

GET /health/ready reports whether the worker reaches each local dependency. The probes are replaced by
in-memory fakes through FastAPI's dependency override, so no network is used.

Contract the tests rely on:
- `worker.readiness.DependencyProbe` is a Protocol with `name: str` and `check() -> bool`.
- `worker.readiness.get_probes()` takes no arguments, is the FastAPI dependency the endpoint uses, and
  returns one probe per dependency for the configured ENVIRONMENT (it only builds probes; it does not
  connect).
- 200 `{"status": "ready", "checks": {<name>: "ok", ...}}` when every probe succeeds; otherwise 503
  `{"status": "not-ready", "checks": {...}}` with "unreachable" for each failing probe. A probe that
  raises counts as unreachable.
- The app validates ENVIRONMENT (local or azure) at startup, in its lifespan, and raises ValueError
  naming ENVIRONMENT otherwise.
"""

from collections.abc import Iterator

import pytest
from fastapi.testclient import TestClient

from worker.main import app
from worker.readiness import DependencyProbe, get_probes

DEPENDENCIES = ("postgres", "blob", "servicebus", "ollama")
NEUTRAL_HOST = "marker-host.invalid"
NEUTRAL_PORT = "6543"


class FakeProbe:
    def __init__(self, name: str, reachable: bool = True, error: str | None = None) -> None:
        self.name = name
        self._reachable = reachable
        self._error = error

    def check(self) -> bool:
        if self._error is not None:
            raise ConnectionError(self._error)
        return self._reachable


def _client_with(probes: list[DependencyProbe]) -> Iterator[TestClient]:
    app.dependency_overrides[get_probes] = lambda: probes
    try:
        with TestClient(app) as client:
            yield client
    finally:
        app.dependency_overrides.pop(get_probes, None)


@pytest.fixture
def all_reachable() -> Iterator[TestClient]:
    yield from _client_with([FakeProbe(name) for name in DEPENDENCIES])


@pytest.fixture
def servicebus_down() -> Iterator[TestClient]:
    yield from _client_with([FakeProbe(name, reachable=name != "servicebus") for name in DEPENDENCIES])


@pytest.fixture
def probes_failing_with_details() -> Iterator[TestClient]:
    probes: list[DependencyProbe] = [
        FakeProbe("postgres", error=f"connection refused by {NEUTRAL_HOST}:{NEUTRAL_PORT}"),
        FakeProbe("blob", reachable=False),
        FakeProbe("servicebus"),
        FakeProbe("ollama"),
    ]
    yield from _client_with(probes)


def test_ready_when_all_probes_succeed(all_reachable: TestClient) -> None:
    response = all_reachable.get("/health/ready")

    assert response.status_code == 200
    assert response.headers["content-type"].startswith("application/json")
    assert response.json() == {"status": "ready", "checks": dict.fromkeys(DEPENDENCIES, "ok")}


def test_not_ready_names_failing_probe(servicebus_down: TestClient) -> None:
    response = servicebus_down.get("/health/ready")

    assert response.status_code == 503
    assert response.json() == {
        "status": "not-ready",
        "checks": {"postgres": "ok", "blob": "ok", "servicebus": "unreachable", "ollama": "ok"},
    }


def test_readiness_reveals_no_hosts_or_errors(probes_failing_with_details: TestClient) -> None:
    response = probes_failing_with_details.get("/health/ready")

    assert response.status_code == 503
    body = response.text
    for detail in (NEUTRAL_HOST, NEUTRAL_PORT, "refused", "Error", "Traceback"):
        assert detail not in body, f"readiness response reveals {detail!r}"
    assert response.json()["checks"] == {
        "postgres": "unreachable",
        "blob": "unreachable",
        "servicebus": "ok",
        "ollama": "ok",
    }


def test_local_environment_builds_a_probe_for_every_dependency() -> None:
    probes = get_probes()

    assert sorted(probe.name for probe in probes) == sorted(DEPENDENCIES)


@pytest.mark.parametrize("value", ["", "staging", "production"])
def test_startup_fails_on_unknown_environment(monkeypatch: pytest.MonkeyPatch, value: str) -> None:
    monkeypatch.setenv("ENVIRONMENT", value)

    with pytest.raises(ValueError, match=r"(?i)environment"), TestClient(app):
        pass


def test_startup_fails_when_environment_missing(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.delenv("ENVIRONMENT", raising=False)

    with pytest.raises(ValueError, match=r"(?i)environment"), TestClient(app):
        pass
