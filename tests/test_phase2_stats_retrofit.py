"""Cycle 2, retrofit row: the gun reads player stats, not WeaponBase directly.

docs/TRACEABILITY.md marks this "*Runs before the cells below*". Taken
literally it asks Phase 2 to depend on `PlayerStatsRuntime`, which is Cycle 3
(`phase3/player-stats-runtime`) -- so the literal reading means inventing
Phase 3's class inside Phase 2, the speculative design the roadmap refuses
everywhere else.

What the note protects against is retrofit cost: by Cycle 3 the gun has
callers, a missile launcher beside it and a power-up system arriving, and
changing where damage comes from then touches all of them. So the seam lands
now and the implementation lands in its own cell. These tests hold the seam to
that shape, so `PlayerStatsRuntime` can implement it without any weapon code
changing.
"""
from __future__ import annotations

import json
from pathlib import Path

import pytest

from tests._csharp import code

REAL_ROOT = Path(__file__).resolve().parent.parent
SCRIPTS = REAL_ROOT / "Assets" / "_Game" / "Scripts"
STATS = SCRIPTS / "Player" / "IPlayerStats.cs"
GUN = SCRIPTS / "Weapon" / "PrimaryGunController.cs"


def test_the_seam_is_an_interface_not_a_concrete_class():
    """Cycle 3 supplies the implementation. A concrete class here would have
    to be deleted then, which is the retrofit this row exists to avoid."""
    source = code(STATS)
    assert "public interface IPlayerStats" in source
    assert "MonoBehaviour" not in source, "the seam must not require a scene object"


def test_the_multipliers_scale_the_weapon_rather_than_replacing_it():
    """WeaponBase stays the source of balance -- data, not code. An absolute
    override would make every weapon's tuning unreachable the moment one
    power-up applied."""
    source = code(STATS)
    assert "DamageMultiplier" in source and "FireRateMultiplier" in source
    gun = code(GUN)
    assert "weaponDef.damage *" in gun, "damage is replaced, not scaled"
    assert "weaponDef.CooldownSeconds /" in gun, "the fire rate is replaced, not scaled"


def test_the_gun_no_longer_reads_the_raw_cooldown_when_firing():
    """The whole point of the row. If Tick still used WeaponBase directly, a
    Phase 3 power-up would change a number nothing reads."""
    source = code(GUN)
    start = source.index("public void Tick(float deltaTime)")
    body = source[start:source.index("public void Fire()")]
    assert "EffectiveCooldownSeconds" in body
    assert "weaponDef.CooldownSeconds" not in body


def test_a_missing_stats_source_keeps_the_gun_firing():
    """A null-object, not null checks. A jet that stops shooting because the
    power-up system has not spawned yet is a worse bug than any this
    prevents."""
    assert "class DefaultPlayerStats" in code(STATS)
    assert "?? DefaultPlayerStats.Instance" in code(GUN)


@pytest.mark.parametrize("expression,reason", [
    ("Mathf.Max(0.01f, stats.FireRateMultiplier)", "a zero fire-rate multiplier divides by zero"),
    ("Mathf.Max(0f, stats.DamageMultiplier)", "a negative damage multiplier heals the target"),
])
def test_the_multipliers_are_clamped(expression, reason):
    """Both values arrive from Phase 3 power-up stacking, which is exactly
    where an out-of-range number will come from."""
    assert expression in code(GUN), reason


def test_phase_3_is_not_pre_empted():
    """The seam only. Naming Phase 3's class here would be the speculative
    design the roadmap declines."""
    assert not (SCRIPTS / "Player" / "PlayerStatsRuntime.cs").exists()
    assert "PlayerStatsRuntime" not in code(GUN)


def test_the_retrofit_row_is_ticked():
    rows = [l for l in (REAL_ROOT / "docs" / "TRACEABILITY.md").read_text().splitlines()
            if "gun reads `PlayerStatsRuntime`" in l]
    assert len(rows) == 1
    assert rows[0].startswith("| [x] |")
