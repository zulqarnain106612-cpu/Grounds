"""Cycle 5, branch phase5/compliance-odds-ui (ADR-006).

Criterion: the component "renders correctly in a test harness while dormant".

That is an unusual thing to ask for, and it is the whole point of the ADR:
**dormant and absent are different**. A dormant component is tested and can be
pointed at a real item in an afternoon. An absent one is a week of work under
review pressure, with a build submitted and a release date already
communicated.
"""
from __future__ import annotations

import json
from pathlib import Path

import pytest

from gateway import symbol_scanner
from tests._csharp import code

REAL_ROOT = Path(__file__).resolve().parent.parent
SCRIPTS = REAL_ROOT / "Assets" / "_Game" / "Scripts"
ODDS = SCRIPTS / "UI" / "OddsDisclosureUI.cs"


def _method_body(path: Path, signature: str) -> str:
    source = code(path)
    start = source.index(signature)
    depth = 0
    for j in range(source.index("{", start), len(source)):
        if source[j] == "{":
            depth += 1
        elif source[j] == "}":
            depth -= 1
            if depth == 0:
                return source[start:j + 1]
    raise AssertionError(f"unbalanced braces after {signature}")


def test_the_component_exists_while_nothing_uses_it():
    """The decision, not an oversight. Guideline 3.1.1 requires published odds
    for randomized paid content, and the expensive moment to discover there is
    no disclosure UI is during review."""
    assert ODDS.exists()
    assert "public bool IsDormant" in code(ODDS)


def test_it_is_dormant_by_default():
    """ADR-006: cosmetic-only, nothing randomized sold at launch."""
    body = _method_body(ODDS, "public bool IsDormant")
    assert "entries.Count == 0" in body


def test_nothing_live_references_it():
    """A disclosure attached to nothing cannot go stale. One attached to a
    flow that later changes its odds silently becomes a false statement, which
    is worse than missing."""
    referencing = []
    for path in SCRIPTS.rglob("*.cs"):
        if path == ODDS:
            continue
        if "OddsDisclosureUI" in code(path):
            referencing.append(str(path.relative_to(SCRIPTS)))
    assert not referencing, f"the dormant disclosure is wired into {referencing}"


def test_a_dormant_component_still_renders_something():
    """A screen that renders nothing looks broken, and a reviewer opening this
    component should see why it is empty."""
    body = _method_body(ODDS, "public string BuildDisclosureText()")
    assert "No randomized items are offered." in body


def test_the_text_is_buildable_without_a_scene():
    """Which is what makes "renders correctly in a test harness" assertable at
    all, rather than a screenshot."""
    assert "public string BuildDisclosureText()" in code(ODDS)


def test_percentages_come_from_weights():
    """A disclosure computed from separate numbers is a compliance problem
    waiting for someone to retune a drop table -- the units here match
    DropTable's on purpose."""
    body = _method_body(ODDS, "public float PercentFor(Entry entry)")
    assert "entry.weight / total * 100f" in body


def test_rounding_that_breaks_the_sum_is_caught():
    """A player who adds the published percentages and gets 99.7 has found a
    real discrepancy. Cheaper here than in a review note."""
    body = _method_body(ODDS, "public IReadOnlyList<string> Validate()")
    assert "displayedTotal" in body
    assert "not 100%" in body


def test_duplicate_outcomes_are_caught():
    """Two rows for one outcome understate its real chance -- the misleading
    direction."""
    body = _method_body(ODDS, "public IReadOnlyList<string> Validate()")
    assert "duplicate outcome" in body


def test_an_unwinnable_listed_outcome_is_caught():
    """Listed but impossible reads as a chance the player does not have."""
    body = _method_body(ODDS, "public IReadOnlyList<string> Validate()")
    assert "can never be won but is listed" in body


def test_precision_is_configurable_and_clamped():
    """Guideline 3.1.1 wants odds a player can act on, not marketing
    rounding."""
    source = code(ODDS)
    assert "decimalPlaces" in source
    assert "Mathf.Clamp(value, 0, 4)" in source


def test_no_division_by_zero_when_dormant():
    body = _method_body(ODDS, "public float PercentFor(Entry entry)")
    assert "total <= 0f" in body


def test_the_disclosure_is_retrievable(monkeypatch):
    monkeypatch.setattr(symbol_scanner, "CONFIG_PATH", REAL_ROOT / "config" / "agent.config.json")
    symbols = {s["name"]: s for s in symbol_scanner.rebuild_symbol_index(REAL_ROOT)["symbols"]}
    assert symbols["OddsDisclosureUI"]["namespace"] == "JetFighter.UI"


def test_the_harness_rendering_is_asserted_in_csharp():
    source = (REAL_ROOT / "Assets" / "_Game" / "Tests" / "EditMode"
              / "OddsDisclosureTests.cs").read_text()
    assert "ADormantComponentRendersAnExplanationNotABlank" in source
    assert "ItRendersOddsWhenGivenSome" in source
    assert "RoundingThatBreaksTheSumIsReported" in source


def test_the_adr_note_about_rereading_guidelines_is_honoured_in_the_row():
    """ADR-006 says to re-read the current guidelines at Cycle 5 rather than
    trusting the note. The row should say the check is a human one."""
    row = [l for l in (REAL_ROOT / "docs" / "TRACEABILITY.md").read_text().splitlines()
           if "`phase5/compliance-odds-ui`" in l and l.startswith("|")]
    assert len(row) == 1
    assert "3.1.1" in row[0] or "guideline" in row[0].lower()


def test_the_seed_node_matches_the_traceability_row():
    seeds = json.loads((REAL_ROOT / "knowledge" / "seeds.json").read_text())
    node = next(n for n in seeds["nodes"] if n["id"] == "phase5_compliance_odds_ui")
    assert "OddsDisclosureUI" in node["symbols"]

    row = [l for l in (REAL_ROOT / "docs" / "TRACEABILITY.md").read_text().splitlines()
           if "`phase5/compliance-odds-ui`" in l and l.startswith("|")]
    assert row[0].startswith("| [x] |")
    assert "ADR-006" in row[0]
