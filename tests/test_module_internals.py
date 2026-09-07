"""Branch-level coverage for the paths the behavioural suite never reaches.

The existing tests exercise the happy paths and the enforcement guarantees.
What they leave uncovered is the error handling: what happens when a file is
missing, a subprocess dies, `gh` is absent, a handler throws, a log line is
unreadable. Those branches are exactly where a silent regression hides, since
nothing else in CI executes them.

Grouped by module, in the order they appear in gateway/.
"""
from __future__ import annotations

import json
import subprocess
from pathlib import Path

import pytest

from gateway import (
    daemon_manager,
    enforcement,
    gateway,
    handlers,
    ingest,
    kb_store,
    log_guard,
    manifest,
    schema_guard,
    symbol_scanner,
)

# Captured at import time, before conftest's autouse fixture replaces it with a
# stub. The stub is right for every other test -- these are the tests that need
# the real thing.
_REAL_GH = handlers._gh


def _request(action: str, payload: dict, domain: str = "retrieval") -> dict:
    return {
        "meta": {
            "schema_version": "1.1.0",
            "session_id": "550e8400-e29b-41d4-a716-446655440000",
            "tick": 0, "phase": "1", "timestamp_utc": "2026-09-07T00:00:00Z",
        },
        "intent": {"action": action, "domain": domain, "priority": 5},
        "payload": {"data": None, **payload},
    }


# --------------------------------------------------------------------------
# enforcement
# --------------------------------------------------------------------------

def test_caps_fall_back_to_the_configured_hard_cap_when_unspecified():
    cfg = enforcement.config()["enforcement"]
    assert enforcement.cap_log_tail(None) == cfg["max_log_tail_lines"]
    assert enforcement.cap_tool_output_tokens(None) == cfg["max_tool_output_tokens"]
    assert enforcement.cap_output_lines(None) == cfg["max_command_output_lines"]


def test_caps_clamp_an_inflated_request_down_to_the_hard_cap():
    cfg = enforcement.config()["enforcement"]
    assert enforcement.cap_log_tail(10**6) == cfg["max_log_tail_lines"]
    assert enforcement.cap_tool_output_tokens(10**6) == cfg["max_tool_output_tokens"]


# --------------------------------------------------------------------------
# gateway.handle -- the three failure modes below the happy path
# --------------------------------------------------------------------------

def test_gateway_reports_an_action_with_no_registered_handler(monkeypatch):
    monkeypatch.setattr(
        handlers, "DISPATCH", {k: v for k, v in handlers.DISPATCH.items() if k != "retrieve"}
    )
    response = gateway.handle(_request("retrieve", {"context": {
        "strategy": "keyword", "scope": "kb", "target": "x", "top_k": 1, "max_tokens": 100,
    }}))["response"]
    assert response["status"] == "error"
    assert response["errors"][0]["code"] == "no_handler"


def test_gateway_converts_a_handler_crash_into_a_schema_valid_error(monkeypatch):
    def _boom(_req):
        raise RuntimeError("handler blew up")

    monkeypatch.setattr(handlers, "DISPATCH", {**handlers.DISPATCH, "retrieve": _boom})
    response = gateway.handle(_request("retrieve", {"context": {
        "strategy": "keyword", "scope": "kb", "target": "x", "top_k": 1, "max_tokens": 100,
    }}))["response"]
    assert response["errors"][0]["code"] == "handler_exception"
    assert "RuntimeError: handler blew up" in response["errors"][0]["message"]


def test_gateway_refuses_to_return_a_response_the_schema_forbids(monkeypatch):
    monkeypatch.setattr(
        handlers, "DISPATCH",
        {**handlers.DISPATCH, "retrieve": lambda _req: {"status": "not-a-valid-status"}},
    )
    response = gateway.handle(_request("retrieve", {"context": {
        "strategy": "keyword", "scope": "kb", "target": "x", "top_k": 1, "max_tokens": 100,
    }}))["response"]
    assert response["errors"][0]["code"] == "response_schema_violation"


