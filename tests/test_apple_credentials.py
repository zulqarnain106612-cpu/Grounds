"""The Apple credential preflight: every way a pasted secret goes wrong.

This exists because the failure it prevents is expensive and late. Signing
happens at the end of a macOS job -- ten times a Linux runner's cost, tens of
minutes after the Unity export -- and Apple's error for a malformed issuer id
reads like a code problem. Each case below is a real paste mistake, and the
test asserts that the preflight names it on Linux in seconds.

The other property pinned here is silence: `check()` must never put a secret's
value into its output. A test that only checked the happy path would let a
debugging `print(value)` through.
"""

from __future__ import annotations

import base64
import importlib.util
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[1]
SCRIPT = REPO_ROOT / "scripts" / "check_apple_credentials.py"

# A real .p8 is an EC private key in PKCS#8 PEM. Only two properties matter to
# the checker -- it is base64, and the PEM behind it says PRIVATE KEY -- so
# this is a stand-in rather than a key, and no key material is in this repo.
FAKE_P8_PEM = (
    "-----BEGIN PRIVATE KEY-----\n"
    "MIGTAgEAMBMGByqGSM49AgEGCCqGSM49AwEHBHkwdwIBAQQg\n"
    "-----END PRIVATE KEY-----\n"
)


def _load():
    spec = importlib.util.spec_from_file_location("check_apple_credentials", SCRIPT)
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


def _b64(text: str) -> str:
    return base64.b64encode(text.encode("utf-8")).decode("ascii")


def _valid() -> dict[str, str]:
    return {
        "APPLE_TEAM_ID": "ABCDE12345",
        "APP_STORE_CONNECT_KEY_ID": "2X9ABCD3EF",
        "APP_STORE_CONNECT_ISSUER_ID": "57246542-96fe-1a63-e053-0824d011072a",
        "APP_STORE_CONNECT_API_KEY_P8": _b64(FAKE_P8_PEM),
    }


def test_a_correct_set_has_no_problems() -> None:
    assert _load().check(_valid()) == []


def test_every_missing_secret_is_named_with_where_to_get_it() -> None:
    module = _load()
    problems = module.check({})
    assert len(problems) == len(module.REQUIRED)
    for name, provenance in module.REQUIRED.items():
        assert any(p.startswith(f"{name} is not set.") for p in problems)
        # The message has to be actionable on its own: a runner log saying
        # "not set" without saying which Apple page it comes from costs a
        # round trip that this preflight exists to save.
        assert any(provenance in p for p in problems)


def test_one_missing_secret_does_not_hide_the_others_being_fine() -> None:
    env = _valid()
    del env["APPLE_TEAM_ID"]
    problems = _load().check(env)
    assert len(problems) == 1
    assert problems[0].startswith("APPLE_TEAM_ID is not set.")


def test_a_blank_secret_counts_as_missing() -> None:
    """GitHub happily stores an empty secret, and `${{ secrets.X }}` then
    expands to the empty string rather than failing."""
    env = _valid()
    env["APP_STORE_CONNECT_KEY_ID"] = "   "
    problems = _load().check(env)
    assert len(problems) == 1
    assert "is not set" in problems[0]


def test_a_trailing_newline_is_caught_rather_than_tolerated() -> None:
    """The single most common paste mistake. Stripping it silently would be
    worse than failing: the secret stored in GitHub stays wrong, and the next
    tool to read it -- xcrun, altool -- has no such tolerance."""
    env = _valid()
    env["APPLE_TEAM_ID"] = "ABCDE12345\n"
    problems = _load().check(env)
    assert len(problems) == 1
    assert "whitespace" in problems[0]


def test_whitespace_is_reported_once_not_also_as_a_format_error() -> None:
    """Two complaints about one paste read as two separate problems."""
    env = _valid()
    env["APP_STORE_CONNECT_ISSUER_ID"] = _valid()["APP_STORE_CONNECT_ISSUER_ID"] + " "
    problems = _load().check(env)
    assert len(problems) == 1


def test_a_team_id_of_the_wrong_shape_is_refused() -> None:
    module = _load()
    for bad in ("ABCDE1234", "ABCDE123456", "abcde12345", "ABCDE-1234"):
        env = _valid()
        env["APPLE_TEAM_ID"] = bad
        problems = module.check(env)
        assert len(problems) == 1, bad
        assert "ten characters" in problems[0]


def test_a_key_id_of_the_wrong_shape_is_refused() -> None:
    env = _valid()
    env["APP_STORE_CONNECT_KEY_ID"] = "TOO-SHORT"
    problems = _load().check(env)
    assert len(problems) == 1
    assert problems[0].startswith("APP_STORE_CONNECT_KEY_ID")


