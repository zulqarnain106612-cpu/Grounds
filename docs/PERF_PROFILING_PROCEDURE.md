# Performance profiling pass — procedure and the Burst decision

`docs/TRACEABILITY.md` row `phase6/perf-profiling-pass` closes on: *sustained
fps floor at low tier on the lowest supported device.*

The spec is explicit that this branch adds **no new gameplay classes** and that
the Burst opt-in is decided "based on actual profiling data, not
speculatively". Both of those make this a measurement cell, not a code one —
so what ships is the instrumentation that makes the measurement repeatable,
plus this procedure.

The row is **left open**. A profiler capture is the criterion and no CI run can
produce one.

## The fps floor

The spec says to define the floor "once you know your device baseline", so it
is not asserted here as a number. What *is* asserted is that the game does not
allocate per frame in its hot paths, because a garbage-collection spike is the
single most common cause of a dropped frame on iOS and it is invisible in an
average frame time.

`FrameBudgetProbe` measures worst-frame and 1%-low frame time rather than the
average. **The average is the wrong statistic**: a run at a steady 60fps with
one 200ms hitch every ten seconds averages fine and feels broken, and it is the
hitch a player reports.

## What is automated

`Assets/_Game/Tests/EditMode/FrameBudgetTests.cs`, run by `unity-test.yml`:

| Property | Why it is checked in CI rather than in a capture |
|---|---|
| No `Instantiate`/`Destroy` in the bullet, missile, enemy or pickup hot paths | The allocation source that causes hitches; already bounded by pools, re-checked here as one property across every system |
| No `GetComponent` in per-frame update paths | A per-frame lookup times the on-screen enemy cap is a measurable cost that never shows up as one obvious frame |
| No LINQ in per-frame paths | Every LINQ expression allocates an enumerator per call |
| Pools are prewarmed | An allocation spike at the exact moment the player is watching |
| Worst-frame tracking exists | So the capture below measures the right statistic |

These do not replace the capture. They stop the capture being invalidated by a
regression introduced after it was taken.

## Procedure

On the **lowest supported device**, on a build with the low tier forced via
`SettingsUI`:

1. **Baseline.** Unity Profiler attached, deep profiling **off** (deep
   profiling changes the numbers it reports). Play a five-minute endless run.
2. **Record:** median frame time, 1% low, worst frame, GC allocations per
   frame, and the count of GC spikes over 16ms.
3. **Identify the top three costs** in the CPU timeline. Do not act on them
   yet.
4. **Decide on Burst.** Only if a measured hot path is arithmetic-bound and
   appears in the top three. Burst on a path that is not the bottleneck costs
   build time and compilation complexity for nothing — that is precisely the
   speculative adoption the spec is guarding against.
5. **Re-run after any change** and compare against step 2. A change without a
   before-and-after is a guess.

## Evidence to attach

- Profiler capture files (`.data`) for the baseline and any after-run.
- The five numbers from step 2, for both.
- Device model and iOS version.
- The Burst decision, with the profiling data that justified it — including
  "not adopted", which is a valid and likely outcome.

## Why the row stays open

There is no device and no capture. Ticking on the automated properties alone
would claim a sustained frame rate nobody measured, which is the same class of
error the Cycle 0 test gate exists to prevent.