# --------------------------------------------------------------------------
# daemon_manager
# --------------------------------------------------------------------------

def test_daemon_status_flags_an_unregistered_id():
    assert daemon_manager.status("no_such_daemon") == {"no_such_daemon": "unknown_daemon"}


def test_daemon_status_reports_a_live_pid_as_running(monkeypatch):
    # Never write this process's own pid here: conftest's teardown SIGTERMs
    # every pid it finds in the state dir, which would kill the test runner.
    daemon_manager._pid_path("symbol_indexer").write_text("4242424")
    monkeypatch.setattr(daemon_manager.os, "kill", lambda pid, sig: None)

    assert daemon_manager.status("symbol_indexer")["symbol_indexer"] == "running(pid=4242424)"


def test_daemon_status_clears_a_stale_pid_file(monkeypatch):
    pid_file = daemon_manager._pid_path("symbol_indexer")
    pid_file.write_text("4242424")
    monkeypatch.setattr(daemon_manager.os, "kill", _raise_oserror)

    assert daemon_manager.status("symbol_indexer") == {"symbol_indexer": "stopped"}
    assert not pid_file.exists(), "a stale pid file must be cleaned up, not left to mislead"


def test_daemon_status_covers_every_registered_daemon_when_no_id_is_given():
    assert set(daemon_manager.status()) == {"symbol_indexer", "log_monitor"}


def _raise_oserror(*_args, **_kwargs):
    raise OSError("no such process")


class _FakeProc:
    pid = 999999


def test_daemon_start_rejects_an_unknown_daemon():
    assert daemon_manager.start("nope") == {"error": "unknown daemon nope"}


def test_daemon_start_is_idempotent_while_already_running(monkeypatch):
    daemon_manager._pid_path("symbol_indexer").write_text("4242424")
    monkeypatch.setattr(daemon_manager.os, "kill", lambda pid, sig: None)

    assert daemon_manager.start("symbol_indexer") == {"status": "already_running"}


def test_daemon_start_launches_the_log_monitor_loop(monkeypatch):
    captured = {}

    def _fake_popen(argv, **kwargs):
        captured["argv"] = argv
        return _FakeProc()

    monkeypatch.setattr(daemon_manager.subprocess, "Popen", _fake_popen)

    assert daemon_manager.start("log_monitor") == {"status": "started", "pid": 999999}
    assert captured["argv"][1] == "-c"
    assert "refresh_failures_cache" in captured["argv"][2]
    assert daemon_manager._pid_path("log_monitor").read_text() == "999999"


def test_daemon_start_launches_the_symbol_indexer(monkeypatch):
    monkeypatch.setattr(daemon_manager.subprocess, "Popen", lambda argv, **kw: _FakeProc())
    assert daemon_manager.start("symbol_indexer")["status"] == "started"


def test_daemon_start_refuses_a_registered_daemon_with_no_launcher(isolated_repo):
    registry_path = isolated_repo / "daemons" / "registry.json"
    data = json.loads(registry_path.read_text())
    data["daemons"]["mystery"] = {"id": "mystery", "action": "nothing"}
    registry_path.write_text(json.dumps(data))

    assert daemon_manager.start("mystery") == {"error": "no launcher defined for mystery"}


def test_daemon_stop_reports_not_running_without_a_pid_file():
    assert daemon_manager.stop("symbol_indexer") == {"status": "not_running"}


def test_daemon_stop_signals_and_removes_the_pid_file(monkeypatch):
    pid_file = daemon_manager._pid_path("symbol_indexer")
    pid_file.write_text("4242424")
    signalled = []
    monkeypatch.setattr(daemon_manager.os, "kill", lambda pid, sig: signalled.append((pid, sig)))

    assert daemon_manager.stop("symbol_indexer") == {"status": "stopped"}
    assert signalled == [(4242424, daemon_manager.signal.SIGTERM)]
    assert not pid_file.exists()


