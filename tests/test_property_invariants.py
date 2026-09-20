"""Property-based tests: invariants over generated input, not chosen input.

The example-based suites prove the gateway handles the cases someone thought
of. These prove properties that must hold for *every* input, including the
ones nobody thought of -- which is the only way to find the input nobody
thought of.

Scope note. Almost everything here drives `schema_guard.validate`, which is a
pure function, rather than `gateway.handle`, which dispatches. Generating
thousands of requests through the dispatcher would eventually generate a valid
`command_exec` and run it. The one property that does use `handle` feeds it
values that cannot pass validation, so no handler is ever reached.

Seeds: Hypothesis derives its own and prints the failing one on a red run.
`TestOp.seed` in the schema exists for the same reason -- a randomised failure
you cannot replay is an anecdote, not a regression.
"""
from __future__ import annotations

import copy
import json
from pathlib import Path

import pytest

hypothesis = pytest.importorskip("hypothesis")
from hypothesis import HealthCheck, given, settings, strategies as st  # noqa: E402

from gateway import schema_guard  # noqa: E402
from gateway.gateway import handle  # noqa: E402

ROOT = Path(__file__).resolve().parent.parent
EXAMPLES = json.loads((ROOT / "schema" / "examples.json").read_text())["examples"]
SCHEMA = json.loads((ROOT / "schema" / "agent.schema.json").read_text())
ACTIONS = set(SCHEMA["properties"]["intent"]["properties"]["action"]["enum"])
DOMAINS = set(SCHEMA["properties"]["intent"]["properties"]["domain"]["enum"])

# autouse `isolated_repo` copies the whole data tree per test. Under Hypothesis
# that fixture runs once per *test*, not once per example, so the deadline is
# the only thing that needs relaxing.
PROPERTY = settings(
    max_examples=200,
    deadline=None,
    suppress_health_check=[HealthCheck.function_scoped_fixture],
)

json_values = st.recursive(
    st.none() | st.booleans() | st.integers() | st.floats(allow_nan=False, allow_infinity=False)
    | st.text(max_size=50),
    lambda children: st.lists(children, max_size=5) | st.dictionaries(st.text(max_size=20), children, max_size=5),
    max_leaves=20,
)

identifiers = st.text(
    alphabet=st.characters(whitelist_categories=("Ll", "Lu", "Nd"), whitelist_characters="_"),
    min_size=1, max_size=30,
)


def _valid(request: dict) -> bool:
    try:
        schema_guard.validate(request)
        return True
    except schema_guard.RequestValidationError:
        return False


# --- totality: the validator classifies, it never crashes ------------------------

@PROPERTY
@given(value=json_values)
def test_validation_is_total_over_arbitrary_json(value):
    """Any JSON value is either accepted or rejected. A TypeError or KeyError
    escaping here is an unhandled shape reaching the gateway boundary."""
    try:
        schema_guard.validate(value)
    except schema_guard.RequestValidationError:
        pass


@PROPERTY
@given(value=json_values)
def test_validation_is_deterministic(value):
    """Same input, same verdict. Validation must not depend on dict ordering,
    hash seed, or any state carried between calls."""
    assert _valid(copy.deepcopy(value)) == _valid(copy.deepcopy(value))


@PROPERTY
@given(value=json_values)
def test_the_gateway_always_answers_with_a_status(value):
    """The envelope is part of the contract even for garbage: a caller must
    never receive something it cannot parse a status out of."""
    response = handle(value)
    assert isinstance(response, dict)
    assert response["response"]["status"] in ("ok", "error", "partial", "skipped")


# --- closure: only what the enum lists is accepted --------------------------------

@PROPERTY
@given(action=st.text(max_size=40))
def test_only_enumerated_actions_are_accepted(action):
    request = copy.deepcopy(EXAMPLES["schema_validate"])
    request["intent"]["action"] = action
    assert _valid(request) == (action in ACTIONS)


@PROPERTY
@given(domain=st.text(max_size=40))
def test_only_enumerated_domains_are_accepted(domain):
    request = copy.deepcopy(EXAMPLES["schema_validate"])
    request["intent"]["domain"] = domain
    assert _valid(request) == (domain in DOMAINS)


