"""Service Bus emulator queue settings needed by US-103 (idempotent job messages).

IQueue sends each message with a MessageId; the run-jobs queue must detect duplicates so the same MessageId sent
twice is delivered once (CLAUDE.md "idempotent job messages"). Reads the file only: no Docker, no network.
"""

from __future__ import annotations

import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SERVICEBUS_CONFIG = ROOT / "infra" / "local" / "servicebus" / "config.json"


def test_run_jobs_queue_detects_duplicate_message_ids() -> None:
    assert SERVICEBUS_CONFIG.is_file(), "infra/local/servicebus/config.json is missing"
    config = json.loads(SERVICEBUS_CONFIG.read_text(encoding="utf-8"))
    queues = {
        queue.get("Name"): queue.get("Properties", {})
        for namespace in config.get("UserConfig", {}).get("Namespaces", [])
        for queue in namespace.get("Queues", [])
    }

    run_jobs = queues.get("run-jobs")
    assert run_jobs is not None, "queue run-jobs is missing"
    assert run_jobs.get("RequiresDuplicateDetection") is True, "run-jobs must set RequiresDuplicateDetection to true"
    window = str(run_jobs.get("DuplicateDetectionHistoryTimeWindow", ""))
    assert window.startswith("PT") and window != "PT0S", "run-jobs needs a duplicate detection window (ISO 8601, e.g. PT10M)"
