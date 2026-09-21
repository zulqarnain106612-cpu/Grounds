#!/usr/bin/env python3
"""Say whether a fresh ingest changed anything a reviewer could act on.

Exit 0 when real content moved, 1 when only the clock did.

The ingest workflow used to gate its pull request on `git diff --cached
--quiet`, which is a byte comparison. Every generator stamps a fresh
`generated_at`/`updated_at` into its output on every run, so that comparison
was never empty and the nightly cron opened a pull request every single day
whose entire content was a moved timestamp. Ten such pull requests (#199-#208)
accumulated against a protected `main`, each touching the same four generated
files, so each also conflicted with the others.

The freshness contract those files actually serve is defined in
`.github/workflows/enforce.yml`, and it is a *content* comparison -- it reads
the same keys named in CONTENT_KEYS below and deliberately ignores timestamps.
This script applies that same definition at proposal time, so the gate that
opens a pull request and the gate that fails the build agree by construction.

`index/manifest.json` is a hash ledger over the rest of the tree, so it is
compared in full except for two things: its own `generated_at`, and the
`bytes`/`sha256` entries of the three artifacts above. Those entries move
whenever their file's embedded timestamp moves, which would reintroduce the
very churn this script exists to suppress; a real change to any of those three
is already caught by its own content keys.
"""

from __future__ import annotations

import json
import subprocess
import sys

# Mirrors the staleness check in .github/workflows/enforce.yml. Keep the two
# in step: a key compared there and not here lets a stale index reach main
# with no pull request proposing the fix.
CONTENT_KEYS = {
    "symbols/index.json": ("symbols",),
    "index/kb.index.json": ("chunks", "chunk_count"),
    "knowledge/graph.json": ("graph",),
}

MANIFEST = "index/manifest.json"


def _head(path: str):
    """The committed version of `path`, or None when it is not tracked yet."""
    out = subprocess.run(
        ["git", "show", f"HEAD:{path}"], capture_output=True, text=True
    )
    if out.returncode != 0:
        return None
    return json.loads(out.stdout)


def _worktree(path: str):
    with open(path, encoding="utf-8") as fh:
        return json.load(fh)


def _manifest_signature(doc: dict) -> dict:
    """The manifest minus the fields that move on every run by construction."""
    volatile = set(CONTENT_KEYS)
    return {
        "version": doc.get("version"),
        "generated_by": doc.get("generated_by"),
        "counts": doc.get("counts"),
        "files": [f for f in doc.get("files", []) if f.get("path") not in volatile],
    }


def main() -> int:
    for path, keys in CONTENT_KEYS.items():
        before, after = _head(path), _worktree(path)
        if before is None:
            print(f"{path} is not tracked yet -- treating as a real change")
            return 0
        for key in keys:
            if before.get(key) != after.get(key):
                print(f"{path}: '{key}' changed")
                return 0

    before, after = _head(MANIFEST), _worktree(MANIFEST)
    if before is None:
        print(f"{MANIFEST} is not tracked yet -- treating as a real change")
        return 0
    if _manifest_signature(before) != _manifest_signature(after):
        print(f"{MANIFEST}: tracked file inventory or hashes changed")
        return 0

    print("only generated_at/updated_at moved; nothing worth a pull request")
    return 1


if __name__ == "__main__":
    sys.exit(main())
