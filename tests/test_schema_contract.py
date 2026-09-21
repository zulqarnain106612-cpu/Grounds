"""Regression tests for the schema's *surface*, not its behaviour.

Every other suite asks "does this request do the right thing". This one asks
"is the contract still the shape we agreed on", which is a different failure
mode and catches a different class of mistake: a definition that quietly grows
`additionalProperties: true`, a `const` that becomes a plain boolean, a cap
that loses its `maximum`, an action that disappears in a bad merge. None of
those break a single behavioural test -- they widen the contract, and a wider
contract fails open.

Two layers, deliberately:

1. **Invariants** (`test_every_*`) are derived, not listed. They hold for
   definitions that do not exist yet, so a new payload object added next year
   is covered the day it lands. These cannot be regenerated away.
2. **The golden surface** (`tests/golden/schema_contract.json`) is an exact
   snapshot. It exists so an intentional change shows up as a reviewable diff
   in the PR that makes it, rather than as nothing at all. Regenerate with:

       python -m tests.contract_surface --update-golden

   Regenerating is meant to be a deliberate act recorded in a commit. If you
   find yourself doing it to make a red build green, the build is right.
"""
from __future__ import annotations

import json
import pytest

from tests.contract_surface import (
    GOLDEN_PATH, ROOT, object_nodes as _object_nodes, load_schema, surface as _surface,
    uncapped_strings as _uncapped_strings,
)

SCHEMA = load_schema()
DEFINITIONS = SCHEMA["definitions"]
INTENT = SCHEMA["properties"]["intent"]["properties"]


# --- layer 1: invariants that hold for definitions not yet written ---------------


def test_every_object_forbids_unknown_fields():
    """`additionalProperties: false` is what turns "unknown field" into a
    guarantee instead of a hope. docs/EXTENDING.md forbids loosening it; this
    is the check that makes the prohibition enforceable."""
    offenders = [
        path for path, node in _object_nodes(SCHEMA)
        if node.get("additionalProperties") is not False
    ]
    assert not offenders, f"objects accepting unknown fields: {offenders}"


# The response envelope is the one object with no `required` list, and that is
# correct: it is the *output* side, filled in by the gateway rather than sent by
# a caller, and which of its fields appear depends on the action. Its own
# sub-objects (AgentError, Symbol, RetrievedChunk) are required-constrained.
_REQUIRED_EXEMPT = {"$.properties.response"}


def test_every_request_object_declares_what_it_requires():
    offenders = [
        path for path, node in _object_nodes(SCHEMA)
        if not node.get("required") and path not in _REQUIRED_EXEMPT
    ]
    assert not offenders, f"objects with no required list: {offenders}"


@pytest.mark.parametrize("name", sorted(DEFINITIONS))
def test_every_definition_is_closed(name):
    """Object definitions must reject unknown fields; scalar ones must be
    value-constrained. Either way a caller cannot put something unforeseen in."""
    body = DEFINITIONS[name]
    if body.get("type") == "object":
        assert body.get("additionalProperties") is False, f"{name} accepts unknown fields"
    else:
        assert "enum" in body or "const" in body, (
            f"{name} is a scalar definition with no enum/const, so it constrains nothing"
        )


# A ratchet, not a clean bill of health. These string fields predate the cap
# rule and are bounded downstream in gateway/enforcement.py rather than in the
# schema. The list may only ever *shrink*: the test below fails on any new
# uncapped string, and fails again once a listed field gains its cap, which is
# what forces the entry to be deleted rather than left to rot.
_UNCAPPED_STRING_BASELINE = {
    "$.properties.meta.agent_id",
    "$.definitions.RetrievalContext.target",
    "$.definitions.CommandExec.working_dir",
    "$.definitions.ToolCall.tool_name",
    "$.definitions.FileOp.path",
    "$.definitions.FileOp.content",
    "$.definitions.FileOp.encoding",
    "$.definitions.LogOp.message",
    "$.definitions.LogOp.domain",
    "$.definitions.KnowledgeNode.id",
    "$.definitions.KnowledgeNode.type",
    "$.definitions.KnowledgeNode.label",
    "$.definitions.KnowledgeNode.edge_to",
    "$.definitions.KnowledgeNode.edge_kind",
    "$.definitions.DaemonOp.daemon_id",
    "$.definitions.AgentError.code",
    "$.definitions.AgentError.message",
    "$.definitions.AgentError.domain",
    "$.definitions.Symbol.name",
    "$.definitions.Symbol.file",
    "$.definitions.Symbol.namespace",
    "$.definitions.RetrievedChunk.source",
    "$.definitions.RetrievedChunk.strategy",
    "$.definitions.CIOp.workflow",
    "$.definitions.CIOp.ref",
}


