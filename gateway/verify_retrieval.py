from __future__ import annotations
import json
import os
import sys
import uuid
from datetime import datetime, timezone
from pathlib import Path

_ENV_ROOT = os.environ.get("JFA_REPO_ROOT")
ROOT = Path(_ENV_ROOT) if _ENV_ROOT else Path(__file__).resolve().parent.parent

# (label, action, strategy, target, minimum expected hits)
PROBES = [
    ("exact chunk id",      "retrieve", "exact",   "schema:action:retrieve", 1),
    ("keyword over schema", "retrieve", "keyword", "daemon status", 1),
    ("keyword over docs",   "retrieve", "keyword", "enforcement schema", 1),
    ("hybrid over configs", "query",    "hybrid",  "retrieval strategy", 1),
]


def _request(action: str, strategy: str, target: str) -> dict:
    return {
        "meta": {
            "schema_version": "1.1.0",
            "session_id": str(uuid.UUID(int=0)),
            "tick": 0,
            "phase": "1",
            "timestamp_utc": datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
        },
        "intent": {"action": action, "domain": "retrieval", "priority": 5},
        "payload": {
            "data": None,
            "context": {
                "strategy": strategy, "scope": "kb",
                "target": target, "top_k": 5, "max_tokens": 1500,
            },
        },
    }


def run_probes() -> tuple[list[dict], list[str]]:
    """Drive real retrieval through the full gateway (schema validation included)
    and report what came back. Returns (results, failures)."""
    from . import gateway

    results: list[dict] = []
    failures: list[str] = []

    kb_path = ROOT / "index" / "kb.index.json"
    kb = json.loads(kb_path.read_text()) if kb_path.exists() else {}
    chunk_count = len(kb.get("chunks", []))
    if chunk_count == 0:
        failures.append("index/kb.index.json has 0 chunks - retrieval cannot return anything. "
                        "Run the ingest workflow.")
        return results, failures

    for label, action, strategy, target, min_hits in PROBES:
        envelope = gateway.handle(json.dumps(_request(action, strategy, target)))
        response = envelope.get("response", {})
        if response.get("status") != "ok":
            failures.append(f"{label}: gateway returned {response.get('status')}: "
                            f"{response.get('errors')}")
            continue
        hits = response.get("retrieved") or []
        results.append({
            "probe": label, "strategy": strategy, "target": target,
            "hits": len(hits),
            "top": hits[0]["source"] if hits else None,
            "top_score": hits[0]["score"] if hits else None,
        })
        if len(hits) < min_hits:
            failures.append(f"{label}: expected >= {min_hits} hit(s) for "
                            f"strategy={strategy} target='{target}', got {len(hits)}")
        for hit in hits:
            for field in ("source", "score", "content"):
                if field not in hit:
                    failures.append(f"{label}: retrieved chunk missing required field '{field}'")
            if not 0 <= hit.get("score", -1) <= 1:
                failures.append(f"{label}: score {hit.get('score')} outside [0,1]")
            if len(hit.get("content", "")) > 2000:
                failures.append(f"{label}: content exceeds RetrievedChunk maxLength 2000")
    return results, failures


def main() -> int:
    results, failures = run_probes()
    for r in results:
        print(f"  [{r['hits']} hit(s)] {r['probe']}: strategy={r['strategy']} "
              f"target='{r['target']}' top={r['top']} score={r['top_score']}")
    summary = os.environ.get("GITHUB_STEP_SUMMARY")
    if summary:
        with open(summary, "a") as f:
            f.write("\n## Retrieval verification\n\n| probe | strategy | hits | top match | score |\n")
            f.write("| --- | --- | --- | --- | --- |\n")
            for r in results:
                f.write(f"| {r['probe']} | {r['strategy']} | {r['hits']} | "
                        f"`{r['top']}` | {r['top_score']} |\n")
            if failures:
                f.write("\n**FAILURES**\n\n" + "\n".join(f"- {x}" for x in failures) + "\n")
    if failures:
        print("[FAIL] retrieval verification:")
        for x in failures:
            print(f"  - {x}")
        return 1
    print(f"[PASS] retrieval verification: {len(results)} probes, all returned live KB hits")
    return 0


if __name__ == "__main__":
    sys.exit(main())