def test_daemon_stop_tolerates_an_already_dead_process(monkeypatch):
    pid_file = daemon_manager._pid_path("symbol_indexer")
    pid_file.write_text("4242424")
    monkeypatch.setattr(daemon_manager.os, "kill", _raise_oserror)

    assert daemon_manager.stop("symbol_indexer") == {"status": "stopped"}
    assert not pid_file.exists()


# --------------------------------------------------------------------------
# kb_store
# --------------------------------------------------------------------------

def test_keyword_scoring_is_zero_without_terms_or_haystack():
    assert kb_store._score_keyword(set(), {"tags": ["a"], "summary": "a"}) == 0.0
    assert kb_store._score_keyword({"a"}, {"tags": [], "summary": ""}) == 0.0


def test_retrieve_returns_nothing_without_a_target():
    assert kb_store.retrieve("keyword", None, 5, 1500) == []


def test_retrieve_returns_nothing_from_an_empty_kb(isolated_repo):
    (isolated_repo / "index" / "kb.index.json").write_text(json.dumps({"chunks": []}))
    assert kb_store.retrieve("keyword", "anything", 5, 1500) == []


def test_retrieve_stops_when_the_token_budget_is_exhausted():
    assert kb_store.retrieve("keyword", "schema action", 5, 0) == []


def test_knowledge_update_can_update_remove_and_link_nodes():
    kb_store.apply_knowledge_op({"op": "add_node", "id": "n1", "label": "first"})

    updated = kb_store.apply_knowledge_op({"op": "update_node", "id": "n1", "label": "second"})
    assert [n for n in updated["graph"]["nodes"] if n["id"] == "n1"][0]["label"] == "second"

    linked = kb_store.apply_knowledge_op({"op": "add_edge", "id": "n1", "edge_to": "domain:player"})
    assert {"from": "n1", "to": "domain:player", "kind": "related"} in linked["graph"]["edges"]

    removed = kb_store.apply_knowledge_op({"op": "remove_node", "id": "n1"})
    assert "n1" not in {n["id"] for n in removed["graph"]["nodes"]}


# --------------------------------------------------------------------------
# log_guard
# --------------------------------------------------------------------------

def test_refresh_skips_clean_lines_and_unreadable_files(isolated_repo):
    log_dir = isolated_repo / "logs"
    (log_dir / "app.log").write_text("INFO all is well\nERROR something broke\nplain line\n")
    # a directory named like a log file: read_text raises IsADirectoryError (an OSError)
    (log_dir / "broken.log").mkdir()

    cache = log_guard.refresh_failures_cache()
    lines = [f["line"] for f in cache["failures"]]
    assert lines == ["ERROR something broke"]


def test_query_failures_without_a_filter_returns_everything(isolated_repo):
    (isolated_repo / "logs" / "app.log").write_text("ERROR one\nWARN two\n")
    log_guard.refresh_failures_cache()
    assert log_guard.query_failures(None, 10) == ["ERROR one", "WARN two"]


def test_query_failures_ignores_an_unknown_level(isolated_repo):
    (isolated_repo / "logs" / "app.log").write_text("ERROR one\nWARN two\n")
    log_guard.refresh_failures_cache()
    # 'debug' has no marker, so filtering must be skipped rather than returning []
    assert log_guard.query_failures("debug", 10) == ["ERROR one", "WARN two"]


def test_query_failures_filters_by_level(isolated_repo):
    (isolated_repo / "logs" / "app.log").write_text("ERROR one\nWARN two\n")
    log_guard.refresh_failures_cache()
    assert log_guard.query_failures("warn", 10) == ["WARN two"]


def test_query_failures_builds_the_cache_when_it_is_missing(isolated_repo):
    (isolated_repo / "logs" / "failures.cache.json").unlink(missing_ok=True)
    (isolated_repo / "logs" / "app.log").write_text("FATAL boom\n")
    assert log_guard.query_failures(None, 10) == ["FATAL boom"]


# --------------------------------------------------------------------------
# manifest
# --------------------------------------------------------------------------

