from __future__ import annotations
import json
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
KB_PATH = ROOT / "index" / "kb.index.json"
GRAPH_PATH = ROOT / "knowledge" / "graph.json"
SYMBOLS_PATH = ROOT / "symbols" / "index.json"

MAX_CONTENT_LEN = 2000  # matches RetrievedChunk.content maxLength in the schema


def _load(path: Path) -> dict:
    return json.loads(path.read_text())


def _save(path: Path, data: dict) -> None:
    path.write_text(json.dumps(data, indent=2) + "\n")


def add_kb_chunk(chunk: dict) -> dict:
    kb = _load(KB_PATH)
    kb.setdefault("chunks", []).append(chunk)
    kb["built_at"] = datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")
    _save(KB_PATH, kb)
    return chunk


def delete_kb_chunk(chunk_id: str) -> bool:
    kb = _load(KB_PATH)
    before = len(kb.get("chunks", []))
    kb["chunks"] = [c for c in kb.get("chunks", []) if c.get("id") != chunk_id]
    changed = len(kb["chunks"]) != before
    if changed:
        kb["built_at"] = datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")
        _save(KB_PATH, kb)
    return changed


def _score_keyword(query_terms: set[str], chunk: dict) -> float:
    hay = set(chunk.get("tags", [])) | set(chunk.get("summary", "").lower().split())
    if not hay or not query_terms:
        return 0.0
    overlap = len(query_terms & {h.lower() for h in hay})
    return min(1.0, overlap / max(1, len(query_terms)))


def retrieve(strategy: str, target: str | None, top_k: int, max_tokens: int) -> list[dict]:
    """Real retrieval over kb.index.json. 'semantic' currently falls back to
    keyword scoring (documented in retrieval/strategies.json) until an
    embedding provider is configured -- it does not silently return raw
    file content under any strategy.
    """
    kb = _load(KB_PATH)
    chunks = kb.get("chunks", [])
    if not chunks or not target:
        return []

    query_terms = {t.lower() for t in target.replace("_", " ").split()}
    scored = []
    for chunk in chunks:
        if strategy == "exact":
            score = 1.0 if chunk.get("id") == target else 0.0
        else:  # keyword / semantic(fallback) / hybrid
            score = _score_keyword(query_terms, chunk)
        if score > 0:
            scored.append((score, chunk))

    scored.sort(key=lambda x: x[0], reverse=True)
    results = []
    budget = max_tokens
    for score, chunk in scored[:top_k]:
        content = json.dumps(chunk.get("summary", ""))[:MAX_CONTENT_LEN]
        approx_tokens = len(content) // 4
        if approx_tokens > budget:
            break
        budget -= approx_tokens
        results.append({
            "source": chunk.get("id", "unknown"),
            "score": round(score, 4),
            "content": chunk.get("summary", "")[:MAX_CONTENT_LEN],
            "strategy": strategy,
        })
    return results


def apply_knowledge_op(op: dict) -> dict:
    graph = _load(GRAPH_PATH)
    graph.setdefault("graph", {}).setdefault("nodes", [])
    graph["graph"].setdefault("edges", [])
    action = op["op"]

    if action == "add_node":
        node = {k: v for k, v in op.items() if k not in ("op", "edge_to", "edge_kind")}
        graph["graph"]["nodes"] = [n for n in graph["graph"]["nodes"] if n.get("id") != node["id"]]
        graph["graph"]["nodes"].append(node)
    elif action == "update_node":
        for n in graph["graph"]["nodes"]:
            if n.get("id") == op["id"]:
                n.update({k: v for k, v in op.items() if k not in ("op", "edge_to", "edge_kind")})
    elif action == "remove_node":
        graph["graph"]["nodes"] = [n for n in graph["graph"]["nodes"] if n.get("id") != op["id"]]
    elif action == "add_edge":
        graph["graph"]["edges"].append({
            "from": op["id"], "to": op.get("edge_to"), "kind": op.get("edge_kind", "related"),
        })

    graph["updated_at"] = datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")
    _save(GRAPH_PATH, graph)
    return graph


def symbol_lookup(target: str) -> list[dict]:
    symbols = _load(SYMBOLS_PATH).get("symbols", [])
    target_l = target.lower()
    return [s for s in symbols if target_l in s.get("name", "").lower() or target_l in (s.get("tags") or [])]
