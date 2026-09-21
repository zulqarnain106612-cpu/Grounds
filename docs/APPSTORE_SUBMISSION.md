# App Store submission — what is gated and what is not

`docs/TRACEABILITY.md` row `phase6/appstore-cert-checklist` closes on:
*archives cleanly with all required Privacy Manifests.* An Xcode archive is
the criterion, so the row stays **open** — no CI run produces one.

What ships is the part that is cheap to automate and expensive to get wrong.

## Why the privacy manifests are gated in CI

iOS 17 requires a `PrivacyInfo.xcprivacy` for many third-party SDKs, and a
missing one is **rejected at upload** — after the archive, after the build,
with a release date already communicated. That is the most expensive place in
this entire project to discover a missing file.

`scripts/check_appstore_readiness.py` reads
`config/appstore.checklist.json` and verifies that every declared manifest
exists, is valid XML, declares the required-reason APIs the checklist says it
needs, and agrees with itself about tracking.

### Two states, deliberately distinguished

| `provided_by` | Missing means | Checker |
|---|---|---|
| `project` | A defect, now | Error, fails the run |
| `sdk` | The SDK is not installed yet | Notice, passes |

Reporting both identically would train everyone to ignore both. `--release`
makes everything blocking, which is the last moment an SDK manifest can be
caught for free.

### What the checker cannot verify

**Whether the declarations are true.** That the ads SDK really does track,
that Firebase really collects what the manifest says — those are read out of
each SDK's documentation by a person. The script checks that a claim exists
and is internally consistent, not that it is honest.

## Consistency the checker does enforce

- **Bundle id and minimum iOS version match `config/ios.build.json`.** Two
  files naming the bundle id is two places to change it, and the one that is
  wrong is discovered at upload.
- **A tracking SDK implies `NSUserTrackingUsageDescription`.** They live in
  different files, so they drift, and the combination is a guaranteed
  rejection.
- **`ITSAppUsesNonExemptEncryption` is declared.** Omitting it means answering
  the export-compliance question by hand on every single upload.

## The manual gates

Listed in the checklist so they are reviewable, not remembered:

- App Store Connect product ids match `config/ios.build.json` and the
  `ProductCatalog` asset **exactly** — an id mismatch makes a product silently
  unpurchasable.
- Screenshots for every required device size.
- Age rating questionnaire, reflecting the ads and IAP that are present.
- Privacy nutrition labels in App Store Connect matching the manifests.
- A sandbox IAP purchase completed end to end on a device (also the Cycle 5
  gate).
- Archive validated in Xcode with no missing-manifest warnings.

## Before submitting

```
python scripts/check_appstore_readiness.py --release
```

It must exit 0. Then archive, and work the manual list above.

## Credentials CI needs

Four repository secrets, at **Settings → Secrets and variables → Actions**.
`.github/workflows/ios-build.yml` reads exactly these and nothing else, and
`scripts/check_apple_credentials.py` validates their shape before anything
expensive runs:

| Secret | What it is | Where it comes from |
| --- | --- | --- |
| `APPLE_TEAM_ID` | Ten characters, `A-Z0-9` | developer.apple.com → Account → Membership details → Team ID |
| `APP_STORE_CONNECT_KEY_ID` | Ten characters, `A-Z0-9` | App Store Connect → Users and Access → Integrations → App Store Connect API → the *Key ID* column |
| `APP_STORE_CONNECT_ISSUER_ID` | A UUID | Same page, shown once above the key table as *Issuer ID* |
| `APP_STORE_CONNECT_API_KEY_P8` | The `AuthKey_<KEYID>.p8`, base64 encoded | Downloadable **exactly once**, when the key is created. `base64 -i AuthKey_XXXXXXXXXX.p8 \| tr -d '\n'` |

Create the API key with the **App Manager** role: Developer cannot create the
signing assets, and Admin grants more than a build needs.

### Why an API key and not a certificate

`xcodebuild -allowProvisioningUpdates` obtains the distribution certificate and
provisioning profile from the App Store Connect key itself. The alternative —
storing an Apple Distribution `.p12`, its password and a `.mobileprovision` —
is three more secrets, and three more things that expire silently: a
certificate lasts a year, a profile less, and both fail at the archive step
with an error that reads like a code problem.

### Paste mistakes the preflight catches

Each of these fails at signing, minutes into a macOS job, with an Apple error
that does not name the cause. The preflight names it on Linux in seconds, and
never prints a value — only which value is wrong:

- a trailing newline, which GitHub stores verbatim;
- the raw `.p8` pasted instead of its base64 (the environment flattens the
  PEM's newlines and the key stops parsing);
- the Key ID and the Issuer ID swapped — they sit on the same page and have
  different shapes, which is what makes the swap detectable;
- the `.cer` base64-encoded instead of the `.p8`.

Run it yourself:

```
python scripts/check_apple_credentials.py --names
```

### Producing the archive

`ios-build` is dispatch-only: Actions → ios-build → Run workflow. Three jobs,
cheapest first, because a macOS runner is roughly ten times a Linux one and an
archive is tens of minutes:

1. **apple-credentials** (Linux, seconds) — the four secrets above are present
   and well-formed. A trailing newline fails here rather than at signing.
2. **export** (Linux) — Unity runs `JetFighter.Editor.IOSBuild.PerformBuild`,
   which applies `config/ios.build.json`, generates the bootstrap scene and
   writes the Xcode project to `build/iOS`. `scripts/check_ios_export.py` then
   names any missing piece, so a bad export never reaches macOS.
3. **archive** (macOS) — `xcodebuild archive -allowProvisioningUpdates` with
   the App Store Connect key. `scripts/check_archive.py` refuses an archive
   whose payload carries no code signature or no embedded provisioning
   profile, which is the form of "App Store Connect will reject this" that can
   be checked before uploading.

The archive lands at `build/JetFighter.xcarchive` on the runner. Signature
*validity* is checked at upload, not by that script.

Nothing in `ios-build` runs on a pull request. That is deliberate: a job
conditioned on the event reports as *skipped*, and a skipped check is not a
verdict. Everything a pull request can check about this pipeline —  the
checker, both reporters, the secret names, the paths, the job ordering, the
key handling — is checked by the Python suites under `validate`.

## Why the row stays open

There is no Xcode archive and there are no installed SDKs. Ticking on a green
checker would claim a clean archive nobody produced — the same class of error
the Cycle 0 test gate exists to prevent.
