"""Local stack tests for US-102 (Docker Compose for the whole local system).

Run from the repository root with the worker's dev environment:

    python -m pytest tests/repo

They read files only: no Docker, no network, no installs, no subprocesses.

`docker-compose.yml` is read with the small YAML reader below, because the repository has no YAML
library and US-102 adds no dependency. The reader understands what a Compose file normally needs:
block mappings and sequences, plain and quoted scalars, single-line flow sequences and mappings and
block scalars (`|`, `>`). It rejects anchors, aliases and merge keys (`&`, `*`, `<<`), so keep the
Compose file free of them.
"""

from __future__ import annotations

import json
import re
import struct
from pathlib import Path
from typing import Any

import pytest

ROOT = Path(__file__).resolve().parents[2]
COMPOSE_FILE = "docker-compose.yml"
ENV_TEMPLATE = "infra/local/env.template"  # D-019: no tracked file may be named .env or .env.*
SERVICEBUS_CONFIG = "infra/local/servicebus/config.json"
POSTGRES_INIT = "infra/local/postgres/init/01-app-role.sh"

REQUIRED_SERVICES = ("postgres", "azurite", "mssql", "servicebus", "ollama", "api", "worker", "mock-agent")
BUILT_SERVICES = {"api": "api", "worker": "worker", "mock-agent": "mock-agent"}
APP_PORTS = {"api": 5080, "worker": 8001, "mock-agent": 8002}
SERVICE_IMAGE_REPOSITORIES = {
    "postgres": ("postgres", "docker.io/postgres", "docker.io/library/postgres", "library/postgres"),
    "azurite": ("mcr.microsoft.com/azure-storage/azurite",),
    "servicebus": ("mcr.microsoft.com/azure-messaging/servicebus-emulator",),
    "mssql": ("mcr.microsoft.com/mssql/server",),
    "ollama": ("ollama/ollama", "docker.io/ollama/ollama"),
}

PINNED_IMAGE = re.compile(r"^(?P<repository>[a-z0-9][a-z0-9._/:-]*?):(?P<tag>[A-Za-z0-9_][A-Za-z0-9_.-]*)@sha256:[0-9a-f]{64}$")
VARIABLE_REFERENCE = re.compile(r"\$\{[A-Za-z_][A-Za-z0-9_]*(:?\?[^}]*)?\}")
ONLY_VARIABLE_REFERENCE = re.compile(r"^\$\{[A-Za-z_][A-Za-z0-9_]*(:?\?[^}]*)?\}$")
SECRET_NAME = re.compile(r"(PASSWORD|PASSWD|SECRET|TOKEN|(^|_)KEY$)", re.IGNORECASE)
CONNECTION_STRING_SECRET = re.compile(r"(?i)\b(password|pwd|accountkey|sharedaccesskey)=([^;]*)")
LONG_BASE64_TOKEN = re.compile(r"[A-Za-z0-9+/]{32,}={0,2}")
# The Service Bus emulator's documented connection string uses this fixed placeholder; it is not a secret.
SERVICEBUS_PLACEHOLDER_KEY = "SAS_KEY_VALUE"
SCANNED_FOR_MODEL_NAME = ("docker-compose.yml", "scripts", "api/src", "worker/src", "mock-agent/src", "frontend", "infra")
IGNORED_PARTS = {"bin", "obj", "node_modules", ".venv", ".next", "__pycache__", ".pytest_cache", ".mypy_cache"}


# --- Minimal YAML reader (Compose subset) -----------------------------------------------------


class YamlSubsetError(ValueError):
    """The file uses YAML outside the subset these tests read."""


def _strip_comment(text: str) -> str:
    quote: str | None = None
    for index, char in enumerate(text):
        if quote:
            if char == quote:
                quote = None
        elif char in "'\"":
            quote = char
        elif char == "#" and (index == 0 or text[index - 1] in " \t"):
            return text[:index].rstrip()
    return text.rstrip()


