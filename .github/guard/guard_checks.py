#!/usr/bin/env python3
"""Deterministic checks for the guardian agent and for CI.

  python .workflow/kit/scripts/guard_checks.py [--base main] [--tests-frozen-since <sha>] [--strict]

Checks:
  * changed files touch no protected path;
  * tests unchanged since the test-writer's commit (--tests-frozen-since);
  * code rules from guard-policy.json on added lines;
  * secrets (gitleaks), vulnerable or outdated dependencies (osv-scanner, dotnet, npm, pip-audit).
Missing scanners are warnings locally and failures with --strict (CI).
Prints a Markdown report; exit code 1 when anything blocks.
"""
import argparse
import fnmatch
import json
import os
import re
import shutil
import subprocess
import sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import flowpaths  # noqa: E402

ROOT = os.environ.get("CLAUDE_PROJECT_DIR") or os.getcwd()
POLICY = flowpaths.policy()


def run(cmd, cwd=ROOT):
    p = subprocess.run(cmd, cwd=cwd, capture_output=True, text=True, shell=isinstance(cmd, str))
    return p.returncode, (p.stdout or "") + (p.stderr or "")


def matches(path, patterns):
    return any(fnmatch.fnmatch(path, p) or fnmatch.fnmatch("/" + path, "*/" + p) for p in patterns)


def changed_files(base):
    _, out = run(["git", "diff", "--name-only", f"{base}...HEAD"])
    _, wt = run(["git", "diff", "--name-only", "HEAD"])
    return sorted({f.strip() for f in (out + wt).splitlines() if f.strip()})


def added_lines(base):
    _, diff = run(["git", "diff", "-U0", f"{base}...HEAD"])
    current, result = None, []
    for line in diff.splitlines():
        if line.startswith("+++ b/"):
            current = line[6:]
        elif line.startswith("+") and not line.startswith("+++") and current:
            result.append((current, line[1:]))
    return result


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--base", default="main")
    ap.add_argument("--tests-frozen-since")
    ap.add_argument("--strict", action="store_true")
    a = ap.parse_args()

    blockers, warnings, clean = [], [], []
    files = changed_files(a.base)

    # 1. protected paths (CI changes to .claude/.github are allowed when made by a human; flag them for review)
    hits = [f for f in files if matches(f, POLICY["protected_paths"])]
    hard = [f for f in hits if not f.startswith((".claude/", ".github/"))]
    if hard:
        blockers.append("Protected paths changed: " + ", ".join(hard))
    if len(hits) > len(hard):
        warnings.append("Guardrail files changed (.claude/ or .github/): a human must review: " +
                        ", ".join(sorted(set(hits) - set(hard))))
    if not hits:
        clean.append("No protected paths changed")

    # 2. tests frozen since the test-writer's commit
    if a.tests_frozen_since:
        _, out = run(["git", "diff", "--name-only", f"{a.tests_frozen_since}", "HEAD"])
        changed_tests = [f for f in out.splitlines() if matches(f, POLICY["test_paths"])]
        if changed_tests:
            blockers.append("Tests changed after the test-writer's commit: " + ", ".join(changed_tests))
        else:
            clean.append("Tests unchanged since the test-writer's commit")

    # 3. code rules on added lines
    rule_hits = []
    for path, line in added_lines(a.base):
        for rule in POLICY.get("code_rules", []):
            if rule.get("only_in") and not matches(path, rule["only_in"]):
                continue
            if rule.get("allow_in") and matches(path, rule["allow_in"]):
                continue
            if matches(path, POLICY["test_paths"]):
                continue
            if re.search(rule["pattern"], line, re.IGNORECASE):
                rule_hits.append(f"`{rule['id']}` in {path}: {rule['message']}")
    if rule_hits:
        blockers.extend(sorted(set(rule_hits)))
    else:
        clean.append("Code rules")

    # 4. scanners
    def scanner(name, cmd, fail_pattern=None, ok_codes=(0,), present_if=None):
        if present_if and not present_if():
            return
        if not shutil.which(cmd[0] if isinstance(cmd, list) else cmd.split()[0]):
            (blockers if a.strict else warnings).append(f"{name}: tool not installed ({cmd[0] if isinstance(cmd, list) else cmd.split()[0]}).")
            return
        code, out = run(cmd)
        bad = code not in ok_codes or (fail_pattern and re.search(fail_pattern, out, re.IGNORECASE))
        if bad:
            blockers.append(f"{name} found problems:\n```\n{out.strip()[:3000]}\n```")
        else:
            clean.append(name)

    skip = {"node_modules", ".git", "bin", "obj", "dist", "build", ".venv", "venv", "stories", "context", ".next"}
    def project_files(pattern):
        found = []
        for dirpath, dirnames, filenames in os.walk(ROOT):
            dirnames[:] = [d for d in dirnames if d not in skip and not d.startswith(".")]
            found += [os.path.join(dirpath, f) for f in filenames if fnmatch.fnmatch(f, pattern)]
        return found
    has = lambda pattern: (lambda: bool(project_files(pattern)))
    scanner("Secrets (gitleaks)", ["gitleaks", "detect", "--no-banner", "--redact", "--source", "."])
    scanner("Known vulnerabilities (osv-scanner)", ["osv-scanner", "scan", "source", "-r", "."])
    scanner(".NET vulnerable packages", "dotnet list package --vulnerable --include-transitive",
            fail_pattern=r"has the following vulnerable packages", present_if=has("*.csproj"))
    scanner(".NET outdated packages", "dotnet list package --outdated",
            fail_pattern=r"has the following updates", present_if=has("*.csproj"))
    for pkg in project_files("package.json"):
        if os.path.exists(os.path.join(os.path.dirname(pkg), "package-lock.json")):
            scanner(f"npm audit ({os.path.relpath(os.path.dirname(pkg), ROOT)})",
                    ["npm", "audit", "--audit-level=moderate", "--prefix", os.path.dirname(pkg)])
    for req in project_files("requirements*.txt"):
        scanner(f"pip-audit ({os.path.relpath(req, ROOT)})", ["pip-audit", "-r", req])

    print("# Guard report\n")
    print("## Blockers\n" + ("\n".join(f"- {b}" for b in blockers) if blockers else "None") + "\n")
    print("## Warnings\n" + ("\n".join(f"- {w}" for w in warnings) if warnings else "None") + "\n")
    print("## Clean\n" + ("\n".join(f"- {c}" for c in clean) if clean else "Nothing checked") + "\n")
    print("RESULT: " + ("BLOCKED" if blockers else "PASS"))
    sys.exit(1 if blockers else 0)


if __name__ == "__main__":
    main()
