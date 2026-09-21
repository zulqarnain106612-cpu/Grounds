"""Cycle 3, branch phase3/enemy-drop-tables.

Criterion: "Drop weights are retunable with no code change", verified by drop
rates matching configured weights within statistical tolerance over N kills.

The statistical half is a C# sweep. The "no code change" half is a property of
where the data lives, which is checkable here -- and it is the half that
erodes, because the first special case someone needs is always easier to write
in code than to express as a weight.
"""
from __future__ import annotations

import json
from pathlib import Path

import pytest

from gateway import symbol_scanner
from tests._csharp import code

REAL_ROOT = Path(__file__).resolve().parent.parent
ENEMY = REAL_ROOT / "Assets" / "_Game" / "Scripts" / "Enemy"
TABLE = ENEMY / "DropTable.cs"
DEF = ENEMY / "EnemyDef.cs"
HEALTH = ENEMY / "EnemyHealth.cs"


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


def test_the_table_lives_on_the_enemy_asset():
    """A designer retuning drop rates opens the asset that already describes
    the enemy, rather than hunting a lookup keyed by enemy type in code."""
    assert "public DropTable dropTable" in code(DEF)
    assert "[Serializable]" in code(TABLE)


def test_no_drop_rate_is_hard_coded():
    """The criterion. The first special case someone needs is always easier to
    write in code than to express as a weight."""
    source = code(TABLE)
    assert "dropChance" in source
    assert "entry.weight" in source
    # No literal probabilities anywhere in the rolling logic.
    body = _method_body(TABLE, "public PowerUpDef Select(float weightRoll)")
    for literal in ("0.5f", "0.25f", "0.1f"):
        assert literal not in body, f"a probability literal ({literal}) is baked into Select"


def test_weights_are_relative_not_percentages():
    """Percentages must sum to 100, so editing one row breaks every other.
    Weights are independent -- doubling one is a local change with an obvious
    meaning."""
    source = code(TABLE)
    assert "TotalWeight" in source
    assert "weightRoll) * total" in source, "the roll is not scaled to the total weight"
    assert "/ 100" not in source


def test_the_roll_is_injected_rather_than_drawn_inside():
    """So the distribution is testable exactly, and so Phase 4 can drive both
    clients from one seed -- a drop each client rolled separately is a desync
    with loot in it."""
    assert "public PowerUpDef Roll(float dropRoll, float weightRoll)" in code(TABLE)
    assert "Random" not in code(TABLE)
    assert "public System.Func<float> RollSource" in code(HEALTH)


def test_the_top_of_the_roll_range_still_drops():
    """`target < cursor` is false for the last entry at exactly 1.0. Left
    unhandled it silently drops nothing once in a few million kills -- rare
    enough never to be reproduced, common enough to be reported."""
    assert "LastUsable()" in code(TABLE)


def test_unusable_entries_are_skipped_not_counted():
    """A designer clearing a slot leaves a null behind; it must not become a
    hole in the distribution."""
    for signature in ("public float TotalWeight", "public PowerUpDef Select(float weightRoll)"):
        body = _method_body(TABLE, signature)
        assert "powerUp != null" in body or "powerUp == null" in body
        assert "weight" in body


def test_an_empty_table_is_a_configuration_state_not_an_error():
    """An enemy that drops nothing is a legitimate design."""
    body = _method_body(TABLE, "public PowerUpDef Select(float weightRoll)")
    assert "total <= 0f" in body
    assert "return null" in body


def test_the_drop_is_announced_rather_than_spawned():
    """EnemyHealth knows about damage, not about which pool owns pickups. The
    same split the health bar already uses, and what lets Phase 4 make the
    host the only roller without touching this class."""
    source = code(HEALTH)
    assert "OnDropped" in source
    assert "Instantiate" not in source
    assert "ObjectPool" not in source


def test_the_drop_is_rolled_before_death_is_announced():
    """A listener that deactivates or pools the enemy on OnDied must not be
    able to cancel the drop."""
    body = _method_body(HEALTH, "public void Die()")
    assert body.index("RollDrop()") < body.index("OnDied.Invoke()")


def test_a_drop_is_never_announced_as_null():
    """Otherwise every listener has to null-check."""
    body = _method_body(HEALTH, "private void RollDrop()")
    assert "dropped != null" in body


def test_an_enemy_with_no_table_still_dies():
    body = _method_body(HEALTH, "private void RollDrop()")
    assert "def.dropTable == null" in body


def test_the_drop_table_is_retrievable(monkeypatch):
    monkeypatch.setattr(symbol_scanner, "CONFIG_PATH", REAL_ROOT / "config" / "agent.config.json")
    symbols = {s["name"]: s for s in symbol_scanner.rebuild_symbol_index(REAL_ROOT)["symbols"]}
    assert symbols["DropTable"]["namespace"] == "JetFighter.Enemy"


def test_the_distribution_is_swept_not_sampled():
    """A test that draws real random numbers and allows a wide tolerance
    passes on a table that is subtly wrong, and fails occasionally on one that
    is right."""
    source = (REAL_ROOT / "Assets" / "_Game" / "Tests" / "EditMode" / "DropTableTests.cs").read_text()
    assert "WeightsDetermineTheShareOfDrops" in source
    assert "RetuningAWeightChangesTheShareWithNoCodeChange" in source
    assert "i / 10000f" in source, "the distribution is sampled rather than swept"


def test_the_seed_node_matches_the_traceability_row():
    seeds = json.loads((REAL_ROOT / "knowledge" / "seeds.json").read_text())
    node = next(n for n in seeds["nodes"] if n["id"] == "phase3_enemy_drop_tables")
    assert "DropTable" in node["symbols"]

    row = [l for l in (REAL_ROOT / "docs" / "TRACEABILITY.md").read_text().splitlines()
           if "`phase3/enemy-drop-tables`" in l and l.startswith("|")]
    assert len(row) == 1 and row[0].startswith("| [x] |")
