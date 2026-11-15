"""Readiness probes behind GET /health/ready (US-102).

Each probe checks one dependency with the standard library only: a PostgreSQL SSLRequest handshake over TCP,
or an HTTP GET to a fixed endpoint. Probes are only built by ``get_probes``; they connect when ``check`` runs.
The result names each dependency "ok" or "unreachable"; hosts, ports and errors go to the log only.
"""

import logging
import socket
import struct
import urllib.error
import urllib.request
from collections.abc import Sequence
from concurrent.futures import ThreadPoolExecutor, wait
from typing import Protocol

from worker.settings import DeploymentEnvironment, load_settings

logger = logging.getLogger(__name__)

_SSL_REQUEST = struct.pack("!ii", 8, 80877103)
# Extra time on top of the probe timeout before readiness stops waiting for a probe.
_GUARD_MARGIN_SECONDS = 1.0


class DependencyProbe(Protocol):
    """Checks whether the worker reaches one dependency."""

    name: str

    def check(self) -> bool:
        """Return True when the dependency answers; False (or raise) when it is unreachable."""
        ...


class PostgresProbe:
    """Sends the PostgreSQL SSLRequest and expects the one-byte answer 'S' or 'N'. No driver, no credentials."""

    def __init__(self, host: str, port: int, timeout_seconds: float) -> None:
        self.name = "postgres"
        self._address = (host, port)
        self._timeout = timeout_seconds

    def check(self) -> bool:
        try:
            with socket.create_connection(self._address, timeout=self._timeout) as connection:
                connection.sendall(_SSL_REQUEST)
                return connection.recv(1) in (b"S", b"N")
        except OSError:
            return False


class _NoRedirect(urllib.request.HTTPRedirectHandler):
    """Never follow redirects: a probe only talks to its configured endpoint."""

    def redirect_request(self, *args: object, **kwargs: object) -> None:
        return None


# No proxy and no redirects, so the probe connects only to the configured host.
_OPENER = urllib.request.build_opener(urllib.request.ProxyHandler({}), _NoRedirect())


class HttpProbe:
    """Sends GET to a fixed http(s) endpoint and succeeds on any HTTP response, whatever its status."""

    def __init__(self, name: str, url: str, timeout_seconds: float) -> None:
        self.name = name
        self._url = url
        self._timeout = timeout_seconds

    def check(self) -> bool:
        # The URL comes from validated settings (http or https only), never from a request.
        request = urllib.request.Request(self._url, method="GET")  # noqa: S310
        try:
            with _OPENER.open(request, timeout=self._timeout):
                return True
        except urllib.error.HTTPError as error:
            error.close()
            return True  # the dependency answered, even if with an error status
        except (urllib.error.URLError, OSError):
            return False


class _NotConfiguredProbe:
    """Stands in for a dependency whose Azure client does not exist yet (US-103). Always unreachable."""

    def __init__(self, name: str) -> None:
        self.name = name

    def check(self) -> bool:
        return False


def get_probes() -> list[DependencyProbe]:
    """FastAPI dependency: one probe per dependency for the configured ENVIRONMENT. Does not connect."""
    settings = load_settings()
    if settings.environment is DeploymentEnvironment.AZURE:
        return [_NotConfiguredProbe(name) for name in ("postgres", "blob", "servicebus", "ollama")]
    timeout = settings.probe_timeout_seconds
    return [
        PostgresProbe(settings.postgres_host, settings.postgres_port, timeout),
        HttpProbe("blob", settings.blob_endpoint, timeout),
        HttpProbe("servicebus", settings.servicebus_endpoint, timeout),
        HttpProbe("ollama", settings.ollama_endpoint, timeout),
    ]


def run_checks(probes: Sequence[DependencyProbe], timeout_seconds: float) -> dict[str, str]:
    """Run every probe in parallel; a probe that fails, raises or exceeds the timeout is "unreachable"."""
    checks: dict[str, str] = {}
    if not probes:
        return checks
    executor = ThreadPoolExecutor(max_workers=len(probes), thread_name_prefix="readiness")
    try:
        futures = [(probe.name, executor.submit(probe.check)) for probe in probes]
        wait([future for _, future in futures], timeout=timeout_seconds + _GUARD_MARGIN_SECONDS)
        for name, future in futures:
            reachable = False
            if not future.done():
                logger.warning("Readiness check %s timed out", name)
            elif (error := future.exception()) is not None:
                logger.warning("Readiness check %s failed", name, exc_info=error)
            elif future.result() is True:
                reachable = True
            else:
                logger.warning("Readiness check %s is unreachable", name)
            # A name listed twice is unreachable if any of its probes fails.
            checks[name] = "ok" if reachable and checks.get(name, "ok") == "ok" else "unreachable"
    finally:
        executor.shutdown(wait=False, cancel_futures=True)
    return checks
