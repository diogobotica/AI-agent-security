#!/usr/bin/env python3
"""Shared paths and policy for the flow plugin's hooks and scripts (standard library only).

The plugin is installed once (user scope) and only acts in projects that opted in with /flow:init,
i.e. that have .workflow/policy.json. Scripts run from two places:
  * the plugin itself (hooks):            <plugin>/scripts/
  * the project's synced copy (commands): <project>/.workflow/kit/scripts/
Both find the base policy next to them (../guard-policy.base.json) and merge the project's .workflow/policy.json.
CI uses .github/guard/guard-policy.json through GUARD_POLICY.
"""
import json
import os

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.environ.get("CLAUDE_PROJECT_DIR") or os.getcwd()
WORKFLOW = os.path.join(ROOT, ".workflow")
KIT = os.path.join(WORKFLOW, "kit")
APPEND_LISTS = {"protected_paths", "typosquat_watchlist", "allowed_licenses", "repo_config_paths"}


def active():
    """True when the project uses the workflow (created by /flow:init)."""
    return os.path.exists(os.path.join(WORKFLOW, "policy.json"))


def read_json(path, default=None):
    try:
        with open(path, encoding="utf-8") as f:
            return json.load(f)
    except (FileNotFoundError, json.JSONDecodeError):
        return default


def merge(base, extra):
    out = dict(base)
    for key, value in (extra or {}).items():
        if key.startswith("_"):
            continue
        if isinstance(value, dict) and isinstance(out.get(key), dict):
            out[key] = merge(out[key], value)
        elif isinstance(value, list) and key in APPEND_LISTS and isinstance(out.get(key), list):
            out[key] = out[key] + [v for v in value if v not in out[key]]
        else:
            out[key] = value
    return out


def base_policy_path():
    return os.path.join(os.path.dirname(HERE), "guard-policy.base.json")


def policy():
    override = os.environ.get("GUARD_POLICY")
    if override:
        path = override if os.path.isabs(override) else os.path.join(ROOT, override)
        with open(path, encoding="utf-8") as f:
            return json.load(f)
    with open(base_policy_path(), encoding="utf-8") as f:
        base = json.load(f)
    return merge(base, read_json(os.path.join(WORKFLOW, "policy.json"), {}))


def plugin_root():
    """The plugin folder when running from it, else the synced kit."""
    return os.path.dirname(HERE)
