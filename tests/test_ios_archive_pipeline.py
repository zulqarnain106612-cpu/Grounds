"""The export and archive reporters, and the workflow that calls them.

Both scripts gate: unlike the coverage reporter, a missing Xcode project or an
unsigned payload is not a missing nice-to-have, it is the artefact the next
step consumes. Every case below is a way a step succeeds while producing
something unusable, which is the only failure mode worth a reporter at all.

Neither script takes a path from argv or the environment. That is what keeps
CodeQL's py/path-injection out of this pair, and a test asserts it rather than
leaving it to reviewers -- the same alert cost several rounds on
report_unity_coverage.py.
"""

from __future__ import annotations

import importlib.util
import plistlib
import sys
from pathlib import Path

import pytest

REAL_ROOT = Path(__file__).resolve().parent.parent
EXPORT = REAL_ROOT / "scripts" / "check_ios_export.py"
ARCHIVE = REAL_ROOT / "scripts" / "check_archive.py"
WORKFLOW = REAL_ROOT / ".github" / "workflows" / "ios-build.yml"


def _load(path: Path):
    spec = importlib.util.spec_from_file_location(path.stem, path)
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


@pytest.fixture
def workspace(tmp_path, monkeypatch) -> Path:
    """A working directory shaped like the runner's.

    Not tmp_path/"repo": conftest's autouse `isolated_repo` fixture owns that
    name for every test.
    """
    ws = tmp_path / "runner"
    ws.mkdir()
    monkeypatch.chdir(ws)
    return ws


def _complete_export(ws: Path) -> Path:
    export = ws / "build" / "iOS"
    (export / "Unity-iPhone.xcodeproj").mkdir(parents=True)
    (export / "Unity-iPhone.xcodeproj" / "project.pbxproj").write_text("// pbxproj")
    (export / "Data").mkdir()
    (export / "Classes").mkdir()
    (export / "Info.plist").write_text("<plist/>")
    return export


def _signed_archive(ws: Path) -> Path:
    app = ws / "build" / "JetFighter.xcarchive" / "Products" / "Applications" / "JetFighter.app"
    app.mkdir(parents=True)
    (app / "embedded.mobileprovision").write_bytes(b"profile")
    (app / "_CodeSignature").mkdir()
    info = ws / "build" / "JetFighter.xcarchive" / "Info.plist"
    with info.open("wb") as handle:
        plistlib.dump({"ApplicationProperties": {
            "CFBundleIdentifier": "com.conquerer.jetfighter",
            "CFBundleShortVersionString": "1.0",
            "CFBundleVersion": "7",
        }}, handle)
    return app


# --- the export reporter ----------------------------------------------------

def test_a_complete_export_passes_and_reports_its_size(workspace, capsys):
    _complete_export(workspace)
    assert _load(EXPORT).main() == 0
    out = capsys.readouterr().out
    assert "Ready to archive" in out
    assert "MB" in out


def test_no_export_directory_is_a_failure(workspace, capsys):
    """Batchmode Unity exits 0 on a failed build unless the entry point stops
    it, so 'the step was green' says nothing about whether a tree exists."""
    assert _load(EXPORT).main() == 1
    assert "::error::" in capsys.readouterr().out


@pytest.mark.parametrize("missing", [
    "Unity-iPhone.xcodeproj/project.pbxproj",
    "Data",
    "Classes",
    "Info.plist",
])
def test_each_missing_piece_is_named(workspace, capsys, missing):
    """Each has its own downstream symptom -- an empty .xcodeproj bundle is a
    parse error from xcodebuild, missing Data is an app that launches to
    nothing -- so naming the piece is what saves the macOS job."""
    export = _complete_export(workspace)
    target = export / missing
    if target.is_dir():
        target.rmdir()
    else:
        target.unlink()
    assert _load(EXPORT).main() == 1
    assert missing.split("/")[-1] in capsys.readouterr().out


# --- the archive reporter ---------------------------------------------------

def test_a_signed_archive_passes_and_reports_the_build_number(workspace, capsys):
    _signed_archive(workspace)
    assert _load(ARCHIVE).main() == 0
    out = capsys.readouterr().out
    # A rejected upload is very often a build number that did not move, and
    # the number is otherwise visible only inside Xcode.
    assert "com.conquerer.jetfighter 1.0 (7)" in out


def test_no_archive_is_a_failure(workspace, capsys):
    assert _load(ARCHIVE).main() == 1
    assert "produced no archive" in capsys.readouterr().out


def test_an_archive_with_no_app_is_a_failure(workspace, capsys):
    (workspace / "build" / "JetFighter.xcarchive" / "Products" / "Applications").mkdir(parents=True)
    assert _load(ARCHIVE).main() == 1
    assert "no .app" in capsys.readouterr().out


