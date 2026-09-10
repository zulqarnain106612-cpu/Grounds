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

## Why the row stays open

There is no Xcode archive and there are no installed SDKs. Ticking on a green
checker would claim a clean archive nobody produced — the same class of error
the Cycle 0 test gate exists to prevent.