def test_manifest_skips_pycache_and_counts_missing_indexes_as_zero(isolated_repo):
    cache_dir = isolated_repo / "gateway" / "__pycache__"
    cache_dir.mkdir(parents=True)
    (cache_dir / "stale.pyc").write_bytes(b"\x00")
    (isolated_repo / "symbols" / "index.json").unlink()

    built = manifest.build_manifest(isolated_repo)
    assert built["counts"]["symbols"] == 0
    assert not any("__pycache__" in e["path"] for e in built["files"])


def test_manifest_main_prints_a_summary(isolated_repo, monkeypatch, capsys):
    monkeypatch.setattr(manifest, "ROOT", isolated_repo)
    monkeypatch.setattr(manifest, "MANIFEST_PATH", isolated_repo / "index" / "manifest.json")

    assert manifest.main() == 0
    out = capsys.readouterr().out
    assert "manifest:" in out and "kb chunks" in out


# --------------------------------------------------------------------------
# schema_guard
# --------------------------------------------------------------------------

@pytest.fixture
def clean_schema_cache():
    """load_schema/validator are lru_cached; a test that swaps the schema file
    must not leave a poisoned cache behind for the rest of the suite."""
    schema_guard.load_schema.cache_clear()
    schema_guard.validator.cache_clear()
    yield
    schema_guard.load_schema.cache_clear()
    schema_guard.validator.cache_clear()


def test_load_schema_errors_when_the_file_is_absent(monkeypatch, tmp_path, clean_schema_cache):
    monkeypatch.setattr(schema_guard, "SCHEMA_PATH", tmp_path / "nope.json")
    with pytest.raises(schema_guard.SchemaLoadError, match="schema not found"):
        schema_guard.load_schema()


def test_load_schema_errors_on_an_invalid_draft7_schema(monkeypatch, tmp_path, clean_schema_cache):
    bad = tmp_path / "bad.json"
    bad.write_text(json.dumps({"type": "not-a-real-type"}))
    monkeypatch.setattr(schema_guard, "SCHEMA_PATH", bad)
    with pytest.raises(schema_guard.SchemaLoadError, match="not a valid Draft-07"):
        schema_guard.load_schema()


def test_is_valid_is_false_for_a_non_conforming_instance():
    assert schema_guard.is_valid({"totally": "wrong"}) is False


# --------------------------------------------------------------------------
# symbol_scanner
# --------------------------------------------------------------------------

CSHARP = """namespace Game.Player
{
    public class JetController
    {
        public void ApplyMovementForces(float x)
        {
        }
    }
}
"""


def test_scan_file_extracts_types_methods_and_namespace(tmp_path):
    source = tmp_path / "JetController.cs"
    source.write_text(CSHARP)

    symbols = symbol_scanner.scan_file(source, tmp_path)
    by_name = {s["name"]: s for s in symbols}
    assert by_name["JetController"]["kind"] == "class"
    assert by_name["JetController"]["namespace"] == "Game.Player"
    assert by_name["ApplyMovementForces"]["kind"] == "method"
    # the no-raw-source guarantee: metadata only, never a line of code
    assert all(set(s) == {"name", "kind", "file", "line", "namespace", "tags"} for s in symbols)


def test_scan_file_falls_back_to_an_absolute_path_outside_the_repo(tmp_path):
    source = tmp_path / "Outside.cs"
    source.write_text("public class Outside { }\n")
    symbols = symbol_scanner.scan_file(source, Path("/definitely/not/a/parent"))
    assert symbols[0]["file"] == str(source)


def test_scan_file_returns_nothing_for_an_unreadable_path(tmp_path):
    unreadable = tmp_path / "dir.cs"
    unreadable.mkdir()
    assert symbol_scanner.scan_file(unreadable, tmp_path) == []


def test_rebuild_symbol_index_picks_up_configured_sources(isolated_repo):
    source = isolated_repo / "Assets" / "_Game" / "Scripts" / "Player" / "JetController.cs"
    source.parent.mkdir(parents=True)
    source.write_text(CSHARP)

    index = symbol_scanner.rebuild_symbol_index(isolated_repo)
    assert "JetController" in {s["name"] for s in index["symbols"]}


