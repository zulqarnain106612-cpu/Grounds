"""Cycle 4, branch phase4/gamekit-transport (ADR-005).

Criterion, verbatim from the matrix: "**Zero gameplay-code changes** when
swapping transport -- this is the abstraction's own test."

That is a claim about a diff, which is exactly the kind of claim that decays
the moment nobody is checking. So it is enforced two ways: a scan proving no
file outside `Network/` names a transport at all, and a C# contract test that
runs the same gameplay component against both implementations.

A git-diff check ("this commit changed no gameplay file") was the obvious
third, and is deliberately absent: it asserts something true only of *this*
commit, so it would fail on every later cell that legitimately touches
gameplay -- a test that has to be deleted the moment it first fires is worse
than no test. The static scan says the same thing permanently: if nothing
outside `Network/` can name a transport, swapping one cannot require a change
there.
"""
from __future__ import annotations

import json
from pathlib import Path

import pytest

from gateway import symbol_scanner
from tests._csharp import code

REAL_ROOT = Path(__file__).resolve().parent.parent
SCRIPTS = REAL_ROOT / "Assets" / "_Game" / "Scripts"
NETWORK = SCRIPTS / "Network"
GAMEKIT = NETWORK / "GameKitTransport.cs"
MATCHMAKER = NETWORK / "NetworkMatchmaker.cs"

TRANSPORT_NAMES = ("INetworkTransport", "LocalLoopbackTransport", "GameKitTransport",
                   "GKMatch", "NetworkMatchmaker", "TransportState")


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


def test_no_file_outside_network_names_a_transport():
    """The criterion, as a property rather than a diff review. If this holds,
    swapping the implementation cannot require a change outside Network/,
    because nothing outside Network/ can tell which one it is."""
    offenders = []
    for path in SCRIPTS.rglob("*.cs"):
        if path.parent.name == "Network":
            continue
        source = code(path)
        for name in TRANSPORT_NAMES:
            if name in source:
                offenders.append(f"{path.relative_to(SCRIPTS)}: {name}")
    assert not offenders, f"the swap would require changes outside Network/: {offenders}"


def test_gamekit_is_behind_one_narrow_interface():
    """This is the one component CI can never exercise end to end, so the
    alternative is a class only ever tested by two people holding phones."""
    source = code(GAMEKIT)
    assert "public interface INativeMatch" in source
    assert "UnityEngine.iOS" not in source
    assert "using Apple" not in source


def test_the_plugin_dependency_is_injected_not_referenced():
    """The spec flags Apple's package as an unmaintained-package risk. Injected,
    an abandoned plugin costs one INativeMatch implementation; referenced, it
    is a build failure."""
    body = _method_body(MATCHMAKER, "private INetworkTransport Create()")
    assert "GameKitFactory?.Invoke()" in body
    assert "new GameKitTransport(" not in code(MATCHMAKER)


def test_a_missing_plugin_fails_the_match_not_the_game():
    """A player with no Game Center still gets a working single-player game."""
    body = _method_body(MATCHMAKER, "public INetworkTransport Begin()")
    assert "OnMatchFailed" in body
    assert "return null" in body


def test_the_transport_choice_lives_in_exactly_one_place():
    """Which is what makes the swap a one-line change rather than a search."""
    constructions = sum(
        code(path).count("new LocalLoopbackTransport(")
        for path in SCRIPTS.rglob("*.cs") if path.name != "LocalLoopbackTransport.cs")
    assert constructions <= 1, "more than one place decides which transport the session uses"


def test_the_sender_id_is_not_forwarded_to_gameplay():
    """Passing it upward would let gameplay grow logic keyed on GameKit's
    identifier format -- the leak this abstraction exists to prevent."""
    body = _method_body(GAMEKIT, "private void HandleData(byte[] payload, string fromPlayerId)")
    assert "fromPlayerId" not in body.split("{", 1)[1], "the sender id is passed on"


def test_a_native_send_that_throws_becomes_a_failed_send():
    """The match can tear down between the state check and the call. Gameplay
    already handles a false return; it does not handle an exception from the
    middle of a frame."""
    body = _method_body(GAMEKIT, "public bool SendState(byte[] payload)")
    assert "catch (Exception" in body
    assert "return false" in body


def test_a_missing_local_id_reads_empty_rather_than_null():
    """HostAuthority treats an empty local id as "not host", which is the safe
    answer before a match exists. Null would be the same answer by accident."""
    assert "?? string.Empty" in code(GAMEKIT)


def test_a_missing_native_match_is_refused_at_construction():
    """Better than a transport that looks fine and fails on the first send."""
    assert "ArgumentNullException" in code(GAMEKIT)


@pytest.mark.parametrize("name", ["GameKitTransport", "NetworkMatchmaker"])
def test_the_gamekit_types_are_retrievable(monkeypatch, name):
    monkeypatch.setattr(symbol_scanner, "CONFIG_PATH", REAL_ROOT / "config" / "agent.config.json")
    symbols = {s["name"]: s for s in symbol_scanner.rebuild_symbol_index(REAL_ROOT)["symbols"]}
    assert symbols[name]["namespace"] == "JetFighter.Network"


def test_both_transports_are_held_to_one_contract_test():
    """The strongest form of the criterion: the same assertions, written
    against the interface, run against both implementations -- so a behaviour
    that differs fails regardless of which one is swapped in."""
    source = (REAL_ROOT / "Assets" / "_Game" / "Tests" / "EditMode"
              / "GameKitTransportTests.cs").read_text()
    assert "AssertHonoursTheContract" in source
    assert "TheLoopbackTransportHonoursTheContract" in source
    assert "TheGameKitTransportHonoursTheContract" in source
    assert "GameplayCodeWorksAgainstEitherTransport" in source


def test_the_seed_node_matches_the_traceability_row():
    seeds = json.loads((REAL_ROOT / "knowledge" / "seeds.json").read_text())
    node = next(n for n in seeds["nodes"] if n["id"] == "phase4_gamekit_transport")
    assert {"GameKitTransport", "NetworkMatchmaker"} <= set(node["symbols"])

    row = [l for l in (REAL_ROOT / "docs" / "TRACEABILITY.md").read_text().splitlines()
           if "`phase4/gamekit-transport`" in l and l.startswith("|")]
    assert len(row) == 1 and row[0].startswith("| [x] |")
