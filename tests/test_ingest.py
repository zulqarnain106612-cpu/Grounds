"""Ingest must produce a KB that retrieval can actually serve from."""
import json

import pytest

from gateway import gateway, ingest, kb_store, manifest


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
