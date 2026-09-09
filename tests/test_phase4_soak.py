"""Cycle 4, branch phase4/network-soak-test (roadmap risk R4).

Criterion: two physical devices, a full co-op run, no visible desync, and a
brief interruption crashing neither client.

Two devices cannot be a CI assertion, so this cell splits the way CLAUDE.md
requires. What ships is the automated half -- latency, jitter, loss and
repeated interruptions driven deterministically for far longer than anyone
will hold two phones -- plus a written device procedure that produces
evidence.

**The row is deliberately left unticked.** The automated suite passing is not
the row's criterion, and ticking on that basis would be exactly the green lie
the Cycle 0 test gate was built to prevent. These tests hold the harness to
the shape that makes the device pass a confirmation rather than the evidence.
"""
from __future__ import annotations

import json
from pathlib import Path

import pytest

from tests._csharp import code

REAL_ROOT = Path(__file__).resolve().parent.parent
LOOPBACK = REAL_ROOT / "Assets" / "_Game" / "Scripts" / "Network" / "LocalLoopbackTransport.cs"
SOAK = REAL_ROOT / "Assets" / "_Game" / "Tests" / "EditMode" / "NetworkSoakTests.cs"
PROCEDURE = REAL_ROOT / "docs" / "DEVICE_SOAK_PROCEDURE.md"


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


def test_the_row_is_not_ticked():
    """The one assertion this file exists for. Two devices are the criterion;
    a green suite is not, and a row ticked on a green suite is worth less than
    an honest open one."""
    row = [l for l in (REAL_ROOT / "docs" / "TRACEABILITY.md").read_text().splitlines()
           if "`phase4/network-soak-test`" in l and l.startswith("|")]
    assert len(row) == 1
    assert row[0].startswith("| [ ] |"), \
        "the soak row was ticked without a two-device run"
    assert "DEVICE_SOAK_PROCEDURE" in row[0], "the row does not say what is still owed"


def test_the_device_procedure_exists_and_names_its_evidence():
    """A procedure that produces a recollection rather than an artifact is a
    procedure nobody can review."""
    text = PROCEDURE.read_text()
    assert "## Evidence to attach" in text
    for evidence in ("recording", "photograph", "Build number"):
        assert evidence.lower() in text.lower()


def test_the_procedure_says_what_devices_can_show_that_ci_cannot():
    """Otherwise the manual half looks like duplicated effort and gets skipped
    on the first tight week."""
    text = PROCEDURE.read_text()
    assert "Real GameKit" in text
    assert "loopback" in text


def test_the_loopback_can_model_latency_and_jitter():
    """Latency is what separates a soak from a round-trip test: every ordering
    bug in the sync layer needs two messages in flight at once to show up, and
    with instant delivery there is never more than one."""
    source = code(LOOPBACK)
    assert "public int LatencyPumps" in source
    assert "public int JitterPumps" in source


def test_the_delay_model_is_deterministic():
    """A reordering bug that appears one run in five is one nobody will
    believe."""
    source = code(LOOPBACK)
    assert "Random" not in source
    assert "sendCounter % 2" in source


def test_delayed_messages_are_not_dropped_or_reordered_by_accident():
    """Only jitter may reorder. A pump that dropped anything still in flight
    would fake packet loss on top of the loss model."""
    body = _method_body(LOOPBACK, "public int Pump()")
    assert "inbox.Enqueue(pending)" in body
    assert "PumpsRemaining--" in body


def test_an_interruption_still_clears_what_was_in_flight():
    body = _method_body(LOOPBACK, "public void Interrupt()")
    assert "inbox.Clear()" in body


@pytest.mark.parametrize("condition", [
    "AFiveMinuteRunNeverDivergesBeyondOneUnsyncedHit",
    "LatencyDoesNotCauseDivergence",
    "JitterAndReorderingDoNotCauseDivergence",
    "HeavyPacketLossDoesNotCauseDivergence",
    "EverythingAtOnceStillConverges",
    "RepeatedInterruptionsCrashNeitherPeer",
    "StateReconvergesAfterEveryInterruption",
    "MalformedTrafficDuringASoakIsSurvivable",
])
def test_every_condition_the_device_run_samples_is_driven_on_purpose(condition):
    """A device run visits one arbitrary sample of these. The suite visits
    them deliberately, which is what makes the device pass a confirmation."""
    assert condition in SOAK.read_text()


def test_the_long_run_is_actually_long():
    """A soak that runs for two seconds is a unit test with a soak's name."""
    assert "Soak(18000)" in SOAK.read_text(), "the longest run is under five minutes at 60fps"


def test_divergence_is_tracked_every_frame_not_at_the_end():
    """An end-of-run check passes on a design that diverges for half a second
    every time -- which is exactly long enough for a player to notice."""
    body = SOAK.read_text()
    assert "worstDivergence = Mathf.Max(worstDivergence" in body


def test_interruptions_are_repeated_not_singular():
    """The first one working proves only that the first one works."""
    text = SOAK.read_text()
    assert "round < 20" in text
    assert "round < 10" in text


def test_a_disconnected_guest_stops_rather_than_drifting():
    """The failure mode that looks like a working game until the players
    compare screens."""
    assert "ADisconnectedGuestStopsApplyingRatherThanDriftingOnItsOwn" in SOAK.read_text()


def test_traffic_volume_is_asserted_over_the_long_run():
    """A soak is also where an accidental per-frame send shows up."""
    assert "TrafficStaysProportionalToTheSendRateOverALongRun" in SOAK.read_text()


def test_the_seed_node_exists_even_though_the_row_is_open():
    """The harness shipped, so the knowledge node is real. A missing node
    would make phase-scoped retrieval return nothing for the work that does
    exist."""
    seeds = json.loads((REAL_ROOT / "knowledge" / "seeds.json").read_text())
    node = next(n for n in seeds["nodes"] if n["id"] == "phase4_network_soak_test")
    assert "NetworkSoakTests" in node["symbols"] or node["symbols"] == []
    assert "phase4" in node["tags"]
