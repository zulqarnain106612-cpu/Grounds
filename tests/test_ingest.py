"""Ingest must produce a KB that retrieval can actually serve from."""
import json
from pathlib import Path

import pytest

from gateway import gateway, ingest, kb_store, manifest

REAL_ROOT = Path(__file__).resolve().parent.parent


@pytest.fixture
def ingested(isolated_repo, monkeypatch):
    monkeypatch.setattr(ingest, "ROOT", isolated_repo)
    kb = ingest.build_kb_index(isolated_repo)
    graph = ingest.build_graph(isolated_repo)
    return isolated_repo, kb, graph


def _retrieve(strategy, target, top_k=5):
    req = {
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
    return gateway.handle(json.dumps(req))["response"]


def test_ingest_produces_chunks(ingested):
    _, kb, _ = ingested
    assert kb["chunk_count"] > 0
    assert kb["chunk_count"] == len(kb["chunks"])
    # every schema action is retrievable as its own chunk
    ids = {c["id"] for c in kb["chunks"]}
    assert "schema:action:retrieve" in ids
    assert "schema:action:ci_dispatch" in ids
    assert "schema:definition:CIOp" in ids


def test_ingest_is_deterministic(ingested):
    root, kb1, _ = ingested
    kb2 = ingest.build_kb_index(root)
    # built_at moves; content must not.
    assert kb1["chunks"] == kb2["chunks"]


def test_chunks_respect_schema_content_cap(ingested):
    _, kb, _ = ingested
    for chunk in kb["chunks"]:
        assert len(chunk["summary"]) <= 2000


def test_graph_has_nodes_and_edges(ingested):
    _, _, graph = ingested
    nodes, edges = graph["graph"]["nodes"], graph["graph"]["edges"]
    assert nodes, "graph has no nodes after ingest"
    assert edges, "graph has no edges after ingest"
    node_ids = {n["id"] for n in nodes}
    for edge in edges:
        assert edge["from"] in node_ids, f"dangling edge source {edge['from']}"
        assert edge["to"] in node_ids, f"dangling edge target {edge['to']}"


def test_exact_retrieval_returns_the_requested_chunk(ingested):
    resp = _retrieve("exact", "schema:action:retrieve")
    assert resp["status"] == "ok", resp
    hits = resp["retrieved"]
    assert len(hits) == 1
    assert hits[0]["source"] == "schema:action:retrieve"
    assert hits[0]["score"] == 1.0


def test_keyword_retrieval_returns_scored_hits(ingested):
    resp = _retrieve("keyword", "daemon status")
    assert resp["status"] == "ok", resp
    hits = resp["retrieved"]
    assert hits, "keyword retrieval returned nothing from a populated KB"
    assert all(0 < h["score"] <= 1 for h in hits)
    # results must be ordered best-first
    assert [h["score"] for h in hits] == sorted((h["score"] for h in hits), reverse=True)


def test_retrieval_respects_top_k(ingested):
    resp = _retrieve("keyword", "schema action request", top_k=3)
    assert len(resp["retrieved"]) <= 3


def test_retrieval_never_returns_raw_file_content(ingested):
    """Every hit must be a bounded summary, not file bytes."""
    resp = _retrieve("keyword", "gateway enforcement schema")
    for hit in resp["retrieved"]:
        assert len(hit["content"]) <= 2000
        assert "def " not in hit["content"], "raw source leaked into a retrieval result"


def test_unknown_target_returns_empty_not_error(ingested):
    resp = _retrieve("keyword", "zzz_no_such_term_anywhere_xyzzy")
    assert resp["status"] == "ok"
    assert resp["retrieved"] == []


def test_manifest_counts_match_reality(ingested, monkeypatch):
    root, kb, graph = ingested
    monkeypatch.setattr(manifest, "MANIFEST_PATH", root / "index" / "manifest.json")
    m = manifest.build_manifest(root)
    assert m["counts"]["kb_chunks"] == kb["chunk_count"]
    assert m["counts"]["graph_nodes"] == len(graph["graph"]["nodes"])
    assert m["counts"]["graph_edges"] == len(graph["graph"]["edges"])
    assert m["counts"]["files"] == len(m["files"])


# --- durable knowledge (ADR-008) -------------------------------------------
# Every phase spec ends with a `knowledge_update` node tagged by phase, and
# roadmap section 9 makes phase-scoped retrieval the justification for
# cell-scoped branching. build_graph() rewrites the graph wholesale, so those
# nodes only survive if they come from a tracked input. These tests pin that.

def _graph_nodes(isolated_repo):
    return json.loads(
        (isolated_repo / "knowledge" / "graph.json").read_text()
    )["graph"]["nodes"]


def test_seed_nodes_survive_a_graph_rebuild(ingested):
    """The regression this layer exists to prevent."""
    isolated_repo, _, graph = ingested
    seeds = json.loads((isolated_repo / "knowledge" / "seeds.json").read_text())
    seeded_ids = {n["id"] for n in seeds["nodes"]}
    assert seeded_ids, "seeds.json must not be empty -- an empty file proves nothing"

    node_ids = {n["id"] for n in graph["graph"]["nodes"]}
    assert seeded_ids <= node_ids

    # and again after a second rebuild, which is when the wholesale overwrite bites
    ingest.build_kb_index(isolated_repo)
    ingest.build_graph(isolated_repo)
    assert seeded_ids <= {n["id"] for n in _graph_nodes(isolated_repo)}


def test_seed_edges_are_merged_and_deduplicated(ingested):
    isolated_repo, _, graph = ingested
    seeds = json.loads((isolated_repo / "knowledge" / "seeds.json").read_text())
    keys = [(e["from"], e["to"], e["kind"]) for e in graph["graph"]["edges"]]
    for edge in seeds["edges"]:
        assert (edge["from"], edge["to"], edge.get("kind", "related")) in keys
    assert len(keys) == len(set(keys)), "duplicate edges would grow the graph on every ingest"


def test_graph_stays_byte_deterministic_with_seeds(ingested):
    """CI's staleness check diffs the 'graph' key, so a moving node or edge
    order would fail enforce.yml on every run."""
    isolated_repo, _, first = ingested
    ingest.build_kb_index(isolated_repo)
    second = ingest.build_graph(isolated_repo)
    assert first["graph"] == second["graph"]


def test_runtime_knowledge_nodes_are_transient_but_seeds_are_not(ingested):
    """The documented split: knowledge_update is session state, seeds.json is
    durable. A cell that never promotes its node loses it -- that is why
    promotion is a required step, not a nicety."""
    isolated_repo, _, _ = ingested
    kb_store.apply_knowledge_op({
        "op": "add_node", "id": "phase1_runtime_only",
        "type": "system", "label": "Runtime Only", "phase": ["1"],
    })
    assert "phase1_runtime_only" in {n["id"] for n in _graph_nodes(isolated_repo)}

    ingest.build_kb_index(isolated_repo)
    ingest.build_graph(isolated_repo)
    after = {n["id"] for n in _graph_nodes(isolated_repo)}
    assert "phase1_runtime_only" not in after
    assert "cycle:1" in after, "a seeded node must outlive the rebuild that erased the runtime one"


def test_missing_seeds_file_degrades_to_a_derived_graph(ingested):
    """An older checkout without seeds.json must still ingest cleanly."""
    isolated_repo, _, _ = ingested
    (isolated_repo / "knowledge" / "seeds.json").unlink()
    ingest.build_kb_index(isolated_repo)
    graph = ingest.build_graph(isolated_repo)
    ids = {n["id"] for n in graph["graph"]["nodes"]}
    assert "domain:player" in ids
    assert not any(i.startswith("cycle:") for i in ids)


def test_malformed_seeds_file_fails_loudly(ingested):
    """Degrading to an empty list here would silently drop durable knowledge --
    exactly the failure mode this layer was added to fix."""
    isolated_repo, _, _ = ingested
    (isolated_repo / "knowledge" / "seeds.json").write_text("{ not json")
    with pytest.raises(json.JSONDecodeError):
        ingest.build_graph(isolated_repo)


# --- seeds.json is hand-authored, so it is the file that can be wrong -------
#
# Everything else under knowledge/ is generated and cannot drift from its
# generator. seeds.json is typed by a person, is unreachable from the request
# schema, and is only read by the nightly ingest -- so before these tests a
# structurally broken entry was dropped in silence and the author found out
# never. These pin that every such entry now fails loudly and early.

def _seeds(isolated_repo, doc):
    (isolated_repo / "knowledge" / "seeds.json").write_text(json.dumps(doc))


def test_seeds_document_must_be_an_object(ingested):
    isolated_repo, _, _ = ingested
    _seeds(isolated_repo, ["not", "a", "document"])
    with pytest.raises(ingest.SeedsError, match="must be a JSON object"):
        ingest.build_graph(isolated_repo)


@pytest.mark.parametrize("key", ["nodes", "edges"])
def test_seeds_collections_must_be_arrays(ingested, key):
    isolated_repo, _, _ = ingested
    _seeds(isolated_repo, {"nodes": [], "edges": [], key: {"id": "x"}})
    with pytest.raises(ingest.SeedsError, match=f"'{key}' must be an array"):
        ingest.build_graph(isolated_repo)


def test_a_node_that_is_not_an_object_is_rejected(ingested):
    isolated_repo, _, _ = ingested
    _seeds(isolated_repo, {"nodes": ["cycle:0"], "edges": []})
    with pytest.raises(ingest.SeedsError, match=r"nodes\[0\]: must be an object"):
        ingest.build_graph(isolated_repo)


@pytest.mark.parametrize("bad_id", [None, "", "   ", 7])
def test_a_node_without_a_usable_id_is_rejected(ingested, bad_id):
    """The old filter dropped exactly these and carried on."""
    isolated_repo, _, _ = ingested
    _seeds(isolated_repo, {"nodes": [{"id": bad_id, "label": "Ghost"}], "edges": []})
    with pytest.raises(ingest.SeedsError, match="'id' must be a non-empty string"):
        ingest.build_graph(isolated_repo)


def test_duplicate_node_ids_are_rejected(ingested):
    """Last-one-wins would make the graph depend on file order."""
    isolated_repo, _, _ = ingested
    _seeds(isolated_repo, {"nodes": [{"id": "cycle:0"}, {"id": "cycle:0"}], "edges": []})
    with pytest.raises(ingest.SeedsError, match="duplicate node id 'cycle:0'"):
        ingest.build_graph(isolated_repo)


def test_an_edge_that_is_not_an_object_is_rejected(ingested):
    isolated_repo, _, _ = ingested
    _seeds(isolated_repo, {"nodes": [], "edges": ["cycle:0 -> domain:player"]})
    with pytest.raises(ingest.SeedsError, match=r"edges\[0\]: must be an object"):
        ingest.build_graph(isolated_repo)


@pytest.mark.parametrize("field", ["from", "to"])
def test_an_edge_missing_an_endpoint_is_rejected(ingested, field):
    isolated_repo, _, _ = ingested
    edge = {"from": "cycle:0", "to": "domain:player", "kind": "delivers"}
    edge[field] = ""
    _seeds(isolated_repo, {"nodes": [], "edges": [edge]})
    with pytest.raises(ingest.SeedsError, match=f"'{field}' must be a non-empty string"):
        ingest.build_graph(isolated_repo)


def test_an_edge_with_a_non_string_kind_is_rejected(ingested):
    isolated_repo, _, _ = ingested
    _seeds(isolated_repo, {"nodes": [], "edges": [
        {"from": "domain:player", "to": "domain:physics", "kind": 3}]})
    with pytest.raises(ingest.SeedsError, match="'kind' must be a non-empty string"):
        ingest.build_graph(isolated_repo)


def test_an_edge_may_omit_kind_entirely(ingested):
    """Optional, and build_graph defaults it to 'related' -- so omitting it is
    not the same as setting it to junk."""
    isolated_repo, _, _ = ingested
    _seeds(isolated_repo, {"nodes": [], "edges": [
        {"from": "domain:player", "to": "domain:physics"}]})
    graph = ingest.build_graph(isolated_repo)
    assert {"from": "domain:player", "to": "domain:physics", "kind": "related"} \
        in graph["graph"]["edges"]


@pytest.mark.parametrize("bad_edge", [
    {"from": "cycle:0", "to": "domain:build-pipeline", "kind": "delivers"},
    {"from": "cycle:99", "to": "domain:player", "kind": "delivers"},
])
def test_an_edge_pointing_at_no_node_is_rejected(ingested, bad_edge):
    """A dangling edge does not crash anything -- retrieval just traverses
    nothing, and `domain:build-pipeline` reviews identically to
    `domain:build_pipeline`."""
    isolated_repo, _, _ = ingested
    _seeds(isolated_repo, {"nodes": [{"id": "cycle:0"}], "edges": [bad_edge]})
    with pytest.raises(ingest.SeedsError, match="matches no node"):
        ingest.build_graph(isolated_repo)


def test_the_committed_seeds_file_passes_its_own_validator():
    """The shipped file, not a fixture -- the checks are worthless if the real
    seeds.json could not survive them."""
    seeds = json.loads((REAL_ROOT / "knowledge" / "seeds.json").read_text())
    nodes, edges = ingest.parse_seeds(seeds)
    assert nodes and edges
    ids = [n["id"] for n in nodes]
    assert ids == sorted(ids), "seeds.json is documented as sorted by id"
