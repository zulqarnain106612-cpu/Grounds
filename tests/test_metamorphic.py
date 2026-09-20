"""Metamorphic tests: relations between runs, where no oracle exists.

tests/test_ingest.py can assert what ingest produces because it knows what the
fixture contains. The interesting failures are the ones where nobody can write
down the right answer in advance -- what *should* the retrieval ranking be for
an arbitrary query? -- and those are exactly the ones example-based tests miss.

A metamorphic test sidesteps the missing oracle. It transforms the input in a
way whose effect on the output is known, and checks that relation instead of
the value. If shuffling the order of `seeds.json` changes the graph, the graph
depends on file ordering, and no one had to know the correct graph to catch it.

Each relation below is named for the property it pins:

  invariance   -- transforming the input must not change the output
  monotonicity -- adding input may only add output, never remove it
  inversion    -- add-then-remove must return to where it started
  idempotence  -- applying twice equals applying once
"""
from __future__ import annotations

import json
import random
from pathlib import Path

import pytest

from gateway import gateway, ingest

REAL_ROOT = Path(__file__).resolve().parent.parent


@pytest.fixture
def repo(isolated_repo, monkeypatch):
    monkeypatch.setattr(ingest, "ROOT", isolated_repo)
    return isolated_repo


def _graph(root) -> dict:
    return ingest.build_graph(root)["graph"]


def _content(graph: dict) -> tuple:
    """Graph content, order included.

    Compared exactly rather than as a set: `build_graph` sorts seed nodes and
    every edge before writing, so order is part of what it promises. Comparing
    sets here would let an ordering regression through, and an ordering
    regression is precisely what makes enforce.yml's staleness check fail on
    PRs that changed nothing.
    """
    return (graph["nodes"], graph["edges"])


def _seeds(root) -> dict:
    return json.loads((root / "knowledge" / "seeds.json").read_text())


def _write_seeds(root, data: dict) -> None:
    (root / "knowledge" / "seeds.json").write_text(json.dumps(data, indent=2) + "\n")


def _new_node(node_id: str) -> dict:
    return {
        "id": node_id, "type": "system", "label": f"Probe {node_id}",
        "tags": ["qa", "probe"], "phase": ["0"], "symbols": [],
    }


# --- invariance ------------------------------------------------------------------

def test_shuffling_seed_order_does_not_change_the_graph(repo):
    """The relation that catches ordering dependence. `build_graph` sorts on
    write so a fresh ingest reproduces seeds.json byte-identically -- if the
    sort were dropped, two contributors adding a node each would produce
    graphs that differ only by line order, and every subsequent PR would show
    a spurious staleness failure."""
    before = _content(_graph(repo))

    data = _seeds(repo)
    rng = random.Random(20260920)
    rng.shuffle(data["nodes"])
    rng.shuffle(data["edges"])
    _write_seeds(repo, data)

    assert _content(_graph(repo)) == before


def test_reformatting_seeds_does_not_change_the_graph(repo):
    """Whitespace and key order are presentation. A graph that moved because
    someone's editor re-indented the file would make every diff suspect."""
    before = _content(_graph(repo))

    data = _seeds(repo)
    reordered = {k: data[k] for k in reversed(list(data))}
    (repo / "knowledge" / "seeds.json").write_text(json.dumps(reordered, separators=(",", ":")))

    assert _content(_graph(repo)) == before


def test_keyword_retrieval_ignores_case_and_surrounding_whitespace(repo):
    """Two spellings of the same query are the same query. Anything else makes
    recall depend on how carefully the caller typed."""
    term = _a_real_query_term(repo)
    plain = _retrieve("keyword", term)
    assert plain, f"query {term!r} came from the index and must match something"
    shouty = _retrieve("keyword", f"  {term.upper()}  ")
    assert _sources(plain) == _sources(shouty)


# --- monotonicity ----------------------------------------------------------------

def test_adding_a_seed_node_adds_exactly_that_node(repo):
    """Adding input may only add output. A node that displaces an unrelated one
    means ids are colliding somewhere they should not."""
    before = {n["id"] for n in _graph(repo)["nodes"]}

    data = _seeds(repo)
    data["nodes"].append(_new_node("qa_probe_alpha"))
    _write_seeds(repo, data)

    after = {n["id"] for n in _graph(repo)["nodes"]}
    assert after - before == {"qa_probe_alpha"}
    assert not before - after, "an existing node disappeared when an unrelated one was added"


