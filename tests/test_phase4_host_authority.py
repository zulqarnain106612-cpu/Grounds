"""Cycle 4, branch phase4/host-authoritative-enemies (ADR-005).

Criterion: "No enemy-HP divergence over loopback" -- the spec's fuller version
is that both instances show identical enemy health *at all times*, because the
guest never simulates independently.

"Never simulates independently" is stronger than "usually agrees", and it is
the difference between a design that cannot diverge and one that happens not
to today.
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
AUTHORITY = NETWORK / "HostAuthority.cs"
ENEMY_STATE = NETWORK / "NetworkedEnemyState.cs"
HEALTH = SCRIPTS / "Enemy" / "EnemyHealth.cs"


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
    offenders = []
    for path in SCRIPTS.rglob("*.cs"):
        if path.parent.name == "Network":
            continue
        source = code(path)
        for name in ("INetworkTransport", "LocalLoopbackTransport", "GameKitTransport", "GKMatch"):
            if name in source:
                offenders.append(f"{path.relative_to(SCRIPTS)}: {name}")
    assert not offenders, f"gameplay code names the transport: {offenders}"


def test_the_election_is_deterministic_and_message_free():
    """A 2-player match does not need an election protocol, and an election is
    one more thing that can end with both peers believing they won -- a desync
    with no symptom until the healths diverge."""
    source = code(AUTHORITY)
    assert "CompareOrdinal" in source
    for negotiation in ("SendState", "OnStateReceived", "await", "Coroutine"):
        assert negotiation not in source


def test_the_comparison_is_ordinal_not_culture_aware():
    """The same two ids must resolve identically on every device, and
    culture-aware ordering of the same strings genuinely differs by locale."""
    source = code(AUTHORITY)
    assert "string.Compare(" not in source
    assert "CompareTo" not in source


def test_a_tie_produces_no_host_rather_than_two():
    """Both claiming host is the worst outcome; both declining is merely a
    dead match."""
    body = _method_body(AUTHORITY, "public static bool IsHost(string localPeerId, string remotePeerId)")
    assert "order == 0" in body
    assert "return false" in body


def test_a_solo_session_hosts():
    """Otherwise enemies never spawn while waiting for a peer."""
    body = _method_body(AUTHORITY, "public static bool IsHost(string localPeerId, string remotePeerId)")
    assert "IsNullOrEmpty(remotePeerId)" in body
    assert "return true" in body


def test_health_travels_absolute_not_as_a_delta():
    """A lost delta leaves the guest permanently wrong by that much and
    nothing later corrects it; an absolute value self-heals on the next
    packet. That single choice is most of what makes zero divergence
    achievable over a lossy link."""
    source = code(ENEMY_STATE)
    assert "currentHealth" in source
    for delta in ("damageDelta", "healthDelta", "ApplyDamage("):
        assert delta not in source, f"enemy state sends a delta ({delta})"


def test_the_guest_never_broadcasts_enemy_state():
    """Two peers overwriting each other is the exact failure this design
    exists to make impossible."""
    for signature in ("public void Tick(float deltaTime)", "public void BroadcastAll()"):
        body = _method_body(ENEMY_STATE, signature)
        assert "isHost" in body, f"{signature} does not gate on being the host"


def test_the_host_ignores_incoming_enemy_state():
    """Its own echo, or a guest that started sending, would overwrite the
    truth it owns."""
    body = _method_body(ENEMY_STATE, "private void HandlePayload(byte[] payload)")
    assert "isHost ||" in body


def test_applied_health_does_not_fire_death_or_roll_a_drop():
    """Only the host rolls. A guest rolling its own would produce different
    loot from the same kill, and the guest never witnessed the damage event
    anyway."""
    body = _method_body(HEALTH, "public void SetNetworkedHealth(float current, float max)")
    assert "OnDied" not in body
    assert "RollDrop" not in body
    assert "OnDamaged.Invoke" in body, "the health bar has nothing to update from"


def test_ids_are_assigned_by_the_host_not_derived():
    """Two peers agreeing on a spawn ordering is the same problem as agreeing
    on state, one level down."""
    assert "public int Register(EnemyHealth enemy, int enemyId = 0)" in code(ENEMY_STATE)


def test_an_unknown_enemy_packet_is_counted():
    """A persistently rising count means spawn replication is behind, which
    would otherwise show up only as enemies appearing late."""
    body = _method_body(ENEMY_STATE, "public void Apply(EnemyStatePayload state)")
    assert "UnknownEnemyPackets++" in body


def test_malformed_enemy_state_is_rejected():
    body = _method_body(ENEMY_STATE, "public static bool TryDecode(byte[] payload, out EnemyStatePayload state)")
    assert "payload.Length < PayloadSize" in body
    assert "IsNaN" in body
    assert "id <= 0" in body


@pytest.mark.parametrize("name", ["HostAuthority", "NetworkedEnemyState"])
def test_the_authority_types_are_retrievable(monkeypatch, name):
    monkeypatch.setattr(symbol_scanner, "CONFIG_PATH", REAL_ROOT / "config" / "agent.config.json")
    symbols = {s["name"]: s for s in symbol_scanner.rebuild_symbol_index(REAL_ROOT)["symbols"]}
    assert symbols[name]["namespace"] == "JetFighter.Network"


def test_divergence_is_checked_every_tick_not_after_settling():
    """A check after a settled sync passes on a design that diverges for half
    a second every time -- which is precisely long enough for a player to
    notice."""
    source = (REAL_ROOT / "Assets" / "_Game" / "Tests" / "EditMode"
              / "HostAuthorityTests.cs").read_text()
    assert "HealthNeverDivergesAcrossAWholeEngagement" in source
    assert "worst = Mathf.Max(worst" in source
    assert "LocalGuestDamageIsOverwrittenByTheNextPacket" in source


def test_the_seed_node_matches_the_traceability_row():
    seeds = json.loads((REAL_ROOT / "knowledge" / "seeds.json").read_text())
    node = next(n for n in seeds["nodes"] if n["id"] == "phase4_host_authoritative_enemies")
    assert {"HostAuthority", "NetworkedEnemyState"} <= set(node["symbols"])

    row = [l for l in (REAL_ROOT / "docs" / "TRACEABILITY.md").read_text().splitlines()
           if "`phase4/host-authoritative-enemies`" in l and l.startswith("|")]
    assert len(row) == 1 and row[0].startswith("| [x] |")
