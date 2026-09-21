"""Cycle 5, branch phase5/product-catalog (ADR-004).

Criterion: the catalog loads and validates -- no duplicate or malformed
product ids -- **before any store UI depends on it**.

The ordering in that sentence is the requirement. Every fault this validator
catches reaches a *customer* rather than a developer if it does not: a
duplicate id credits the wrong amount, which is a refund conversation; a
whitespace id makes every purchase of that product fail with no visible cause.
"""
from __future__ import annotations

import json
from pathlib import Path

import pytest

from gateway import symbol_scanner
from tests._csharp import code

REAL_ROOT = Path(__file__).resolve().parent.parent
ECONOMY = REAL_ROOT / "Assets" / "_Game" / "Scripts" / "Economy"
CATALOG = ECONOMY / "ProductCatalog.cs"
ITEM = ECONOMY / "StoreItemDef.cs"


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


def test_pricing_and_grants_are_data():
    """The Cycle 5 gate states it as "price changes touch no code", and it
    matters more here than anywhere: store pricing is retuned against live
    conversion data, weekly, by someone who is not going to open Unity."""
    for path in (CATALOG, ITEM):
        assert ": ScriptableObject" in code(path)
    source = code(ITEM)
    assert "public int cost" in source
    assert "public CurrencyType currency" in source


def test_the_catalog_validates_before_anything_consumes_it():
    """The criterion's ordering. A store UI is the wrong place to discover a
    duplicate id."""
    source = code(CATALOG)
    assert "public IReadOnlyList<string> Validate()" in source
    assert "public bool IsUsable" in source


def test_duplicate_ids_are_caught():
    """The fault that becomes a refund conversation."""
    body = _method_body(CATALOG, "public IReadOnlyList<string> Validate()")
    assert "seen.Add(product.productId)" in body
    assert "duplicate product id" in body


def test_whitespace_in_an_id_is_caught():
    """Invisible in the inspector, and it makes the id not match App Store
    Connect -- so every purchase of that product fails with no obvious
    cause."""
    body = _method_body(CATALOG, "public IReadOnlyList<string> Validate()")
    assert "Trim() != product.productId" in body


def test_an_inverted_tier_is_caught():
    """A higher tier granting fewer gems makes the expensive option strictly
    worse, and no store UI can present that in a way that is not a bug."""
    body = _method_body(CATALOG, "public IReadOnlyList<string> Validate()")
    assert "grants fewer gems than" in body


def test_validation_returns_every_fault():
    """A designer fixing a catalog wants all of them, not one per build."""
    body = _method_body(CATALOG, "public IReadOnlyList<string> Validate()")
    assert "problems.Add" in body
    assert "throw" not in body


def test_lookup_is_ordinal():
    """An App Store product id is an exact byte match; culture-aware
    comparison would accept ids the store will not."""
    assert "StringComparison.Ordinal" in code(CATALOG)
    assert "StringComparer.Ordinal" in code(CATALOG)


def test_an_unknown_product_id_does_not_throw():
    body = _method_body(CATALOG, "public bool TryGet(string productId, out ProductCatalog.Product product)") \
        if "ProductCatalog.Product product" in code(CATALOG) \
        else _method_body(CATALOG, "public bool TryGet(string productId, out Product product)")
    assert "IsNullOrWhiteSpace(productId)" in body
    assert "return false" in body


def test_the_bonus_rounds_down():
    """A player who computes the advertised bonus and gets one gem fewer
    complains; one who gets one more never does. The advertised number should
    be the floor."""
    body = _method_body(CATALOG, "public static int GemsFor(Product product)")
    assert "FloorToInt" in body


def test_gem_grants_cannot_go_negative():
    body = _method_body(CATALOG, "public static int GemsFor(Product product)")
    assert "Mathf.Max(0f, product.bonusPercent)" in body
    assert "Mathf.Max(0, product.baseGems)" in body


def test_the_catalog_grants_gems_not_coins():
    """ADR-004: coins are the only in-run earn, gems are IAP-primary. A
    catalog that could grant coins would make that decision unenforceable."""
    source = code(CATALOG)
    assert "baseGems" in source
    assert "Coins" not in source


def test_only_consumables_repurchase():
    """A cosmetic marked repeatable charges the player twice for the same
    hat, and there is no design in which that is intended."""
    assert "CanRepurchase => repeatable && itemType == ItemType.Consumable" in code(ITEM)


def test_the_item_id_is_documented_as_persisted():
    """Renaming it orphans everything already owned, which is a support
    problem rather than a bug."""
    source = ITEM.read_text()
    assert "Persisted in the save" in source


def test_no_phase_3_economy_code_changed():
    """The spec's premise: "No refactor of the Phase 3 economy core needed if
    that phase was built as specified." This cell is where that is either true
    or quietly false."""
    for name in ("Wallet.cs", "CurrencyType.cs", "SaveService.cs"):
        source = code(ECONOMY / name)
        for phase5 in ("ProductCatalog", "StoreItemDef", "IAPManager", "StoreUI"):
            assert phase5 not in source, f"{name} was changed to accommodate Phase 5"


@pytest.mark.parametrize("name", ["ProductCatalog", "StoreItemDef"])
def test_the_catalog_types_are_retrievable(monkeypatch, name):
    monkeypatch.setattr(symbol_scanner, "CONFIG_PATH", REAL_ROOT / "config" / "agent.config.json")
    symbols = {s["name"]: s for s in symbol_scanner.rebuild_symbol_index(REAL_ROOT)["symbols"]}
    assert symbols[name]["namespace"] == "JetFighter.Economy"


def test_every_fault_is_covered_in_csharp():
    source = (REAL_ROOT / "Assets" / "_Game" / "Tests" / "EditMode"
              / "ProductCatalogTests.cs").read_text()
    for case in ("ADuplicateProductIdIsRejected", "WhitespaceAroundAProductIdIsRejected",
                 "AHigherTierGrantingFewerGemsIsRejected", "TheBonusRoundsDown",
                 "LookupIsExactAndOrdinal"):
        assert case in source


def test_the_seed_node_matches_the_traceability_row():
    seeds = json.loads((REAL_ROOT / "knowledge" / "seeds.json").read_text())
    node = next(n for n in seeds["nodes"] if n["id"] == "phase5_product_catalog")
    assert {"ProductCatalog", "StoreItemDef"} <= set(node["symbols"])

    row = [l for l in (REAL_ROOT / "docs" / "TRACEABILITY.md").read_text().splitlines()
           if "`phase5/product-catalog`" in l and l.startswith("|")]
    assert len(row) == 1 and row[0].startswith("| [x] |")
    assert "ADR-004" in row[0]
