from __future__ import annotations
import hashlib
import json
import os
import re
from datetime import datetime, timezone
from pathlib import Path

_ENV_ROOT = os.environ.get("JFA_REPO_ROOT")
ROOT = Path(_ENV_ROOT) if _ENV_ROOT else Path(__file__).resolve().parent.parent
KB_PATH = ROOT / "index" / "kb.index.json"
GRAPH_PATH = ROOT / "knowledge" / "graph.json"
CONFIG_PATH = ROOT / "config" / "agent.config.json"

MAX_SUMMARY_LEN = 2000  # matches RetrievedChunk.content maxLength in the schema

_HEADING_RE = re.compile(r"^(#{1,3})\s+(.+?)\s*$")
_WORD_RE = re.compile(r"[a-z][a-z0-9_]{2,}")

_STOPWORDS = {
    "the", "and", "for", "that", "this", "with", "are", "was", "not", "but", "you",
    "all", "any", "can", "has", "have", "from", "its", "via", "per", "use", "used",
    "when", "what", "which", "into", "than", "then", "them", "they", "there", "here",
}


def _tags_from(text: str, limit: int = 12) -> list[str]:
    """Deterministic keyword tags. Frequency-ranked, ties broken alphabetically
    so the same input always yields byte-identical output in CI."""
    counts: dict[str, int] = {}
    for word in _WORD_RE.findall(text.lower()):
        if word in _STOPWORDS:
            continue
        counts[word] = counts.get(word, 0) + 1
    ranked = sorted(counts.items(), key=lambda kv: (-kv[1], kv[0]))
    return [w for w, _ in ranked[:limit]]


def _chunk(chunk_id: str, summary: str, source: str, kind: str, extra_tags: list[str]) -> dict:
    summary = summary.strip()[:MAX_SUMMARY_LEN]
    tags = sorted(set(extra_tags) | set(_tags_from(summary)))
    return {
        "id": chunk_id,
        "kind": kind,
        "source": source,
        "summary": summary,
        "tags": tags,
        "checksum": hashlib.sha256(summary.encode("utf-8")).hexdigest()[:16],
    }


def _ingest_markdown(repo_root: Path) -> list[dict]:
    """One chunk per H1/H2/H3 section of every tracked markdown doc.

    Reads files only to build metadata + a bounded summary; raw file bytes are
    never returned to a caller. Retrieval serves the summary, not the file.
    """
    chunks: list[dict] = []
    doc_paths = sorted(repo_root.glob("docs/*.md"))
    readme = repo_root / "README.md"
    if readme.exists():
        doc_paths.insert(0, readme)

    for path in doc_paths:
        rel = str(path.relative_to(repo_root))
        try:
            lines = path.read_text(encoding="utf-8", errors="replace").splitlines()
        except OSError:
            continue

        current_title = None
        current_line = 1
        buffer: list[str] = []

        def flush():
            if current_title is None or not buffer:
                return
            slug = re.sub(r"[^a-z0-9]+", "_", current_title.lower()).strip("_")
            chunks.append(_chunk(
                chunk_id=f"doc:{rel}#{slug}",
                summary=f"{current_title}. " + " ".join(buffer),
                source=f"{rel}:{current_line}",
                kind="doc_section",
                extra_tags=["doc", Path(rel).stem.lower()],
            ))

        for i, line in enumerate(lines, start=1):
            heading = _HEADING_RE.match(line)
            if heading:
                flush()
                current_title = heading.group(2)
                current_line = i
                buffer = []
            elif line.strip():
                buffer.append(line.strip())
        flush()
    return chunks


def _ingest_schema(repo_root: Path) -> list[dict]:
    """One chunk per schema action, domain and definition, so `retrieve` can
    answer questions about the contract itself."""
    schema = json.loads((repo_root / "schema" / "agent.schema.json").read_text())
    chunks: list[dict] = []

    intent = schema["properties"]["intent"]["properties"]
    for action in intent["action"]["enum"]:
        chunks.append(_chunk(
            chunk_id=f"schema:action:{action}",
            summary=(
                f"Agent action '{action}'. A request with intent.action={action} is "
                f"dispatched by gateway/handlers.py DISPATCH to its handler. Every action "
                f"has a worked request example in schema/examples.json under key '{action}'."
            ),
            source="schema/agent.schema.json",
            kind="schema_action",
            extra_tags=["schema", "action", action],
        ))

    for domain in intent["domain"]["enum"]:
        chunks.append(_chunk(
            chunk_id=f"schema:domain:{domain}",
            summary=(
                f"Agent domain '{domain}'. Set intent.domain={domain} to scope a request "
                f"to the {domain.replace('_', ' ')} area of the project."
            ),
            source="schema/agent.schema.json",
            kind="schema_domain",
            extra_tags=["schema", "domain", domain],
        ))

    for name, body in schema.get("definitions", {}).items():
        if not isinstance(body, dict):
            continue
        required = body.get("required", [])
        props = sorted(body.get("properties", {}).keys())
        chunks.append(_chunk(
            chunk_id=f"schema:definition:{name}",
            summary=(
                f"Schema definition {name}. Required fields: {', '.join(required) or 'none'}. "
                f"Available fields: {', '.join(props) or 'n/a'}."
            ),
            source="schema/agent.schema.json",
            kind="schema_definition",
            extra_tags=["schema", "definition", name.lower()],
        ))
    return chunks


