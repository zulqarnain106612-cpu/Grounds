"""Cycle 2, branch phase2/enemy-healthbar-ui.

Criterion: "Bar updates on event, not per-frame polling." The positive half is
easy to satisfy accidentally; the negative half is the one that decays, and it
is usually evidenced with a profiler screenshot, which does not fail a build.
These hold the structure that makes the C# assertion possible.
"""
from __future__ import annotations

import json
from pathlib import Path

import pytest

from gateway import symbol_scanner
from tests._csharp import code

REAL_ROOT = Path(__file__).resolve().parent.parent
BAR = REAL_ROOT / "Assets" / "_Game" / "Scripts" / "UI" / "EnemyHealthBarUI.cs"
TESTS = REAL_ROOT / "Assets" / "_Game" / "Tests"


def _method_body(source: str, signature: str) -> str:
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


def test_the_bar_subscribes_rather_than_polls():
    source = code(BAR)
    assert "OnDamaged.AddListener" in source
    assert "OnDamaged.RemoveListener" in source, "a bar that never unsubscribes leaks with every pooled enemy"


def test_no_per_frame_code_reads_health():
    """The rule is about *health*. Billboarding is legitimately per-frame --
    the camera moves whether or not anyone takes damage -- so the check is
    that LateUpdate touches placement only."""
    for hook in ("private void LateUpdate()", ):
        body = _method_body(code(BAR), hook)
        for forbidden in ("health.", "PercentRemaining", "CurrentHealth", "Refresh("):
            assert forbidden not in body, f"{hook} reads health via {forbidden}"


def test_there_is_no_update_at_all():
    assert "private void Update()" not in code(BAR)


def test_refreshes_are_countable():
    """RefreshCount is what turns 'no per-frame polling' from a profiler
    observation into an assertion that fails a build."""
    assert "public int RefreshCount" in code(BAR)


def test_subscription_is_idempotent():
    """Pooled enemies enable and disable constantly, and a doubled listener is
    invisible until profiling."""
    body = _method_body(code(BAR), "private void Subscribe()")
    assert body.index("RemoveListener") < body.index("AddListener"), \
        "Subscribe adds without removing first, so a re-enable doubles the listener"


def test_the_bar_unsubscribes_on_disable():
    assert "private void OnDisable()" in code(BAR)
    assert "Unsubscribe" in _method_body(code(BAR), "private void OnDisable()")


def test_the_colour_ramp_is_a_pure_function():
    """So the thresholds are a unit test rather than a screenshot."""
    assert "public static Color ColorFor(" in code(BAR)


def test_the_warning_band_passes_through_yellow():
    """The spec says green -> yellow -> red. A single red-to-green lerp goes
    through a muddy olive, which reads as a rendering fault rather than a
    warning."""
    body = _method_body(code(BAR), "public static Color ColorFor(")
    assert "Color.yellow" in body
    assert body.count("Color.Lerp") >= 2, "one lerp cannot pass through a midpoint colour"


def test_the_thresholds_cannot_be_inverted_by_the_inspector():
    body = _method_body(code(BAR), "public static Color ColorFor(")
    assert "Mathf.Max(healthyThreshold, criticalThreshold)" in body
    assert "Mathf.Min(healthyThreshold, criticalThreshold)" in body


def test_the_bar_is_retrievable(monkeypatch):
    monkeypatch.setattr(symbol_scanner, "CONFIG_PATH", REAL_ROOT / "config" / "agent.config.json")
    symbols = {s["name"]: s for s in symbol_scanner.rebuild_symbol_index(REAL_ROOT)["symbols"]}
    assert symbols["EnemyHealthBarUI"]["namespace"] == "JetFighter.UI"


def test_the_idle_frame_assertion_exists_in_playmode():
    """EditMode cannot prove this: OnEnable/OnDisable do not run there, and
    neither do frames."""
    play = (TESTS / "PlayMode" / "EnemyHealthBarPlayModeTests.cs").read_text()
    assert "IdleFramesDoNotRefreshTheBar" in play
    assert "RefreshCount" in play


def test_the_seed_node_matches_the_traceability_row():
    seeds = json.loads((REAL_ROOT / "knowledge" / "seeds.json").read_text())
    node = next(n for n in seeds["nodes"] if n["id"] == "phase2_enemy_healthbar_ui")
    assert "EnemyHealthBarUI" in node["symbols"]

    row = [l for l in (REAL_ROOT / "docs" / "TRACEABILITY.md").read_text().splitlines()
           if "`phase2/enemy-healthbar-ui`" in l and l.startswith("|")]
    assert len(row) == 1 and row[0].startswith("| [x] |")
