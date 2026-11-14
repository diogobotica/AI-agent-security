#!/usr/bin/env python3
"""Dependency vulnerability scanner (no install needed; uses the OSV.dev database).

  python .workflow/kit/scripts/scan_deps.py                 # scan now, write stories/security/dependencies.md
  python .workflow/kit/scripts/scan_deps.py --if-older 24   # skip if the last scan is newer than 24 hours
  python .workflow/kit/scripts/scan_deps.py --fail-on high  # exit 1 on high or critical findings (CI)

Reads every lock file and manifest in the repo:
  npm: package-lock.json · Python: requirements*.txt, poetry.lock, uv.lock, Pipfile.lock ·
  .NET: packages.lock.json, *.csproj, Directory.Packages.props
Each exact version is checked against OSV (covers GitHub advisories, PyPI, npm and NuGet advisories).
Exit codes: 0 clean or below threshold, 1 findings at or above --fail-on, 2 scan could not run.
"""
import argparse
import datetime as dt
import json
import os
import re
import sys
import urllib.request
import xml.etree.ElementTree as ET

ROOT = os.environ.get("CLAUDE_PROJECT_DIR") or os.getcwd()
OUT_DIR = os.environ.get("SCAN_OUT_DIR") or os.path.join(ROOT, "stories", "security")
SKIP_DIRS = {"node_modules", ".git", "bin", "obj", ".venv", "venv", "__pycache__", ".next", "dist", "out", ".terraform"}
RANK = {"low": 1, "moderate": 2, "medium": 2, "high": 3, "critical": 4}