def test_an_issuer_id_that_is_not_a_uuid_is_refused() -> None:
    """The Key ID and the Issuer ID sit on the same App Store Connect page and
    get swapped. They have different shapes, so the swap is detectable."""
    env = _valid()
    env["APP_STORE_CONNECT_ISSUER_ID"] = "2X9ABCD3EF"
    problems = _load().check(env)
    assert len(problems) == 1
    assert "is not a UUID" in problems[0]


def test_the_raw_p8_file_is_refused_with_the_encode_command() -> None:
    """Pasting the .p8 itself looks reasonable and fails at signing: the
    environment flattens the PEM's newlines and the key stops parsing."""
    env = _valid()
    # .strip() so this case is isolated: a real paste keeps the file's final
    # newline and would also, correctly, raise the whitespace complaint.
    env["APP_STORE_CONNECT_API_KEY_P8"] = FAKE_P8_PEM.strip()
    problems = _load().check(env)
    assert len(problems) == 1
    assert "base64" in problems[0]
    assert "base64 -i AuthKey_" in problems[0]


def test_a_p8_that_is_not_base64_at_all_is_refused() -> None:
    env = _valid()
    env["APP_STORE_CONNECT_API_KEY_P8"] = "not base64 !!!"
    problems = _load().check(env)
    assert len(problems) == 1
    assert "not valid base64" in problems[0]


def test_base64_of_the_wrong_file_is_refused() -> None:
    """Encoding the .cer, or the downloaded certificate, instead of the key."""
    env = _valid()
    env["APP_STORE_CONNECT_API_KEY_P8"] = _b64("-----BEGIN CERTIFICATE-----\nAAAA\n")
    problems = _load().check(env)
    assert len(problems) == 1
    assert "not to a private key" in problems[0]


def test_no_problem_message_ever_quotes_a_secret_value() -> None:
    """A preflight that echoes the thing it is validating turns a public
    Actions log into a credential leak."""
    module = _load()
    env = {
        "APPLE_TEAM_ID": "SECRETTEAM\n",
        "APP_STORE_CONNECT_KEY_ID": "secretkeyid",
        "APP_STORE_CONNECT_ISSUER_ID": "secret-issuer-value",
        "APP_STORE_CONNECT_API_KEY_P8": _b64("secret-key-material"),
    }
    joined = " ".join(module.check(env))
    for value in env.values():
        assert value.strip() not in joined


def test_main_reports_every_problem_and_exits_nonzero(monkeypatch, capsys) -> None:
    module = _load()
    monkeypatch.setattr(module.os, "environ", {}, raising=False)
    assert module.main([]) == 1
    out = capsys.readouterr().out
    # ::error:: so GitHub surfaces each one as an annotation, which is what
    # pr-status-comment.yml copies into the pull request comment.
    assert out.count("::error::") == len(module.REQUIRED) + 1
    assert "Settings -> Secrets and variables -> Actions" in out


def test_main_exits_zero_on_a_good_environment(monkeypatch, capsys) -> None:
    module = _load()
    monkeypatch.setattr(module.os, "environ", _valid(), raising=False)
    assert module.main([]) == 0
    assert "::error::" not in capsys.readouterr().out


def test_names_mode_needs_no_environment(monkeypatch, capsys) -> None:
    """`--names` is how a human asks what to store, so it must work on a
    machine that has none of it set."""
    module = _load()
    monkeypatch.setattr(module.os, "environ", {}, raising=False)
    assert module.main(["--names"]) == 0
    out = capsys.readouterr().out
    for name in module.REQUIRED:
        assert name in out


def test_the_workflow_passes_exactly_the_secrets_the_checker_requires() -> None:
    """The drift that makes this whole preflight useless: a secret added here
    and not to the workflow's env is 'not set' on every run, and one passed by
    the workflow but dropped here is never validated."""
    module = _load()
    workflow = (REPO_ROOT / ".github/workflows/ios-build.yml").read_text(
        encoding="utf-8"
    )
    preflight = workflow.split("run: python scripts/check_apple_credentials.py", 1)[0]
    for name in module.REQUIRED:
        assert f"{name}: ${{{{ secrets.{name} }}}}" in preflight, name

    # And nothing else: an Apple-looking secret in that env block that the
    # checker does not know about is one nobody is validating.
    for line in preflight.splitlines():
        stripped = line.strip()
        if stripped.startswith("APPLE_") or stripped.startswith("APP_STORE_"):
            assert stripped.split(":", 1)[0] in module.REQUIRED, stripped
