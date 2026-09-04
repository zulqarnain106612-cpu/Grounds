"""The no-local-heavy-ops policy must be enforced by code, not by documentation."""
import json
import os
import re
from pathlib import Path

import pytest

from gateway import gateway, handlers, schema_guard

REAL_ROOT = Path(__file__).resolve().parent.parent


def _req(action, domain, ci_op=None):
    payload = {"data": None}
    if ci_op is not None:
        payload["ci_op"] = ci_op
    return {
        "meta": {
            "schema_version": "1.1.0",
            "session_id": "550e8400-e29b-41d4-a716-446655440000",
            "tick": 0, "phase": "1", "timestamp_utc": "2026-09-04T00:00:00Z",
        },
        "intent": {"action": action, "domain": domain, "priority": 5},
        "payload": payload,
    }


def test_ciop_forbids_blocking_and_local_runs():
    """A request that waits on CI, or claims a local run, must not validate."""
    base = {"op": "dispatch", "workflow": "ingest.yml", "remote_only": True, "wait": False}
    schema_guard.validate(_req("ci_dispatch", "ci", base))

    assert not schema_guard.is_valid(_req("ci_dispatch", "ci", {**base, "wait": True}))
    assert not schema_guard.is_valid(_req("ci_dispatch", "ci", {**base, "remote_only": False}))
    assert not schema_guard.is_valid(_req("ci_dispatch", "ci", {k: v for k, v in base.items()
                                                                if k != "remote_only"}))


def test_ingest_outside_ci_does_not_run_locally(monkeypatch):
    """Outside CI, `ingest` must delegate to a workflow instead of building."""
    monkeypatch.delenv("GITHUB_ACTIONS", raising=False)
    monkeypatch.delenv("JFA_CI", raising=False)

    calls = []

    def fake_gh(args, timeout_s=20.0):
        calls.append(args)
        return 0, "dispatched"

    monkeypatch.setattr(handlers, "_gh", fake_gh)

    def explode(*a, **k):
        raise AssertionError("ingest ran locally - execution policy violated")

    from gateway import ingest
    monkeypatch.setattr(ingest, "build_kb_index", explode)
    monkeypatch.setattr(ingest, "build_graph", explode)

    env = gateway.handle(json.dumps(_req(
        "ingest", "knowledge_base",
        {"op": "dispatch", "workflow": "ingest.yml", "remote_only": True, "wait": False},
    )))
    resp = env["response"]
    assert resp["status"] == "ok", resp
    assert resp["result"]["delegated_action"] == "ingest"
    assert resp["result"]["blocking"] is False
    assert calls and calls[0][:3] == ["workflow", "run", "ingest.yml"]


def test_manifest_outside_ci_does_not_run_locally(monkeypatch):
    monkeypatch.delenv("GITHUB_ACTIONS", raising=False)
    monkeypatch.delenv("JFA_CI", raising=False)
    monkeypatch.setattr(handlers, "_gh", lambda args, timeout_s=20.0: (0, "dispatched"))

    from gateway import manifest
    monkeypatch.setattr(manifest, "build_manifest",
                        lambda *a, **k: (_ for _ in ()).throw(
                            AssertionError("manifest ran locally")))

    env = gateway.handle(json.dumps(_req(
        "manifest", "build_pipeline",
        {"op": "dispatch", "workflow": "manifest.yml", "remote_only": True, "wait": False},
    )))
    assert env["response"]["status"] == "ok"
    assert env["response"]["result"]["delegated_action"] == "manifest"


def test_ingest_inside_ci_runs_in_process(monkeypatch, isolated_repo):
    """Inside CI the same action does the real work."""
    monkeypatch.setenv("JFA_CI", "1")
    monkeypatch.setattr(
        handlers, "_gh",
        lambda args, timeout_s=20.0: (_ for _ in ()).throw(
            AssertionError("dispatched a workflow from inside CI - infinite loop risk")),
    )
    env = gateway.handle(json.dumps(_req(
        "ingest", "knowledge_base",
        {"op": "dispatch", "workflow": "ingest.yml", "remote_only": True, "wait": False},
    )))
    resp = env["response"]
    assert resp["status"] == "ok", resp
    assert resp["result"]["chunk_count"] > 0


def test_no_handler_blocks_on_a_ci_run():
    """Guards against someone reintroducing `gh run watch` or a sleep loop."""
    src = (REAL_ROOT / "gateway" / "handlers.py").read_text()
    for pattern in (r"gh\s+run\s+watch", r"--wait\b", r"\btime\.sleep\("):
        assert not re.search(pattern, src), f"handlers.py blocks on CI: /{pattern}/"


def test_every_policy_workflow_exists():
    policy = schema_guard.load_schema()["x-enforcement"]["execution_policy"]
    assert policy["local_heavy_ops"] == "FORBIDDEN"
    assert policy["required_execution_target"] == "github_actions"
    for name, path in policy["workflows"].items():
        assert (REAL_ROOT / path).exists(), f"workflow '{name}' missing at {path}"