def _split_flow(text: str) -> list[str]:
    items: list[str] = []
    depth = 0
    quote: str | None = None
    current = ""
    for char in text:
        if quote:
            current += char
            if char == quote:
                quote = None
            continue
        if char in "'\"":
            quote = char
        elif char in "[{":
            depth += 1
        elif char in "]}":
            depth -= 1
        elif char == "," and depth == 0:
            items.append(current.strip())
            current = ""
            continue
        current += char
    if current.strip():
        items.append(current.strip())
    return items


def _scalar(text: str) -> Any:
    text = text.strip()
    if text in ("", "~", "null"):
        return None
    if text.startswith(("&", "*")) or text.startswith("<<"):
        raise YamlSubsetError(f"anchors, aliases and merge keys are not supported: {text!r}")
    if text.startswith("["):
        if not text.endswith("]"):
            raise YamlSubsetError(f"unterminated flow sequence: {text!r}")
        return [_scalar(item) for item in _split_flow(text[1:-1])]
    if text.startswith("{"):
        if not text.endswith("}"):
            raise YamlSubsetError(f"unterminated flow mapping: {text!r}")
        mapping: dict[str, Any] = {}
        for item in _split_flow(text[1:-1]):
            key, _, value = item.partition(":")
            mapping[str(_scalar(key))] = _scalar(value)
        return mapping
    if text.startswith('"') and text.endswith('"') and len(text) >= 2:
        body = text[1:-1]
        return re.sub(r"\\(.)", lambda m: {"n": "\n", "t": "\t"}.get(m.group(1), m.group(1)), body)
    if text.startswith("'") and text.endswith("'") and len(text) >= 2:
        return text[1:-1].replace("''", "'")
    return text


_KEY = re.compile(r"""^(?P<key>"[^"]*"|'[^']*'|[^\s"'#\-\[\]{}][^:#]*?|-[^\s:#][^:#]*?)\s*:(?:\s+(?P<value>.*)|$)""")


class _Lines:
    def __init__(self, text: str) -> None:
        self.items: list[tuple[int, str]] = []
        for raw in text.splitlines():
            if "\t" in raw[: len(raw) - len(raw.lstrip())]:
                raise YamlSubsetError("tabs are not allowed for indentation")
            content = _strip_comment(raw)
            if not content.strip() or content.strip() in ("---", "..."):
                continue
            self.items.append((len(content) - len(content.lstrip(" ")), content.strip()))


def _is_sequence_item(text: str) -> bool:
    return text == "-" or text.startswith("- ")


def _parse_block(lines: list[tuple[int, str]], index: int, indent: int) -> tuple[Any, int]:
    if _is_sequence_item(lines[index][1]):
        return _parse_sequence(lines, index, indent)
    return _parse_mapping(lines, index, indent)


def _parse_value(lines: list[tuple[int, str]], index: int, indent: int, value: str | None) -> tuple[Any, int]:
    """Parse the value that follows `key:` or `- ` on line index-1 whose indentation is `indent`."""
    if value is None or value == "":
        if index < len(lines) and lines[index][0] > indent:
            return _parse_block(lines, index, lines[index][0])
        if index < len(lines) and lines[index][0] == indent and _is_sequence_item(lines[index][1]):
            return _parse_sequence(lines, index, indent)
        return None, index
    if value[0] in "|>":
        collected: list[str] = []
        while index < len(lines) and lines[index][0] > indent:
            collected.append(lines[index][1])
            index += 1
        return ("\n" if value[0] == "|" else " ").join(collected), index
    if value.startswith(("[", "{")):
        opener, closer = value[0], "]" if value[0] == "[" else "}"
        while value.count(opener) > value.count(closer) and index < len(lines):
            value += " " + lines[index][1]
            index += 1
    return _scalar(value), index


