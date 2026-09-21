"""Cycle 5, branch phase5/iap-integration (ADR-004).

Criterion: a sandbox purchase completes end to end and credits gems via the
existing Phase 3 `Wallet`.

A sandbox purchase is a manual step. What is testable -- and what a sandbox run
would almost never surface -- is everything around it: a redelivered
transaction, a product pulled from the catalog, a purchase arriving before
initialisation. Each of those is free gems or lost gems.
"""
from __future__ import annotations

import json
from pathlib import Path

import pytest

from gateway import symbol_scanner
from tests._csharp import code

REAL_ROOT = Path(__file__).resolve().parent.parent
ECONOMY = REAL_ROOT / "Assets" / "_Game" / "Scripts" / "Economy"
IAP = ECONOMY / "IAPManager.cs"


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


def test_the_store_sdk_is_behind_a_narrow_interface():
    """Real-money code CI can never exercise. The alternative is a class only
    ever tested by making sandbox purchases by hand."""
    source = code(IAP)
    assert "public interface IStoreBackend" in source
    # Unity IAP's own types by name. `ProductCatalog.Product` is this project's
    # and is fine -- the leak to guard against is the SDK's vocabulary, not the
    # word "product".
    for sdk in ("UnityEngine.Purchasing", "IStoreController", "PurchaseEventArgs",
                "ConfigurationBuilder", "IStoreListener"):
        assert sdk not in source, f"the SDK's shape leaked in via {sdk}"


def test_crediting_is_idempotent_per_transaction():
    """Apple redelivers unfinished transactions on every launch -- that is how
    a purchase survives the app being killed mid-flow -- so the same id
    arrives more than once as a matter of course, and a credit per arrival is
    free gems."""
    body = _method_body(IAP, "public bool Credit(string productId, string transactionId)")
    assert "creditedTransactions.Add(transactionId)" in body
    assert "DuplicateTransactions++" in body


def test_a_duplicate_is_still_confirmed():
    """Otherwise the store redelivers it forever, on every launch."""
    body = _method_body(IAP, "public bool Credit(string productId, string transactionId)")
    duplicate = body.index("DuplicateTransactions++")
    assert "ConfirmPendingPurchase" in body[duplicate:body.index("int gems")]


def test_the_wallet_is_saved_before_the_purchase_is_confirmed():
    """Confirm-then-save loses the gems if the process dies in between, and
    the store never redelivers a transaction it was told was handled."""
    body = _method_body(IAP, "public bool Credit(string productId, string transactionId)")
    tail = body[body.index("int gems"):]
    assert tail.index("SaveService.Save(wallet)") < tail.index("ConfirmPendingPurchase")


def test_an_unknown_product_is_refused_but_confirmed():
    """A product pulled from the catalog but still purchasable on the store.
    Crediting an amount nobody defined is worse than refusing; not confirming
    loops the redelivery forever."""
    body = _method_body(IAP, "public bool Credit(string productId, string transactionId)")
    unknown = body.index("UnknownProductPurchases++")
    assert "ConfirmPendingPurchase" in body[unknown:unknown + 400]


def test_a_purchase_is_never_started_for_an_uncatalogued_product():
    """Its grant would have no defined amount."""
    body = _method_body(IAP, "public bool PurchaseProduct(string productId)")
    assert "catalog.TryGet(productId, out _)" in body
    assert body.index("TryGet") < body.index("backend.Purchase")


def test_not_attempted_is_distinguishable_from_declined():
    """The player deserves a different message for each."""
    body = _method_body(IAP, "public bool PurchaseProduct(string productId)")
    assert '"store not ready"' in body
    assert '"unknown product"' in body


def test_a_malformed_catalog_refuses_to_initialise():
    """Sending a malformed id to the store is how a product silently never
    appears, and discovering that during review costs a submission cycle."""
    body = _method_body(IAP, "public bool Initialize(IStoreBackend storeBackend, Wallet targetWallet)")
    assert "catalog.IsUsable" in body
    assert "return false" in body


def test_redelivery_across_a_relaunch_can_be_recognised():
    """The credited set lives in memory, so a relaunch has to be told what was
    already paid or the first redelivery is free gems."""
    assert "public void RestoreCreditedTransactions(" in code(IAP)
    assert "public IReadOnlyCollection<string> CreditedTransactions" in code(IAP)


def test_transaction_ids_are_compared_ordinally():
    """A transaction id is an opaque token; culture-aware comparison could
    treat two distinct ones as equal."""
    assert "StringComparer.Ordinal" in code(IAP)


def test_only_gems_are_credited():
    """ADR-004: gems are bought, coins are earned. An IAP that could mint
    coins makes that split unenforceable."""
    body = _method_body(IAP, "public bool Credit(string productId, string transactionId)")
    assert "CurrencyType.Gems" in body
    assert "CurrencyType.Coins" not in body


def test_phase_3_economy_is_still_untouched():
    """The spec's premise, re-checked now that real money flows through it."""
    for name in ("Wallet.cs", "CurrencyType.cs", "SaveService.cs"):
        source = code(ECONOMY / name)
        assert "IAPManager" not in source, f"{name} was changed to accommodate IAP"


def test_the_iap_manager_is_retrievable(monkeypatch):
    monkeypatch.setattr(symbol_scanner, "CONFIG_PATH", REAL_ROOT / "config" / "agent.config.json")
    symbols = {s["name"]: s for s in symbol_scanner.rebuild_symbol_index(REAL_ROOT)["symbols"]}
    assert symbols["IAPManager"]["namespace"] == "JetFighter.Economy"


def test_the_expensive_to_reproduce_cases_are_covered_in_csharp():
    source = (REAL_ROOT / "Assets" / "_Game" / "Tests" / "EditMode" / "IAPManagerTests.cs").read_text()
    for case in ("ARedeliveredTransactionIsNotCreditedTwice",
                 "RedeliveryAfterARelaunchIsRecognised",
                 "APurchaseIsSavedBeforeItIsConfirmed",
                 "APurchaseForAProductNotInTheCatalogIsNotCredited",
                 "AMalformedCatalogRefusesToInitialise"):
        assert case in source


def test_the_seed_node_matches_the_traceability_row():
    seeds = json.loads((REAL_ROOT / "knowledge" / "seeds.json").read_text())
    node = next(n for n in seeds["nodes"] if n["id"] == "phase5_iap_integration")
    assert {"IAPManager", "ProductCatalog"} <= set(node["symbols"])

    row = [l for l in (REAL_ROOT / "docs" / "TRACEABILITY.md").read_text().splitlines()
           if "`phase5/iap-integration`" in l and l.startswith("|")]
    assert len(row) == 1 and row[0].startswith("| [x] |")
