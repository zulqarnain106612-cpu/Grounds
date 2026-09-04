import json
from pathlib import Path

from gateway import enforcement, log_guard
from gateway.gateway import handle


def _req(action, domain, payload_extra, session="550e8400-e29b-41d4-a716-446655440000"):
    return {
        "meta": {"schema_version": "1.1.0", "session_id": session, "tick": 1,
                  "phase": "1", "timestamp_utc": "2026-01-01T00:00:00Z"},
        "intent": {"action": action, "domain": domain, "priority": 5},
        "payload": {"data": None, **payload_extra},
    }


def test_forbidden_cat_command_is_blocked_not_executed():
    req = _req("command_exec", "build_pipeline",
               {"command": {"cmd": "cat", "args": ["schema/agent.schema.json"], "capped": True}})
    resp = handle(req)
    assert resp["response"]["status"] == "error"
    assert resp["response"]["errors"][0]["code"] == "forbidden_command"


def test_forbidden_large_tail_is_blocked():
    req = _req("command_exec", "log", {"command": {"cmd": "tail", "args": ["-n", "5000", "logs/agent.jsonl"], "capped": True}})
    resp = handle(req)
    assert resp["response"]["errors"][0]["code"] == "forbidden_command"


def test_command_output_is_truncated_server_side_even_if_output_lines_omitted():
    long_echo = ";".join([f"echo line{i}" for i in range(200)])
    req = _req("command_exec", "build_pipeline",
               {"command": {"cmd": "bash", "args": ["-c", long_echo], "capped": True}})
    resp = handle(req)
    assert resp["response"]["status"] == "ok"
    out_lines = resp["response"]["result"]["output"].splitlines()
    assert len(out_lines) <= enforcement.config()["enforcement"]["max_command_output_lines"]
    assert resp["response"]["result"]["truncated"] is True


def test_log_query_never_exceeds_hard_cap_regardless_of_requested_tail_lines(isolated_repo):
    # write a synthetic log with many ERROR lines
    log_path = isolated_repo / "logs" / "test_synth.log"
    log_path.write_text("\n".join(f"ERROR synthetic failure {i}" for i in range(100)))
    try:
        log_guard.refresh_failures_cache()
        req = _req("log_query", "log", {"log_op": {"op": "query_failure", "filter_level": "error", "tail_lines": 20}})
        resp = handle(req)
        assert len(resp["response"]["result"]) <= 20
    finally:
        log_path.unlink(missing_ok=True)
        log_guard.refresh_failures_cache()


def test_retrieved_chunk_content_never_exceeds_schema_cap():
    huge_summary = "x " * 5000
    from gateway import kb_store
    kb_store.add_kb_chunk({"id": "kb-huge", "domain": "test", "tags": ["hugetest"], "summary": huge_summary})
    try:
        req = _req("retrieve", "knowledge_base",
                   {"context": {"strategy": "keyword", "scope": "kb", "target": "hugetest", "top_k": 1, "max_tokens": 2000}})
        resp = handle(req)
        for chunk in resp["response"]["retrieved"]:
            assert len(chunk["content"]) <= 2000
    finally:
        kb_store.delete_kb_chunk("kb-huge")


def test_symbol_scanner_emits_repo_relative_paths(tmp_path):
    from gateway import symbol_scanner
    src_dir = tmp_path / "Assets" / "_Game" / "Scripts" / "Player"
    src_dir.mkdir(parents=True)
    src_file = src_dir / "Foo.cs"
    src_file.write_text("namespace X\n{\n    public class Foo\n    {\n    }\n}\n")
    symbols = symbol_scanner.scan_file(src_file, repo_root=tmp_path)
    assert symbols[0]["file"] == "Assets/_Game/Scripts/Player/Foo.cs"
    assert not symbols[0]["file"].startswith("/")


def test_fileop_write_then_delete_round_trip_no_read_op_exists(isolated_repo):
    from gateway.schema_guard import load_schema
    schema = load_schema()
    assert "read" not in schema["definitions"]["FileOp"]["properties"]["op"]["enum"]

    target = "logs/_enforcement_test_scratch.txt"
    write_req = _req("write", "asset", {"file_op": {"op": "write", "path": target, "content": "hello"}})
    resp = handle(write_req)
    assert resp["response"]["status"] == "ok"
    assert (isolated_repo / target).exists()

    del_req = _req("delete", "asset", {"file_op": {"op": "delete", "path": target}})
    resp = handle(del_req)
    assert resp["response"]["status"] == "ok"
    assert not (isolated_repo / target).exists()
