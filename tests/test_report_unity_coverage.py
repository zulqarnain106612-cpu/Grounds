"""Path containment in the C# coverage reporter.

CodeQL flagged three "uncontrolled data used in path expression" alerts here:
the artifacts directory comes from argv and the summary target from
GITHUB_STEP_SUMMARY. Neither is attacker-controlled in this repo -- the only
caller is unity-test.yml passing a matrix literal -- but the containment
checks are cheap and hold for whatever calls this later, so they are pinned
rather than left to the next reader's goodwill.

The reporter must never gate: every case below exits 0, because a missing or
unreadable coverage number is not a reason to fail a run whose tests passed.
"""

from __future__ import annotations

import importlib.util
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[1]
SCRIPT = REPO_ROOT / "scripts" / "report_unity_coverage.py"

SUMMARY_XML = (
    "<CoverageReport><Summary><Linecoverage>73.4</Linecoverage>"
    "</Summary></CoverageReport>"
)


def _load():
    spec = importlib.util.spec_from_file_location("report_unity_coverage", SCRIPT)
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


def _workspace(tmp_path: Path) -> tuple[Path, Path]:
    """A workspace laid out the way the runner lays one out.

    Not tmp_path/"repo": conftest's autouse `isolated_repo` fixture already
    owns that name for every test.
    """
    ws = tmp_path / "ws"
    (ws / "editmode-artifacts" / "Report").mkdir(parents=True)
    (ws / "editmode-artifacts" / "Report" / "Summary.xml").write_text(
        SUMMARY_XML, encoding="utf-8"
    )
    temp = tmp_path / "runner_temp"
    temp.mkdir()
    return ws, temp


def _run(monkeypatch, ws: Path, temp: Path, artifacts: str, step_summary: Path | None):
    module = _load()
    monkeypatch.chdir(ws)
    monkeypatch.setenv("GITHUB_WORKSPACE", str(ws))
    monkeypatch.setenv("RUNNER_TEMP", str(temp))
    if step_summary is None:
        monkeypatch.delenv("GITHUB_STEP_SUMMARY", raising=False)
    else:
        monkeypatch.setenv("GITHUB_STEP_SUMMARY", str(step_summary))
    return module.main(["report_unity_coverage.py", artifacts, "editmode"])


def test_the_runner_shaped_invocation_reports_the_number(tmp_path, monkeypatch, capsys):
    ws, temp = _workspace(tmp_path)
    target = temp / "summary.md"

    assert _run(monkeypatch, ws, temp, "editmode-artifacts", target) == 0
    assert "73.4" in capsys.readouterr().out
    assert "73.4" in target.read_text(encoding="utf-8")


def test_an_artifacts_path_escaping_the_workspace_reports_nothing(
    tmp_path, monkeypatch, capsys
):
    """A traversal out of the workspace must not be searched or read."""
    ws, temp = _workspace(tmp_path)
    outside = tmp_path / "outside" / "Report"
    outside.mkdir(parents=True)
    (outside / "Summary.xml").write_text(SUMMARY_XML, encoding="utf-8")

    assert _run(monkeypatch, ws, temp, "../outside", None) == 0
    out = capsys.readouterr().out
    assert "No coverage summary was produced" in out
    assert "73.4" not in out


def test_a_step_summary_outside_the_allowed_roots_is_not_written(
    tmp_path, monkeypatch, capsys
):
    """The report still prints; only the out-of-bounds write is refused."""
    ws, temp = _workspace(tmp_path)
    forbidden = tmp_path / "forbidden.md"
    forbidden.write_text("untouched\n", encoding="utf-8")

    assert _run(monkeypatch, ws, temp, "editmode-artifacts", forbidden) == 0
    assert "73.4" in capsys.readouterr().out
    assert forbidden.read_text(encoding="utf-8") == "untouched\n"


def test_a_missing_artifacts_directory_is_reported_not_raised(
    tmp_path, monkeypatch, capsys
):
    ws, temp = _workspace(tmp_path)
    assert _run(monkeypatch, ws, temp, "playmode-artifacts", None) == 0
    assert "No coverage summary was produced" in capsys.readouterr().out
