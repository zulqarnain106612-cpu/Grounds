"""Cycle 6, branch phase6/analytics-instrumentation.

Criterion: every listed event appears in the Firebase dashboard during a real
run.

A dashboard check is slow, manual, and only possible after a build ships -- and
the events that never fire are precisely the ones nobody notices, because a
missing event looks exactly like a feature nobody used. So the *wiring* is
asserted here, and the dashboard visit confirms the pipe rather than
discovering the missing call.
"""
from __future__ import annotations

import json
from pathlib import Path

import pytest

from gateway import symbol_scanner
from tests._csharp import code

REAL_ROOT = Path(__file__).resolve().parent.parent
SCRIPTS = REAL_ROOT / "Assets" / "_Game" / "Scripts"
ANALYTICS = SCRIPTS / "Analytics"
SERVICE = ANALYTICS / "AnalyticsService.cs"
EVENTS = ANALYTICS / "AnalyticsEvents.cs"
RUN = ANALYTICS / "RunAnalytics.cs"

# The spec's event set, and where each is wired.
WIRING = {
    "RunStart": SCRIPTS / "Player" / "IntroSequenceController.cs",
    "RunEnd": RUN,
    "DeathCause": RUN,
    "PowerUpCollected": SCRIPTS / "PowerUp" / "PowerUpController.cs",
    "IapPurchase": SCRIPTS / "Economy" / "IAPManager.cs",
    "AdWatched": SCRIPTS / "Economy" / "AdsManager.cs",
    "DifficultyTierReached": SCRIPTS / "Enemy" / "DifficultyManager.cs",
}


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


@pytest.mark.parametrize("event,path", sorted(WIRING.items()), ids=lambda v: getattr(v, "name", str(v)))
def test_every_listed_event_is_actually_wired(event, path):
    """The criterion, made checkable before a build ships. A missing event
    looks exactly like a feature nobody used."""
    assert f"AnalyticsEvents.{event}" in code(path), \
        f"{event} is declared but never sent from {path.name}"


def test_the_spec_lists_seven_events_and_all_seven_exist():
    source = code(EVENTS)
    for name in ("run_start", "run_end", "death_cause", "powerup_collected",
                 "iap_purchase", "ad_watched", "difficulty_tier_reached"):
        assert f'"{name}"' in source


def test_names_are_constants_not_literals_at_call_sites():
    """An analytics typo does not fail and does not warn. It produces a
    dashboard that looks complete and is missing a funnel step, discovered
    weeks later when someone tries to answer a question."""
    for path in WIRING.values():
        source = code(path)
        for literal in ('"run_start"', '"run_end"', '"iap_purchase"', '"ad_watched"'):
            assert literal not in source, f"{path.name} uses a string literal event name"


def test_names_are_validated_before_sending():
    """Firebase drops an over-long or malformed name silently."""
    body = _method_body(SERVICE, "public static bool IsValidName(string name)")
    assert "MaxNameLength" in body
    assert "char.IsLetter(name[0])" in body
    assert "char.IsUpper(c)" in body, \
        "run_start and Run_Start would be two events on the dashboard"


def test_a_dropped_event_is_counted():
    """Counting is the difference between a bug and a mystery."""
    assert "public static int DroppedEvents" in code(SERVICE)


def test_analytics_never_throws_into_gameplay():
    """An analytics failure that takes down a run is worse than a lost data
    point, and it is the failure that makes teams rip analytics out."""
    body = _method_body(SERVICE, "public static bool LogEvent(string name, IReadOnlyDictionary<string, object> parameters = null)")
    assert "catch (Exception" in body


def test_no_backend_is_not_a_failure():
    """Analytics is absent in the editor and in a build with consent withheld,
    and neither is a bug."""
    body = _method_body(SERVICE, "public static bool LogEvent(string name, IReadOnlyDictionary<string, object> parameters = null)")
    assert "backend == null" in body
    dropped_before_null = body.index("backend == null")
    assert "DroppedEvents++" not in body[dropped_before_null:dropped_before_null + 120]


def test_the_sdk_is_behind_a_seam():
    source = code(SERVICE)
    assert "public interface IAnalyticsBackend" in source
    for sdk in ("Firebase.Analytics", "FirebaseAnalytics", "Parameter["):
        assert sdk not in source


def test_the_tier_event_fires_once_per_tier_not_per_spawn():
    """An event per spawn would be tens of thousands per session -- past
    Firebase's daily limits and useless besides."""
    body = _method_body(SCRIPTS / "Enemy" / "DifficultyManager.cs", "public void ReportTierIfNew(float playerPowerLevel)")
    assert "tier <= highestTierReported" in body


def test_run_end_and_death_cause_are_separate_events():
    """They answer different questions -- how long runs last versus what kills
    people -- and merging them makes every retention query filter on a death
    reason it does not care about."""
    body = _method_body(RUN, "public bool EndRun(string cause, int score)")
    assert "AnalyticsEvents.RunEnd" in body
    assert "AnalyticsEvents.DeathCause" in body


def test_a_run_can_only_end_once():
    """A game-over screen reachable twice -- which a rewarded continue
    produces -- would double every run-length statistic."""
    body = _method_body(RUN, "public bool EndRun(string cause, int score)")
    assert "!running || ended" in body


def test_analytics_is_additive_to_gameplay():
    """Removing it must change no behaviour, which is the property that lets
    it be added late and removed if consent is withheld."""
    source = code(RUN)
    assert "MonoBehaviour" in source
    for gameplay in ("JetController", "EnemyHealth", "Wallet"):
        assert gameplay not in source


def test_run_start_fires_when_control_is_handed_over():
    """A run the player never got control of is not a run, and counting it
    would inflate every funnel that starts here."""
    body = _method_body(SCRIPTS / "Player" / "IntroSequenceController.cs", "private void Enter(State next)")
    assert "AnalyticsEvents.RunStart" in body
    assert "State.PlayerControl" in body


def test_ad_outcomes_are_all_logged_not_just_completions():
    """Completions alone cannot tell a placement nobody accepts from one
    nobody is offered."""
    body = _method_body(SCRIPTS / "Economy" / "AdsManager.cs", "public void ShowRewardedAd(Action<AdOutcome> onFinished)")
    assert "AnalyticsEvents.AdWatched" in body
    assert "outcome.ToString()" in body


@pytest.mark.parametrize("name", ["AnalyticsService", "AnalyticsEvents", "RunAnalytics"])
def test_the_analytics_types_are_retrievable(monkeypatch, name):
    monkeypatch.setattr(symbol_scanner, "CONFIG_PATH", REAL_ROOT / "config" / "agent.config.json")
    symbols = {s["name"]: s for s in symbol_scanner.rebuild_symbol_index(REAL_ROOT)["symbols"]}
    assert symbols[name]["namespace"] == "JetFighter.Analytics"


def test_the_seed_node_matches_the_traceability_row():
    seeds = json.loads((REAL_ROOT / "knowledge" / "seeds.json").read_text())
    node = next(n for n in seeds["nodes"] if n["id"] == "phase6_analytics_instrumentation")
    assert {"AnalyticsService", "AnalyticsEvents"} <= set(node["symbols"])

    row = [l for l in (REAL_ROOT / "docs" / "TRACEABILITY.md").read_text().splitlines()
           if "`phase6/analytics-instrumentation`" in l and l.startswith("|")]
    assert len(row) == 1 and row[0].startswith("| [x] |")