# --------------------------------------------------------------------------
# ingest
# --------------------------------------------------------------------------

@pytest.fixture
def docs_repo(isolated_repo):
    """conftest's copy deliberately omits docs/ and README.md, so the markdown
    ingest path was never executed. Give it something to chunk."""
    (isolated_repo / "README.md").write_text("# Title\n\nintro line\n")
    docs = isolated_repo / "docs"
    docs.mkdir(exist_ok=True)
    (docs / "GUIDE.md").write_text("# One\n\nalpha\n\n## Two\n\nbeta\n")
    (docs / "EMPTY.md").write_text("no headings here at all\n")
    return isolated_repo


def test_markdown_ingest_chunks_per_heading(docs_repo):
    chunks = ingest._ingest_markdown(docs_repo)
    ids = {c["id"] for c in chunks}
    assert "doc:README.md#title" in ids
    assert "doc:docs/GUIDE.md#one" in ids
    assert "doc:docs/GUIDE.md#two" in ids
    # a file with no heading produces no chunk rather than an untitled one
    assert not any(c["id"].startswith("doc:docs/EMPTY.md") for c in chunks)
    guide = [c for c in chunks if c["id"] == "doc:docs/GUIDE.md#two"][0]
    assert guide["summary"] == "Two. beta"


def test_schema_ingest_skips_non_object_definitions(isolated_repo):
    schema_path = isolated_repo / "schema" / "agent.schema.json"
    schema = json.loads(schema_path.read_text())
    schema["definitions"]["NotADict"] = "a bare string"
    schema_path.write_text(json.dumps(schema))

    ids = {c["id"] for c in ingest._ingest_schema(isolated_repo)}
    assert "schema:definition:NotADict" not in ids
    assert "schema:definition:FileOp" in ids


def test_config_ingest_tolerates_missing_config_files(isolated_repo):
    (isolated_repo / "retrieval" / "strategies.json").unlink()
    (isolated_repo / "daemons" / "registry.json").unlink()
    assert ingest._ingest_configs(isolated_repo) == []


def test_config_ingest_accepts_a_registry_expressed_as_a_list(isolated_repo):
    (isolated_repo / "daemons" / "registry.json").write_text(json.dumps(
        {"daemons": [{"id": "watcher", "description": "list-shaped registry entry"}]}
    ))
    ids = {c["id"] for c in ingest._ingest_configs(isolated_repo)}
    assert "daemon:watcher" in ids


def test_symbol_ingest_is_empty_without_a_symbol_index(isolated_repo):
    (isolated_repo / "symbols" / "index.json").unlink()
    assert ingest._ingest_symbols(isolated_repo) == []


def test_symbol_ingest_describes_each_symbol_without_source_text(isolated_repo):
    (isolated_repo / "symbols" / "index.json").write_text(json.dumps({"symbols": [
        {"name": "JetController", "kind": "class", "file": "A.cs", "line": 7,
         "namespace": "Game.Player"},
        {"name": "Bare", "kind": "struct", "file": "B.cs", "line": 1, "namespace": None},
    ]}))
    chunks = ingest._ingest_symbols(isolated_repo)
    summaries = {c["id"]: c["summary"] for c in chunks}
    assert summaries["symbol:Game.Player.JetController"] == (
        "Class JetController in namespace Game.Player, declared at A.cs line 7."
    )
    assert summaries["symbol:Bare"] == "Struct Bare, declared at B.cs line 1."


def test_ingest_main_reports_what_it_built(docs_repo, monkeypatch, capsys):
    monkeypatch.setattr(ingest, "ROOT", docs_repo)
    monkeypatch.setattr(ingest, "KB_PATH", docs_repo / "index" / "kb.index.json")
    monkeypatch.setattr(ingest, "GRAPH_PATH", docs_repo / "knowledge" / "graph.json")

    assert ingest.main() == 0
    out = capsys.readouterr().out
    assert "ingested" in out and "kb chunks" in out
    assert "built graph:" in out