def test_no_new_uncapped_string_field_is_introduced():
    """An uncapped string is an unbounded allocation reachable from the gateway
    boundary. Anything added from here on carries a maxLength."""
    new = _uncapped_strings() - _UNCAPPED_STRING_BASELINE
    assert not new, (
        f"new uncapped string field(s): {sorted(new)}. Add a maxLength in "
        f"schema/agent.schema.json rather than extending the baseline."
    )


def test_the_uncapped_baseline_has_no_stale_entries():
    """Forces the list to shrink as fields gain caps, instead of becoming a
    permanent record of a problem nobody is fixing."""
    fixed = _UNCAPPED_STRING_BASELINE - _uncapped_strings()
    assert not fixed, (
        f"these fields are capped now: {sorted(fixed)}. Delete them from "
        f"_UNCAPPED_STRING_BASELINE."
    )


def test_there_is_still_no_way_to_read_a_file():
    """docs/ENFORCEMENT.md's central guarantee. The absence of 'read' from
    FileOp.op is the enforcement -- there is no policy flag to check."""
    assert "read" not in DEFINITIONS["FileOp"]["properties"]["op"]["enum"]


@pytest.mark.parametrize("definition,field,value", [
    ("CommandExec", "capped", True),
    ("CIOp", "remote_only", True),
    ("CIOp", "wait", False),
    ("TestOp", "remote_only", True),
    ("TestOp", "wait", False),
])
def test_policy_assertions_stay_const(definition, field, value):
    """These fields exist so that a policy violation is not *expressible*. A
    `const` that decays into a plain boolean silently turns each one back into
    a field the caller may simply set to whatever it likes."""
    spec = DEFINITIONS[definition]["properties"][field]
    assert spec.get("const") is value, f"{definition}.{field} is no longer const {value}"


def test_no_test_request_can_tolerate_zero_discovered_tests():
    """pytest and Unity's runner both exit 0 on an empty discovery. The floor
    on min_tests is the only thing standing between that and a green PR."""
    assert DEFINITIONS["TestOp"]["properties"]["min_tests"]["minimum"] == 1


def test_randomised_suites_must_carry_a_seed():
    """A property or soak failure without a seed cannot be replayed, so it
    cannot become a regression test. Enforced in the schema, not by convention."""
    conditions = DEFINITIONS["TestOp"]["allOf"]
    seeded = [
        c for c in conditions
        if set(c["if"]["properties"]["suite"].get("enum", [])) >= {"property", "soak"}
    ]
    assert seeded, "no allOf condition requires a seed for randomised suites"
    assert seeded[0]["then"]["required"] == ["seed"]


# --- layer 2: the exact surface, as a reviewable diff ---------------------------

def test_the_contract_surface_matches_the_golden_snapshot():
    if not GOLDEN_PATH.exists():
        pytest.fail(
            f"{GOLDEN_PATH.relative_to(ROOT)} is missing. Create it with "
            f"`python -m tests.contract_surface --update-golden`."
        )
    golden = json.loads(GOLDEN_PATH.read_text())
    current = _surface()
    if current == golden:
        return

    added = sorted(set(current["actions"]) - set(golden["actions"]))
    removed = sorted(set(golden["actions"]) - set(current["actions"]))
    defs_added = sorted(set(current["definitions"]) - set(golden["definitions"]))
    defs_removed = sorted(set(golden["definitions"]) - set(current["definitions"]))
    changed = sorted(
        name for name in set(current["definitions"]) & set(golden["definitions"])
        if current["definitions"][name] != golden["definitions"][name]
    )
    pytest.fail(
        "the schema's public surface changed.\n"
        f"  actions added:      {added or '-'}\n"
        f"  actions removed:    {removed or '-'}\n"
        f"  definitions added:  {defs_added or '-'}\n"
        f"  definitions removed:{defs_removed or '-'}\n"
        f"  definitions changed:{changed or '-'}\n"
        "If this was intentional, regenerate the golden in the same commit:\n"
        "  python -m tests.contract_surface --update-golden"
    )
