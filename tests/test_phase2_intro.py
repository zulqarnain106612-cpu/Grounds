"""Cycle 2, branch phase2/intro-sequence.

Criterion: "Control hands off to physics only at `Go`" -- the jet cannot be
moved or fire during the countdown, and both enable exactly at Go, not before.

The "not before" half is the one that decays, and it decays by a later branch
re-enabling one system for its own reasons. So the structural claim worth
holding here is that there is exactly one gate.
"""
from __future__ import annotations

import json
from pathlib import Path

import pytest

from gateway import symbol_scanner
from tests._csharp import code

REAL_ROOT = Path(__file__).resolve().parent.parent
INTRO = REAL_ROOT / "Assets" / "_Game" / "Scripts" / "Player" / "IntroSequenceController.cs"
TESTS = REAL_ROOT / "Assets" / "_Game" / "Tests"


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


def test_there_is_exactly_one_gate():
    """Each system checking a flag for itself is how "not before Go" becomes
    "not before Go, except the gun, which someone re-enabled in a later
    branch"."""
    source = code(INTRO)
    assert source.count("public void ApplyControl(bool enabled)") == 1
    # The declaration plus exactly one internal caller: the state transition.
    # A third occurrence means some other path toggles control on its own.
    assert source.count("ApplyControl(") == 2, \
        "control is toggled outside the single state transition"
    assert "ApplyControl(playerHasControl)" in _method_body(INTRO, "private void Enter(State next)")


def test_every_player_driven_system_passes_through_the_gate():
    body = _method_body(INTRO, "public void ApplyControl(bool enabled)")
    for system in ("jetBody", "inputRouter", "primaryGun", "missileLauncher"):
        assert system in body, f"{system} is not gated"


def test_control_arrives_at_go_and_not_before():
    body = _method_body(INTRO, "private void Enter(State next)")
    assert "next == State.Go" in body
    assert "State.PlayerControl" in body


def test_the_spawn_tween_is_not_physics_driven():
    """A jet flown in by forces arrives carrying velocity that the plane
    constraint has to fight on the first frame of player control."""
    source = code(INTRO)
    assert "AnimationCurve" in source
    assert "AddForce" not in source
    body = _method_body(INTRO, "private void TickSpawn()")
    assert "transform.position =" in body


def test_the_jet_lands_exactly_on_its_rest_position():
    """An overshooting ease would hand the player a jet a few centimetres off
    its plane, and the constraint would visibly yank it back."""
    body = _method_body(INTRO, "private void TickSpawn()")
    assert "transform.position = restPosition" in body


def test_velocity_is_cleared_as_the_gate_closes_not_as_it_opens():
    """Velocity banked before the gate would be waiting to launch the jet the
    instant control arrives."""
    body = _method_body(INTRO, "public void ApplyControl(bool enabled)")
    assert "linearVelocity = Vector3.zero" in body
    assert "if (!enabled)" in body


def test_a_held_stick_cannot_resume_at_go():
    """The router normally zeroes the input vector. Disabled, it cannot."""
    body = _method_body(INTRO, "public void ApplyControl(bool enabled)")
    assert "inputVector = Vector2.zero" in body


def test_the_countdown_cannot_skip_numbers_on_a_hitch():
    """A frame longer than one count must still emit every number the HUD
    renders, not jump from five to one."""
    body = _method_body(INTRO, "private void TickCountdown()")
    assert "while (" in body, "a single `if` drops counts across a long frame"
    assert "stateElapsed -= secondsPerCount" in body, \
        "the remainder is discarded, so the countdown drifts"


def test_the_sequence_is_driven_by_delta_time():
    """The criterion is about what is enabled at each instant; waiting six
    real seconds per assertion would make that suite unusable."""
    assert "public void Tick(float deltaTime)" in code(INTRO)


def test_the_gate_is_reusable_rather_than_private():
    """Phase 4's disconnect handling and any pause menu need the same lock. A
    second gate would be a second thing to keep in step."""
    assert "public void ApplyControl(" in code(INTRO)


def test_the_intro_is_retrievable(monkeypatch):
    monkeypatch.setattr(symbol_scanner, "CONFIG_PATH", REAL_ROOT / "config" / "agent.config.json")
    symbols = {s["name"]: s for s in symbol_scanner.rebuild_symbol_index(REAL_ROOT)["symbols"]}
    assert symbols["IntroSequenceController"]["namespace"] == "JetFighter.Player"


def test_the_not_before_half_is_asserted_continuously():
    """Sampled once mid-countdown, this passes on a sequence that unlocks
    early for a single frame."""
    source = (TESTS / "EditMode" / "IntroSequenceTests.cs").read_text()
    assert "NothingIsControllableAtAnyPointBeforeGo" in source
    assert "AssertLocked" in source
    assert "ControlArrivesExactlyAtGo" in source


def test_the_seed_node_matches_the_traceability_row():
    seeds = json.loads((REAL_ROOT / "knowledge" / "seeds.json").read_text())
    node = next(n for n in seeds["nodes"] if n["id"] == "phase2_intro_sequence")
    assert "IntroSequenceController" in node["symbols"]

    row = [l for l in (REAL_ROOT / "docs" / "TRACEABILITY.md").read_text().splitlines()
           if "`phase2/intro-sequence`" in l and l.startswith("|")]
    assert len(row) == 1 and row[0].startswith("| [x] |")