# --------------------------------------------------------------------------
# handlers -- error branches
# --------------------------------------------------------------------------

@pytest.mark.parametrize("action, payload, code", [
    ("symbol_lookup", {"context": {"strategy": "symbol", "scope": "symbol_table"}}, "missing_target"),
    ("log_query", {"log_op": {"op": "rotate"}}, "unsupported_op"),
    ("command_exec", {}, "missing_command"),
    ("retrieve", {}, "missing_context"),
    ("write", {}, "nothing_to_write"),
    ("delete", {}, "nothing_to_delete"),
    ("knowledge_update", {}, "missing_knowledge_op"),
    ("schema_validate", {}, "invalid_candidate"),
    ("ci_dispatch", {}, "missing_ci_op"),
    ("ci_dispatch", {"ci_op": {"op": "dispatch", "remote_only": True, "wait": False}}, "missing_workflow"),
])
def test_handlers_reject_incomplete_payloads(action, payload, code):
    result = handlers.DISPATCH[action](_request(action, payload))
    assert result["status"] == "error"
    assert result["errors"][0]["code"] == code


def test_tool_call_rejects_an_unregistered_tool():
    result = handlers.h_tool_call(_request("tool_call", {
        "tool": {"tool_name": "not_registered", "parameters": {}, "max_output": 100}
    }))
    assert result["errors"][0]["code"] == "unknown_tool"


def test_command_exec_reports_a_timeout(monkeypatch):
    def _timeout(*_args, **_kwargs):
        raise subprocess.TimeoutExpired(cmd="sleep", timeout=0.001)

    monkeypatch.setattr(handlers.subprocess, "run", _timeout)
    result = handlers.h_command_exec(_request("command_exec", {
        "command": {"cmd": "sleep", "args": ["9"], "capped": True, "timeout_ms": 1}
    }))
    assert result["errors"][0]["code"] == "timeout"


def test_command_exec_reports_a_failed_launch(monkeypatch):
    def _oserror(*_args, **_kwargs):
        raise OSError("no such binary")

    monkeypatch.setattr(handlers.subprocess, "run", _oserror)
    result = handlers.h_command_exec(_request("command_exec", {
        "command": {"cmd": "definitely-not-a-binary", "args": [], "capped": True}
    }))
    assert result["errors"][0]["code"] == "exec_failed"


def test_delete_of_a_missing_file_reports_no_mutation():
    result = handlers.h_delete(_request("delete", {
        "file_op": {"op": "delete", "path": "logs/never_existed.txt"}
    }, domain="log"))
    assert result["status"] == "ok"
    assert result["mutations"] == []


def test_schema_validate_reports_a_valid_candidate():
    candidate = _request("retrieve", {"context": {
        "strategy": "keyword", "scope": "kb", "target": "x", "top_k": 1, "max_tokens": 100,
    }})
    result = handlers.h_schema_validate(_request("schema_validate", {"data": candidate},
                                                domain="schema"))
    assert result["result"] == {"valid": True}


# --- the `gh` shell-out itself -------------------------------------------------

def test_gh_short_circuits_when_dispatch_is_disabled(monkeypatch):
    monkeypatch.setattr(handlers, "_gh", _REAL_GH)
    monkeypatch.setenv("JFA_NO_DISPATCH", "1")
    assert handlers._gh(["run", "list"]) == (0, "[]")


def test_gh_reports_a_missing_cli_rather_than_crashing(monkeypatch):
    monkeypatch.setattr(handlers, "_gh", _REAL_GH)
    monkeypatch.delenv("JFA_NO_DISPATCH", raising=False)
    monkeypatch.setattr(handlers.subprocess, "run", _raise_filenotfound)

    code, out = handlers._gh(["run", "list"])
    assert code == 127
    assert "gh CLI not found" in out


