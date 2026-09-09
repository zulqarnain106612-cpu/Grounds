"""CI reads are scoped to one PR, and log reads are probe-then-exact.

Two rules, both enforced by the shape of the contract rather than by
convention:

1. Status and logs are always scoped to a single pull request. A repo-wide
   run listing is not expressible -- `pr` is required by the schema and
   refused by the handler.
2. A log read that names no line count returns the fixed two-line error
   probe, never a large window. Naming a count returns exactly that many.
"""
from __future__ import annotations

import json

import pytest

from gateway import enforcement, handlers, schema_guard


def _req(action: str, ci_op: dict | None = None) -> dict:
    payload = {"data": None}
    if ci_op is not None:
        payload["ci_op"] = ci_op
    return {
        "meta": {
            "schema_version": "1.1.0",
            "session_id": "550e8400-e29b-41d4-a716-446655440000",
            "tick": 0, "phase": "1", "timestamp_utc": "2026-09-09T00:00:00Z",
        },
        "intent": {"action": action, "domain": "ci", "priority": 5},
        "payload": payload,
    }


def _gh_script(*responses):
    """Return a _gh stub that replays `responses` in call order and records args."""
    calls: list[list[str]] = []
    queue = list(responses)

    def _fake(args, timeout_s=20.0):
        calls.append(args)
        return queue.pop(0)

    _fake.calls = calls
    return _fake


# --------------------------------------------------------------------------
# the contract
# --------------------------------------------------------------------------

def test_status_without_a_pr_is_not_schema_valid():
    """The whole point: 'show me every run' cannot be expressed."""
    assert not schema_guard.is_valid(_req("ci_status", {
        "op": "status", "remote_only": True, "wait": False}))
    assert schema_guard.is_valid(_req("ci_status", {
        "op": "status", "pr": 10, "remote_only": True, "wait": False}))


def test_logs_without_a_pr_is_not_schema_valid():
    assert not schema_guard.is_valid(_req("ci_logs", {
        "op": "logs", "remote_only": True, "wait": False}))
    assert schema_guard.is_valid(_req("ci_logs", {
        "op": "logs", "pr": 10, "remote_only": True, "wait": False}))


def test_the_repo_wide_list_runs_op_no_longer_exists():
    assert not schema_guard.is_valid(_req("ci_status", {
        "op": "list_runs", "pr": 10, "remote_only": True, "wait": False}))
    assert "list_runs" not in schema_guard.load_schema()[
        "definitions"]["CIOp"]["properties"]["op"]["enum"]


def test_a_log_request_cannot_ask_for_more_than_the_ceiling():
    base = {"op": "logs", "pr": 10, "remote_only": True, "wait": False}
    assert schema_guard.is_valid(_req("ci_logs", {**base, "log_tail_lines": 50}))
    assert not schema_guard.is_valid(_req("ci_logs", {**base, "log_tail_lines": 51}))
    assert not schema_guard.is_valid(_req("ci_logs", {**base, "log_tail_lines": 0}))


def test_dispatch_is_unaffected_by_the_pr_requirement():
    """The new allOf must not leak into dispatch, which has no PR."""
    assert schema_guard.is_valid(_req("ci_dispatch", {
        "op": "dispatch", "workflow": "enforce.yml", "remote_only": True, "wait": False}))


# --------------------------------------------------------------------------
# caps
# --------------------------------------------------------------------------

def test_naming_no_line_count_yields_the_probe_not_the_ceiling():
    enf = enforcement.config()["enforcement"]
    assert enforcement.cap_ci_log_tail(None) == enf["max_ci_log_probe_lines"] == 2
    assert enforcement.cap_ci_log_tail(12) == 12
    assert enforcement.cap_ci_log_tail(10**6) == enf["max_ci_log_lines"] == 50


# --------------------------------------------------------------------------
# ci_status
# --------------------------------------------------------------------------

def test_ci_status_refuses_without_a_pr():
    result = handlers.h_ci_status(_req("ci_status", {"op": "status"}))
    assert result["errors"][0]["code"] == "missing_pr"