def _ingest_configs(repo_root: Path) -> list[dict]:
    """Chunks for retrieval strategies and registered daemons."""
    chunks: list[dict] = []

    strategies_path = repo_root / "retrieval" / "strategies.json"
    if strategies_path.exists():
        data = json.loads(strategies_path.read_text())
        for name, body in (data.get("strategies") or {}).items():
            desc = body.get("description", "") if isinstance(body, dict) else str(body)
            chunks.append(_chunk(
                chunk_id=f"retrieval:strategy:{name}",
                summary=f"Retrieval strategy '{name}'. {desc}",
                source="retrieval/strategies.json",
                kind="retrieval_strategy",
                extra_tags=["retrieval", "strategy", name],
            ))

    registry_path = repo_root / "daemons" / "registry.json"
    if registry_path.exists():
        data = json.loads(registry_path.read_text())
        daemons = data.get("daemons") or {}
        items = daemons.items() if isinstance(daemons, dict) else (
            (d.get("id", str(i)), d) for i, d in enumerate(daemons)
        )
        for name, body in items:
            desc = body.get("description", "") if isinstance(body, dict) else str(body)
            chunks.append(_chunk(
                chunk_id=f"daemon:{name}",
                summary=f"Daemon '{name}'. {desc}",
                source="daemons/registry.json",
                kind="daemon",
                extra_tags=["daemon", name],
            ))
    return chunks


def _ingest_symbols(repo_root: Path) -> list[dict]:
    """Chunks derived from the generated symbol index -- metadata only, never
    source text, so the no-raw-code guarantee still holds."""
    symbols_path = repo_root / "symbols" / "index.json"
    if not symbols_path.exists():
        return []
    symbols = json.loads(symbols_path.read_text()).get("symbols", [])
    chunks = []
    for sym in symbols:
        ns = sym.get("namespace") or ""
        chunks.append(_chunk(
            chunk_id=f"symbol:{ns}.{sym['name']}" if ns else f"symbol:{sym['name']}",
            summary=(
                f"{sym['kind'].capitalize()} {sym['name']}"
                + (f" in namespace {ns}" if ns else "")
                + f", declared at {sym['file']} line {sym['line']}."
            ),
            source=f"{sym['file']}:{sym['line']}",
            kind="symbol",
            extra_tags=["symbol", sym["kind"], sym["name"].lower()],
        ))
    return chunks


def build_kb_index(repo_root: Path | None = None) -> dict:
    """Rebuild index/kb.index.json from docs, schema, configs and symbols.

    Deterministic and idempotent: chunks are sorted by id and the timestamp is
    the only field that moves, so CI can diff the chunk list for staleness.
    """
    repo_root = repo_root or ROOT
    chunks = (
        _ingest_markdown(repo_root)
        + _ingest_schema(repo_root)
        + _ingest_configs(repo_root)
        + _ingest_symbols(repo_root)
    )
    chunks.sort(key=lambda c: c["id"])

    index = {
        "version": "1.1.0",
        "built_at": datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
        "generated_by": "gateway/ingest.py",
        "note": "Auto-generated by the ingest workflow. Never hand-edit.",
        "chunk_count": len(chunks),
        "chunks": chunks,
    }
    (repo_root / "index" / "kb.index.json").write_text(json.dumps(index, indent=2) + "\n")
    return index


def build_graph(repo_root: Path | None = None) -> dict:
    """Derive the knowledge graph from the freshly built KB index.

    Nodes: one per schema domain, plus one per ingested chunk kind.
    Edges: chunk-kind -> domain where a chunk's tags name that domain.
    """
    repo_root = repo_root or ROOT
    kb = json.loads((repo_root / "index" / "kb.index.json").read_text())
    schema = json.loads((repo_root / "schema" / "agent.schema.json").read_text())
    domains = schema["properties"]["intent"]["properties"]["domain"]["enum"]

    nodes = [
        {"id": f"domain:{d}", "type": "domain", "label": d.replace("_", " ").title(), "tags": [d]}
        for d in domains
    ]
    kinds = sorted({c["kind"] for c in kb.get("chunks", [])})
    nodes += [
        {"id": f"kind:{k}", "type": "chunk_kind", "label": k.replace("_", " ").title(), "tags": [k]}
        for k in kinds
    ]

    edges = []
    seen: set[tuple[str, str]] = set()
    for chunk in kb.get("chunks", []):
        for domain in domains:
            if domain in chunk.get("tags", []):
                key = (f"kind:{chunk['kind']}", f"domain:{domain}")
                if key not in seen:
                    seen.add(key)
                    edges.append({"from": key[0], "to": key[1], "kind": "covers"})
    edges.sort(key=lambda e: (e["from"], e["to"]))

    graph = {
        "version": "1.1.0",
        "updated_at": datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
        "generated_by": "gateway/ingest.py",
        "note": "Auto-generated by the ingest workflow from index/kb.index.json. Never hand-edit.",
        "graph": {"nodes": nodes, "edges": edges},
    }
    (repo_root / "knowledge" / "graph.json").write_text(json.dumps(graph, indent=2) + "\n")
    return graph


def main() -> int:
    kb = build_kb_index()
    graph = build_graph()
    print(f"ingested {kb['chunk_count']} kb chunks -> {KB_PATH}")
    print(f"built graph: {len(graph['graph']['nodes'])} nodes, "
          f"{len(graph['graph']['edges'])} edges -> {GRAPH_PATH}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