def test_an_unsigned_payload_is_refused(workspace, capsys):
    """The failure this reporter exists for: xcodebuild succeeds, the archive
    looks complete, and App Store Connect rejects it at upload."""
    app = _signed_archive(workspace)
    (app / "_CodeSignature").rmdir()
    assert _load(ARCHIVE).main() == 1
    assert "never signed" in capsys.readouterr().out


def test_a_missing_provisioning_profile_is_refused(workspace, capsys):
    """-allowProvisioningUpdates silently obtaining nothing is the whole risk
    of cloud signing, and it is invisible until install time."""
    app = _signed_archive(workspace)
    (app / "embedded.mobileprovision").unlink()
    assert _load(ARCHIVE).main() == 1
    assert "embedded.mobileprovision" in capsys.readouterr().out


def test_an_unreadable_archive_plist_does_not_raise(workspace, capsys):
    """A reporter that throws turns a signing problem into a traceback, and
    the traceback is what gets read."""
    app = _signed_archive(workspace)
    (app.parents[2] / "Info.plist").write_text("not a plist")
    assert _load(ARCHIVE).main() == 0
    assert "unknown" in capsys.readouterr().out


# --- the workflow that calls them -------------------------------------------

@pytest.mark.parametrize("script,constant", [
    (EXPORT, 'EXPORT_DIR = Path("build/iOS")'),
    (ARCHIVE, 'ARCHIVE = Path("build/JetFighter.xcarchive")'),
])
def test_neither_script_takes_a_path_from_input(script, constant):
    source = script.read_text(encoding="utf-8")
    assert constant in source
    assert "sys.argv" not in source
    assert "os.environ" not in source
    assert "argparse" not in source


def test_the_workflow_paths_match_the_scripts_constants():
    """The drift that makes both reporters useless: xcodebuild writing its
    archive somewhere the checker does not look would report 'no archive' on
    a perfectly good build, every time."""
    workflow = WORKFLOW.read_text(encoding="utf-8")
    assert "-archivePath build/JetFighter.xcarchive" in workflow
    assert "buildsPath: build" in workflow
    assert "path: build/iOS" in workflow


def test_the_expensive_jobs_are_gated_on_the_cheap_one():
    """Ordering is the whole economy of this workflow: a macOS runner is
    roughly ten times a Linux one, so a bad secret must fail on Linux."""
    workflow = WORKFLOW.read_text(encoding="utf-8")
    assert "needs: apple-credentials" in workflow
    assert "needs: export" in workflow
    assert "runs-on: macos-latest" in workflow


def test_the_signing_key_is_written_outside_the_workspace_and_removed():
    """The workspace is what upload-artifact collects. A private key written
    there leaves the runner inside an artifact that outlives the job."""
    workflow = WORKFLOW.read_text(encoding="utf-8")
    assert '$RUNNER_TEMP/private_keys' in workflow
    assert "rm -f" in workflow
    assert "chmod 600" in workflow


def test_the_third_party_builder_is_pinned_to_a_commit():
    """A floating tag on a third-party action is an unreviewed code path into
    a job holding a Unity credential."""
    workflow = WORKFLOW.read_text(encoding="utf-8")
    import re
    for uses in re.findall(r"uses:\s*(game-ci/[^\s]+)", workflow):
        _, _, ref = uses.partition("@")
        assert len(ref) == 40 and all(c in "0123456789abcdef" for c in ref), uses


def test_nothing_in_this_workflow_can_report_as_skipped():
    """A skipped check is not a verdict.

    The earlier version ran the credential check on pull requests touching it
    and held the expensive jobs behind `if: github.event_name ==
    'workflow_dispatch'`. That put two permanently grey squares on every such
    pull request: neither pass nor fail, not requirable, and meaningless
    without knowing why they are grey.

    What a pull request can verify about this workflow, the suites under
    `validate` verify -- this file is most of them. What it cannot (are the
    secrets real, does Unity export, does Xcode sign) needs a dispatch either
    way. So the trigger goes, rather than the jobs being conditioned.
    """
    # Comments are stripped first: this file explains both defects at length,
    # and a test that matched prose would fail on its own documentation.
    code = "\n".join(
        line for line in WORKFLOW.read_text(encoding="utf-8").splitlines()
        if not line.lstrip().startswith("#")
    )
    trigger = code.split("permissions:", 1)[0]
    assert "workflow_dispatch:" in trigger
    for never in ("pull_request:", "push:", "schedule:"):
        assert never not in trigger, (
            f"{never} would make this workflow's jobs appear on pull requests, "
            f"where they can only be skipped or spend a Unity seat"
        )
    assert "if: github.event_name" not in code, (
        "an event-conditioned job reports as skipped rather than absent"
    )