def test_ci_status_asks_gh_only_about_the_one_pr(monkeypatch):
    stub = _gh_script((0, json.dumps([
        {"bucket": "pass", "state": "SUCCESS", "workflow": "coverage", "name": "coverage"},
        {"bucket": "fail", "state": "FAILURE", "workflow": "enforce", "name": "validate"},
    ])))
    monkeypatch.setattr(handlers, "_gh", stub)

    result = handlers.h_ci_status(_req("ci_status", {"op": "status", "pr": 10}))
    assert result["result"]["pr"] == 10
    assert result["result"]["failed"] == ["validate"]
    assert result["result"]["total"] == 2
    # the guarantee: PR-scoped, and never a run listing
    assert stub.calls[0][:3] == ["pr", "checks", "10"]
    assert not any("run" in c[0] for c in stub.calls)


def test_ci_status_treats_pending_checks_as_a_state_not_a_failure(monkeypatch):
    monkeypatch.setattr(handlers, "_gh", _gh_script((8, json.dumps(
        [{"bucket": "pending", "state": "IN_PROGRESS", "workflow": "w", "name": "n"}]))))
    result = handlers.h_ci_status(_req("ci_status", {"op": "status", "pr": 10}))
    assert result["status"] == "ok"
    assert result["result"]["failed"] == []


def test_ci_status_surfaces_a_real_gh_failure(monkeypatch):
    monkeypatch.setattr(handlers, "_gh", _gh_script((1, "not authenticated")))
    result = handlers.h_ci_status(_req("ci_status", {"op": "status", "pr": 10}))
    assert result["errors"][0]["code"] == "status_failed"


def test_ci_status_rejects_unparseable_gh_output(monkeypatch):
    monkeypatch.setattr(handlers, "_gh", _gh_script((0, "not json")))
    result = handlers.h_ci_status(_req("ci_status", {"op": "status", "pr": 10}))
    assert result["errors"][0]["code"] == "bad_gh_output"


def test_ci_status_caps_the_returned_check_list(monkeypatch):
    checks = [{"bucket": "pass", "name": f"c{i}"} for i in range(30)]
    monkeypatch.setattr(handlers, "_gh", _gh_script((0, json.dumps(checks))))
    result = handlers.h_ci_status(_req("ci_status", {"op": "status", "pr": 10, "limit": 3}))
    assert len(result["result"]["checks"]) == 3
    assert result["result"]["total"] == 30


# --------------------------------------------------------------------------
# ci_logs
# --------------------------------------------------------------------------

_LOG = "\n".join([
    "job\tstep\t2026-09-07T00:35:20.1Z Run pytest",
    "job\tstep\t2026-09-07T00:35:21.2Z ........................ [ 46%]",
    "job\tstep\t2026-09-07T00:35:22.3Z Terminated",
    "job\tstep\t2026-09-07T00:35:23.4Z ##[error]Process completed with exit code 143.",
    "job\tstep\t2026-09-07T00:35:24.5Z Cleaning up orphan processes",
    "job\tstep\t2026-09-07T00:35:25.6Z ##[warning]Node.js 20 is deprecated",
])


def _log_stubs(*, log=_LOG, branch="phase0/x", run=99):
    return _gh_script(
        (0, json.dumps({"headRefName": branch, "headRefOid": "abc1234"})),
        (0, json.dumps([{"databaseId": run}])),
        (0, log),
    )


def test_ci_logs_refuses_without_a_pr():
    result = handlers.h_ci_logs(_req("ci_logs", {"op": "logs"}))
    assert result["errors"][0]["code"] == "missing_pr"


def test_the_probe_returns_the_error_annotation_not_the_last_lines(monkeypatch):
    """Verified against real runs: the final physical lines of a failed job are
    runner teardown, so a plain tail reports noise instead of the cause."""
    monkeypatch.setattr(handlers, "_gh", _log_stubs())
    result = handlers.h_ci_logs(_req("ci_logs", {"op": "logs", "pr": 10}))["result"]

    assert result["probe"] is True
    assert result["lines"] == ["##[error]Process completed with exit code 143."]
    assert "Cleaning up orphan processes" not in result["lines"]
    # the position is what sizes the follow-up request
    assert result["error_line"] == 4
    assert result["total_lines"] == 6


