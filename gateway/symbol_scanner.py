from __future__ import annotations
import json
import os
import re
from datetime import datetime, timezone
from pathlib import Path

_ENV_ROOT = os.environ.get("JFA_REPO_ROOT")
ROOT = Path(_ENV_ROOT) if _ENV_ROOT else Path(__file__).resolve().parent.parent
SYMBOLS_PATH = ROOT / "symbols" / "index.json"
CONFIG_PATH = ROOT / "config" / "agent.config.json"

# Matches: [access] [modifiers] (class|interface|enum|struct) Name
_TYPE_RE = re.compile(
    r"^\s*(?:public|private|protected|internal|static|sealed|abstract|partial|\s)*"
    r"(class|interface|enum|struct)\s+([A-Za-z_][A-Za-z0-9_]*)",
)
# Matches: [access] [modifiers] ReturnType MethodName(
_METHOD_RE = re.compile(
    r"^\s*(?:public|private|protected|internal|static|virtual|override|async|\s)+"
    r"[A-Za-z_][A-Za-z0-9_<>\[\],\.\s]*\s+([A-Za-z_][A-Za-z0-9_]*)\s*\([^;{]*\)\s*\{?\s*$"
)
_NAMESPACE_RE = re.compile(r"^\s*namespace\s+([A-Za-z0-9_.]+)")


def _kind_for(match_kind: str) -> str:
    return {"class": "class", "interface": "interface", "enum": "enum", "struct": "struct"}[match_kind]


def scan_file(path: Path, repo_root: Path | None = None) -> list[dict]:
    """Return symbol metadata (name/kind/file/line/namespace) for one source file.

    Never returns file content. Reads the file only to compute line-numbered
    metadata; the raw text never leaves this function. `file` is always
    stored repo-relative so the index is portable across machines/CI.
    """
    symbols: list[dict] = []
    namespace = None
    try:
        rel = str(path.relative_to(repo_root)) if repo_root else str(path)
    except ValueError:
        rel = str(path)
    try:
        lines = path.read_text(encoding="utf-8", errors="replace").splitlines()
    except OSError:
        return symbols

    for i, line in enumerate(lines, start=1):
        ns_match = _NAMESPACE_RE.match(line)
        if ns_match:
            namespace = ns_match.group(1)
            continue
        type_match = _TYPE_RE.match(line)
        if type_match:
            symbols.append({
                "name": type_match.group(2),
                "kind": _kind_for(type_match.group(1)),
                "file": rel,
                "line": i,
                "namespace": namespace,
                "tags": [],
            })
            continue
        method_match = _METHOD_RE.match(line)
        if method_match and namespace:
            symbols.append({
                "name": method_match.group(1),
                "kind": "method",
                "file": rel,
                "line": i,
                "namespace": namespace,
                "tags": [],
            })
    return symbols


def rebuild_symbol_index(repo_root: Path | None = None) -> dict:
    """Rebuild symbols/index.json from configured source_globs. Idempotent."""
    repo_root = repo_root or ROOT
    cfg = json.loads(CONFIG_PATH.read_text())
    globs = cfg.get("source_globs", [])
    all_symbols: list[dict] = []
    for pattern in globs:
        for path in sorted(repo_root.glob(pattern)):
            if path.is_file():
                all_symbols.extend(scan_file(path, repo_root))

    index = {
        "version": "1.1.0",
        "updated_at": datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
        "generated_by": "gateway/symbol_scanner.py",
        "note": "Auto-generated. Never hand-edit.",
        "symbols": all_symbols,
    }
    SYMBOLS_PATH.write_text(json.dumps(index, indent=2) + "\n")
    return index


if __name__ == "__main__":
    result = rebuild_symbol_index()
    print(f"indexed {len(result['symbols'])} symbols -> {SYMBOLS_PATH}")
