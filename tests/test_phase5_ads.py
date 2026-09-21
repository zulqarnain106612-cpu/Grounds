"""Cycle 5, branch phase5/ads-integration.

Criterion: the rewarded continue revives the run **only after ad completion**
(not on ad *start* or skip), and the interstitial respects `RemoveAdsFlag`.

The first half is the bug that ships. On a fast test device the reward callback
and the close callback arrive close enough together that a human watching
cannot tell which one granted the continue -- so the distinction has to be
mechanical rather than a matter of timing.
"""
from __future__ import annotations

import json
from pathlib import Path

import pytest

from gateway import symbol_scanner
from tests._csharp import code

REAL_ROOT = Path(__file__).resolve().parent.parent
ECONOMY = REAL_ROOT / "Assets" / "_Game" / "Scripts" / "Economy"
ADS = ECONOMY / "AdsManager.cs"
FLAG = ECONOMY / "RemoveAdsFlag.cs"
CONTINUE = REAL_ROOT / "Assets" / "_Game" / "Scripts" / "UI" / "ContinueOnDeathUI.cs"


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


def test_the_callback_reports_an_outcome_not_just_completion():
    """A caller physically cannot treat a skip as a completion if the callback
    hands it the difference."""
    source = code(ADS)
    assert "enum AdOutcome" in source
    for outcome in ("Completed", "Skipped", "Failed", "NotAvailable"):
        assert outcome in source
    assert "void ShowRewardedAd(Action<AdOutcome> onFinished)" in source


def test_the_continue_is_granted_only_on_completion():
    body = _method_body(CONTINUE, "public void AcceptOffer()")
    assert "outcome != AdsManager.AdOutcome.Completed" in body
    assert body.index("OnContinueDeclined") < body.index("OnContinueGranted")


def test_the_continue_is_counted_on_completion_not_on_start():
    """Counting at start burns the player's one continue on an ad that failed
    halfway through."""
    body = _method_body(CONTINUE, "public void AcceptOffer()")
    granted = body.index("continuesUsed++")
    assert body.index("outcome != AdsManager.AdOutcome.Completed") < granted


def test_a_double_callback_grants_one_reward():
    """Several SDKs call back twice on an orientation change mid-ad."""
    body = _method_body(ADS, "public void ShowRewardedAd(Action<AdOutcome> onFinished)")
    assert "answered" in body


def test_not_available_is_distinct_from_failed():
    """The UI offers a different fallback for "try again" than for "no ad to
    show"."""
    body = _method_body(ADS, "public void ShowRewardedAd(Action<AdOutcome> onFinished)")
    assert "AdOutcome.NotAvailable" in body


def test_the_entitlement_is_checked_in_one_place():
    """A call site that forgot is an ad shown to someone who paid not to see
    one -- a refund request and a review."""
    body = _method_body(ADS, "public bool ShowInterstitial(Action onClosed = null)")
    assert "RemoveAdsFlag.IsActive" in body
    assert code(CONTINUE).count("RemoveAdsFlag") == 0, \
        "a second call site checks the entitlement itself"


def test_suppression_still_runs_the_closed_callback():
    """A caller waiting for a callback that never comes hangs on the game-over
    screen."""
    body = _method_body(ADS, "public bool ShowInterstitial(Action onClosed = null)")
    suppressed = body.index("InterstitialsSuppressed++")
    assert "onClosed?.Invoke()" in body[suppressed:suppressed + 200]


def test_rewarded_ads_are_not_suppressed_by_remove_ads():
    """Remove-ads buys freedom from interruption, not from an ad the player
    deliberately chose to watch for a reward."""
    body = _method_body(ADS, "public void ShowRewardedAd(Action<AdOutcome> onFinished)")
    assert "RemoveAdsFlag" not in body


def test_the_entitlement_is_not_in_the_wallet_save():
    """The wallet save is the file most likely to be rewritten as the economy
    grows, and a migration bug there would resell an entitlement the player
    already paid for."""
    source = code(FLAG)
    assert 'FileName = "entitlements.json"' in source
    assert "SaveService" not in source


def test_the_entitlement_write_is_atomic():
    """Same reason as the wallet: on a phone the process is killed at the
    OS's convenience."""
    body = _method_body(FLAG, "public static bool Grant(string transactionId = null)")
    assert ".tmp" in body
    assert "File.Move(temporary, path)" in body


def test_the_entitlement_is_set_in_memory_even_if_the_write_fails():
    """A player who paid and then saw an ad because a disk write failed has
    been charged for nothing; the retry can happen next launch."""
    body = _method_body(FLAG, "public static bool Grant(string transactionId = null)")
    assert body.index("cached = true") < body.index("try")


def test_nothing_clears_the_entitlement_except_an_explicit_reset():
    """"We could not read the file" and "you did not buy it" must not produce
    the same behaviour, and no error path may downgrade an entitlement."""
    source = code(FLAG)
    assert source.count("cached = false") == 0
    assert "public static void Reset()" in source


def test_the_entitlement_read_is_cached():
    """It is checked before every interstitial, and a file read per game over
    is a stall the player feels at the worst moment."""
    source = code(FLAG)
    assert "cached.HasValue" in source


def test_the_ad_sdk_is_behind_a_seam():
    """The roadmap deliberately deferred choosing one to this phase. Behind
    the seam the choice stays deferred -- swapping networks happens for revenue
    reasons, not technical ones."""
    source = code(ADS)
    assert "public interface IAdBackend" in source
    for sdk in ("UnityEngine.Advertisements", "GoogleMobileAds", "IronSource", "AppLovin"):
        assert sdk not in source


@pytest.mark.parametrize("name", ["AdsManager", "RemoveAdsFlag"])
def test_the_ads_types_are_retrievable(monkeypatch, name):
    monkeypatch.setattr(symbol_scanner, "CONFIG_PATH", REAL_ROOT / "config" / "agent.config.json")
    symbols = {s["name"]: s for s in symbol_scanner.rebuild_symbol_index(REAL_ROOT)["symbols"]}
    assert symbols[name]["namespace"] == "JetFighter.Economy"


def test_both_halves_of_the_criterion_are_asserted_in_csharp():
    source = (REAL_ROOT / "Assets" / "_Game" / "Tests" / "EditMode"
              / "AdsIntegrationTests.cs").read_text()
    for case in ("TheRunIsNotRevivedWhenTheAdStarts", "TheRunIsRevivedOnlyOnCompletion",
                 "ASkippedAdDoesNotRevive", "RemoveAdsSuppressesInterstitials",
                 "SuppressionIsPermanentAcrossRelaunches", "ADoubleCallbackGrantsOneRewardNotTwo"):
        assert case in source


def test_the_seed_node_matches_the_traceability_row():
    seeds = json.loads((REAL_ROOT / "knowledge" / "seeds.json").read_text())
    node = next(n for n in seeds["nodes"] if n["id"] == "phase5_ads_integration")
    assert {"AdsManager", "RemoveAdsFlag", "ContinueOnDeathUI"} <= set(node["symbols"])

    row = [l for l in (REAL_ROOT / "docs" / "TRACEABILITY.md").read_text().splitlines()
           if "`phase5/ads-integration`" in l and l.startswith("|")]
    assert len(row) == 1 and row[0].startswith("| [x] |")
