"""Repository layout tests for US-101 (repository and solution skeleton).

Run from the repository root with the worker's dev environment:

    python -m pytest tests/repo

They read files only: no network, no installs, no builds, no subprocesses.
"""

from __future__ import annotations

import json
import re
import tomllib
import xml.etree.ElementTree as ET
from pathlib import Path
from typing import Any

ROOT = Path(__file__).resolve().parents[2]

COMPONENT_FOLDERS = ("api", "worker", "frontend", "infra", "mock-agent", "docs")
PYTHON_PROJECTS = ("worker", "mock-agent")
NPM_PROJECTS = ("frontend", "e2e")  # e2e/ arrives with US-101-T7; checked when present

EXACT_NPM_VERSION = re.compile(r"^\d+\.\d+\.\d+$")
EXACT_NUGET_VERSION = re.compile(r"^\d+\.\d+\.\d+(\.\d+)?$")
EXACT_PEP508 = re.compile(
    r"^[A-Za-z0-9][A-Za-z0-9._-]*(\[[A-Za-z0-9._,\s-]+\])?\s*==\s*\d+(\.\d+)*(\.post\d+)?\s*(;.*)?$"
)
OPEN_SOURCE_GRANTS = (
    "permission is hereby granted",
    "apache license",
    "gnu general public license",
    "gnu lesser general public license",
    "mozilla public license",
    "mit license",
    "redistribution and use in source and binary forms",
    "creative commons",
)


def _read(relative: str) -> str:
    path = ROOT / relative
    assert path.is_file(), f"{relative} is missing"
    return path.read_text(encoding="utf-8")


def _load_jsonc(relative: str) -> Any:
    """Load a JSON file that may carry line comments or trailing commas (tsconfig style)."""
    text = _read(relative)
    text = re.sub(r"^\s*//.*$", "", text, flags=re.MULTILINE)
    text = re.sub(r"/\*.*?\*/", "", text, flags=re.DOTALL)
    text = re.sub(r",(\s*[}\]])", r"\1", text)
    return json.loads(text)


def _markdown_section(text: str, heading_pattern: str) -> str | None:
    """Return the body of the first heading matching heading_pattern, up to the next heading of the same or higher level."""
    lines = text.splitlines()
    for index, line in enumerate(lines):
        match = re.match(r"^(#{1,6})\s+(.*)$", line)
        if match and re.search(heading_pattern, match.group(2), re.IGNORECASE):
            level = len(match.group(1))
            body: list[str] = []
            for following in lines[index + 1 :]:
                next_heading = re.match(r"^(#{1,6})\s+", following)
                if next_heading and len(next_heading.group(1)) <= level:
                    break
                body.append(following)
            return "\n".join(body)
    return None


def _project_files(folder: str, pattern: str) -> list[Path]:
    ignored_parts = {"bin", "obj", "node_modules", ".venv", ".next"}
    return sorted(
        path for path in (ROOT / folder).rglob(pattern) if not ignored_parts.intersection(path.relative_to(ROOT).parts)
    )


# --- Folders and READMEs ---------------------------------------------------------------------


def test_top_level_component_folders_exist() -> None:
    missing = [name for name in COMPONENT_FOLDERS if not (ROOT / name).is_dir()]
    assert not missing, f"Missing top-level folders: {', '.join(missing)}"


def test_each_component_folder_has_readme() -> None:
    problems: list[str] = []
    for name in COMPONENT_FOLDERS:
        readme = ROOT / name / "README.md"
        if not readme.is_file():
            problems.append(f"{name}/README.md is missing")
            continue
        body_lines = [
            line
            for line in readme.read_text(encoding="utf-8").splitlines()
            if line.strip() and not line.lstrip().startswith("#")
        ]
        if not body_lines:
            problems.append(f"{name}/README.md does not say what the folder holds (no text beyond headings)")
    assert not problems, "; ".join(problems)


# --- .NET SDK pin ----------------------------------------------------------------------------


