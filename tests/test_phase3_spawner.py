"""Cycle 3, branch phase3/enemy-spawner-waves.

Criterion: "Archetypes unlock at the configured thresholds" -- and the half
worth defending is the negative one: an archetype must appear only after its
threshold, never before.
"""
from __future__ import annotations

import json
from pathlib import Path

import pytest

from gateway import symbol_scanner
from tests._csharp import code

REAL_ROOT = Path(__file__).resolve().parent.parent
ENEMY = REAL_ROOT / "Assets" / "_Game" / "Scripts" / "Enemy"
SPAWNER = ENEMY / "EnemySpawner.cs"
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


def test_the_unlocked_set_is_asked_for_every_spawn():
    """A cached list is how an archetype leaks in early after a power level
    drops and rises again -- and the report is "an enemy I shouldn't have seen
    yet", which nobody can reproduce."""
    body = _method_body(SPAWNER, "public GameObject SpawnOne(")
    assert "difficulty.GetUnlockedArchetypes(playerPowerLevel)" in body
    source = code(SPAWNER)
    assert "cachedUnlocks" not in source and "unlockedCache" not in source


def test_power_level_is_an_argument_rather_than_looked_up_in_the_hot_path():
    """The only way to hold the player exactly at a threshold and assert both
    sides of it."""
    assert "public void Tick(float deltaTime, float playerPowerLevel, float playerDps)" in code(SPAWNER)
    assert "public GameObject SpawnOne(float playerPowerLevel, float playerDps)" in code(SPAWNER)


def test_the_spawner_does_not_search_the_scene_every_frame():
    """Found by the Cycle 6 hot-path sweep: Update ran two
    FindFirstObjectByType calls per frame for the whole run. That never appears
    as one obvious slow frame, which is exactly the kind the profiling pass
    exists to catch, so the references are cached and BindPlayer lets a scene
    avoid the search entirely."""
    source = code(SPAWNER)
    assert "cachedPowerUps" in source and "cachedGun" in source
    assert "public void BindPlayer(" in source
    body = _method_body(SPAWNER, "private float CurrentPowerLevel()")
    assert "cachedPowerUps == null" in body


def test_every_archetype_is_pooled_and_bounded():
    """A spawner is the easiest place in an endless runner to leak instances
    forever, because nothing ever tells it to stop."""
    source = code(SPAWNER)
    assert "new ObjectPool(prefab, poolCapacityPerArchetype" in source
    assert "Prewarm" in source
    assert "Instantiate" not in source


def test_a_hitch_still_owes_the_wave_its_enemies_but_cannot_hang_the_frame():
    body = _method_body(SPAWNER, "public void Tick(float deltaTime, float playerPowerLevel, float playerDps)")
    assert "while (" in body, "a single `if` swallows the enemies a hitch skipped"
    assert "guard++ < 32" in body, "an unguarded catch-up loop hangs on a huge deltaTime"


def test_scaling_is_written_to_the_instance_not_the_archetype():
    """The same rule PlayerStatsRuntime follows: a ScriptableObject edited in
    play mode keeps the change in the editor, so one scaled spawn during
    testing becomes the archetype's authored health."""
    body = _method_body(SPAWNER, "private void ApplyDifficulty(")
    assert "health.SetScaledHealth(" in body
    assert "archetype.maxHealth =" not in code(SPAWNER)
    assert "public void SetScaledHealth(" in code(HEALTH)


def test_a_recycled_enemy_is_rescaled():
    """These come from a pool; one keeping the previous spawn's health would
    be trivially killable or unkillable for no visible reason."""
    body = _method_body(SPAWNER, "private void ApplyDifficulty(")
    assert "health.Def = archetype" in body
    reset = _method_body(HEALTH, "public void ResetHealth()")
    assert "scaledMaxHealth = 0f" in reset


def test_the_authored_health_stays_readable():
    """Needed by the difficulty curve, which scales from the authored value
    rather than compounding on an already-scaled one."""
    assert "public float AuthoredMaxHealth" in code(HEALTH)


def test_the_spawner_prefers_an_archetype_the_player_can_kill():
    """DifficultyManager cannot scale an enemy below its authored stats, so
    choosing well here is the only lever left -- this is what
    ExceedsCeilingUnscaled was surfaced for."""
    body = _method_body(SPAWNER, "private EnemyDef ChooseKillable(")
    assert "DifficultyManager.ExceedsCeilingUnscaled(" in body


def test_an_all_unkillable_wave_still_spawns_and_reports():
    """An empty screen reads as the game having stopped. A spawner that
    quietly stops is indistinguishable from a broken one."""
    body = _method_body(SPAWNER, "private EnemyDef ChooseKillable(")
    assert "SkippedAsUnkillable++" in body
    assert "return fallback" in body


def test_a_missing_prefab_is_skipped_rather_than_spawned_as_nothing():
    """A wiring mistake must not look like the threshold rule failing."""
    body = _method_body(SPAWNER, "public GameObject SpawnOne(")
    assert "prefab == null" in body


def test_the_spawner_is_retrievable(monkeypatch):
    monkeypatch.setattr(symbol_scanner, "CONFIG_PATH", REAL_ROOT / "config" / "agent.config.json")
    symbols = {s["name"]: s for s in symbol_scanner.rebuild_symbol_index(REAL_ROOT)["symbols"]}
    assert symbols["EnemySpawner"]["namespace"] == "JetFighter.Enemy"


def test_the_negative_half_is_asserted_over_many_attempts():
    """One attempt below the threshold proves nothing about a random pick."""
    source = (REAL_ROOT / "Assets" / "_Game" / "Tests" / "EditMode" / "EnemySpawnerTests.cs").read_text()
    assert "ALockedArchetypeNeverSpawns" in source
    assert "SpawnMany(200, 1.99f)" in source
    assert "AnArchetypeThatUnlockedDoesNotLeakBackInWhenPowerFalls" in source


def test_the_seed_node_matches_the_traceability_row():
    seeds = json.loads((REAL_ROOT / "knowledge" / "seeds.json").read_text())
    node = next(n for n in seeds["nodes"] if n["id"] == "phase3_enemy_spawner_waves")
    assert "EnemySpawner" in node["symbols"]

    row = [l for l in (REAL_ROOT / "docs" / "TRACEABILITY.md").read_text().splitlines()
           if "`phase3/enemy-spawner-waves`" in l and l.startswith("|")]
    assert len(row) == 1 and row[0].startswith("| [x] |")