def _parse_mapping(lines: list[tuple[int, str]], index: int, indent: int) -> tuple[dict[str, Any], int]:
    mapping: dict[str, Any] = {}
    while index < len(lines) and lines[index][0] == indent and not _is_sequence_item(lines[index][1]):
        text = lines[index][1]
        match = _KEY.match(text)
        if not match:
            raise YamlSubsetError(f"cannot read line as a mapping entry: {text!r}")
        key = str(_scalar(match.group("key")))
        if key == "<<":
            raise YamlSubsetError("merge keys (<<) are not supported")
        value, index = _parse_value(lines, index + 1, indent, match.group("value"))
        mapping[key] = value
    if index < len(lines) and lines[index][0] > indent:
        raise YamlSubsetError(f"unexpected indentation at: {lines[index][1]!r}")
    return mapping, index


def _parse_sequence(lines: list[tuple[int, str]], index: int, indent: int) -> tuple[list[Any], int]:
    sequence: list[Any] = []
    while index < len(lines) and lines[index][0] == indent and _is_sequence_item(lines[index][1]):
        rest = lines[index][1][1:].lstrip()
        if not rest:
            value, index = _parse_value(lines, index + 1, indent, None)
            sequence.append(value)
            continue
        inner_indent = indent + (len(lines[index][1]) - len(rest))
        if _KEY.match(rest) and not rest.startswith(("[", "{", '"', "'")):
            lines[index] = (inner_indent, rest)
            value, index = _parse_mapping(lines, index, inner_indent)
            sequence.append(value)
            continue
        value, index = _parse_value(lines, index + 1, inner_indent, rest)
        sequence.append(value)
    return sequence, index


def load_yaml_subset(text: str) -> Any:
    lines = _Lines(text).items
    if not lines:
        return None
    value, index = _parse_block(lines, 0, lines[0][0])
    if index != len(lines):
        raise YamlSubsetError(f"could not read the file past: {lines[index][1]!r}")
    return value


# --- Helpers ----------------------------------------------------------------------------------


def _read(relative: str) -> str:
    path = ROOT / relative
    assert path.is_file(), f"{relative} is missing"
    return path.read_text(encoding="utf-8")


def _compose() -> dict[str, Any]:
    document = load_yaml_subset(_read(COMPOSE_FILE))
    assert isinstance(document, dict), f"{COMPOSE_FILE} is not a mapping"
    return document


def _services() -> dict[str, dict[str, Any]]:
    services = _compose().get("services")
    assert isinstance(services, dict) and services, f"{COMPOSE_FILE} defines no services"
    return {name: (definition or {}) for name, definition in services.items()}


def _environment(service: dict[str, Any]) -> dict[str, str | None]:
    raw = service.get("environment")
    if raw is None:
        return {}
    if isinstance(raw, dict):
        return {str(key): None if value is None else str(value) for key, value in raw.items()}
    assert isinstance(raw, list), f"environment must be a mapping or a list, got {raw!r}"
    environment: dict[str, str | None] = {}
    for entry in raw:
        name, separator, value = str(entry).partition("=")
        environment[name] = value if separator else None
    return environment


def _port_entries(service: dict[str, Any]) -> list[Any]:
    ports = service.get("ports") or []
    assert isinstance(ports, list), f"ports must be a list, got {ports!r}"
    return ports


def _host_ip_and_published(entry: Any) -> tuple[str | None, str | None]:
    if isinstance(entry, dict):
        host_ip = entry.get("host_ip")
        published = entry.get("published")
        return (None if host_ip is None else str(host_ip), None if published is None else str(published))
    parts = str(entry).split(":")
    if len(parts) == 3:
        return parts[0], parts[1]
    if len(parts) == 2:
        return None, parts[0]
    return None, None


def _dotenv(relative: str) -> dict[str, str]:
    values: dict[str, str] = {}
    for raw in _read(relative).splitlines():
        line = raw.strip()
        if not line or line.startswith("#"):
            continue
        name, separator, value = line.partition("=")
        assert separator, f"{relative}: line is not NAME=value: {line!r}"
        values[name.strip()] = value.strip().strip('"').strip("'")
    return values