def post(url, body):
    req = urllib.request.Request(url, data=json.dumps(body).encode(), headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(req, timeout=30) as r:
        return json.loads(r.read().decode())


def get(url):
    with urllib.request.urlopen(url, timeout=30) as r:
        return json.loads(r.read().decode())


# ---------- collect packages ----------

def walk():
    for dirpath, dirnames, filenames in os.walk(ROOT):
        dirnames[:] = [d for d in dirnames if d not in SKIP_DIRS and not d.startswith(".") or d == ".github"]
        for f in filenames:
            yield os.path.join(dirpath, f)


def rel(p):
    return os.path.relpath(p, ROOT).replace("\\", "/")


def parse_file(path):
    name = os.path.basename(path).lower()
    found = []
    try:
        text = open(path, encoding="utf-8", errors="replace").read()
    except OSError:
        return found
    if name == "package-lock.json":
        data = json.loads(text)
        for key, info in (data.get("packages") or {}).items():
            if key and "node_modules/" in key and info.get("version"):
                found.append(("npm", key.split("node_modules/")[-1], info["version"]))
        if not data.get("packages"):
            def deps(d):
                for n, i in (d or {}).items():
                    if i.get("version"):
                        found.append(("npm", n, i["version"]))
                    deps(i.get("dependencies"))
            deps(data.get("dependencies"))
    elif re.fullmatch(r"requirements.*\.txt", name):
        for line in text.splitlines():
            m = re.match(r"^\s*([A-Za-z0-9_.\-]+)(\[[^\]]*\])?\s*==\s*([^\s;#]+)", line)
            if m:
                found.append(("PyPI", m.group(1), m.group(3)))
    elif name in ("poetry.lock", "uv.lock"):
        for block in re.split(r"\n\[\[package\]\]\n", "\n" + text):
            n = re.search(r'^name\s*=\s*"([^"]+)"', block, re.M)
            v = re.search(r'^version\s*=\s*"([^"]+)"', block, re.M)
            if n and v:
                found.append(("PyPI", n.group(1), v.group(1)))
    elif name == "pipfile.lock":
        data = json.loads(text)
        for section in ("default", "develop"):
            for n, i in (data.get(section) or {}).items():
                v = (i.get("version") or "").lstrip("=")
                if v:
                    found.append(("PyPI", n, v))
    elif name == "packages.lock.json":
        data = json.loads(text)
        for _, deps in (data.get("dependencies") or {}).items():
            for n, i in deps.items():
                if i.get("resolved"):
                    found.append(("NuGet", n, i["resolved"]))
    elif name.endswith(".csproj") or name == "directory.packages.props":
        try:
            tree = ET.fromstring(text)
        except ET.ParseError:
            return found
        for el in tree.iter():
            tag = el.tag.split("}")[-1]
            if tag in ("PackageReference", "PackageVersion"):
                n = el.get("Include") or el.get("Update")
                v = el.get("Version") or (el.findtext("Version") or "")
                if n and v and re.match(r"^\d", v):
                    found.append(("NuGet", n, v.strip("[]")))
    return found


def collect():
    packages = {}
    for path in walk():
        for eco, name, version in parse_file(path):
            packages.setdefault((eco, name, version), set()).add(rel(path))
    return packages


# ---------- query OSV ----------

def severity(vuln):
    s = ((vuln.get("database_specific") or {}).get("severity") or "").lower()
    if s in RANK:
        return s
    for aff in vuln.get("affected", []):
        s = ((aff.get("ecosystem_specific") or {}).get("severity") or "").lower()
        if s in RANK:
            return s
    return "unknown"


def fixed_versions(vuln, name):
    out = set()
    for aff in vuln.get("affected", []):
        if (aff.get("package") or {}).get("name", "").lower() != name.lower():
            continue
        for r in aff.get("ranges", []):
            for e in r.get("events", []):
                if "fixed" in e:
                    out.add(e["fixed"])
    return sorted(out)


def scan(packages):
    keys = list(packages)
    results = []
    for i in range(0, len(keys), 500):
        chunk = keys[i:i + 500]
        res = post("https://api.osv.dev/v1/querybatch", {"queries": [
            {"package": {"ecosystem": e, "name": n}, "version": v} for e, n, v in chunk]})
        for key, r in zip(chunk, res.get("results", [])):
            for v in r.get("vulns", []) or []:
                results.append((key, v["id"]))
    details = {}
    for _, vid in results:
        if vid not in details:
            try:
                details[vid] = get(f"https://api.osv.dev/v1/vulns/{vid}")
            except Exception:
                details[vid] = {"id": vid}
    findings = []
    for (eco, name, version), vid in results:
        d = details[vid]
        findings.append({
            "ecosystem": eco, "package": name, "version": version, "id": vid,
            "aliases": [a for a in d.get("aliases", []) if a.startswith("CVE-")],
            "severity": severity(d), "summary": (d.get("summary") or "").strip(),
            "fixed": fixed_versions(d, name), "files": sorted(packages[(eco, name, version)]),
        })
    order = {"critical": 0, "high": 1, "moderate": 2, "medium": 2, "low": 3, "unknown": 4}
    findings.sort(key=lambda f: (order.get(f["severity"], 5), f["package"]))
    return findings


# ---------- report ----------

def write_report(packages, findings, error=None):
    os.makedirs(OUT_DIR, exist_ok=True)
    now = dt.datetime.now().strftime("%Y-%m-%d %H:%M")
    counts = {}
    for f in findings:
        counts[f["severity"]] = counts.get(f["severity"], 0) + 1
    summary = {"scanned_at": now, "packages": len(packages), "findings": len(findings), "by_severity": counts, "error": error}
    with open(os.path.join(OUT_DIR, "dependencies.json"), "w", encoding="utf-8") as f:
        json.dump({"summary": summary, "findings": findings}, f, indent=2)
    lines = ["# Dependency scan", "",
             f"**Scanned:** {now} · **Packages:** {len(packages)} · **Vulnerabilities:** {len(findings)}"
             + (" · " + ", ".join(f"{k} {v}" for k, v in counts.items()) if counts else ""), ""]
    if error:
        lines += [f"**The scan could not finish:** {error}", ""]
    elif not packages:
        lines += ["No lock files or manifests found yet.", ""]
    elif not findings:
        lines += ["No known vulnerabilities in the pinned versions.", ""]
    else:
        lines += ["| Severity | Package | Version | Advisory | Fixed in | Where |", "| --- | --- | --- | --- | --- | --- |"]
        for x in findings:
            adv = x["id"] + (f" ({', '.join(x['aliases'][:2])})" if x["aliases"] else "")
            lines.append(f"| {x['severity']} | {x['package']} ({x['ecosystem']}) | {x['version']} | "
                         f"[{adv}](https://osv.dev/vulnerability/{x['id']}) | {', '.join(x['fixed']) or 'no fix yet'} | "
                         f"{', '.join(x['files'][:3])} |")
        lines += ["", "Fix: upgrade to a fixed version (the dependency guard enforces the 14-day rule), "
                  "then re-run `python .workflow/kit/scripts/scan_deps.py`."]
    with open(os.path.join(OUT_DIR, "dependencies.md"), "w", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")
    return summary


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--if-older", type=float, help="hours; skip when the last scan is newer")
    ap.add_argument("--fail-on", choices=["low", "moderate", "high", "critical"])
    a = ap.parse_args()

    last = os.path.join(OUT_DIR, "dependencies.json")
    if a.if_older and os.path.exists(last):
        age_h = (dt.datetime.now().timestamp() - os.path.getmtime(last)) / 3600
        if age_h < a.if_older:
            print(f"Dependency scan skipped (last scan {age_h:.1f} h ago).")
            return 0

    packages = collect()
    try:
        findings = scan(packages) if packages else []
        error = None
    except Exception as e:
        findings, error = [], f"{e.__class__.__name__}: {e}"
    summary = write_report(packages, findings, error)
    try:
        sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
        import tracker
        tracker.render()
    except Exception:
        pass

    print(f"Dependency scan: {summary['packages']} packages, {summary['findings']} vulnerabilities "
          f"{summary['by_severity'] or ''} -> {os.path.relpath(os.path.join(OUT_DIR, 'dependencies.md'), ROOT)}")
    if error:
        print(f"Scan error: {error}")
        return 2
    if a.fail_on and any(RANK.get(f["severity"], 0) >= RANK[a.fail_on] for f in findings):
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
