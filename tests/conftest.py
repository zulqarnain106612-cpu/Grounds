import json
import os
import shutil
from pathlib import Path
import pytest

REAL_ROOT = Path(__file__).resolve().parent.parent
COPY_DIRS = ["schema", "config", "retrieval", "daemons", "tools", "knowledge", "symbols", "index", "logs"]


@pytest.fixture(autouse=True)
def isolated_repo(tmp_path, monkeypatch):
    """Run every test against a throwaway copy of the repo's data files.

    Without this, tests that exercise write/delete/knowledge_update/etc.
    would mutate the real tracked seed files (index/kb.index.json,
    knowledge/graph.json, ...), making the test suite non-idempotent and
    corrupting repo state on every run. Each module's path constants are
    monkeypatched to point at the tmp copy for the duration of one test.
    """
    fake_root = tmp_path / "repo"
    fake_root.mkdir()
    for d in COPY_DIRS:
        shutil.copytree(REAL_ROOT / d, fake_root / d)
    (fake_root / "logs" / "failures.cache.json").unlink(missing_ok=True)

    from gateway import kb_store, log_guard, context_store, gateway as gw, \
        daemon_manager, enforcement, symbol_scanner, handlers

    monkeypatch.setattr(kb_store, "KB_PATH", fake_root / "index" / "kb.index.json")
    monkeypatch.setattr(kb_store, "GRAPH_PATH", fake_root / "knowledge" / "graph.json")
    monkeypatch.setattr(kb_store, "SYMBOLS_PATH", fake_root / "symbols" / "index.json")

    monkeypatch.setattr(log_guard, "LOG_DIR", fake_root / "logs")
    monkeypatch.setattr(log_guard, "FAILURES_CACHE", fake_root / "logs" / "failures.cache.json")

    state_dir = fake_root / "daemons" / "state"
    state_dir.mkdir(parents=True, exist_ok=True)
    monkeypatch.setattr(context_store, "STATE_DIR", state_dir)
    monkeypatch.setattr(daemon_manager, "STATE_DIR", state_dir)
    monkeypatch.setattr(daemon_manager, "REGISTRY_PATH", fake_root / "daemons" / "registry.json")

    monkeypatch.setattr(gw, "AUDIT_LOG", fake_root / "logs" / "agent.jsonl")
    monkeypatch.setattr(gw, "ROOT", fake_root)

    monkeypatch.setattr(handlers, "ROOT", fake_root)

    # The CI actions (ci_dispatch/ci_status/ingest/manifest) shell out to `gh`.
    # Tests must never touch the network or depend on the tmp copy being a git
    # repo, so `_gh` is stubbed to a deterministic success here. Tests that care
    # about dispatch behaviour override this with their own stub -- see
    # tests/test_ci_policy.py, which asserts what is and is not dispatched.
    def _fake_gh(args, timeout_s=20.0):
        # `gh pr view --json headRefName` must yield an object; returning "[]"
        # for every command only happened to satisfy the list-shaped callers.
        if args[:2] == ["pr", "view"]:
            return 0, json.dumps({"headRefName": "stub-branch", "headRefOid": "0" * 40})
        return 0, "[]"

    monkeypatch.setattr(handlers, "_gh", _fake_gh)

    monkeypatch.setattr(symbol_scanner, "ROOT", fake_root)
    monkeypatch.setattr(symbol_scanner, "SYMBOLS_PATH", fake_root / "symbols" / "index.json")
    monkeypatch.setattr(symbol_scanner, "CONFIG_PATH", fake_root / "config" / "agent.config.json")

    monkeypatch.setattr(enforcement, "CONFIG_PATH", fake_root / "config" / "agent.config.json")
    monkeypatch.setattr(enforcement, "PATTERNS_PATH", fake_root / "tools" / "patterns.json")
    enforcement.config.cache_clear()
    enforcement.forbidden_patterns.cache_clear()

    # daemon_manager can spawn a *real subprocess* (daemon_start action).
    # A subprocess computes its own module-level ROOT fresh, so in-process
    # monkeypatching cannot reach it -- JFA_REPO_ROOT is the only channel
    # that does. Without this, daemon_start in a test would silently
    # rebuild the real repo's symbols/index.json from a leaked process.
    monkeypatch.setattr(daemon_manager, "ROOT", fake_root)
    monkeypatch.setenv("JFA_REPO_ROOT", str(fake_root))

    yield fake_root

    # Never let a test leak a background process, real-repo or not.
    for pid_file in state_dir.glob("*.pid"):
        try:
            pid = int(pid_file.read_text().strip())
            import signal as _signal
            os.kill(pid, _signal.SIGTERM)
        except (OSError, ValueError):
            pass
        finally:
            pid_file.unlink(missing_ok=True)