def _dockerfile_stages(relative: str) -> list[tuple[str, str | None, list[str]]]:
    """Return (image, stage name, instructions) per FROM stage, with line continuations joined."""
    text = re.sub(r"\\\r?\n", " ", _read(relative))
    stages: list[tuple[str, str | None, list[str]]] = []
    for raw in text.splitlines():
        line = raw.strip()
        if not line or line.startswith("#"):
            continue
        from_match = re.match(r"(?i)^FROM\s+(?:--platform=\S+\s+)?(\S+)(?:\s+AS\s+(\S+))?\s*$", line)
        if from_match:
            stages.append((from_match.group(1), from_match.group(2), []))
        elif stages:
            stages[-1][2].append(line)
    assert stages, f"{relative} has no FROM instruction"
    return stages


def _git_dir() -> Path:
    dot_git = ROOT / ".git"
    if dot_git.is_file():  # worktree or submodule: ".git" holds "gitdir: <path>"
        target = dot_git.read_text(encoding="utf-8").strip().removeprefix("gitdir:").strip()
        return (ROOT / target).resolve()
    return dot_git


def _tracked_paths() -> list[str]:
    """Paths in git's index (what `git ls-files` lists), read from .git/index without running git.

    Supports index versions 2, 3 and 4 (https://git-scm.com/docs/index-format).
    """
    index_path = _git_dir() / "index"
    if not index_path.is_file():
        pytest.skip("not a git checkout (no .git/index)")
    data = index_path.read_bytes()
    signature, version, count = struct.unpack(">4sLL", data[:12])
    assert signature == b"DIRC" and version in (2, 3, 4), f"unexpected git index format {signature!r} v{version}"
    paths: list[str] = []
    position = 12
    previous = b""
    for _ in range(count):
        start = position
        (flags,) = struct.unpack(">H", data[position + 60 : position + 62])
        position += 62
        if version >= 3 and flags & 0x4000:
            position += 2  # extended flags
        if version == 4:
            byte = data[position]
            position += 1
            strip = byte & 0x7F
            while byte & 0x80:
                byte = data[position]
                position += 1
                strip = ((strip + 1) << 7) | (byte & 0x7F)
            end = data.index(b"\0", position)
            name = previous[: len(previous) - strip] + data[position:end]
            position = end + 1
        else:
            end = data.index(b"\0", position)
            name = data[position:end]
            position = start + ((end - start) // 8 + 1) * 8  # 1-8 NUL bytes pad each entry to 8 bytes
        previous = name
        paths.append(name.decode("utf-8", errors="replace"))
    return paths


def _active_gitignore_lines() -> list[str]:
    return [line.strip() for line in _read(".gitignore").splitlines() if line.strip() and not line.strip().startswith("#")]


# --- C1: services, images, Service Bus configuration --------------------------------------------


def test_compose_defines_every_local_service() -> None:
    services = _services()
    missing = [name for name in REQUIRED_SERVICES if name not in services]
    assert not missing, f"{COMPOSE_FILE} is missing services: {', '.join(missing)}"

    problems: list[str] = []
    for name, folder in BUILT_SERVICES.items():
        build = services[name].get("build")
        context = build.get("context") if isinstance(build, dict) else build
        if context is None or Path(str(context)).as_posix().strip("./") != folder:
            problems.append(f"{name} must be built from ./{folder} (build: {build!r})")
    for name, repositories in SERVICE_IMAGE_REPOSITORIES.items():
        image = str(services[name].get("image") or "")
        repository = image.split("@", 1)[0].rsplit(":", 1)[0]
        if repository not in repositories:
            problems.append(f"{name} must use {repositories[0]} (image: {image!r})")
    compose_text = _read(COMPOSE_FILE).lower()
    if "azure-sql-edge" in compose_text:
        problems.append("Azure SQL Edge is retired; the Service Bus emulator uses SQL Server (D-017)")
    assert not problems, "; ".join(problems)


def test_every_image_pinned_by_tag_and_digest() -> None:
    problems: list[str] = []
    for name, service in _services().items():
        image = service.get("image")
        if image is None:
            if service.get("build") is None:
                problems.append(f"{name} has neither image nor build")
            continue  # built locally; its base images are checked in the Dockerfile test
        if service.get("build") is not None:
            continue  # the image key only names the locally built image
        match = PINNED_IMAGE.fullmatch(str(image))
        if not match:
            problems.append(f"{name}: {image!r} is not pinned as name:tag@sha256:<64 hex>")
        elif match.group("tag") == "latest":
            problems.append(f"{name}: {image!r} uses the 'latest' tag")
    assert not problems, "Images not pinned: " + "; ".join(problems)


def test_dockerfile_base_images_pinned_by_digest() -> None:
    problems: list[str] = []
    for folder in BUILT_SERVICES.values():
        relative = f"{folder}/Dockerfile"
        stage_names: set[str] = set()
        for image, stage_name, _ in _dockerfile_stages(relative):
            if image.lower() not in stage_names:
                match = PINNED_IMAGE.fullmatch(image)
                if not match:
                    problems.append(f"{relative}: FROM {image} is not pinned as name:tag@sha256:<64 hex>")
                elif match.group("tag") == "latest":
                    problems.append(f"{relative}: FROM {image} uses the 'latest' tag")
            if stage_name:
                stage_names.add(stage_name.lower())
    assert not problems, "; ".join(problems)


def test_service_dockerfiles_run_as_non_root() -> None:
    problems: list[str] = []
    for folder in BUILT_SERVICES.values():
        relative = f"{folder}/Dockerfile"
        _, _, instructions = _dockerfile_stages(relative)[-1]
        users = [line.split(None, 1)[1].strip() for line in instructions if re.match(r"(?i)^USER\s+\S", line)]
        if not users:
            problems.append(f"{relative}: the final stage sets no USER, so it runs as root")
            continue
        user = users[-1].split(":", 1)[0]
        if user in ("root", "0"):
            problems.append(f"{relative}: the final stage runs as {users[-1]!r}")
    assert not problems, "; ".join(problems)


def test_dockerignore_excludes_env_file() -> None:
    problems: list[str] = []
    for folder in BUILT_SERVICES.values():
        relative = f"{folder}/.dockerignore"
        path = ROOT / relative
        if not path.is_file():
            problems.append(f"{relative} is missing")
            continue
        patterns = {
            line.strip().removeprefix("**/").removeprefix("/").rstrip("/")
            for line in path.read_text(encoding="utf-8").splitlines()
            if line.strip() and not line.strip().startswith("#")
        }
        if not {".env", ".env*", ".env.*"} & patterns:
            problems.append(f"{relative} does not exclude .env")
    assert not problems, "; ".join(problems)


def test_servicebus_config_declares_run_queues_with_dead_lettering() -> None:
    config = json.loads(_read(SERVICEBUS_CONFIG))
    namespaces = config.get("UserConfig", {}).get("Namespaces", [])
    namespace = next((item for item in namespaces if item.get("Name") == "sbemulatorns"), None)
    assert namespace is not None, f"{SERVICEBUS_CONFIG} has no namespace 'sbemulatorns'"
    queues = {queue.get("Name"): queue.get("Properties", {}) for queue in namespace.get("Queues", [])}

    assert "run-jobs" in queues, "queue run-jobs is missing"
    assert "run-events" in queues, "queue run-events is missing"
    run_jobs = queues["run-jobs"]
    assert run_jobs.get("MaxDeliveryCount") == 3, f"run-jobs MaxDeliveryCount must be 3, got {run_jobs.get('MaxDeliveryCount')!r}"
    assert run_jobs.get("DeadLetteringOnMessageExpiration") is True, "run-jobs must dead-letter expired messages"

    volumes = [str(volume) for volume in _services().get("servicebus", {}).get("volumes") or []]
    assert any(SERVICEBUS_CONFIG in volume.replace("\\", "/") for volume in volumes), (
        f"the servicebus service does not mount {SERVICEBUS_CONFIG}"
    )


def test_postgres_init_creates_non_superuser_app_role() -> None:
    script = _read(POSTGRES_INIT)
    lowered = script.lower()
    assert "platform_app" in lowered, f"{POSTGRES_INIT} does not create the platform_app role"
    assert "nosuperuser" in lowered, f"{POSTGRES_INIT} must create platform_app with NOSUPERUSER (RLS skips superusers)"
    assert re.search(r"create\s+database\s+\"?platform\b", lowered), f"{POSTGRES_INIT} does not create the platform database"
    assert re.search(r"\$\{?[A-Z_]*PASSWORD", script), f"{POSTGRES_INIT} must take the role password from the environment"
    assert not re.search(r"(?i)password\s+'[^'$]+'", script), f"{POSTGRES_INIT} contains a literal password"

    volumes = [str(volume).replace("\\", "/") for volume in _services().get("postgres", {}).get("volumes") or []]
    assert any("infra/local/postgres/init" in volume and "/docker-entrypoint-initdb.d" in volume for volume in volumes), (
        "the postgres service does not mount infra/local/postgres/init into /docker-entrypoint-initdb.d"
    )


# --- C2: ENVIRONMENT ----------------------------------------------------------------------------


def test_compose_sets_environment_local_for_api_and_worker() -> None:
    services = _services()
    problems = [
        f"{name}: ENVIRONMENT = {_environment(services.get(name, {})).get('ENVIRONMENT')!r}"
        for name in ("api", "worker")
        if _environment(services.get(name, {})).get("ENVIRONMENT") != "local"
    ]
    assert not problems, "api and worker must run with ENVIRONMENT=local: " + "; ".join(problems)


# --- C3: README ---------------------------------------------------------------------------------


def test_readme_documents_one_command_and_troubleshooting() -> None:
    readme = _read("README.md")
    assert "python scripts/local_stack.py up" in readme, "README.md does not document `python scripts/local_stack.py up`"
    assert re.search(r"(?im)^#{1,6}\s+.*troubleshooting", readme), "README.md has no Troubleshooting heading"


# --- Rule 4: secrets ----------------------------------------------------------------------------


def test_compose_has_no_literal_secrets() -> None:
    problems: list[str] = []
    for name, service in _services().items():
        for key, value in _environment(service).items():
            if value is None:
                continue
            if SECRET_NAME.search(key) and not ONLY_VARIABLE_REFERENCE.fullmatch(value.strip()):
                problems.append(f"{name}.{key} must be a ${{VAR}} reference to .env")
            for field, secret in CONNECTION_STRING_SECRET.findall(value):
                remainder = VARIABLE_REFERENCE.sub("", secret).replace(SERVICEBUS_PLACEHOLDER_KEY, "").strip()
                if remainder:
                    problems.append(f"{name}.{key}: {field}= holds a literal value")
    without_digests = re.sub(r"@sha256:[0-9a-f]{64}", "", _read(COMPOSE_FILE))
    tokens = LONG_BASE64_TOKEN.findall(VARIABLE_REFERENCE.sub("", without_digests))
    problems.extend(f"literal key-like value in {COMPOSE_FILE} ({token[:6]}...)" for token in tokens)
    assert not problems, "; ".join(problems)


def test_env_template_leaves_secrets_empty() -> None:
    values = _dotenv(ENV_TEMPLATE)
    secret_keys = [key for key in values if SECRET_NAME.search(key)]
    assert secret_keys, f"{ENV_TEMPLATE} names no secret keys for the script to fill"
    filled = [key for key in secret_keys if values[key]]
    assert not filled, f"{ENV_TEMPLATE} must leave secret values empty: {', '.join(filled)}"


def test_env_file_is_git_ignored() -> None:
    active = _active_gitignore_lines()
    assert any(line.removeprefix("/") == ".env" for line in active), ".gitignore does not ignore .env"
    assert any(line.removeprefix("/") in (".env.*", ".env*") for line in active), ".gitignore does not ignore .env.*"
    exceptions = [line for line in active if line.startswith("!") and ".env" in line]
    assert not exceptions, f".gitignore must not re-include env files (D-019): {', '.join(exceptions)}"
    assert (ROOT / ENV_TEMPLATE).is_file(), f"{ENV_TEMPLATE} is missing"


def test_no_tracked_file_is_an_env_file() -> None:
    # D-019: `.env` and `.env.*` are protected paths; the committed template is infra/local/env.template.
    tracked = _tracked_paths()
    env_files = sorted(path for path in tracked if re.fullmatch(r"\.env(\..*)?", path.rsplit("/", 1)[-1]))
    assert not env_files, f"Tracked env files (rename the template to {ENV_TEMPLATE}): {', '.join(env_files)}"


# --- Rule 3: model names ------------------------------------------------------------------------


def test_model_name_only_in_env_template() -> None:
    model = _dotenv(ENV_TEMPLATE).get("OLLAMA_MODEL", "")
    assert model, f"{ENV_TEMPLATE} must set OLLAMA_MODEL"
    assert "${OLLAMA_MODEL" in _read(COMPOSE_FILE), f"{COMPOSE_FILE} must take the model from ${{OLLAMA_MODEL}}"

    offenders: list[str] = []
    for relative in SCANNED_FOR_MODEL_NAME:
        target = ROOT / relative
        files = [target] if target.is_file() else sorted(target.rglob("*")) if target.is_dir() else []
        for path in files:
            parts = path.relative_to(ROOT).parts
            if not path.is_file() or IGNORED_PARTS.intersection(parts) or path.suffix.lower() == ".md":
                continue
            if path.name.startswith(".env") or path == ROOT / ENV_TEMPLATE:
                continue  # the template itself, and local untracked env files
            try:
                text = path.read_text(encoding="utf-8")
            except UnicodeDecodeError:
                continue
            if model in text:
                offenders.append(path.relative_to(ROOT).as_posix())
    assert not offenders, f"The model name appears outside {ENV_TEMPLATE}: {', '.join(offenders)}"


# --- Ports --------------------------------------------------------------------------------------


def test_published_ports_bound_to_loopback() -> None:
    problems: list[str] = []
    for name, service in _services().items():
        for entry in _port_entries(service):
            host_ip, _ = _host_ip_and_published(entry)
            if host_ip != "127.0.0.1":
                problems.append(f"{name}: {entry!r}")
    assert not problems, "Published ports must bind to 127.0.0.1: " + "; ".join(problems)


def test_app_services_published_on_documented_ports() -> None:
    services = _services()
    problems: list[str] = []
    for name, port in APP_PORTS.items():
        published = {_host_ip_and_published(entry)[1] for entry in _port_entries(services.get(name, {}))}
        if str(port) not in published:
            problems.append(f"{name} must publish 127.0.0.1:{port} (published: {sorted(p for p in published if p)})")
    assert not problems, "; ".join(problems)


# --- The reader itself --------------------------------------------------------------------------


def test_yaml_subset_reader_reads_compose_shapes() -> None:
    sample = """
services:
  db:
    image: "example/db:1.0@sha256:{digest}"  # pinned
    environment:
      DB_ROLE: ${{DB_ROLE}}
      MODE: local
    ports:
      - "127.0.0.1:5432:5432"
    healthcheck:
      test: ["CMD-SHELL", "probe --quiet"]
      interval: 5s
  app:
    build:
      context: ./app
    environment:
    - ENVIRONMENT=local
    command: >
      run
      --flag
    ports:
      - target: 8080
        published: 5080
        host_ip: 127.0.0.1
""".format(digest="0" * 64)
    document = load_yaml_subset(sample)

    assert document["services"]["db"]["image"] == "example/db:1.0@sha256:" + "0" * 64
    assert document["services"]["db"]["environment"] == {"DB_ROLE": "${DB_ROLE}", "MODE": "local"}
    assert document["services"]["db"]["ports"] == ["127.0.0.1:5432:5432"]
    assert document["services"]["db"]["healthcheck"]["test"] == ["CMD-SHELL", "probe --quiet"]
    assert document["services"]["app"]["environment"] == ["ENVIRONMENT=local"]
    assert document["services"]["app"]["command"] == "run --flag"
    assert document["services"]["app"]["ports"] == [{"target": "8080", "published": "5080", "host_ip": "127.0.0.1"}]
