"""Shared test setup for the worker.

The worker validates ENVIRONMENT when the app starts (US-102). Tests run as ENVIRONMENT=local unless a
test sets another value itself.

Tests marked ``integration`` need the local Docker Compose stack (US-103). A plain ``pytest`` run leaves them
out so it stays offline; run them with ``pytest -m integration`` once ``python scripts/local_stack.py up`` is done.
Any explicit ``-m`` expression (for example ``-m "not integration"``) is honoured as given.
"""

import pytest


def pytest_configure(config: pytest.Config) -> None:
    config.addinivalue_line(
        "markers", "integration: needs the local Docker Compose stack (python scripts/local_stack.py up)"
    )


def pytest_collection_modifyitems(config: pytest.Config, items: list[pytest.Item]) -> None:
    if config.getoption("markexpr"):
        return
    offline = [item for item in items if item.get_closest_marker("integration") is None]
    stack_only = [item for item in items if item.get_closest_marker("integration") is not None]
    if stack_only:
        config.hook.pytest_deselected(items=stack_only)
        items[:] = offline


@pytest.fixture(autouse=True)
def local_environment(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setenv("ENVIRONMENT", "local")
