"""Cycle 2, branch phase2/missile-system (ADR-002).

Criterion: the missile destroys the targeted ground enemy, the cooldown holds,
and the pool stays bounded under soak. All three are C# assertions; these hold
the structure they depend on -- chiefly that the missile obeys the same pool
contract as the bullet, since a leaked instance is what breaks the bound.
"""
from __future__ import annotations

import json
from pathlib import Path

import pytest

from gateway import symbol_scanner
from tests._csharp import code

REAL_ROOT = Path(__file__).resolve().parent.parent
SCRIPTS = REAL_ROOT / "Assets" / "_Game" / "Scripts"
MISSILE = SCRIPTS / "Weapon" / "MissileController.cs"
LAUNCHER = SCRIPTS / "Weapon" / "MissileLauncher.cs"
BUTTON = SCRIPTS / "UI" / "Input" / "MissileTriggerButton.cs"


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


def test_the_launcher_reuses_the_phase_1_pool():
    """Which is why ObjectPool was written generic in Phase 1 rather than as
    part of the gun."""
    source = code(LAUNCHER)
    assert "new ObjectPool(" in source
    assert "Prewarm" in source


def test_the_missile_returns_itself_exactly_once():
    """A leaked instance is what breaks the bound the criterion measures."""
    body = _method_body(MISSILE, "public void Finish()")
    assert "if (spent)" in body
    assert "spent = true" in body
    assert "OnFinished?.Invoke" in body


def test_a_missile_whose_target_disappears_still_comes_back():
    """Enemies are pooled, so a killed one is deactivated rather than
    destroyed. A missile that froze or threw on a dead target would leak."""
    body = _method_body(MISSILE, "public void Step(float deltaTime)")
    assert "activeInHierarchy" in body
    assert "lifetimeRemaining" in body


def test_impact_is_tested_after_the_move_as_well():
    """At 60 units/second with a 1-unit radius a 60fps step is exactly the
    radius, so a before-move-only test tunnels straight through the target."""
    body = _method_body(MISSILE, "public void Step(float deltaTime)")
    move = body.index("transform.position +=")
    assert "Detonate()" in body[:move], "no impact check before the move"
    assert "Detonate()" in body[move:], "no impact check after the move -- fast missiles will tunnel"


def test_guidance_is_a_capped_turn_not_a_snap():
    """The cap is what makes a missile dodgeable, and therefore a weapon
    rather than a guarantee."""
    assert "public static Quaternion Steer(" in code(MISSILE)
    body = _method_body(MISSILE, "public static Quaternion Steer(")
    assert "RotateTowards" in body
    assert "turnDegreesPerSecond * deltaTime" in body


def test_guidance_is_arcade_not_simulation():
    """The roadmap asks for feel over fidelity, and a physically guided
    missile is also unpredictable to balance."""
    source = code(MISSILE)
    for physics in ("AddForce", "linearVelocity", "Rigidbody rb"):
        assert physics not in source


def test_the_missile_damages_through_the_interface():
    source = code(MISSILE)
    assert "IDamageable" in source
    assert "EnemyHealth" not in source


def test_the_launcher_never_auto_fires():
    """ADR-002's split. It is also what makes the cooldown meaningful -- an
    auto-firing missile spends its own cooldown and the player never feels the
    resource."""
    body = _method_body(LAUNCHER, "private void Update()")
    assert "Fire(" not in body
    assert "Tick(" in body


def test_the_cooldown_cannot_be_banked():
    """Left to run negative, two minutes of idling buys two minutes of instant
    missiles the moment the player presses."""
    body = _method_body(LAUNCHER, "public void Tick(float deltaTime)")
    assert "Mathf.Max(0f," in body


def test_a_refused_press_does_not_spend_the_cooldown():
    body = _method_body(LAUNCHER, "public bool Fire(Transform target)")
    null_check = body.index("target == null")
    cooldown_set = body.index("cooldownRemaining = cooldownSeconds")
    assert null_check < cooldown_set
    assert "return false" in body[null_check:cooldown_set]


def test_the_button_fires_on_press_not_on_click():
    """A UGUI click needs the release on the same button, so a thumb that
    drifts while flying swallows the shot -- which the player reads as the
    game ignoring them."""
    source = code(BUTTON)
    assert "IPointerDownHandler" in source
    assert "IPointerClickHandler" not in source


def test_the_button_asks_the_reticle_rather_than_raycasting_itself():
    """Two places deciding what is targetable is two places to change when
    ADR-002 is confirmed or reversed."""
    source = code(BUTTON)
    assert "CurrentTarget" in source
    assert "Raycast" not in source


@pytest.mark.parametrize("name", ["MissileController", "MissileLauncher"])
def test_the_missile_types_are_retrievable(monkeypatch, name):
    monkeypatch.setattr(symbol_scanner, "CONFIG_PATH", REAL_ROOT / "config" / "agent.config.json")
    symbols = {s["name"]: s for s in symbol_scanner.rebuild_symbol_index(REAL_ROOT)["symbols"]}
    assert symbols[name]["namespace"] == "JetFighter.Weapon"


def test_all_three_criteria_are_asserted_in_csharp():
    edit = (REAL_ROOT / "Assets" / "_Game" / "Tests" / "EditMode" / "MissileSystemTests.cs").read_text()
    assert "AMissileReachesAndDestroysItsTarget" in edit
    assert "TheCooldownBlocksASecondPress" in edit
    assert "ThePoolStaysBoundedOverASoak" in edit


def test_the_seed_node_matches_the_traceability_row():
    seeds = json.loads((REAL_ROOT / "knowledge" / "seeds.json").read_text())
    node = next(n for n in seeds["nodes"] if n["id"] == "phase2_missile_system")
    assert {"MissileController", "MissileLauncher"} <= set(node["symbols"])

    row = [l for l in (REAL_ROOT / "docs" / "TRACEABILITY.md").read_text().splitlines()
           if "`phase2/missile-system`" in l and l.startswith("|")]
    assert len(row) == 1 and row[0].startswith("| [x] |")
    assert "ADR-002" in row[0]
