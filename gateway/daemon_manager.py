from __future__ import annotations
import json
import os
import signal
import subprocess
import sys
from pathlib import Path

_ENV_ROOT = os.environ.get("JFA_REPO_ROOT")
ROOT = Path(_ENV_ROOT) if _ENV_ROOT else Path(__file__).resolve().parent.parent
REGISTRY_PATH = ROOT / "daemons" / "registry.json"
STATE_DIR = ROOT / "daemons" / "state"
STATE_DIR.mkdir(parents=True, exist_ok=True)

_ACTION_ENTRYPOINT = {
    "gateway.symbol_scanner:rebuild_symbol_index": ["-m", "gateway.symbol_scanner"],
    "gateway.log_guard:refresh_failures_cache": ["-c", "from gateway.log_guard import refresh_failures_cache as r; import time\nwhile True:\n import time; r(); time.sleep(2)"],
}


def _registry() -> dict:
    return json.loads(REGISTRY_PATH.read_text())["daemons"]


def _pid_path(daemon_id: str) -> Path:
    return STATE_DIR / f"{daemon_id}.pid"


def status(daemon_id: str | None = None) -> dict:
    reg = _registry()
    ids = [daemon_id] if daemon_id else list(reg.keys())
    out = {}
    for did in ids:
        if did not in reg:
            out[did] = "unknown_daemon"
            continue
        pid_file = _pid_path(did)
        if not pid_file.exists():
            out[did] = "stopped"
            continue
        pid = int(pid_file.read_text().strip())
        try:
            os.kill(pid, 0)
            out[did] = f"running(pid={pid})"
        except OSError:
            pid_file.unlink(missing_ok=True)
            out[did] = "stopped"
    return out


def start(daemon_id: str) -> dict:
    reg = _registry()
    if daemon_id not in reg:
        return {"error": f"unknown daemon {daemon_id}"}
    if status(daemon_id)[daemon_id].startswith("running"):
        return {"status": "already_running"}

    # symbol_indexer runs one rebuild pass per invocation (invoked by hooks/CI);
    # log_monitor runs as a small polling loop for local dev convenience.
    # JFA_REPO_ROOT is always passed explicitly so a spawned subprocess
    # operates on the same repo root as this process -- required both for
    # correctness when this package is installed elsewhere on PYTHONPATH
    # and for test isolation (see tests/conftest.py).
    child_env = {**os.environ, "JFA_REPO_ROOT": str(ROOT)}

    if daemon_id == "symbol_indexer":
        proc = subprocess.Popen([sys.executable, "-m", "gateway.symbol_scanner"], cwd=ROOT, env=child_env)
    elif daemon_id == "log_monitor":
        loop = (
            "from gateway.log_guard import refresh_failures_cache as r\n"
            "import time\n"
            "while True:\n"
            "    r()\n"
            "    time.sleep(2)\n"
        )
        proc = subprocess.Popen([sys.executable, "-c", loop], cwd=ROOT, env=child_env)
    else:
        return {"error": f"no launcher defined for {daemon_id}"}

    _pid_path(daemon_id).write_text(str(proc.pid))
    return {"status": "started", "pid": proc.pid}


def stop(daemon_id: str) -> dict:
    pid_file = _pid_path(daemon_id)
    if not pid_file.exists():
        return {"status": "not_running"}
    pid = int(pid_file.read_text().strip())
    try:
        os.kill(pid, signal.SIGTERM)
    except OSError:
        pass
    pid_file.unlink(missing_ok=True)
    return {"status": "stopped"}
