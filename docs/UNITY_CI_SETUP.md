# Unity licence for CI

The C# test gate (`.github/workflows/unity-test.yml`) fails until a Unity
licence is configured, and every PR from #14 upward inherits that gate. This
is the procedure to satisfy it.

It is written down because the obvious approach no longer works and the dead
end costs a CI run to discover.

## What does not work any more

`game-ci/unity-request-activation-file` is retired. Dispatching it fails with:

```
##[error]This action is no longer supported. Please use the updated
activation instructions found at https://game.ci/docs/github/activation
```

So the old loop — request a `.alf` in CI, upload it to
`license.unity3d.com/manual`, download a `.ulf` — is gone. **No workflow can
produce the licence.** The `.ulf` is now generated locally by Unity Hub, which
is the one step in this repo that genuinely cannot run on GitHub Actions.

## Procedure

### 1. Produce the `.ulf` locally

1. Install Unity Hub and sign in to your Unity account.
2. **Preferences → Licenses → Add → Get a free personal license.**
3. The licence file lands at:

   | OS | Path |
   |---|---|
   | Linux | `~/.local/share/unity3d/Unity/Unity_lic.ulf` |
   | macOS | `/Library/Application Support/Unity/Unity_lic.ulf` |
   | Windows | `C:\ProgramData\Unity\Unity_lic.ulf` |

Installing the matching editor version is not required to obtain the licence,
but the project targets the version pinned in
`ProjectSettings/ProjectVersion.txt` — currently **6000.0.32f1** (ADR-009,
Unity 6 LTS). Installing that version locally keeps the editor and CI in step.

### 2. Set a Unity password if the account uses SSO

GameCI activates a Personal licence with **three** secrets:
`UNITY_LICENSE`, `UNITY_EMAIL` and `UNITY_PASSWORD`.

An account created through **Sign in with Apple** (or Google) has no Unity
password, so `UNITY_PASSWORD` cannot be filled. Add one under
**Unity ID → Account settings** before continuing, or activation fails after
the container pull rather than at the preflight.

If Apple's *Hide My Email* was used at signup, the address Unity holds is the
`@privaterelay.appleid.com` relay, not your own address. `UNITY_EMAIL` must
match what Unity has on file, whichever that is.

### 3. Store the secrets

Never commit the `.ulf`. This repository is public; a licence file in a public
repo is both a leak and grounds for revocation.

```bash
gh secret set UNITY_LICENSE < ~/.local/share/unity3d/Unity/Unity_lic.ulf
```

```bash
gh secret set UNITY_EMAIL
```

```bash
gh secret set UNITY_PASSWORD
```

The last two prompt for the value rather than taking it on the command line,
so it stays out of shell history.

### 4. Confirm

```bash
gh secret list
```

Then re-run the gate on any affected PR:

```bash
make ci-status PR=14
```

`unity-test.yml`'s `preflight` job passes as soon as either `UNITY_LICENSE` or
both of `UNITY_EMAIL`/`UNITY_PASSWORD` are present. The `test` job needs the
licence itself, so a green preflight is necessary but not sufficient — the
EditMode and PlayMode suites must then actually run.

## What is already correct

Verified against the scaffold, so these are not things to fix:

- `ProjectSettings/ProjectVersion.txt` pins **6000.0.32f1**, matching ADR-009.
- `Packages/manifest.json` includes `com.unity.test-framework@1.4.5`, without
  which no tests are discoverable.
- PR #14 carries `Assets/_Game/Tests/EditMode/ScaffoldSmokeTest.cs` and
  `Assets/_Game/Tests/PlayMode/ScaffoldPlayModeTest.cs`, so
  `scripts/check_unity_results.py --min-tests 1` has something to count. A
  green run with zero discovered tests is the failure that check exists to
  prevent.

## Scope

This unblocks the gate; it does not make the chain green on its own. The other
failure class across PRs #14–#47 is a stale `symbols/index.json`, fixed by
dispatching `ingest.yml` on the branch and merging the index PR it opens —
see `docs/SDLC_SPIRAL.md` §7.
