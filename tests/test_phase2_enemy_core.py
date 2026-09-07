"""Cycle 2, branch phase2/enemy-core.

Criterion: "Damage pipeline unit-tested through `IDamageable`". The tests
themselves are C#; what is checkable here is that the pipeline has the shape
the rest of Phase 2 and Phase 4 depend on -- damage through the interface,
health changes as events rather than a pollable field, and no Phase 3 data
smuggled in early.
"""
from __future__ import annotations

import json
from pathlib import Path

import pytest

from gateway import symbol_scanner
from tests._csharp import code

REAL_ROOT = Path(__file__).resolve().parent.parent
SCRIPTS = REAL_ROOT / "Assets" / "_Game" / "Scripts"
DAMAGEABLE = SCRIPTS / "Shared" / "IDamageable.cs"
HEALTH = SCRIPTS / "Enemy" / "EnemyHealth.cs"
CONTROLLER = SCRIPTS / "Enemy" / "EnemyController.cs"
DEF = SCRIPTS / "Enemy" / "EnemyDef.cs"


def test_damage_arrives_through_an_interface_not_a_concrete_type():
    """Phase 2 ships two weapon types and Phase 4 makes enemy health
    host-authoritative. A bullet holding EnemyHealth needs changing for each
    of those; one holding IDamageable needs changing for none."""
    assert "public interface IDamageable" in code(DAMAGEABLE)
    assert "void ApplyDamage(float" in code(DAMAGEABLE)
    assert ": MonoBehaviour, IDamageable" in code(HEALTH)


def test_damage_is_synchronous():
    """The caller is a bullet, and it has to know whether it hit something in
    time to return itself to its pool on the same frame."""
    source = code(DAMAGEABLE)
    assert "event" not in source and "UnityEvent" not in source
    assert "void ApplyDamage" in source, "damage returning a value or a coroutine breaks the pool return"


def test_health_changes_are_pushed_not_polled():
    """A roadmap performance requirement, and the only version that survives
    Phase 4: host-authoritative health changes arrive on no particular frame,
    so a poller shows them late on one device and on time on the other."""
    source = code(HEALTH)
    assert "UnityEvent<float> OnDamaged" in source
    assert "OnDamaged.Invoke" in source


def test_death_is_an_event_rather_than_a_destroy():
    """Phase 3 hooks drop tables onto death, and these enemies come from a
    pool -- a destroyed enemy cannot be returned to the pool it came from."""
    source = code(HEALTH)
    assert "OnDied.Invoke" in source
    assert "Destroy(" not in source
    assert "SetActive(false)" in source


def test_death_cannot_fire_twice():
    """Several bullets can land on the same frame. A drop table fired twice is
    duplicated loot; in Phase 4 it is a desync."""
    source = code(HEALTH)
    body = source[source.index("public void ApplyDamage"):source.index("public void Die()")]
    assert "IsDead" in body, "ApplyDamage does not short-circuit on a dead enemy"


def test_negative_damage_cannot_heal():
    body = code(HEALTH)
    assert "amount <= 0f" in body


def test_health_is_restorable_for_pool_reuse():
    """A recycled enemy that kept its old health dies to a single bullet."""
    assert "public void ResetHealth()" in code(HEALTH)


def test_the_movement_step_is_pure_and_frame_rate_independent():
    source = code(CONTROLLER)
    assert "public static Vector3 NextPosition(" in source
    assert "deltaTime" in source
    assert "Time." not in source[source.index("public static Vector3 NextPosition("):]


def test_the_approach_cannot_overshoot_the_loiter_ring():
    """speed * dt alone carries the enemy past its hold distance on a long
    frame and back the next one, which reads as jitter rather than as AI."""
    body = code(CONTROLLER)
    assert "Mathf.Min(speed * deltaTime, distance - loiterDistance)" in body


def test_phase_3_data_is_not_smuggled_in_early():
    """The spec is explicit that the drop table is Phase 3. A data shape
    carrying fields nothing reads is how a schema becomes fiction."""
    source = code(DEF)
    assert "dropTable" not in source
    assert "difficulty" not in source.lower()


@pytest.mark.parametrize("name,namespace", [
    ("IDamageable", "JetFighter.Shared"),
    ("EnemyDef", "JetFighter.Enemy"),
    ("EnemyHealth", "JetFighter.Enemy"),
    ("EnemyController", "JetFighter.Enemy"),
])
def test_the_enemy_types_are_retrievable(monkeypatch, name, namespace):
    monkeypatch.setattr(symbol_scanner, "CONFIG_PATH", REAL_ROOT / "config" / "agent.config.json")
    symbols = {s["name"]: s for s in symbol_scanner.rebuild_symbol_index(REAL_ROOT)["symbols"]}
    assert symbols[name]["namespace"] == namespace


def test_the_pipeline_is_exercised_through_the_interface_in_the_tests():
    """Testing the concrete class would prove something no weapon does."""
    source = (REAL_ROOT / "Assets" / "_Game" / "Tests" / "EditMode" / "EnemyCoreTests.cs").read_text()
    assert "IDamageable AsDamageable" in source
    assert source.count("AsDamageable.ApplyDamage") >= 6


def test_the_seed_node_matches_the_traceability_row():
    seeds = json.loads((REAL_ROOT / "knowledge" / "seeds.json").read_text())
    node = next(n for n in seeds["nodes"] if n["id"] == "phase2_enemy_core")
    assert {"EnemyDef", "EnemyHealth", "EnemyController", "IDamageable"} <= set(node["symbols"])

    row = [l for l in (REAL_ROOT / "docs" / "TRACEABILITY.md").read_text().splitlines()
           if "`phase2/enemy-core`" in l and l.startswith("|")]
    assert len(row) == 1 and row[0].startswith("| [x] |")