def test_gh_reports_its_own_timeout(monkeypatch):
    monkeypatch.setattr(handlers, "_gh", _REAL_GH)
    monkeypatch.delenv("JFA_NO_DISPATCH", raising=False)

    def _timeout(*_args, **_kwargs):
        raise subprocess.TimeoutExpired(cmd="gh", timeout=20.0)

    monkeypatch.setattr(handlers.subprocess, "run", _timeout)
    code, out = handlers._gh(["run", "list"])
    assert code == 124
    assert "exceeded" in out


def test_gh_concatenates_stdout_and_stderr(monkeypatch):
    monkeypatch.setattr(handlers, "_gh", _REAL_GH)
    monkeypatch.delenv("JFA_NO_DISPATCH", raising=False)
    monkeypatch.setattr(handlers.subprocess, "run",
                        lambda *a, **k: _CompletedProc(0, "out", "err"))
    assert handlers._gh(["run", "list"]) == (0, "outerr")


def _raise_filenotfound(*_args, **_kwargs):
    raise FileNotFoundError("gh")


class _CompletedProc:
    def __init__(self, returncode, stdout, stderr):
        self.returncode, self.stdout, self.stderr = returncode, stdout, stderr


# --- CI handlers ---------------------------------------------------------------

def test_ci_dispatch_surfaces_a_failed_dispatch(monkeypatch):
    monkeypatch.setattr(handlers, "_gh", lambda args, timeout_s=20.0: (1, "workflow not found"))
    result = handlers.h_ci_dispatch(_request("ci_dispatch", {
        "ci_op": {"op": "dispatch", "workflow": "nope.yml", "remote_only": True, "wait": False}
    }, domain="ci"))
    assert result["errors"][0]["code"] == "dispatch_failed"


def test_ci_dispatch_passes_inputs_through(monkeypatch):
    seen = {}

    def _capture(args, timeout_s=20.0):
        seen["args"] = args
        return 0, ""

    monkeypatch.setattr(handlers, "_gh", _capture)
    result = handlers.h_ci_dispatch(_request("ci_dispatch", {
        "ci_op": {"op": "dispatch", "workflow": "ingest.yml", "ref": "topic",
                  "inputs": {"reason": "test"}, "remote_only": True, "wait": False}
    }, domain="ci"))
    assert result["result"] == {"dispatched": "ingest.yml", "ref": "topic", "blocking": False}
    assert seen["args"][-2:] == ["-f", "reason=test"]


def test_ci_status_without_a_workflow_filter(monkeypatch):
    seen = {}

    def _capture(args, timeout_s=20.0):
        seen["args"] = args
        return 0, "[]"

    monkeypatch.setattr(handlers, "_gh", _capture)
    result = handlers.h_ci_status(_request("ci_status", {"ci_op": {"op": "status", "remote_only": True, "wait": False}},
                                           domain="ci"))
    assert result["result"] == {"runs": []}
    assert "--workflow" not in seen["args"]


def test_ci_status_surfaces_a_failed_call(monkeypatch):
    monkeypatch.setattr(handlers, "_gh", lambda args, timeout_s=20.0: (1, "not authenticated"))
    result = handlers.h_ci_status(_request("ci_status", {}, domain="ci"))
    assert result["errors"][0]["code"] == "status_failed"


def test_ci_status_rejects_unparseable_gh_output(monkeypatch):
    monkeypatch.setattr(handlers, "_gh", lambda args, timeout_s=20.0: (0, "not json at all"))
    result = handlers.h_ci_status(_request("ci_status", {}, domain="ci"))
    assert result["errors"][0]["code"] == "bad_gh_output"


def test_remote_only_returns_the_dispatch_error_unchanged(monkeypatch):
    monkeypatch.delenv("GITHUB_ACTIONS", raising=False)
    monkeypatch.delenv("JFA_CI", raising=False)
    monkeypatch.setattr(handlers, "_gh", lambda args, timeout_s=20.0: (1, "boom"))

    result = handlers.h_ingest(_request("ingest", {}, domain="knowledge_base"))
    assert result["status"] == "error"
    assert "delegated_action" not in result.get("result", {})
