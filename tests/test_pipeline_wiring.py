"""Integration tests for the seams between components.

Unit tests prove each part works alone. These prove the parts are *connected*
-- that what ingest writes is the shape retrieval reads, that the scanner's
output is the input symbol_lookup expects, that a handler's return value is a
shape the response schema accepts.

Wiring defects are the ones unit tests structurally cannot see: both sides of
a seam pass their own tests while disagreeing about a key name, a path, or a
unit. Every test below crosses at least one real seam with no mock in between
-- the only substitution is `isolated_repo`, which redirects the same code at
a throwaday copy of the repo's data.

The pipelines covered:

    docs/ ─ingest─> kb.index.json ─kb_store─> retrieve ─gateway─> response
    Scripts/*.cs ─scanner─> symbols/index.json ─> symbol_lookup
    knowledge_op ─handler─> graph.json ─> seeds survive ─> retrieval
    stdin ─cli─> gateway.handle ─> stdout
    every handler ─> response ─> schema (the contract seam)
"""
from __future__ import annotations

import json
import subprocess
import sys
from pathlib import Path

import pytest

from gateway import gateway, ingest, schema_guard, symbol_scanner

REAL_ROOT = Path(__file__).resolve().parent.parent
EXAMPLES = json.loads((REAL_ROOT / "schema" / "examples.json").read_text())["examples"]


def _request(action: str, domain: str, payload: dict, priority: int = 5) -> dict:
    return {
        "meta": {
            "schema_version": "1.1.0",
            "session_id": "550e8400-e29b-41d4-a716-446655440000",
            "tick": 0, "phase": "1", "timestamp_utc": "2026-09-04T00:00:00Z",
        },
        "intent": {"action": action, "domain": domain, "priority": priority},
        "payload": payload,
    }


def _call(action: str, domain: str, payload: dict) -> dict:
    return gateway.handle(_request(action, domain, payload))["response"]


# --- seam 1: ingest writes what retrieval reads ----------------------------

def test_a_document_ingested_is_a_document_retrievable(isolated_repo, monkeypatch):
    """The whole KB pipeline end to end. Both halves have their own tests and
    could each pass while disagreeing about the chunk id format, in which case
    ingest indexes documents that retrieval can never find."""
    monkeypatch.setattr(ingest, "ROOT", isolated_repo)
    docs = isolated_repo / "docs"
    docs.mkdir(exist_ok=True)
    (docs / "WIRING_PROBE.md").write_text(
        "# Aerodynamics probe\n\n"
        "A uniquely worded section about vectored thrust and gimbal limits.\n"
    )

    index = ingest.build_kb_index(isolated_repo)
    ingested = [c for c in index["chunks"] if "WIRING_PROBE" in c["id"]]
    assert ingested, "ingest produced no chunk for a document that exists"

    chunk_id = ingested[0]["id"]
    response = _call("retrieve", "retrieval", {"data": None, "context": {
        "strategy": "exact", "scope": "kb", "target": chunk_id,
        "top_k": 5, "max_tokens": 1500,
    }})

    assert response["status"] == "ok"
    sources = [r["source"] for r in response.get("retrieved", [])]
    assert chunk_id in sources, (
        f"ingest wrote {chunk_id} but an exact retrieval for it returned {sources}"
    )


def test_source_code_never_reaches_retrieval_through_the_ingest_pipeline(isolated_repo, monkeypatch):
    """The guarantee in docs/ENFORCEMENT.md, asserted across the whole
    pipeline rather than inside one module.

    Note what is *not* claimed: a document's own prose does appear in its
    chunk summary, because documents are the knowledge base -- that is the
    feature. The guarantee is about source, which is indexed by symbol name
    only. A method body must not survive the trip from disk to response.
    """
    monkeypatch.setattr(ingest, "ROOT", isolated_repo)
    monkeypatch.setattr(symbol_scanner, "ROOT", isolated_repo)

    secret = "CANARY7F3A9_METHOD_BODY"
    scripts = isolated_repo / "Assets" / "_Game" / "Scripts" / "Probe"
    scripts.mkdir(parents=True)
    (scripts / "Leak.cs").write_text(
        "namespace JetFighter.Probe\n{\n"
        "    public class ThrustVectorProbe\n    {\n"
        f"        void Configure() {{ var token = \"{secret}\"; }}\n"
        "    }\n}\n"
    )

    symbol_scanner.rebuild_symbol_index(isolated_repo)
    index = ingest.build_kb_index(isolated_repo)

    assert any("ThrustVectorProbe" in json.dumps(c) for c in index["chunks"]), (
        "the symbol never reached the KB index, so this assertion would be vacuous"
    )
    assert secret not in json.dumps(index["chunks"]), "a method body leaked into the KB index"

    response = _call("retrieve", "retrieval", {"data": None, "context": {
        "strategy": "keyword", "scope": "kb", "target": "ThrustVectorProbe",
        "top_k": 10, "max_tokens": 1500,
    }})
    assert secret not in json.dumps(response), "a method body leaked into a retrieval result"
    for hit in response.get("retrieved", []):
        assert len(hit["content"]) <= 2000, "a retrieval hit exceeded the schema's content cap"


