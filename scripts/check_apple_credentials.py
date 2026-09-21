#!/usr/bin/env python3
"""Fail early and specifically when the Apple credentials are not usable.

`docs/TRACEABILITY.md` row `phase6/appstore-cert-checklist` closes on an
Xcode archive, and an archive needs Apple credentials. The expensive failure
mode is the one this exists to prevent: a macOS runner is roughly ten times
the cost of a Linux one, a Unity iOS export plus an archive is tens of
minutes, and discovering at the signing step that an issuer id was pasted
with a trailing newline wastes all of it. This runs first, in seconds, on
Linux, and says exactly which value is wrong and where to get a correct one.

It deliberately never prints a secret's value -- not a prefix, not a length
for the key material. That is enforced by shape rather than by discipline:
`check()` returns only a (secret name, reason key) pair, both drawn from the
constant tables below, and `describe()` renders the message from those tables
alone. No value a caller passed in can reach the output, which is also what
clears CodeQL's py/clear-text-logging-sensitive-data -- a test asserting the
messages happen to be clean would not have.

Cloud signing, not a stored certificate. `xcodebuild -allowProvisioningUpdates`
takes the App Store Connect API key and issues or downloads the signing
assets itself, so there is no Apple Distribution `.p12` and no
`.mobileprovision` in this repo's secrets. That is three fewer secrets and,
more to the point, three fewer things that expire silently: a certificate
lasts a year and a profile less, and both fail at the archive step with an
error that reads like a code problem.

Usage:
    python scripts/check_apple_credentials.py          # reads the environment
    python scripts/check_apple_credentials.py --names  # print the names only
"""
from __future__ import annotations

import argparse
import base64
import binascii
import os
import re
import sys

# Every secret this project needs to produce a signed iOS archive, with the
# exact place each value comes from. A name here that the workflow does not
# pass, or the reverse, is caught by tests/test_apple_credentials.py.
REQUIRED = {
    "APPLE_TEAM_ID": (
        "Ten characters, A-Z and 0-9, e.g. ABCDE12345. "
        "developer.apple.com -> Account -> Membership details -> Team ID."
    ),
    "APP_STORE_CONNECT_KEY_ID": (
        "Ten characters, A-Z and 0-9. App Store Connect -> Users and Access "
        "-> Integrations -> App Store Connect API -> the Key ID column."
    ),
    "APP_STORE_CONNECT_ISSUER_ID": (
        "A UUID, e.g. 57246542-96fe-1a63-e053-0824d011072a. Same page as the "
        "Key ID, shown once above the key table as Issuer ID."
    ),
    "APP_STORE_CONNECT_API_KEY_P8": (
        "The AuthKey_<KEYID>.p8 file's contents, base64 encoded. Apple lets "
        "you download that file exactly once, when the key is created. "
        "Encode it with:  base64 -i AuthKey_XXXXXXXXXX.p8 | tr -d '\\n'"
    ),
}

TEN_CHAR_ID = re.compile(r"^[A-Z0-9]{10}$")
UUID = re.compile(
    r"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-"
    r"[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$"
)


def _decoded_p8(raw: str) -> bytes | None:
    """The PEM behind the base64, or None when it is not base64 at all."""
    try:
        # validate=True so a pasted PEM (which contains '-' and newlines)
        # is rejected here rather than silently decoding to garbage.
        return base64.b64decode(raw, validate=True)
    except (binascii.Error, ValueError):
        return None


# Why a problem is a (name, reason) pair and not a sentence: see the module
# docstring. Every sentence below is a constant, so rendering one cannot
# quote a value.
REASONS = {
    "missing": "{name} is not set. {provenance}",
    "whitespace": (
        "{name} has leading or trailing whitespace. GitHub stores a secret "
        "verbatim, including the newline a copy-paste adds, and Apple rejects "
        "the value without saying why."
    ),
    "not_ten_chars": "{name} is not ten characters of A-Z and 0-9. {provenance}",
    "not_uuid": "{name} is not a UUID. {provenance}",
    "raw_pem": (
        "{name} looks like the raw .p8 file. It must be base64 encoded: a "
        "multi-line PEM loses its newlines passing through the environment, "
        "and the key then fails to parse at the signing step. {provenance}"
    ),
    "not_base64": "{name} is not valid base64. {provenance}",
    "not_a_key": (
        "{name} decodes, but not to a private key. Check that the encoded "
        "file is the AuthKey_<KEYID>.p8 Apple issued, not the .cer or the "
        "public key."
    ),
}


def check(env: dict[str, str]) -> list[tuple[str, str]]:
    """Every problem with the credentials in `env`, as (name, reason) pairs.

    Both halves of every pair are keys of the constant tables above. Nothing
    from `env` is returned, so nothing from `env` can be printed.
    """
    problems: list[tuple[str, str]] = []

    for name in REQUIRED:
        value = env.get(name, "")
        if not value.strip():
            problems.append((name, "missing"))
        elif value != value.strip():
            problems.append((name, "whitespace"))

    def present(name: str) -> str:
        value = env.get(name, "").strip()
        return value if not any(p[0] == name for p in problems) else ""

    team = present("APPLE_TEAM_ID")
    if team and not TEN_CHAR_ID.match(team):
        problems.append(("APPLE_TEAM_ID", "not_ten_chars"))

    key_id = present("APP_STORE_CONNECT_KEY_ID")
    if key_id and not TEN_CHAR_ID.match(key_id):
        problems.append(("APP_STORE_CONNECT_KEY_ID", "not_ten_chars"))

    issuer = present("APP_STORE_CONNECT_ISSUER_ID")
    if issuer and not UUID.match(issuer):
        problems.append(("APP_STORE_CONNECT_ISSUER_ID", "not_uuid"))

    p8_name = "APP_STORE_CONNECT_API_KEY_P8"
    p8 = present(p8_name)
    if p8:
        if p8.startswith("-----BEGIN"):
            problems.append((p8_name, "raw_pem"))
        else:
            pem = _decoded_p8(p8)
            if pem is None:
                problems.append((p8_name, "not_base64"))
            elif b"PRIVATE KEY" not in pem:
                problems.append((p8_name, "not_a_key"))

    return problems


def describe(problems: list[tuple[str, str]]) -> list[str]:
    """Readable sentences, built from the constant tables and nothing else."""
    return [
        REASONS[reason].format(name=name, provenance=REQUIRED[name])
        for name, reason in problems
    ]


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--names",
        action="store_true",
        help="print the required secret names and where each comes from",
    )
    args = parser.parse_args(argv)

    if args.names:
        print("Repository secrets required for the iOS archive:\n")
        for name, provenance in REQUIRED.items():
            print(f"  {name}\n      {provenance}\n")
        return 0

    problems = check(dict(os.environ))
    if problems:
        for problem in describe(problems):
            print(f"::error::{problem}")
        print(
            f"::error::{len(problems)} problem(s) with the Apple credentials. "
            f"Set them at Settings -> Secrets and variables -> Actions."
        )
        return 1

    print("Apple credentials present and well-formed "
          f"({len(REQUIRED)} secrets checked).")
    return 0


if __name__ == "__main__":
    sys.exit(main())
