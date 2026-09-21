"""The batchmode entry point that exports the Xcode project.

`phase6/appstore-cert-checklist` closes on an Xcode archive, and nothing can
archive what Unity has not exported. No runner here boots Unity, so these
assert the properties of `IOSBuild.cs` that decide whether a green export job
means anything -- all of them are ways a batchmode build reports success and
produces nothing usable:

- Unity exits 0 whatever the BuildReport said, so the report's verdict has to
  be carried out of the process by hand;
- a build with no scenes succeeds and produces a black screen, catchable only
  by a human on a device;
- a development player has different frame times and cannot be uploaded.

These read the real file, not a fixture copy, so they assert what ships.
"""

from __future__ import annotations

import re
from pathlib import Path

import pytest

REAL_ROOT = Path(__file__).resolve().parent.parent
BUILD_SCRIPT = REAL_ROOT / "Assets" / "_Game" / "Editor" / "IOSBuild.cs"


@pytest.fixture
def source() -> str:
    return BUILD_SCRIPT.read_text(encoding="utf-8")


def test_the_entry_point_exists_and_is_editor_only(source: str) -> None:
    """Editor API in a runtime assembly fails the player build, not the
    editor, so the guard has to be the first thing in the file."""
    assert source.lstrip().startswith("#if UNITY_EDITOR")
    assert source.rstrip().endswith("#endif")
    assert "namespace JetFighter.Editor" in source
    assert "public static void PerformBuild()" in source


def test_a_failed_build_exits_nonzero(source: str) -> None:
    """The defect this file exists to prevent. Without an explicit Exit, a
    failed build leaves the job green with no Xcode project in it, and the
    failure surfaces at xcodebuild as a missing path -- which reads like a
    workflow bug rather than a build one.
    """
    assert "BuildResult.Succeeded" in source
    assert re.search(r"EditorApplication\.Exit\(\s*FailedExitCode\s*\)", source)


def test_zero_scenes_is_refused_rather_than_exported(source: str) -> None:
    """A scene-less build succeeds and launches to a black screen. There is
    no committed .unity scene in this repo yet, so this is the state a build
    would be started in today -- the refusal is what makes that visible in a
    log instead of on a device."""
    assert "scenes.Length == 0" in source
    assert re.search(r"EditorApplication\.Exit\(\s*RefusedExitCode\s*\)", source)


def test_the_refusal_and_the_failure_have_different_exit_codes(source: str) -> None:
    """'Unity could not build this' and 'there was nothing to build' need
    different fixes, so a log must not have to be read to tell them apart."""
    codes = dict(re.findall(r"const int (\w+ExitCode) = (\d+);", source))
    assert codes.get("FailedExitCode") == "1"
    assert codes.get("RefusedExitCode") == "2"


def test_player_settings_come_from_the_committed_json(source: str) -> None:
    """ADR-012: config/ios.build.json is the source of truth and
    ProjectSettings.asset is Unity-generated output. A build that used
    whatever the runner's .asset happened to hold could ship a different
    bundle id than every test in this suite checks."""
    apply_at = source.index("IOSPlayerSettings.Apply();")
    build_at = source.index("BuildPipeline.BuildPlayer")
    assert apply_at < build_at, "settings must be applied before the build"


def test_the_export_is_not_a_development_player(source: str) -> None:
    """A development player carries the profiler and the script debugger,
    which change the frame times phase6/perf-profiling-pass measures, and
    App Store Connect rejects it."""
    assert "BuildOptions.None" in source
    assert "BuildOptions.Development" not in source
    assert "allowDebugging" not in source


def test_it_builds_for_ios_and_nothing_else(source: str) -> None:
    assert "BuildTarget.iOS" in source
    assert "BuildTargetGroup.iOS" in source
    for wrong in ("BuildTarget.Android", "BuildTarget.StandaloneOSX",
                  "BuildTarget.StandaloneWindows64"):
        assert wrong not in source


def test_disabled_scenes_are_excluded(source: str) -> None:
    """The Build Settings checkbox is how a scene is taken out of a build.
    Ignoring it here would make CI build something the editor does not."""
    assert "scene.enabled" in source


def test_the_build_path_is_an_argument_with_a_default(source: str) -> None:
    """The workflow passes -buildPath; a developer running it by hand should
    not have to. A default that is not under the repo would write outside the
    workspace, so it is asserted to be relative."""
    default = re.search(r'DefaultBuildPath\s*=\s*"([^"]+)"', source)
    assert default, "IOSBuild must declare a default build path"
    assert not default.group(1).startswith("/")
    assert ".." not in default.group(1)
    assert '"-buildPath"' in source


def test_no_scene_is_committed() -> None:
    """The scene is build output, not an authored asset.

    A .unity file is Unity-generated YAML with GUID references: it merges
    badly, cannot be asserted on without booting the editor, and a broken
    reference in one produces an empty GameObject rather than an error. ADR-012
    settled that class of file for the player settings. BootstrapSceneBuilder
    composes this one from code on every build instead, into a gitignored
    directory -- so a .unity appearing under version control means somebody
    authored one by hand, which is the thing being avoided.
    """
    generated = REAL_ROOT / "Assets" / "_Game" / "Generated"
    committed = [
        p for p in REAL_ROOT.rglob("*.unity")
        if ".git" not in p.parts and generated not in p.parents
    ]
    assert committed == [], (
        f"scenes committed: {[str(p.relative_to(REAL_ROOT)) for p in committed]}. "
        f"Compose them in BootstrapSceneBuilder instead."
    )


def test_the_generated_scene_directory_is_ignored() -> None:
    """Without this line the scene is committed the first time anyone builds,
    silently, because a new .unity looks like an authored asset."""
    ignored = (REAL_ROOT / ".gitignore").read_text(encoding="utf-8").splitlines()
    assert "Assets/_Game/Generated/" in [line.strip() for line in ignored]


def test_the_exporter_generates_the_scene_before_it_checks_for_one(
    source: str,
) -> None:
    """Every CI run is a fresh checkout, so the scene never exists at the
    start of one. An exporter that expected it rather than building it would
    refuse every run, and the refusal would look like the black-screen guard
    firing correctly."""
    build_at = source.index("BootstrapSceneBuilder.Build()")
    check_at = source.index("scenes.Length == 0")
    assert build_at < check_at


def test_the_refusal_names_the_scene_builder(source: str) -> None:
    """After the builder runs, zero scenes means the builder failed -- so the
    message has to point at it rather than at File > Build Settings."""
    assert "BootstrapSceneBuilder" in source[source.index("scenes.Length == 0"):]