def test_dotnet_sdk_pinned_to_lts_in_global_json() -> None:
    global_json = _load_jsonc("api/global.json")
    sdk = global_json.get("sdk")
    assert isinstance(sdk, dict), "api/global.json has no sdk section"
    version = sdk.get("version")
    assert isinstance(version, str) and re.fullmatch(r"10\.0\.\d+", version), (
        f"api/global.json must pin an exact .NET 10 (LTS) SDK version such as 10.0.100, got {version!r}"
    )
    roll_forward = sdk.get("rollForward", "patch")
    assert roll_forward in {"disable", "patch", "latestPatch"}, (
        f"api/global.json rollForward {roll_forward!r} would let the SDK move past the pinned feature band"
    )
    assert sdk.get("allowPrerelease") is not True, "api/global.json must not allow prerelease SDKs"


# --- Frontend TypeScript ---------------------------------------------------------------------


def test_frontend_typescript_is_strict() -> None:
    tsconfig = _load_jsonc("frontend/tsconfig.json")
    compiler_options = tsconfig.get("compilerOptions", {})
    assert compiler_options.get("strict") is True, "frontend/tsconfig.json must set compilerOptions.strict to true"


# --- Exact pins ------------------------------------------------------------------------------


def test_npm_dependencies_are_pinned_exactly() -> None:
    assert (ROOT / "frontend" / "package.json").is_file(), "frontend/package.json is missing"
    problems: list[str] = []
    checked = 0
    for folder in NPM_PROJECTS:
        manifest_path = ROOT / folder / "package.json"
        if not manifest_path.is_file():
            continue
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
        for section in ("dependencies", "devDependencies", "optionalDependencies"):
            for name, version in manifest.get(section, {}).items():
                checked += 1
                if not isinstance(version, str) or not EXACT_NPM_VERSION.fullmatch(version):
                    problems.append(f"{folder}/package.json {section}.{name} = {version!r}")
    assert checked > 0, "frontend/package.json declares no dependencies"
    assert not problems, "npm dependencies not pinned to an exact x.y.z version: " + "; ".join(problems)


def test_python_requirements_are_pinned_exactly() -> None:
    problems: list[str] = []
    for project in PYTHON_PROJECTS:
        for file_name in ("requirements.txt", "requirements-dev.txt"):
            path = ROOT / project / file_name
            if not path.is_file():
                problems.append(f"{project}/{file_name} is missing")
                continue
            pinned = 0
            for raw in path.read_text(encoding="utf-8").splitlines():
                line = raw.split(" #", 1)[0].strip()
                if not line or line.startswith("#"):
                    continue
                if re.match(r"^(-r|--requirement)\s+\S+$", line):
                    continue
                requirement = re.sub(r"\s+--hash=\S+", "", line).rstrip("\\").strip()
                if EXACT_PEP508.fullmatch(requirement):
                    pinned += 1
                else:
                    problems.append(f"{project}/{file_name}: {line!r}")
            if file_name == "requirements.txt" and pinned == 0:
                problems.append(f"{project}/requirements.txt pins no packages")
        pyproject_path = ROOT / project / "pyproject.toml"
        if pyproject_path.is_file():
            pyproject = tomllib.loads(pyproject_path.read_text(encoding="utf-8"))
            declared: list[str] = list(pyproject.get("project", {}).get("dependencies", []))
            for group in pyproject.get("project", {}).get("optional-dependencies", {}).values():
                declared.extend(group)
            for group in pyproject.get("dependency-groups", {}).values():
                declared.extend(entry for entry in group if isinstance(entry, str))
            problems.extend(
                f"{project}/pyproject.toml: {entry!r}" for entry in declared if not EXACT_PEP508.fullmatch(entry.strip())
            )
    assert not problems, "Python requirements not pinned with ==: " + "; ".join(problems)


