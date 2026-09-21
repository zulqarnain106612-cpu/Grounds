"""Cycle 1, branch phase1/weapon-gun-stub.

The row's criterion has two mechanical halves -- the pool stays bounded, and
there is no Instantiate/Destroy in the fire path. The first is a C# soak. The
second is a property of the source, which is checkable here and today, and is
the one that quietly stops being true when someone adds a muzzle flash.
"""
from __future__ import annotations

import json
import re
from pathlib import Path

import pytest

from gateway import symbol_scanner
from tests._csharp import code

REAL_ROOT = Path(__file__).resolve().parent.parent
SCRIPTS = REAL_ROOT / "Assets" / "_Game" / "Scripts"
GUN = SCRIPTS / "Weapon" / "PrimaryGunController.cs"
POOL = SCRIPTS / "Shared" / "ObjectPool.cs"
WEAPON = SCRIPTS / "Weapon" / "WeaponBase.cs"


def _method_body(path: Path, signature: str) -> str:
    """Source of one method, from its signature to the matching brace."""
    source = code(path)
    start = source.index(signature)
    depth, i = 0, source.index("{", start)
    for j in range(i, len(source)):
        if source[j] == "{":
            depth += 1
        elif source[j] == "}":
            depth -= 1
            if depth == 0:
                return source[start:j + 1]
    raise AssertionError(f"unbalanced braces after {signature}")


def test_the_fire_path_neither_instantiates_nor_destroys():
    """The criterion, stated as a property of the code. A muzzle flash added
    here in six months is exactly how this stops being true, and a soak test
    on a fast machine would not notice."""
    body = _method_body(GUN, "public void Fire()")
    for forbidden in ("Instantiate", "Destroy"):
        assert forbidden not in body, f"Fire() calls {forbidden}"


def test_the_pool_never_destroys_anything():
    """Destroying a pooled instance defeats the pool: the next Get has to
    allocate, and the churn returns under exactly the sustained fire the pool
    was built for."""
    assert "Destroy" not in code(POOL)


def test_the_pool_allocates_only_below_its_capacity():
    """The bound. An unbounded pool under a Phase 3 fire-rate power-up is a
    slow leak that looks exactly like a pool doing its job."""
    body = _method_body(POOL, "public GameObject Get()")
    assert "Count < capacity" in body, "Get() allocates without checking the cap"
    assert "live.Dequeue()" in body, "there is no path for an exhausted pool"


def test_the_gun_prewarms():
    """The first shot is the one moment the player is guaranteed to be
    watching."""
    assert "Prewarm" in code(GUN)


def test_the_cooldown_is_time_based_not_frame_counted():
    """The criterion says 1.0s +/- 0.02s *regardless of frame rate*, and this
    project ships a 30fps low tier. A frame counter cannot satisfy that by
    construction."""
    source = code(GUN)
    assert "cooldownTimer -= deltaTime" in source
    assert "Time.frameCount" not in source
    assert "cooldownTimer +=" in source, \
        "the cooldown resets instead of carrying its remainder, which drifts by a frame per shot"


def test_tick_takes_delta_time_so_a_soak_need_not_take_five_real_minutes():
    assert "public void Tick(float deltaTime)" in code(GUN)


def test_a_long_frame_still_owes_every_shot_it_swallowed():
    body = _method_body(GUN, "public void Tick(float deltaTime)")
    assert "while (" in body, "a single `if` silently eats shots across a hitch"
    assert re.search(r"guard\+\+ ?< ?\d+", body), \
        "an unguarded catch-up loop hangs the frame if deltaTime is enormous"


def test_balance_lives_in_data_not_in_the_controller():
    """Phase 3's fire-rate power-ups and Phase 2's damage variety both edit
    the ScriptableObject, not this class."""
    source = code(WEAPON)
    for field in ("fireRatePerSecond", "damage", "poolCapacity", "poolTag"):
        assert field in source
    assert not re.search(r"\b1\.0f\s*/\s*\d", code(GUN)), "a fire rate is hard-coded in the controller"


def test_a_zero_fire_rate_cannot_divide_by_zero():
    assert "Mathf.Max(0.01f, fireRatePerSecond)" in code(WEAPON)


@pytest.mark.parametrize("name,namespace", [
    ("ObjectPool", "JetFighter.Shared"),
    ("WeaponBase", "JetFighter.Weapon"),
    ("PrimaryGunController", "JetFighter.Weapon"),
])
def test_the_weapon_types_are_retrievable(monkeypatch, name, namespace):
    monkeypatch.setattr(symbol_scanner, "CONFIG_PATH", REAL_ROOT / "config" / "agent.config.json")
    symbols = {s["name"]: s for s in symbol_scanner.rebuild_symbol_index(REAL_ROOT)["symbols"]}
    assert symbols[name]["namespace"] == namespace


def test_the_soak_is_long_enough_to_expose_growth():
    """The row asks for a five-minute continuous-fire soak."""
    source = (REAL_ROOT / "Assets" / "_Game" / "Tests" / "EditMode"
              / "PrimaryGunControllerTests.cs").read_text()
    assert "Simulate(300f" in source, "the longest soak is under the five minutes the row asks for"


def test_the_seed_node_matches_the_traceability_row():
    seeds = json.loads((REAL_ROOT / "knowledge" / "seeds.json").read_text())
    node = next(n for n in seeds["nodes"] if n["id"] == "phase1_weapon_gun_stub")
    assert {"ObjectPool", "WeaponBase", "PrimaryGunController"} <= set(node["symbols"])

    row = [l for l in (REAL_ROOT / "docs" / "TRACEABILITY.md").read_text().splitlines()
           if "`phase1/weapon-gun-stub`" in l and l.startswith("|")]
    assert len(row) == 1 and row[0].startswith("| [x] |")
