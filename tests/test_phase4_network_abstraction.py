"""Cycle 4, branch phase4/network-abstraction (ADR-005).

Criterion: the loopback transport round-trips a message with zero
gameplay-code awareness of the transport's implementation.

The second half is the one with teeth, and it is not testable by round-tripping
anything -- it is a property of what gameplay code is allowed to name. Cell 4
proves it by swapping the implementation; this cell is where the property has
to be established, and where it is cheapest to keep.
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
TRANSPORT = NETWORK / "INetworkTransport.cs"
LOOPBACK = NETWORK / "LocalLoopbackTransport.cs"


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


def test_no_gameplay_code_knows_the_network_exists_yet():
    """The abstraction's real test is cell 4's swap, and it can only pass if
    nothing outside Network/ ever named a transport. Establishing that now is
    free; discovering it was violated three cells later is not."""
    offenders = []
    for path in SCRIPTS.rglob("*.cs"):
        if path.parent.name == "Network":
            continue
        source = code(path)
        for name in ("INetworkTransport", "LocalLoopbackTransport", "GameKitTransport", "GKMatch"):
            if name in source:
                offenders.append(f"{path.relative_to(SCRIPTS)}: {name}")
    assert not offenders, f"gameplay code names the transport: {offenders}"


def test_the_transport_surface_is_byte_oriented():
    """GKMatch sends bytes. A transport taking typed messages would own
    serialisation -- the part most likely to change independently of the
    transport, and the part the sync-rate work touches."""
    source = code(TRANSPORT)
    assert "bool SendState(byte[] payload)" in source
    assert "event Action<byte[]> OnStateReceived" in source


def test_the_surface_has_no_async_or_coroutines():
    """Wrapping delivery in a task per message allocates once per packet at
    20Hz for no benefit, and no real transport works that way anyway."""
    source = code(TRANSPORT)
    for shape in ("Task", "async", "IEnumerator", "await"):
        assert shape not in source


def test_connection_state_distinguishes_never_connected_from_dropped():
    """Gameplay reacts differently: a drop closes the control gate and shows a
    message; not-connected-yet is just the lobby."""
    source = code(TRANSPORT)
    for state in ("Disconnected", "Connecting", "Connected", "Failed"):
        assert state in source
    assert "event Action<TransportState> OnStateChanged" in source


def test_sending_while_disconnected_fails_rather_than_queues():
    """At 20Hz a queue that survives a disconnect delivers a burst of stale
    state on reconnect, which looks exactly like a desync."""
    body = _method_body(LOOPBACK, "public bool SendState(byte[] payload)")
    assert "state != TransportState.Connected" in body
    assert "return false" in body


def test_delivery_is_not_synchronous_with_sending():
    """A loopback that delivered inside SendState would let code work that
    reads its own state back the same frame -- which no real transport
    permits, so it would fail only on device."""
    body = _method_body(LOOPBACK, "public bool SendState(byte[] payload)")
    assert "peer.inbox.Enqueue" in body
    assert "OnStateReceived" not in body
    assert "public int Pump()" in code(LOOPBACK)


def test_payloads_are_copied_not_referenced():
    """Reusing one buffer between sends is what anyone does at 20Hz to avoid
    allocating; referencing it mutates a message already in flight."""
    body = _method_body(LOOPBACK, "public bool SendState(byte[] payload)")
    assert "Buffer.BlockCopy" in body


def test_simulated_loss_is_deterministic():
    """A flaky test is worse than no test."""
    source = code(LOOPBACK)
    assert "Random" not in source
    assert "sendCounter" in source


def test_a_lost_packet_is_still_reported_as_sent():
    """A real lossy link does not tell the sender. Code treating a false
    return as "retry" would spin."""
    body = _method_body(LOOPBACK, "public bool SendState(byte[] payload)")
    loss = body.index("DroppedByLoss++")
    assert "return true" in body[loss:body.index("Buffer.BlockCopy")]


def test_an_interruption_can_be_simulated():
    """The soak cell's "a brief interruption crashes neither client" starts
    here, three cells before two devices exist."""
    assert "public void Interrupt()" in code(LOOPBACK)
    body = _method_body(LOOPBACK, "public void Interrupt()")
    assert "inbox.Clear()" in body


def test_peer_ids_are_stable():
    """HostAuthority elects from these in cell 3; a non-deterministic id makes
    the election non-deterministic, which is a desync by construction."""
    assert "string LocalPeerId { get; }" in code(TRANSPORT)
    assert "public string LocalPeerId { get; }" in code(LOOPBACK)


def test_connect_and_disconnect_are_idempotent():
    """A transport that announced a state change on every redundant call would
    have gameplay reacting to drops that did not happen."""
    for method in ("public void Connect()", "public void Disconnect()"):
        body = _method_body(LOOPBACK, method)
        assert "state" in body


@pytest.mark.parametrize("name", ["INetworkTransport", "LocalLoopbackTransport"])
def test_the_network_types_are_retrievable(monkeypatch, name):
    monkeypatch.setattr(symbol_scanner, "CONFIG_PATH", REAL_ROOT / "config" / "agent.config.json")
    symbols = {s["name"]: s for s in symbol_scanner.rebuild_symbol_index(REAL_ROOT)["symbols"]}
    assert symbols[name]["namespace"] == "JetFighter.Network"


def test_the_round_trip_is_asserted_through_the_interface():
    """A test written against the concrete type would pass after cell 4's swap
    and prove nothing."""
    source = (REAL_ROOT / "Assets" / "_Game" / "Tests" / "EditMode"
              / "NetworkTransportTests.cs").read_text()
    assert "INetworkTransport sender = host" in source
    assert "AMessageRoundTripsThroughTheInterface" in source


def test_the_seed_node_matches_the_traceability_row():
    seeds = json.loads((REAL_ROOT / "knowledge" / "seeds.json").read_text())
    node = next(n for n in seeds["nodes"] if n["id"] == "phase4_network_abstraction")
    assert {"INetworkTransport", "LocalLoopbackTransport"} <= set(node["symbols"])

    row = [l for l in (REAL_ROOT / "docs" / "TRACEABILITY.md").read_text().splitlines()
           if "`phase4/network-abstraction`" in l and l.startswith("|")]
    assert len(row) == 1 and row[0].startswith("| [x] |")
    assert "ADR-005" in row[0]