# Names the schema actually declares for each container. A generated name that
# collided with one of these -- `meta.agent_id` is an optional free string --
# would be legitimately accepted, and the property below would report a failure
# that is really a bad generator.
_DECLARED = {
    container: set(SCHEMA["properties"][container]["properties"])
    for container in ("meta", "intent", "payload")
}


@PROPERTY
@given(name=identifiers, value=json_values, example=st.sampled_from(sorted(EXAMPLES)))
def test_no_undeclared_field_survives_anywhere_in_a_request(name, value, example):
    """`additionalProperties: false` is asserted once per object in
    test_schema_contract.py. This asserts the *consequence*: whatever you add,
    wherever you add it, the request stops validating."""
    for container in ("meta", "intent", "payload"):
        if name in _DECLARED[container]:
            continue
        request = copy.deepcopy(EXAMPLES[example])
        request[container][name] = value
        assert not _valid(request), f"undeclared field {name!r} accepted in {container}"


# --- caps hold for every value, not just the one in the example -------------------

@PROPERTY
@given(lines=st.integers(min_value=-1000, max_value=1000))
def test_log_tail_lines_cap_holds_for_every_integer(lines):
    """docs/ENFORCEMENT.md caps a log window at 50 lines. The schema is the
    first of the two enforcement points; handlers.py is the second."""
    request = copy.deepcopy(EXAMPLES["ci_logs"])
    request["payload"]["ci_op"]["log_tail_lines"] = lines
    assert _valid(request) == (1 <= lines <= 50)


@PROPERTY
@given(minimum=st.integers(min_value=-1000, max_value=1000))
def test_min_tests_never_admits_a_zero_test_run(minimum):
    request = copy.deepcopy(EXAMPLES["test_run"])
    request["payload"]["test_op"]["min_tests"] = minimum
    assert _valid(request) == (minimum >= 1)


@PROPERTY
@given(coverage=st.floats(min_value=-500, max_value=500, allow_nan=False, allow_infinity=False))
def test_coverage_floor_stays_a_percentage(coverage):
    request = copy.deepcopy(EXAMPLES["test_run"])
    request["payload"]["test_op"]["coverage_min"] = coverage
    assert _valid(request) == (0 <= coverage <= 100)


@PROPERTY
@given(scope=st.text(max_size=400))
def test_scope_cannot_grow_into_a_command_line(scope):
    request = copy.deepcopy(EXAMPLES["test_run"])
    request["payload"]["test_op"]["scope"] = scope
    assert _valid(request) == (len(scope) <= 200)


# --- policy assertions cannot be talked out of ------------------------------------

@PROPERTY
@given(remote=st.booleans(), wait=st.booleans())
def test_a_local_or_blocking_test_run_is_never_expressible(remote, wait):
    """The whole point of the two const fields. Only (True, False) validates,
    so "run the tests here" and "block until they finish" have no encoding."""
    request = copy.deepcopy(EXAMPLES["test_run"])
    request["payload"]["test_op"]["remote_only"] = remote
    request["payload"]["test_op"]["wait"] = wait
    assert _valid(request) == (remote is True and wait is False)


@PROPERTY
@given(suite=st.sampled_from(["mutation", "performance", "regression", "property", "soak"]))
def test_every_randomised_or_scored_suite_requires_its_own_evidence(suite):
    """Dropping the field that makes a suite's result actionable must break the
    request, for each suite that has such a field."""
    required = {
        "mutation": "mutation_score_min",
        "performance": "budget_ms",
        "regression": "baseline",
        "property": "seed",
        "soak": "seed",
    }[suite]
    supplied = {
        "mutation_score_min": 80, "budget_ms": 30000,
        "baseline": "flight-v1", "seed": 1729,
    }

    request = copy.deepcopy(EXAMPLES["test_run"])
    op = request["payload"]["test_op"]
    op["suite"] = suite
    for key in ("mutation_score_min", "budget_ms", "baseline", "seed"):
        op.pop(key, None)

    assert not _valid(request), f"{suite} validated without {required}"
    op[required] = supplied[required]
    assert _valid(request), f"{suite} with {required} should validate"
