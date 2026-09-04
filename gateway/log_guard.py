from __future__ import annotations
import json
import os
from datetime import datetime, timezone
from pathlib import Path

from . import enforcement

_ENV_ROOT = os.environ.get("JFA_REPO_ROOT")
ROOT = Path(_ENV_ROOT) if _ENV_ROOT else Path(__file__).resolve().parent.parent
LOG_DIR = ROOT / "logs"
FAILURES_CACHE = LOG_DIR / "failures.cache.json"

_LEVEL_MARKERS = {"error": "ERROR", "fatal": "FATAL", "warn": "WARN"}


def refresh_failures_cache() -> dict:
    """Scan logs/*.log, keep only WARN/ERROR/FATAL lines, write the cache.

    This is the only function that ever opens a raw .log file. Every other
    code path (log_query action) reads FAILURES_CACHE, never the raw file.
    """
    failures: list[dict] = []
    for log_file in sorted(LOG_DIR.glob("*.log")):
        try:
            for line in log_file.read_text(errors="replace").splitlines():
                upper = line.upper()
                if any(marker in upper for marker in _LEVEL_MARKERS.values()):
                    failures.append({"source": log_file.name, "line": line})
        except OSError:
            continue

    cache = {
        "updated_at": datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
        "failures": failures[-500:],  # bounded cache size
    }
    FAILURES_CACHE.write_text(json.dumps(cache, indent=2) + "\n")
    return cache


def query_failures(filter_level: str | None, tail_lines: int | None) -> list[str]:
    """Return at most `tail_lines` (capped) failure lines, filtered by level."""
    if not FAILURES_CACHE.exists():
        refresh_failures_cache()
    cache = json.loads(FAILURES_CACHE.read_text())
    lines = [f["line"] for f in cache.get("failures", [])]

    if filter_level:
        marker = _LEVEL_MARKERS.get(filter_level)
        if marker:
            lines = [l for l in lines if marker in l.upper()]

    capped = enforcement.cap_log_tail(tail_lines)
    return lines[-capped:]
