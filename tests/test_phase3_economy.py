"""Cycle 3, branch phase3/economy-coins (ADR-004).

Criterion: "Adding a third currency requires no `Wallet` change", plus coins
accumulating from both survival and kills and surviving a relaunch.

The currency-generic claim is a property of the source, and it is the one that
erodes: the first time someone needs "just the coin balance" in a hurry, a
`coins` field is the shortest path, and the third currency then costs a save
migration.
"""
from __future__ import annotations

import json
from pathlib import Path

import pytest

from gateway import symbol_scanner
from tests._csharp import code

REAL_ROOT = Path(__file__).resolve().parent.parent
ECONOMY = REAL_ROOT / "Assets" / "_Game" / "Scripts" / "Economy"
WALLET = ECONOMY / "Wallet.cs"
SAVE = ECONOMY / "SaveService.cs"
EARN = ECONOMY / "CoinEarnController.cs"
CURRENCY = ECONOMY / "CurrencyType.cs"


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


def test_the_wallet_names_no_currency():
    """ADR-004's criterion. A `coins` field and a `gems` field would each need
    a sibling for the third, plus a save migration."""
    source = code(WALLET)
    for named in ("coins", "Coins", "gems", "Gems"):
        assert named not in source, f"Wallet names a specific currency: {named}"
    assert "Dictionary<CurrencyType, int>" in source


def test_the_save_format_names_no_currency_either():
    """A named field per currency in the file is the same trap one layer
    down, and that one costs a migration rather than a refactor."""
    source = code(SAVE)
    assert "public string currency;" in source
    for named in ("public int coins", "public int gems"):
        assert named not in source


def test_gems_exist_before_they_are_earned():
    """The roadmap's "architect once", applied literally: Phase 5 adds an IAP
    granting gems and touches no enum, no Wallet and no save format."""
    assert "Gems" in code(CURRENCY)
    assert "Coins" in code(CURRENCY)


def test_the_wallet_is_not_a_monobehaviour():
    """Single responsibility, and it makes the arithmetic testable with no
    scene at all."""
    source = code(WALLET)
    assert ": MonoBehaviour" not in source
    assert "using UnityEngine" not in source


def test_the_wallet_holds_no_persistence_logic():
    """SaveService serialises a wallet; the wallet does not know it is being
    saved, which is what lets Phase 5 swap the store without touching the
    arithmetic."""
    source = code(WALLET)
    for io in ("File.", "JsonUtility", "PlayerPrefs"):
        assert io not in source


def test_spending_is_all_or_nothing():
    """A partial spend leaves the player charged for something they did not
    get."""
    body = _method_body(WALLET, "public bool Spend(CurrencyType type, int amount)")
    assert "current < amount" in body
    assert "return false" in body


def test_non_positive_amounts_are_ignored():
    """An accidental negative credit is how a currency goes missing with no
    spend to explain it."""
    assert "amount <= 0" in _method_body(WALLET, "public void Add(CurrencyType type, int amount)")
    assert "amount <= 0" in _method_body(WALLET, "public bool Spend(CurrencyType type, int amount)")


def test_balances_saturate_rather_than_overflow():
    body = _method_body(WALLET, "public void Add(CurrencyType type, int amount)")
    assert "int.MaxValue" in body


def test_persistence_is_a_file_not_playerprefs():
    """PlayerPrefs is fine for a trivial flag. This is structured,
    economy-critical data that Phase 5 grows with receipts -- and PlayerPrefs
    has no atomicity, so a kill mid-write leaves half-updated keys with no way
    to tell."""
    source = code(SAVE)
    assert "PlayerPrefs" not in source
    assert "persistentDataPath" in source
    assert "JsonUtility" in source


def test_the_write_is_atomic():
    """On a phone the process is killed at the OS's convenience, so an
    interrupted write is the normal case rather than the rare one."""
    body = _method_body(SAVE, "public static bool Save(Wallet wallet)")
    assert ".tmp" in body
    assert "File.Move(temporary, path)" in body


def test_a_failed_save_does_not_take_the_run_down():
    body = _method_body(SAVE, "public static bool Save(Wallet wallet)")
    assert "catch (Exception" in body
    assert "return false" in body


def test_a_corrupt_save_is_survivable():
    """Refusing to start is a worse outcome than a lost balance."""
    body = _method_body(SAVE, "public static bool Load(Wallet wallet)")
    assert "catch (Exception" in body


def test_an_unknown_currency_in_the_save_does_not_discard_the_rest():
    """A save from a newer build, or a hand-edited file."""
    body = _method_body(SAVE, "public static bool Load(Wallet wallet)")
    assert "Enum.TryParse" in body


def test_saving_is_debounced_rather_than_per_coin():
    """A write per kill is a file write several times a second on a phone --
    both a stall and a way to be mid-write when the OS kills the process."""
    source = code(EARN)
    assert "saveIntervalSeconds" in source
    assert "OnApplicationPause" in source, "the most likely last moment before the process dies"
    assert "OnApplicationQuit" in source


def test_survival_earnings_carry_their_remainder():
    """A fractional second dropped every frame costs a visible amount over a
    five-minute run."""
    body = _method_body(EARN, "public void Tick(float deltaTime)")
    assert "survivalRemainder -= wholeSeconds" in body


def test_a_pooled_enemy_cannot_pay_its_bounty_twice():
    body = _method_body(EARN, "public void Track(EnemyHealth enemy)")
    assert body.index("RemoveListener") < body.index("AddListener")


def test_earn_rates_are_data():
    source = code(EARN)
    assert "coinsPerSecondSurvived" in source and "coinsPerKill" in source


@pytest.mark.parametrize("name", ["Wallet", "SaveService", "CoinEarnController"])
def test_the_economy_types_are_retrievable(monkeypatch, name):
    monkeypatch.setattr(symbol_scanner, "CONFIG_PATH", REAL_ROOT / "config" / "agent.config.json")
    symbols = {s["name"]: s for s in symbol_scanner.rebuild_symbol_index(REAL_ROOT)["symbols"]}
    assert symbols[name]["namespace"] == "JetFighter.Economy"


def test_the_relaunch_claim_is_asserted_in_csharp():
    source = (REAL_ROOT / "Assets" / "_Game" / "Tests" / "EditMode" / "EconomyTests.cs").read_text()
    assert "ABalanceSurvivesARelaunch" in source
    assert "EarningsSurviveARelaunch" in source
    assert "CoinsAccumulateFromSurvivalTime" in source
    assert "CoinsAccumulateFromKills" in source


def test_the_seed_node_matches_the_traceability_row():
    seeds = json.loads((REAL_ROOT / "knowledge" / "seeds.json").read_text())
    node = next(n for n in seeds["nodes"] if n["id"] == "phase3_economy_coins")
    assert {"Wallet", "CurrencyType", "SaveService"} <= set(node["symbols"])

    row = [l for l in (REAL_ROOT / "docs" / "TRACEABILITY.md").read_text().splitlines()
           if "`phase3/economy-coins`" in l and l.startswith("|")]
    assert len(row) == 1 and row[0].startswith("| [x] |")
    assert "ADR-004" in row[0]
