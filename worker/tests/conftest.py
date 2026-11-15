"""Shared test setup for the worker.

The worker validates ENVIRONMENT when the app starts (US-102). Tests run as ENVIRONMENT=local unless a
test sets another value itself.
"""

import pytest


@pytest.fixture(autouse=True)
def local_environment(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setenv("ENVIRONMENT", "local")
