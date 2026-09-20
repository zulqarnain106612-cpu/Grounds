"""The guard that decides whether an ingest run is worth a pull request.

`scripts/ingest_content_changed.py` replaced a `git diff --cached --quiet`
check that could never fire, because every generator stamps a fresh
timestamp on each run. The nightly cron therefore opened a pull request a day
carrying nothing but a moved clock (#199-#208). These tests pin the two
properties that matter: a timestamp-only refresh is suppressed, and every
kind of real change still gets through.
"""

from __future__ import annotations

import json
import subprocess
import sys
from pathlib import Path

import pytest

REPO_ROOT = Path(__file__).resolve().parents[1]
GUARD = REPO_ROOT / "scripts" / "ingest_content_changed.py"

NO_CHANGE = 1
CHANGE = 0


def _write(root: Path, rel: str, doc: dict) -> None:
    path = root / rel
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(doc, indent=2), encoding="utf-8")


def _git(root: Path, *args: str) -> None:
    subprocess.run(["git", *args], cwd=root, check=True, capture_output=True)


@pytest.fixture
def ingested(tmp_path: Path) -> Path:
    """A throwaway repo holding one committed generation of the four files."""
    root = tmp_path / "repo"
    root.mkdir()
    _git(root, "init", "-q")
    _git(root, "config", "user.email", "t@example.com")
    _git(root, "config", "user.name", "t")

    _write(root, "symbols/index.json",
           {"generated_by": "x", "updated_at": "2026-01-01T00:00:00Z",
            "symbols": [{"name": "JetController"}]})
    _write(root, "index/kb.index.json",
           {"generated_by": "x", "generated_at": "2026-01-01T00:00:00Z",
            "chunk_count": 2, "chunks": [{"id": "a"}, {"id": "b"}]})
    _write(root, "knowledge/graph.json",
           {"generated_by": "x", "updated_at": "2026-01-01T00:00:00Z",
            "graph": {"nodes": ["adr-008"]}})
    _write(root, "index/manifest.json",
           {"version": "1.1.0", "generated_at": "2026-01-01T00:00:00Z",
            "generated_by": "gateway/manifest.py", "counts": {"files": 5},
            "files": [{"path": "docs/SDLC_SPIRAL.md", "bytes": 10, "sha256": "aa"},
                      {"path": "index/kb.index.json", "bytes": 20, "sha256": "bb"},
                      {"path": "knowledge/graph.json", "bytes": 30, "sha256": "cc"},
                      {"path": "symbols/index.json", "bytes": 40, "sha256": "dd"}]})

    # The guard is invoked by absolute path with cwd set here, so only the
    # four generated files need to exist and be committed in this repo.
    _git(root, "add", "-A")
    _git(root, "commit", "-q", "-m", "seed")
    return root


def _run(root: Path) -> int:
    return subprocess.run([sys.executable, str(GUARD)], cwd=root,
                          capture_output=True, text=True).returncode


def _patch(root: Path, rel: str, **fields) -> None:
    path = root / rel
    doc = json.loads(path.read_text(encoding="utf-8"))
    doc.update(fields)
    path.write_text(json.dumps(doc, indent=2), encoding="utf-8")


def test_unchanged_tree_proposes_nothing(ingested: Path) -> None:
    assert _run(ingested) == NO_CHANGE


def test_moved_timestamps_alone_propose_nothing(ingested: Path) -> None:
    """The exact shape of #199-#208: every stamp advances, no content moves."""
    later = "2026-09-20T08:33:48Z"
    _patch(ingested, "symbols/index.json", updated_at=later)
    _patch(ingested, "knowledge/graph.json", updated_at=later)
    _patch(ingested, "index/kb.index.json", generated_at=later)
    _patch(ingested, "index/manifest.json", generated_at=later)

    # A moved timestamp inside a generated file changes that file's hash, so
    # the manifest's ledger entry for it moves too. That is still not content.
    path = ingested / "index/manifest.json"
    doc = json.loads(path.read_text(encoding="utf-8"))
    for entry in doc["files"]:
        if entry["path"] != "docs/SDLC_SPIRAL.md":
            entry["sha256"] = "rehashed-" + entry["sha256"]
    path.write_text(json.dumps(doc, indent=2), encoding="utf-8")

    assert _run(ingested) == NO_CHANGE


@pytest.mark.parametrize("rel,fields", [
    ("index/kb.index.json", {"chunk_count": 3}),
    ("index/kb.index.json", {"chunks": [{"id": "a"}, {"id": "z"}]}),
    ("knowledge/graph.json", {"graph": {"nodes": ["adr-008", "adr-009"]}}),
    ("symbols/index.json", {"symbols": [{"name": "QualityTierManager"}]}),
])
def test_real_content_change_is_proposed(ingested: Path, rel: str, fields: dict) -> None:
    _patch(ingested, rel, **fields)
    assert _run(ingested) == CHANGE


def test_manifest_inventory_change_is_proposed(ingested: Path) -> None:
    """A source file ingest does not chunk still reaches the manifest ledger."""
    path = ingested / "index/manifest.json"
    doc = json.loads(path.read_text(encoding="utf-8"))
    doc["files"].append({"path": "gateway/handlers.py", "bytes": 99, "sha256": "ee"})
    path.write_text(json.dumps(doc, indent=2), encoding="utf-8")
    assert _run(ingested) == CHANGE


def test_hash_change_of_an_unrelated_tracked_file_is_proposed(ingested: Path) -> None:
    path = ingested / "index/manifest.json"
    doc = json.loads(path.read_text(encoding="utf-8"))
    for entry in doc["files"]:
        if entry["path"] == "docs/SDLC_SPIRAL.md":
            entry["sha256"] = "changed"
    path.write_text(json.dumps(doc, indent=2), encoding="utf-8")
    assert _run(ingested) == CHANGE


def test_guard_keys_match_the_enforce_staleness_check() -> None:
    """The proposal gate and the build gate must agree on what 'fresh' means.

    enforce.yml fails the build when these keys differ from a fresh ingest. If
    the guard stopped comparing one of them, a genuinely stale index would be
    suppressed here and then fail every build with no pull request offering
    the fix.
    """
    enforce = (REPO_ROOT / ".github/workflows/enforce.yml").read_text(encoding="utf-8")
    for path, keys in (("symbols/index.json", ["symbols"]),
                       ("index/kb.index.json", ["chunks", "chunk_count"]),
                       ("knowledge/graph.json", ["graph"])):
        assert f'("{path}", [' in enforce, f"enforce.yml no longer checks {path}"
        for key in keys:
            assert f'"{key}"' in enforce

    guard = GUARD.read_text(encoding="utf-8")
    assert '"symbols/index.json": ("symbols",)' in guard
    assert '"index/kb.index.json": ("chunks", "chunk_count")' in guard
    assert '"knowledge/graph.json": ("graph",)' in guard
