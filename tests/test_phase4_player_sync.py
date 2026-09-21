"""Cycle 4, branch phase4/player-state-sync (ADR-005).

Criterion: two loopback instances see each other's jet move correctly at the
target sync rate -- and the row adds "passes over loopback before any device
is involved", which is the point of having built the loopback first.
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
SYNC = NETWORK / "NetworkedPlayerState.cs"
MESSAGE = NETWORK / "NetworkMessage.cs"


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


def test_gameplay_still_names_no_transport():
    """Cell 4's acceptance test is the swap, so this has to keep holding as
    the network grows -- and this is the cell that adds the first real
    consumer."""
    offenders = []
    for path in SCRIPTS.rglob("*.cs"):
        if path.parent.name == "Network":
            continue
        source = code(path)
        for name in ("INetworkTransport", "LocalLoopbackTransport", "GameKitTransport", "GKMatch"):
            if name in source:
                offenders.append(f"{path.relative_to(SCRIPTS)}: {name}")
    assert not offenders, f"gameplay code names the transport: {offenders}"


def test_every_message_carries_a_type_tag():
    """Without it the receiver infers the type from the length, which works
    exactly until two message types are the same size -- and then produces
    garbage state rather than an error."""
    source = code(MESSAGE)
    assert "enum MessageType : byte" in source
    assert "buffer[0] = (byte)MessageType.PlayerState" in source


def test_the_wire_format_is_binary_not_json():
    """At 20Hz for a whole session this runs thousands of times. JSON of a
    Vector3 is roughly ten times the bytes and allocates a string per packet,
    and GKMatch is byte-oriented anyway."""
    source = code(MESSAGE)
    assert "JsonUtility" not in source
    assert "BitConverter" in source


def test_a_malformed_packet_is_rejected_rather_than_half_applied():
    """A truncated send or a build-version mismatch is a normal event on a
    real link. Throwing would take the run down over one bad packet in
    thousands."""
    body = _method_body(MESSAGE, "public static bool TryDecodePlayerState(")
    assert "return false" in body
    assert "payload.Length < PlayerStateSize" in body


def test_nan_is_rejected_at_the_boundary():
    """NaN assigned to a Transform poisons every subsequent physics query on
    that object. Rejecting costs one frame of remote position; accepting
    corrupts the scene."""
    body = _method_body(MESSAGE, "public static bool TryDecodePlayerState(")
    assert "IsFinite" in body


def test_each_device_is_authoritative_for_its_own_jet():
    """Which is why player sync is simpler than enemy sync: there is no
    arbitration, only transmission."""
    source = code(SYNC)
    assert "localJet" in source and "remoteJet" in source
    body = _method_body(SYNC, "public void Broadcast()")
    assert "localJet.position" in body
    assert "remoteJet" not in body


def test_broadcasts_are_rate_limited_not_per_frame():
    """Sending per frame is the easiest accidental way to quadruple traffic."""
    body = _method_body(SYNC, "private void TickSend(float deltaTime)")
    assert "sendTimer" in body
    assert "1f / sendRateHz" in body


def test_a_missed_broadcast_is_not_made_up_for():
    """Unlike the gun's cooldown. Sending four packets back to back after a
    hitch wastes bandwidth to deliver three positions the remote will never
    render."""
    body = _method_body(SYNC, "private void TickSend(float deltaTime)")
    assert "sendTimer = 1f / sendRateHz" in body
    assert "sendTimer +=" not in body


def test_stale_packets_are_rejected():
    """UDP-style transports reorder. Applying an older packet after a newer
    one snaps the remote jet backwards, which reads as rubber-banding and gets
    blamed on latency."""
    body = _method_body(SYNC, "private void ApplyPlayerState(byte[] payload)")
    assert "state.sequence <= lastAppliedSequence" in body
    assert "RejectedAsStale++" in body


def test_the_first_packet_snaps():
    """The remote jet starts at the origin; easing from there flies it across
    the level in front of the player."""
    body = _method_body(SYNC, "private void ApplyPlayerState(byte[] payload)")
    assert "!hasRemoteState" in body
    assert "remoteJet.position = state.position" in body


def test_there_is_no_speculative_prediction():
    """The spec is explicit that prediction and interpolation are not
    justified until playtesting shows they are needed -- and a rollback bug
    looks exactly like a physics bug while being far harder to isolate."""
    source = code(SYNC)
    for shape in ("Predict", "Rollback", "InputBuffer", "Reconcile"):
        assert shape not in source


def test_remote_smoothing_is_frame_rate_independent():
    """A rate that depended on tick length would make the remote jet visibly
    smoother on one device than the other."""
    body = _method_body(SYNC, "private void TickRemote(float deltaTime)")
    assert "Mathf.Exp(-smoothing * deltaTime)" in body


def test_rebinding_unsubscribes_first():
    """A reconnect that left the old subscription would apply every packet
    twice, and the second application is always the stale one."""
    body = _method_body(SYNC, "public void Bind(INetworkTransport newTransport)")
    assert body.index("-= HandlePayload") < body.index("+= HandlePayload")


def test_unknown_message_types_are_ignored_not_errors():
    """Enemy state is another component's, and an unknown tag is a peer on a
    newer build."""
    body = _method_body(SYNC, "private void HandlePayload(byte[] payload)")
    assert "default:" in body


def test_the_sync_types_are_retrievable(monkeypatch):
    monkeypatch.setattr(symbol_scanner, "CONFIG_PATH", REAL_ROOT / "config" / "agent.config.json")
    symbols = {s["name"]: s for s in symbol_scanner.rebuild_symbol_index(REAL_ROOT)["symbols"]}
    assert symbols["NetworkedPlayerState"]["namespace"] == "JetFighter.Network"
    assert symbols["NetworkMessage"]["namespace"] == "JetFighter.Network"


def test_the_rate_claim_is_asserted_over_loopback():
    """The row's "passes over loopback before any device is involved"."""
    source = (REAL_ROOT / "Assets" / "_Game" / "Tests" / "EditMode"
              / "PlayerStateSyncTests.cs").read_text()
    assert "BroadcastsHappenAtTheConfiguredRate" in source
    assert "TheRemoteJetFollowsTheLocalOne" in source
    assert "CreatePair" in source


def test_the_seed_node_matches_the_traceability_row():
    seeds = json.loads((REAL_ROOT / "knowledge" / "seeds.json").read_text())
    node = next(n for n in seeds["nodes"] if n["id"] == "phase4_player_state_sync")
    assert "NetworkedPlayerState" in node["symbols"]

    row = [l for l in (REAL_ROOT / "docs" / "TRACEABILITY.md").read_text().splitlines()
           if "`phase4/player-state-sync`" in l and l.startswith("|")]
    assert len(row) == 1 and row[0].startswith("| [x] |")
