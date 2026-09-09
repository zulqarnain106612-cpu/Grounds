"""Cycle 2, branch phase2/weapon-bullet-damage.

Criterion: "Bullet applies damage via `IDamageable`", and the phase spec's
sharper version -- the gun kills a test enemy in the number of hits
`damage x maxHealth` predicts. A hit count only matches arithmetic if a bullet
hits exactly once and always comes back, so those two properties are what
these guard.
"""
from __future__ import annotations

import json
from pathlib import Path

import pytest

from gateway import symbol_scanner
from tests._csharp import code

REAL_ROOT = Path(__file__).resolve().parent.parent
SCRIPTS = REAL_ROOT / "Assets" / "_Game" / "Scripts"
BULLET = SCRIPTS / "Weapon" / "Bullet.cs"
GUN = SCRIPTS / "Weapon" / "PrimaryGunController.cs"


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


def test_the_bullet_damages_through_the_interface():
    """Not through EnemyHealth. Phase 4 makes enemy health host-authoritative
    and the missile shares this pipeline."""
    source = code(BULLET)
    assert "IDamageable" in source
    assert "EnemyHealth" not in source


def test_a_bullet_can_only_hit_once():
    """A bullet passing through a formation on one frame would damage every
    member, and the hit count would stop matching the arithmetic the criterion
    is stated in."""
    body = _method_body(BULLET, "public void HandleHit(")
    assert "if (spent)" in body


def test_a_bullet_always_returns_itself():
    """Including when it hits terrain or a corpse. A bullet that only returns
    on a successful hit drains the pool over a game."""
    body = _method_body(BULLET, "public void HandleHit(")
    assert body.count("Finish()") >= 2, "some hit outcome leaves the bullet in flight"


def test_release_happens_exactly_once():
    """A double release corrupts the pool's counts, which is the bound the
    previous cell established."""
    body = _method_body(BULLET, "public void Finish()")
    assert "if (spent)" in body
    assert "spent = true" in body


def test_an_unspent_bullet_expires():
    """Without a lifetime a missed bullet never comes back, the pool drains,
    and the gun starts recycling live bullets in front of the player."""
    body = _method_body(BULLET, "public void Step(float deltaTime)")
    assert "lifetimeRemaining -= deltaTime" in body
    assert "Finish()" in body


def test_a_recycled_bullet_is_rearmed_rather_than_reused_as_is():
    """The pool hands the same instance back; a bullet still carrying the last
    shot's countdown expires mid-screen."""
    assert "public void Launch(" in code(BULLET)
    body = _method_body(GUN, "public void Fire()")
    assert "projectile.Launch(" in body, "the gun takes a bullet from the pool without arming it"


def test_the_shots_damage_is_fixed_when_it_is_fired():
    """The gun applies the player's multiplier at fire time. A bullet that
    re-read the weapon asset on impact would ignore every power-up."""
    body = _method_body(GUN, "public void Fire()")
    assert "EffectiveDamage" in body
    assert "weaponDef.damage" not in code(BULLET)


def test_the_gun_does_not_track_bullets_in_flight():
    """Returning to the pool is the bullet's own job -- it is the only thing
    that knows the shot is over."""
    assert "OnFinished" in code(BULLET)
    assert "ReleaseProjectile" in code(GUN)
    for forbidden in ("List<Bullet>", "Bullet[]", "activeBullets"):
        assert forbidden not in code(GUN)


def test_the_fire_path_still_neither_instantiates_nor_destroys():
    """The Phase 1 bound, re-checked now that Fire() does more than it did."""
    body = _method_body(GUN, "public void Fire()")
    for forbidden in ("Instantiate", "Destroy"):
        assert forbidden not in body


def test_projectile_speed_and_lifetime_are_data():
    """Balance stays in the ScriptableObject, per ADR-004's principle."""
    source = code(SCRIPTS / "Weapon" / "WeaponBase.cs")
    assert "projectileSpeed" in source and "projectileLifetime" in source


def test_the_bullet_is_retrievable(monkeypatch):
    monkeypatch.setattr(symbol_scanner, "CONFIG_PATH", REAL_ROOT / "config" / "agent.config.json")
    symbols = {s["name"]: s for s in symbol_scanner.rebuild_symbol_index(REAL_ROOT)["symbols"]}
    assert symbols["Bullet"]["namespace"] == "JetFighter.Weapon"


def test_the_predicted_hit_count_is_asserted_in_csharp():
    edit = (REAL_ROOT / "Assets" / "_Game" / "Tests" / "EditMode" / "BulletDamageTests.cs").read_text()
    play = (REAL_ROOT / "Assets" / "_Game" / "Tests" / "PlayMode"
            / "BulletPipelinePlayModeTests.cs").read_text()
    assert "TheHitCountMatchesTheArithmetic" in edit
    assert "TheGunKillsTheEnemyInThePredictedNumberOfShots" in play


def test_the_seed_node_matches_the_traceability_row():
    seeds = json.loads((REAL_ROOT / "knowledge" / "seeds.json").read_text())
    node = next(n for n in seeds["nodes"] if n["id"] == "phase2_weapon_bullet_damage")
    assert "Bullet" in node["symbols"]

    row = [l for l in (REAL_ROOT / "docs" / "TRACEABILITY.md").read_text().splitlines()
           if "`phase2/weapon-bullet-damage`" in l and l.startswith("|")]
    assert len(row) == 1 and row[0].startswith("| [x] |")