# --- seam 2: the scanner writes what symbol_lookup reads -------------------

def test_a_scanned_symbol_is_a_lookupable_symbol(isolated_repo, monkeypatch):
    """symbol_scanner and kb_store.symbol_lookup never import each other; the
    only thing joining them is the shape of symbols/index.json."""
    scripts = isolated_repo / "Assets" / "_Game" / "Scripts" / "Probe"
    scripts.mkdir(parents=True)
    (scripts / "WiringProbe.cs").write_text(
        "namespace JetFighter.Probe\n{\n"
        "    public class GimbalLimiterProbe\n    {\n"
        "        public void Engage() { }\n    }\n}\n"
    )
    monkeypatch.setattr(symbol_scanner, "ROOT", isolated_repo)

    index = symbol_scanner.rebuild_symbol_index(isolated_repo)
    names = {s["name"] for s in index["symbols"]}
    assert "GimbalLimiterProbe" in names, f"the scanner missed the class; found {sorted(names)[:10]}"

    response = _call("symbol_lookup", "retrieval", {"data": None, "context": {
        "strategy": "symbol", "scope": "symbol_table",
        "target": "GimbalLimiterProbe", "max_tokens": 200,
    }})
    assert response["status"] == "ok"
    assert any(s["name"] == "GimbalLimiterProbe" for s in response.get("symbols", [])), (
        "the scanner indexed the symbol but symbol_lookup could not find it"
    )


def test_a_symbol_response_never_carries_the_source_line(isolated_repo, monkeypatch):
    scripts = isolated_repo / "Assets" / "_Game" / "Scripts" / "Probe"
    scripts.mkdir(parents=True)
    body = "var apiKey = \"CANARY_SOURCE_BODY_9931\";"
    (scripts / "Probe.cs").write_text(
        f"namespace P\n{{\n    public class LeakProbe\n    {{\n        void M() {{ {body} }}\n    }}\n}}\n"
    )
    monkeypatch.setattr(symbol_scanner, "ROOT", isolated_repo)
    symbol_scanner.rebuild_symbol_index(isolated_repo)

    response = _call("symbol_lookup", "retrieval", {"data": None, "context": {
        "strategy": "symbol", "scope": "symbol_table", "target": "LeakProbe", "max_tokens": 200,
    }})
    assert "CANARY_SOURCE_BODY_9931" not in json.dumps(response)


# --- seam 3: the contract between every handler and the schema -------------

@pytest.mark.parametrize("name", sorted(EXAMPLES))
def test_every_handler_returns_a_shape_the_schema_accepts(name):
    """The handler/schema contract, checked per action.

    `gateway.handle` validates the response and rewrites it into a
    `response_schema_violation` error if it does not fit. That means a
    handler returning a malformed shape still produces a *valid envelope*,
    so a test that only checked the envelope would pass. This looks for the
    rewrite itself.
    """
    response = gateway.handle(json.loads(json.dumps(EXAMPLES[name])))["response"]

    codes = {e.get("code") for e in response.get("errors", [])}
    assert "response_schema_violation" not in codes, (
        f"the {name} handler returned a shape the schema rejects: {response}"
    )
    assert response["status"] in ("ok", "error", "partial", "skipped")


@pytest.mark.parametrize("name", sorted(EXAMPLES))
def test_the_full_envelope_still_validates_after_a_handler_ran(name):
    """Request in, request+response out: the whole document must remain
    schema-valid, not just the response half."""
    full = gateway.handle(json.loads(json.dumps(EXAMPLES[name])))
    schema_guard.validate(full)


# --- seam 4: state that has to survive between calls -----------------------

