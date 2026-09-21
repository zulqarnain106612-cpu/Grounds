"""The C# coverage reporter: what it prints, and what it refuses to touch.

CodeQL raised three high `py/path-injection` alerts on this script when it
took an artifacts *path* from argv and opened `$GITHUB_STEP_SUMMARY` itself.
Neither was attacker-controlled -- the only caller is unity-test.yml passing
a matrix value -- but both sinks were removable without contorting anything,
so they were removed rather than annotated:

- the artifacts directory is built from a mode validated against `MODES`, so
  no path is derived from input;
- the job summary is written by a shell redirect in the workflow, so this
  process never opens a path from the environment.

The reporter must never gate. Every case here exits 0 or 2, never raises: a
missing or unreadable coverage number is not a reason to fail a run whose
tests passed.
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


def _workspace(tmp_path: Path, mode: str = "editmode") -> Path:
    """A working directory shaped like the runner's, with a coverage report.

    Not tmp_path/"repo": conftest's autouse `isolated_repo` fixture already
    owns that name for every test.
    """
    ws = tmp_path / "ws"
    report = ws / f"{mode}-artifacts" / "Report"
    report.mkdir(parents=True)
    (report / "Summary.xml").write_text(SUMMARY_XML, encoding="utf-8")
    return ws


def _run(monkeypatch, ws: Path, *argv: str) -> int:
    module = _load()
    monkeypatch.chdir(ws)
    return module.main(["report_unity_coverage.py", *argv])


def test_it_reports_the_number_for_a_real_run(tmp_path, monkeypatch, capsys):
    ws = _workspace(tmp_path)
    assert _run(monkeypatch, ws, "editmode") == 0
    out = capsys.readouterr().out
    assert "73.4" in out
    assert "C# coverage -- editmode" in out


def test_each_declared_mode_resolves_its_own_artifacts(tmp_path, monkeypatch, capsys):
    """playmode must not read editmode's report, or the two legs would agree
    by accident and the number would stop meaning anything."""
    module = _load()
    ws = tmp_path / "ws"
    for mode, value in (("editmode", "11.1"), ("playmode", "22.2")):
        report = ws / f"{mode}-artifacts" / "Report"
        report.mkdir(parents=True)
        report.joinpath("Summary.xml").write_text(
            SUMMARY_XML.replace("73.4", value), encoding="utf-8"
        )
    monkeypatch.chdir(ws)
    for mode, value in (("editmode", "11.1"), ("playmode", "22.2")):
        assert module.main(["report_unity_coverage.py", mode]) == 0
        assert value in capsys.readouterr().out


def test_an_unknown_mode_is_refused(tmp_path, monkeypatch, capsys):
    """The check that keeps an arbitrary path out of this script."""
    ws = _workspace(tmp_path)
    for bad in ("../../etc", "editmode-artifacts", "", "EditMode"):
        assert _run(monkeypatch, ws, bad) == 2
        assert "unknown test mode" in capsys.readouterr().err


def test_no_arguments_is_refused(tmp_path, monkeypatch, capsys):
    ws = _workspace(tmp_path)
    assert _run(monkeypatch, ws) == 2
    assert "usage:" in capsys.readouterr().err


def test_a_missing_artifacts_directory_is_reported_not_raised(
    tmp_path, monkeypatch, capsys
):
    """The Unity runner can die before writing anything; that is the gate's
    business, not this reporter's."""
    ws = _workspace(tmp_path, mode="editmode")
    assert _run(monkeypatch, ws, "playmode") == 0
    assert "No coverage summary was produced" in capsys.readouterr().out


def test_an_unreadable_summary_is_reported_not_raised(tmp_path, monkeypatch, capsys):
    ws = tmp_path / "ws"
    report = ws / "editmode-artifacts" / "Report"
    report.mkdir(parents=True)
    report.joinpath("Summary.xml").write_text("not xml at all", encoding="utf-8")
    assert _run(monkeypatch, ws, "editmode") == 0
    assert "could not read a line-coverage figure" in capsys.readouterr().out


def test_the_script_opens_no_path_from_the_environment() -> None:
    """The second removed sink. A future edit that reopens
    $GITHUB_STEP_SUMMARY here brings the CodeQL alert back with it; the
    workflow redirects instead.
    """
    source = SCRIPT.read_text(encoding="utf-8")
    # The docstring names the variable to explain who writes it, so the check
    # is on what the code does: no os import, no environment read, no open().
    assert "import os" not in source
    assert "os.environ" not in source
    assert "open(" not in source

    workflow = (REPO_ROOT / ".github/workflows/unity-test.yml").read_text(
        encoding="utf-8"
    )
    assert 'report_unity_coverage.py "${{ matrix.testMode }}" | tee -a' in workflow