def test_adding_a_document_never_drops_an_existing_chunk(repo):
    """The same relation for the KB index. Ingest is additive over docs/."""
    before = {c["id"] for c in ingest.build_kb_index(repo)["chunks"]}

    docs = repo / "docs"
    docs.mkdir(exist_ok=True)
    (docs / "QA_PROBE.md").write_text(
        "# QA probe\n\nA document that exists only for the duration of one test.\n"
    )

    after = {c["id"] for c in ingest.build_kb_index(repo)["chunks"]}
    assert not before - after, f"chunks vanished when a doc was added: {sorted(before - after)}"
    assert after - before, "adding a document produced no new chunk at all"


def test_a_larger_top_k_never_returns_fewer_or_reordered_hits(repo):
    """Widening the window may append results; it must not change the ones
    already there. A ranking that reshuffles with top_k is not a ranking."""
    term = _a_real_query_term(repo)
    narrow = _sources(_retrieve("keyword", term, top_k=2))
    wide = _sources(_retrieve("keyword", term, top_k=8))
    assert narrow, f"query {term!r} came from the index and must match something"
    assert len(wide) >= len(narrow)
    assert wide[:len(narrow)] == narrow


# --- inversion and idempotence ---------------------------------------------------

def test_adding_then_removing_a_seed_restores_the_original_graph(repo):
    before = _content(_graph(repo))

    data = _seeds(repo)
    data["nodes"].append(_new_node("qa_probe_beta"))
    _write_seeds(repo, data)
    assert _content(_graph(repo)) != before

    data["nodes"] = [n for n in data["nodes"] if n["id"] != "qa_probe_beta"]
    _write_seeds(repo, data)
    assert _content(_graph(repo)) == before


def test_rebuilding_without_changing_anything_is_a_no_op(repo):
    """Idempotence. This is what enforce.yml's staleness check depends on: a
    rebuild of unchanged inputs must produce an unchanged file, or every PR
    fails the check for reasons unrelated to its own diff."""
    first = _content(_graph(repo))
    assert _content(_graph(repo)) == first
    assert _content(_graph(repo)) == first


def test_a_seed_node_overrides_a_derived_node_of_the_same_id(repo):
    """Seeds win on collision -- a hand-authored node is the more specific
    statement (docs/CYCLE0_BOOTSTRAP_SPEC.md section 2). The relation: seeding
    over a derived id must replace it, never duplicate it."""
    derived = next(n["id"] for n in _graph(repo)["nodes"] if n["id"].startswith("domain:"))

    data = _seeds(repo)
    override = _new_node(derived)
    override["label"] = "Hand-authored override"
    data["nodes"].append(override)
    _write_seeds(repo, data)

    matching = [n for n in _graph(repo)["nodes"] if n["id"] == derived]
    assert len(matching) == 1, f"{derived} appears {len(matching)} times after seeding over it"
    assert matching[0]["label"] == "Hand-authored override"


# --- helpers ---------------------------------------------------------------------

def _sources(results: list[dict]) -> list[str]:
    """Retrieval returns `source`, not `id` -- see gateway/kb_store.py:retrieve."""
    return [r["source"] for r in results]


def _a_real_query_term(repo) -> str:
    """A tag taken from the index that was just built.

    Hardcoding a query word would make these relations depend on the fixture's
    documents rather than on retrieval's behaviour, and they would start
    failing the day someone renamed a heading.
    """
    kb = ingest.build_kb_index(repo)
    for chunk in kb["chunks"]:
        for tag in chunk.get("tags", []):
            if tag.isalpha() and len(tag) > 3:
                return tag
    pytest.skip("the isolated fixture produced no usable tag to query with")



def _retrieve(strategy: str, target: str, top_k: int = 5) -> list[dict]:
    request = {
        "meta": {
            "schema_version": "1.1.0",
            "session_id": "550e8400-e29b-41d4-a716-446655440000",
            "tick": 0, "phase": "1", "timestamp_utc": "2026-09-04T00:00:00Z",
        },
        "intent": {"action": "retrieve", "domain": "retrieval", "priority": 5},
        "payload": {"data": None, "context": {
            "strategy": strategy, "scope": "kb", "target": target,
            "top_k": top_k, "max_tokens": 1500,
        }},
    }
    return gateway.handle(json.dumps(request))["response"].get("retrieved", [])