def test_context_pushed_by_one_call_is_visible_to_the_next(isolated_repo):
    """Two separate gateway invocations sharing state through context_store.
    In-process this is a module global; the seam is that both calls resolve to
    the same store."""
    session = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"

    push = _request("context_push", "knowledge_base", {"data": {"frame": "wiring probe"}})
    push["meta"]["session_id"] = session
    assert gateway.handle(push)["response"]["result"]["depth"] == 1

    pop = _request("context_pop", "knowledge_base", {"data": None})
    pop["meta"]["session_id"] = session
    popped = gateway.handle(pop)["response"]
    assert popped["status"] == "ok"
    assert popped["result"]["depth"] == 0


def test_a_knowledge_node_added_at_runtime_is_immediately_in_the_graph(isolated_repo):
    response = _call("knowledge_update", "knowledge_base", {"data": None, "knowledge_op": {
        "op": "add_node", "id": "wiring_probe_node", "type": "system",
        "label": "Wiring Probe", "tags": ["qa"],
    }})
    assert response["status"] == "ok"

    graph = json.loads((isolated_repo / "knowledge" / "graph.json").read_text())
    assert any(n["id"] == "wiring_probe_node" for n in graph["graph"]["nodes"])


def test_a_runtime_node_does_not_survive_the_next_ingest(isolated_repo, monkeypatch):
    """The documented split in docs/SDLC_SPIRAL.md section 6, asserted across
    the seam: `knowledge_update` writes session state, `seeds.json` is the
    durable channel, and `build_graph` is where the difference materialises.
    A test inside either module alone would not show it."""
    monkeypatch.setattr(ingest, "ROOT", isolated_repo)
    _call("knowledge_update", "knowledge_base", {"data": None, "knowledge_op": {
        "op": "add_node", "id": "transient_probe", "type": "system",
        "label": "Transient", "tags": ["qa"],
    }})

    ingest.build_graph(isolated_repo)
    graph = json.loads((isolated_repo / "knowledge" / "graph.json").read_text())
    ids = {n["id"] for n in graph["graph"]["nodes"]}

    assert "transient_probe" not in ids, "a runtime node survived ingest; the split has broken"
    seeded = {n["id"] for n in json.loads((isolated_repo / "knowledge" / "seeds.json").read_text())["nodes"]}
    assert seeded <= ids, "a seeded node did not survive ingest; the durable channel has broken"


# --- seam 5: the process boundary ------------------------------------------

def test_the_cli_reads_stdin_and_writes_a_json_response():
    """The seam a human and the Makefile both use. `handle` being correct in
    process says nothing about argument parsing, stdin decoding, or whether
    stdout is valid JSON."""
    request = json.dumps(EXAMPLES["schema_validate"])
    proc = subprocess.run(
        [sys.executable, "-m", "gateway.cli"],
        input=request, capture_output=True, text=True, cwd=REAL_ROOT, timeout=60,
    )
    assert proc.returncode == 0, proc.stderr
    parsed = json.loads(proc.stdout)
    assert parsed["response"]["status"] == "ok"


def test_the_cli_reports_malformed_input_as_json_not_a_traceback():
    proc = subprocess.run(
        [sys.executable, "-m", "gateway.cli"],
        input="{not json", capture_output=True, text=True, cwd=REAL_ROOT, timeout=60,
    )
    assert proc.returncode == 1
    assert json.loads(proc.stdout)["response"]["errors"][0]["code"] == "invalid_json"
    assert "Traceback" not in proc.stderr


# --- seam 6: the audit trail -----------------------------------------------

def test_every_handled_request_leaves_exactly_one_audit_line(isolated_repo):
    audit = isolated_repo / "logs" / "agent.jsonl"
    before = len(audit.read_text().splitlines()) if audit.exists() else 0

    _call("schema_validate", "schema", {"data": {"probe": True}})
    _call("schema_validate", "schema", {"data": {"probe": True}})

    lines = audit.read_text().splitlines()
    assert len(lines) - before == 2, "the audit trail and the dispatcher disagree on how many calls happened"
    assert json.loads(lines[-1])["action"] == "schema_validate"


def test_a_rejected_request_is_not_audited_as_a_handled_one(isolated_repo):
    """Validation happens before dispatch, so a rejected request never
    reaches a handler and must not appear as though it did."""
    audit = isolated_repo / "logs" / "agent.jsonl"
    before = len(audit.read_text().splitlines()) if audit.exists() else 0

    bad = _request("schema_validate", "schema", {"data": None})
    bad["intent"]["action"] = "definitely_not_an_action"
    assert gateway.handle(bad)["response"]["status"] == "error"

    after = len(audit.read_text().splitlines()) if audit.exists() else 0
    assert after == before, "a schema-rejected request was written to the audit trail"
