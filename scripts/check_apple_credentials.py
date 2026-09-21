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
for the key material. The failures it reports are about *shape*, and a shape
can be described without quoting the thing.

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


def check(env: dict[str, str]) -> list[str]:
    """Every problem with the credentials in `env`, as readable sentences."""
    problems: list[str] = []

    for name, provenance in REQUIRED.items():
        value = env.get(name, "")
        if not value.strip():
            problems.append(f"{name} is not set. {provenance}")
            continue
        if value != value.strip():
            problems.append(
                f"{name} has leading or trailing whitespace. GitHub stores a "
                f"secret verbatim, including the newline a copy-paste adds, "
                f"and Apple rejects the value without saying why."
            )

    team = env.get("APPLE_TEAM_ID", "").strip()
    if team and not TEN_CHAR_ID.match(team):
        problems.append(
            f"APPLE_TEAM_ID is not ten characters of A-Z and 0-9. "
            f"{REQUIRED['APPLE_TEAM_ID']}"
        )

    key_id = env.get("APP_STORE_CONNECT_KEY_ID", "").strip()
    if key_id and not TEN_CHAR_ID.match(key_id):
        problems.append(
            f"APP_STORE_CONNECT_KEY_ID is not ten characters of A-Z and 0-9. "
            f"{REQUIRED['APP_STORE_CONNECT_KEY_ID']}"
        )

    issuer = env.get("APP_STORE_CONNECT_ISSUER_ID", "").strip()
    if issuer and not UUID.match(issuer):
        problems.append(
            f"APP_STORE_CONNECT_ISSUER_ID is not a UUID. "
            f"{REQUIRED['APP_STORE_CONNECT_ISSUER_ID']}"
        )

    p8 = env.get("APP_STORE_CONNECT_API_KEY_P8", "").strip()
    if p8:
        if p8.startswith("-----BEGIN"):
            problems.append(
                "APP_STORE_CONNECT_API_KEY_P8 looks like the raw .p8 file. It "
                "must be base64 encoded: a multi-line PEM loses its newlines "
                "passing through the environment, and the key then fails to "
                "parse at the signing step. "
                f"{REQUIRED['APP_STORE_CONNECT_API_KEY_P8']}"
            )
        else:
            pem = _decoded_p8(p8)
            if pem is None:
                problems.append(
                    "APP_STORE_CONNECT_API_KEY_P8 is not valid base64. "
                    f"{REQUIRED['APP_STORE_CONNECT_API_KEY_P8']}"
                )
            elif b"PRIVATE KEY" not in pem:
                problems.append(
                    "APP_STORE_CONNECT_API_KEY_P8 decodes, but not to a "
                    "private key. Check that the encoded file is the "
                    "AuthKey_<KEYID>.p8 Apple issued, not the .cer or the "
                    "public key."
                )

    return problems


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
        for problem in problems:
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
