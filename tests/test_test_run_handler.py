"""Unit tests for the `test_run` action.

The schema stops a malformed request at the gateway boundary, so most of what
this handler rejects is unreachable through `gateway.handle`. It is tested
directly anyway, for two reasons: the handler is also reachable from other
Python (`handlers.DISPATCH` is a public dict), and a validation rule that
exists in only one of the two places is a rule that disappears the moment
someone adds a second caller.

The one behaviour here that the schema cannot express at all is the last test:
that a test run is dispatched even when the process is itself a CI runner.
"""
from __future__ import annotations

import copy
import json
from pathlib import Path

import pytest

from gateway import handlers
from gateway.gateway import handle

ROOT = Path(__file__).resolve().parent.parent
EXAMPLE = json.loads((ROOT / "schema" / "examples.json").read_text())["examples"]["test_run"]


@pytest.fixture
def dispatched(monkeypatch):
    """Capture the `gh` argv h_test_run would have run, without running it."""
    calls: list[list[str]] = []

    def _fake_gh(args, timeout_s=20.0):
        calls.append(list(args))
        return 0, "[]"

    monkeypatch.setattr(handlers, "_gh", _fake_gh)
    return calls


def _request(**test_op) -> dict:
    request = copy.deepcopy(EXAMPLE)
    request["payload"]["test_op"].update(test_op)
    return request


def _inputs(argv: list[str]) -> dict:
    """The -f key=value pairs out of a `gh workflow run` argv."""
    out = {}
    for i, token in enumerate(argv):
        if token == "-f":
            key, _, value = argv[i + 1].partition("=")
            out[key] = value
    return out


# --- rejection ------------------------------------------------------------------

def test_a_request_with_no_test_op_is_rejected():
    request = copy.deepcopy(EXAMPLE)
    del request["payload"]["test_op"]
    response = handlers.h_test_run(request)
    assert response["status"] == "error"
    assert response["errors"][0]["code"] == "missing_test_op"


def test_an_unknown_suite_is_rejected():
    response = handlers.h_test_run(_request(suite="smoke"))
    assert response["errors"][0]["code"] == "unknown_suite"


@pytest.mark.parametrize("value", [0, -1, "3", 2.0, None])
def test_a_min_tests_that_is_not_a_positive_integer_is_rejected(value):
    response = handlers.h_test_run(_request(min_tests=value))
    assert response["errors"][0]["code"] == "invalid_min_tests"


def test_a_boolean_is_not_accepted_as_min_tests():
    """`True == 1` in Python, so a bare isinstance(x, int) check would accept
    `min_tests: true` and dispatch a run with a floor of 1 that nobody asked
    for. json.loads produces real booleans, so this is reachable."""
    response = handlers.h_test_run(_request(min_tests=True))
    assert response["errors"][0]["code"] == "invalid_min_tests"


def test_a_failed_dispatch_is_reported_as_an_error(monkeypatch):
    monkeypatch.setattr(handlers, "_gh", lambda args, timeout_s=20.0: (1, "no such workflow"))
    response = handlers.h_test_run(_request())
    assert response["status"] == "error"
    assert response["errors"][0]["code"] == "dispatch_failed"


# --- dispatch -------------------------------------------------------------------

def test_a_valid_request_dispatches_the_qa_workflow(dispatched):
    response = handlers.h_test_run(_request())
    assert response["status"] == "ok"
    assert response["result"]["delegated_action"] == "test_run"
    assert response["result"]["suite"] == "property"
    assert dispatched, "nothing was dispatched"
    assert dispatched[0][:3] == ["workflow", "run", "qa.yml"]


def test_every_supplied_field_reaches_the_workflow(dispatched):
    handlers.h_test_run(_request(
        suite="mutation", scope="gateway/handlers.py", min_tests=12,
        seed=99, coverage_min=99, mutation_score_min=80,
        budget_ms=30000, baseline="flight-v1",
    ))
    inputs = _inputs(dispatched[0])
    assert inputs == {
        "suite": "mutation", "scope": "gateway/handlers.py", "min_tests": "12",
        "seed": "99", "coverage_min": "99", "mutation_score_min": "80",
        "budget_ms": "30000", "baseline": "flight-v1",
    }


def test_an_omitted_min_tests_still_reaches_the_runner_as_one(dispatched):
    """The schema defaults min_tests to 1 but a default is not a value -- it
    never appears in the request. Re-applying it here is what stops the
    workflow's own default from being the only thing holding the floor."""
    request = copy.deepcopy(EXAMPLE)
    del request["payload"]["test_op"]["min_tests"]
    response = handlers.h_test_run(request)
    assert response["result"]["min_tests"] == 1
    assert _inputs(dispatched[0])["min_tests"] == "1"


def test_omitted_optional_fields_are_not_sent_as_empty_strings(dispatched):
    request = copy.deepcopy(EXAMPLE)
    for key in ("scope", "coverage_min"):
        request["payload"]["test_op"].pop(key, None)
    handlers.h_test_run(request)
    inputs = _inputs(dispatched[0])
    assert "scope" not in inputs and "coverage_min" not in inputs


def test_the_ref_defaults_to_main_and_can_be_overridden(dispatched):
    handlers.h_test_run(_request())
    assert "--ref" in dispatched[0] and dispatched[0][dispatched[0].index("--ref") + 1] == "main"

    request = _request()
    request["payload"]["ci_op"] = {"op": "dispatch", "ref": "phase0/qa", "remote_only": True}
    handlers.h_test_run(request)
    argv = dispatched[1]
    assert argv[argv.index("--ref") + 1] == "phase0/qa"


# --- policy ---------------------------------------------------------------------

def test_a_test_run_is_dispatched_even_from_inside_ci(monkeypatch, dispatched):
    """The deliberate difference from h_ingest/h_manifest, which do run
    in-process under CI. pytest invoking itself inside its own process gives
    the inner run the outer run's coverage context and attributes its result to
    the wrong suite, so test_run has no in-process path at all."""
    monkeypatch.setenv("GITHUB_ACTIONS", "true")
    assert handlers._in_ci() is True

    response = handlers.h_test_run(_request())
    assert response["status"] == "ok"
    assert dispatched, "h_test_run executed in-process instead of dispatching"
    assert dispatched[0][:3] == ["workflow", "run", "qa.yml"]


def test_the_handler_suite_list_matches_the_schema():
    """Two lists that must agree: the handler's guard and the schema enum. They
    are in different files, so nothing but this test stops them drifting."""
    schema = json.loads((ROOT / "schema" / "agent.schema.json").read_text())
    enum = schema["definitions"]["TestOp"]["properties"]["suite"]["enum"]
    assert list(handlers._TEST_SUITES) == enum


def test_the_action_is_reachable_through_the_gateway(dispatched):
    """End to end: schema validation, dispatch lookup, response validation."""
    response = handle(json.dumps(EXAMPLE))["response"]
    assert response["status"] == "ok"
    assert response["result"]["delegated_action"] == "test_run"