def test_the_probe_falls_back_to_a_tail_when_no_annotation_exists(monkeypatch):
    plain = "\n".join(f"job\tstep\t2026-09-07T00:00:0{i}.0Z line {i}" for i in range(5))
    monkeypatch.setattr(handlers, "_gh", _log_stubs(log=plain))
    result = handlers.h_ci_logs(_req("ci_logs", {"op": "logs", "pr": 10}))["result"]

    assert result["error_line"] is None
    assert result["lines"] == ["line 3", "line 4"]


def test_a_named_count_returns_exactly_that_many_lines(monkeypatch):
    monkeypatch.setattr(handlers, "_gh", _log_stubs())
    result = handlers.h_ci_logs(_req("ci_logs", {
        "op": "logs", "pr": 10, "log_tail_lines": 3}))["result"]

    assert result["probe"] is False
    assert len(result["lines"]) == 3
    assert result["lines"][-1] == "##[warning]Node.js 20 is deprecated"


def test_an_offset_walks_the_window_back_to_the_error(monkeypatch):
    """The real workflow: probe says error at line 4 of 6, so ask for 3 lines
    skipping the last 2 to land the window on the cause."""
    monkeypatch.setattr(handlers, "_gh", _log_stubs())
    result = handlers.h_ci_logs(_req("ci_logs", {
        "op": "logs", "pr": 10, "log_tail_lines": 3, "log_offset": 2}))["result"]

    assert result["lines"] == [
        "........................ [ 46%]",
        "Terminated",
        "##[error]Process completed with exit code 143.",
    ]


def test_an_inflated_count_is_clamped_server_side(monkeypatch):
    monkeypatch.setattr(handlers, "_gh", _log_stubs())
    result = handlers.h_ci_logs(_req("ci_logs", {
        "op": "logs", "pr": 10, "log_tail_lines": 10**6}))["result"]
    assert len(result["lines"]) <= 50


def test_an_explicit_run_id_skips_the_run_lookup(monkeypatch):
    stub = _gh_script((0, json.dumps({"headRefName": "b", "headRefOid": "abc"})), (0, _LOG))
    monkeypatch.setattr(handlers, "_gh", stub)

    result = handlers.h_ci_logs(_req("ci_logs", {"op": "logs", "pr": 10, "run_id": 55}))
    assert result["result"]["failed_run"] == 55
    assert not any(c[:2] == ["run", "list"] for c in stub.calls)


def test_ci_logs_reports_when_the_pr_has_no_failed_run(monkeypatch):
    monkeypatch.setattr(handlers, "_gh", _gh_script(
        (0, json.dumps({"headRefName": "b", "headRefOid": "abc"})), (0, "[]")))
    result = handlers.h_ci_logs(_req("ci_logs", {"op": "logs", "pr": 10}))["result"]
    assert result["failed_run"] is None
    assert result["lines"] == []


@pytest.mark.parametrize("responses, code", [
    ([(1, "no such PR")], "pr_lookup_failed"),
    ([(0, "not json")], "bad_gh_output"),
    ([(0, json.dumps({"wrong": "shape"}))], "bad_gh_output"),
    ([(0, json.dumps({"headRefName": "b", "headRefOid": "a"})), (1, "gh exploded")], "run_lookup_failed"),
    ([(0, json.dumps({"headRefName": "b", "headRefOid": "a"})), (0, "not json")], "bad_gh_output"),
])
def test_ci_logs_error_paths(monkeypatch, responses, code):
    monkeypatch.setattr(handlers, "_gh", _gh_script(*responses))
    result = handlers.h_ci_logs(_req("ci_logs", {"op": "logs", "pr": 10}))
    assert result["errors"][0]["code"] == code


# --------------------------------------------------------------------------
# prefix stripping
# --------------------------------------------------------------------------

@pytest.mark.parametrize("line, expected", [
    ("job\tstep\t2026-09-07T00:35:23.4Z ##[error]boom", "##[error]boom"),
    ("job\tstep\tno timestamp here", "no timestamp here"),
    ("job\tstep\tfooZ bar", "fooZ bar"),
    ("no tabs at all", "no tabs at all"),
])
def test_runner_prefix_is_stripped_only_when_it_is_really_a_prefix(line, expected):
    assert handlers._strip_runner_prefix(line) == expected
