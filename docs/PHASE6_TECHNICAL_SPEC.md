# JetFighter — Phase 6 Technical Spec (iOS Hardening & Analytics)

Final pre-ship phase. Instruments everything built so far, exposes the adjustable-settings requirement (2c), and closes out App Store submission requirements.

---

## 1. Folder Additions

```
Assets/_Game/Scripts/
  Analytics/
    AnalyticsService.cs
    AnalyticsEvents.cs
  Settings/
    SettingsUI.cs
```

## 2. Class List

### `Analytics/AnalyticsService.cs` — static/service, wraps Firebase Analytics
`LogEvent(string name, Dictionary<string, object> parameters)`.

### `Analytics/AnalyticsEvents.cs` — constants
`run_start`, `run_end`, `death_cause`, `powerup_collected`, `iap_purchase`, `ad_watched`, `difficulty_tier_reached`. Wired into **existing** classes from prior phases (`IntroSequenceController`, death handling, `PowerUpController`, `IAPManager`, `AdsManager`, `DifficultyManager`) — this phase adds calls into those classes, not new gameplay systems.

### `Settings/SettingsUI.cs` — MonoBehaviour
Exposes the adjustable frame-rate/resolution/quality settings required by spec item 2c, on top of `QualityTierManager`'s (Phase 1) auto-detected tier. Manual override persisted via `SaveService` (Phase 3).

---

## 3. Branch Sequence

1. `phase6/analytics-instrumentation` — `AnalyticsService`, `AnalyticsEvents`, calls added into existing systems across all prior phases.
2. `phase6/settings-ui` — `SettingsUI`, manual quality/resolution override.
3. `phase6/perf-profiling-pass` — no new gameplay classes; dedicated branch for a Unity Profiler pass on your lowest supported non-legacy device. Burst opt-in decision made here, based on actual profiling data, not speculatively.
4. `phase6/appstore-cert-checklist` — `build_pipeline` branch: App Store metadata, and **Privacy Manifest files** for the Firebase/IAP/Ads SDKs (iOS 17+ requires these for many third-party SDKs — a current, real requirement, not optional).

## 4. Acceptance Criteria

| Branch | Criteria |
|---|---|
| analytics-instrumentation | All listed events appear correctly in the Firebase dashboard during a real test run. |
| settings-ui | Manual quality override persists across app relaunch and visibly changes render settings. |
| perf-profiling-pass | Sustained acceptable frame rate at "low" quality tier on your lowest supported device across a multi-minute endless run (define your fps floor here once you know your device baseline). |
| appstore-cert-checklist | App builds and archives cleanly in Xcode with all required Privacy Manifests present; submission checklist fully green. |

## 5. `knowledge_update` Pattern

`phase: ["6"]`, e.g.:
```json
{"op":"add_node","id":"phase6_analytics_instrumentation","type":"system","label":"Firebase Analytics Event Wiring","tags":["analytics","phase6"],"phase":["6"],"symbols":["AnalyticsService","AnalyticsEvents"]}
```

---

## Post / Upgrade (backlog — not fully scaffolded yet, by design)

Speccing these now would be premature — they depend on real post-launch analytics (Post) and on actual Unity/SDK release timing (Upgrade), neither of which exists yet. Anticipated branches, listed only as a placeholder for future planning:

- **Post:** balance patches driven by Phase 6 analytics data, new enemy archetypes, store rotations, season-pass layer (extends the `CurrencyType`/`Wallet` design from Phase 3 — should not require a redesign if that phase was built as specified).
- **Upgrade:** Unity LTS version bumps, `INetworkTransport` swap if cross-platform expansion is ever pursued (Phase 4's abstraction exists specifically to make this a contained change).

These get their own technical specs, in this same format, once you're actually approaching them.
