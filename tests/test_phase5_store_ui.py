"""Cycle 5, branch phase5/store-ui (ADR-004).

Criterion: both currencies purchase and deduct correctly, and the
insufficient-balance case is handled -- "no negative balances, ever".

Phase 3's `Wallet` already refuses to overspend, so the risk here is not a
missing check. It is a *second* check: a UI that decides affordability itself
and then calls `Spend` has two sources of truth, and the one the player sees is
the one that is wrong.
"""
from __future__ import annotations

import json
from pathlib import Path

import pytest

from gateway import symbol_scanner
from tests._csharp import code

REAL_ROOT = Path(__file__).resolve().parent.parent
ECONOMY = REAL_ROOT / "Assets" / "_Game" / "Scripts" / "Economy"
STORE = ECONOMY / "StoreUI.cs"


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


def test_the_wallet_is_the_only_thing_that_moves_money():
    """Not a comparison here followed by a Spend. Two sources of truth means
    the button can stay enabled for an item the wallet refuses, which the
    player reads as the game taking their coins and giving nothing."""
    body = _method_body(STORE, "public PurchaseResult Purchase(StoreItemDef item)")
    assert "wallet.Spend(item.currency, item.cost)" in body
    assert "GetBalance" not in body, "the purchase path compares balances itself"


def test_affordability_asks_the_wallet_too():
    body = _method_body(STORE, "public bool CanAfford(StoreItemDef item)")
    assert "wallet.GetBalance(item.currency)" in body


def test_there_is_one_purchase_path():
    """A second one that skips a check is how a free item eventually ships."""
    source = code(STORE)
    assert source.count("wallet.Spend(") == 1


def test_both_currencies_use_the_same_code_path():
    """Phase 3's enum is what makes that possible -- a per-currency branch
    here would be the refactor ADR-004 was shaped to avoid."""
    source = code(STORE)
    assert "item.currency" in source
    for named in ("CurrencyType.Coins", "CurrencyType.Gems"):
        assert named not in source, f"the store branches on {named}"


def test_a_refusal_reports_which_refusal():
    """The UI shows a different message for each, and "you cannot buy this"
    with no reason is the message players screenshot."""
    source = code(STORE)
    for reason in ("InsufficientFunds", "AlreadyOwned", "UnknownItem", "NotReady"):
        assert reason in source


def test_ownership_is_not_stored_in_the_wallet():
    """Wallet is currency arithmetic and nothing else (ADR-004's shape); an
    owned-items set is a different concern with a different lifetime."""
    assert "owned" in code(STORE)
    assert "StoreUI" not in code(ECONOMY / "Wallet.cs")


def test_a_cosmetic_cannot_be_repurchased():
    body = _method_body(STORE, "public PurchaseResult Purchase(StoreItemDef item)")
    assert "IsOwned(item) && !item.CanRepurchase" in body


def test_an_item_not_on_the_shelf_is_refused():
    """A stale UI reference after a catalog change looks exactly like this."""
    body = _method_body(STORE, "public PurchaseResult Purchase(StoreItemDef item)")
    assert "items.Contains(item)" in body


def test_the_purchase_is_persisted_immediately():
    """An item bought and lost to a crash is a support ticket -- and for a gem
    purchase it is a refund."""
    body = _method_body(STORE, "public PurchaseResult Purchase(StoreItemDef item)")
    assert "SaveService.Save(wallet)" in body


def test_ownership_survives_a_relaunch():
    assert "public void RestoreOwned(" in code(STORE)


def test_item_ids_are_compared_ordinally():
    assert "StringComparer.Ordinal" in code(STORE)


def test_the_store_is_retrievable(monkeypatch):
    monkeypatch.setattr(symbol_scanner, "CONFIG_PATH", REAL_ROOT / "config" / "agent.config.json")
    symbols = {s["name"]: s for s in symbol_scanner.rebuild_symbol_index(REAL_ROOT)["symbols"]}
    assert symbols["StoreUI"]["namespace"] == "JetFighter.Economy"


def test_the_never_negative_claim_is_swept():
    """"Ever" is a sweep, not a spot check."""
    source = (REAL_ROOT / "Assets" / "_Game" / "Tests" / "EditMode" / "StoreUITests.cs").read_text()
    assert "NoSequenceOfPurchasesEverProducesANegativeBalance" in source
    assert "i < 500" in source
    assert "TheButtonStateAgreesWithWhatPurchaseWillDo" in source, \
        "nothing checks that the button state matches what Purchase actually does"


def test_phase_3_economy_is_still_untouched():
    for name in ("Wallet.cs", "CurrencyType.cs", "SaveService.cs"):
        source = code(ECONOMY / name)
        for phase5 in ("StoreUI", "IAPManager", "ProductCatalog"):
            assert phase5 not in source, f"{name} was changed to accommodate {phase5}"


def test_the_seed_node_matches_the_traceability_row():
    seeds = json.loads((REAL_ROOT / "knowledge" / "seeds.json").read_text())
    node = next(n for n in seeds["nodes"] if n["id"] == "phase5_store_ui")
    assert "StoreUI" in node["symbols"]

    row = [l for l in (REAL_ROOT / "docs" / "TRACEABILITY.md").read_text().splitlines()
           if "`phase5/store-ui`" in l and l.startswith("|")]
    assert len(row) == 1 and row[0].startswith("| [x] |")