def test_nuget_packages_are_pinned_exactly() -> None:
    props_path = ROOT / "api" / "Directory.Packages.props"
    assert props_path.is_file(), "api/Directory.Packages.props (central package management) is missing"
    props = ET.parse(props_path).getroot()
    central = [element.text for element in props.iter() if element.tag.endswith("ManagePackageVersionsCentrally")]
    assert any((value or "").strip().lower() == "true" for value in central), (
        "api/Directory.Packages.props must set ManagePackageVersionsCentrally to true"
    )
    problems: list[str] = []
    package_versions = [element for element in props.iter() if element.tag.endswith("PackageVersion")]
    assert package_versions, "api/Directory.Packages.props declares no PackageVersion entries"
    for element in package_versions:
        version = element.get("Version", "")
        if not EXACT_NUGET_VERSION.fullmatch(version):
            problems.append(f"Directory.Packages.props {element.get('Include')} = {version!r}")
    for project_file in _project_files("api", "*.csproj"):
        for element in ET.parse(project_file).getroot().iter():
            if not element.tag.endswith("PackageReference"):
                continue
            for attribute in ("Version", "VersionOverride"):
                version = element.get(attribute)
                if version is not None and not EXACT_NUGET_VERSION.fullmatch(version):
                    relative = project_file.relative_to(ROOT).as_posix()
                    problems.append(f"{relative} {element.get('Include')} {attribute} = {version!r}")
    assert not problems, "NuGet packages not pinned to an exact version: " + "; ".join(problems)


# --- Root README -----------------------------------------------------------------------------


def test_root_readme_describes_each_folder() -> None:
    readme = _read("README.md")
    missing = [
        f"{name}/" for name in COMPONENT_FOLDERS if not re.search(rf"(?<![\w/-]){re.escape(name)}/", readme)
    ]
    assert not missing, f"README.md does not describe these folders: {', '.join(missing)}"


def test_root_readme_has_run_locally_section() -> None:
    readme = _read("README.md")
    section = _markdown_section(readme, r"^run(ning)?\b.*\blocally\b")
    assert section is not None, "README.md has no 'Run locally' heading"
    lowered = section.lower()
    expected = {
        "the API (dotnet)": "dotnet",
        "the worker": "worker",
        "the mock agent": "mock-agent",
        "the Python services (uvicorn)": "uvicorn",
        "the frontend (npm)": "npm",
    }
    missing = [what for what, marker in expected.items() if marker not in lowered]
    assert not missing, f"The Run locally section does not explain how to run: {', '.join(missing)}"


# --- LICENSE and .gitignore ------------------------------------------------------------------


def test_license_is_present_and_reserves_all_rights() -> None:
    license_text = _read("LICENSE")
    lowered = license_text.lower()
    assert "copyright" in lowered, "LICENSE must name the copyright holder"
    assert "all rights reserved" in lowered, "LICENSE must state 'All rights reserved'"
    grants = [grant for grant in OPEN_SOURCE_GRANTS if grant in lowered]
    assert not grants, f"LICENSE must not grant an open-source licence (found: {', '.join(grants)})"


def _covers(active_patterns: list[str], required: str) -> bool:
    """True when an active .gitignore line ignores `required` anywhere in the tree or in a component folder."""
    for pattern in active_patterns:
        normalized = pattern.removeprefix("/").removeprefix("**/")
        if normalized == required or normalized.endswith("/" + required):
            return True
    return False


def test_gitignore_covers_all_stack_build_outputs() -> None:
    lines = _read(".gitignore").splitlines()
    active = [line.strip() for line in lines if line.strip() and not line.lstrip().startswith(("#", "!"))]
    required = {
        ".NET build output": ["bin/", "obj/", "TestResults/"],
        "Python caches and virtual environments": [
            "__pycache__/",
            ".venv/",
            ".pytest_cache/",
            ".mypy_cache/",
            ".ruff_cache/",
        ],
        "Next.js and npm output": ["node_modules/", ".next/", "*.tsbuildinfo", "next-env.d.ts"],
        "Playwright output": ["test-results/", "playwright-report/"],
        "Terraform state and providers": [".terraform/", "*.tfstate"],
    }
    missing = [
        f"{pattern} ({stack})"
        for stack, patterns in required.items()
        for pattern in patterns
        if not _covers(active, pattern)
    ]
    assert not missing, ".gitignore does not cover these build outputs: " + ", ".join(missing)
