# Two-device network soak — procedure and evidence

`docs/TRACEABILITY.md` row `phase4/network-soak-test` closes on: *two physical
devices, full co-op run, no visible desync; brief interruption crashes neither
client.*

Two devices cannot be a CI assertion, so this cell splits the way `CLAUDE.md`
requires: the objective half is automated in
`Assets/_Game/Tests/EditMode/NetworkSoakTests.cs`, and this file is the manual
half — a procedure that produces **evidence**, not a recollection.

## What is already automated

Run by `unity-test.yml`, so it holds on every PR rather than on soak day:

| Condition | Automated coverage |
|---|---|
| Long run | 18,000 frames (5 minutes at 60fps), divergence checked **every frame** |
| Latency | 6 pumps each way (~100ms), 6,000 frames |
| Jitter / reordering | latency 4 + jitter 5, 6,000 frames |
| Packet loss | 50%, 6,000 frames, >100 packets dropped |
| All four at once | latency + jitter + 34% loss, convergence asserted |
| Interruptions | 20 disconnect/reconnect rounds; re-convergence asserted after each |
| Malformed traffic | 400 junk packets mid-run |
| Traffic volume | packet count stays proportional to the send rate |

A device run visits **one arbitrary sample** of those conditions. The suite
visits them on purpose. That is why the device pass below is a confirmation
rather than the evidence.

## What only devices can show

Three things, all of which are why this row exists:

1. **Real GameKit.** Every automated test above runs on the loopback. The
   GameKit transport is contract-tested (`GameKitTransportTests`) but never
   against Apple's actual matchmaking, which is where a real handshake,
   authentication and NAT traversal live.
2. **Visible desync.** The suite asserts numeric health equality. A player
   sees position and animation, and a jet that is numerically synced can still
   *look* wrong.
3. **Real interruption.** `Interrupt()` is a clean, instant drop. A phone
   losing Wi-Fi mid-run is a slow degradation, a radio switch, and possibly a
   backgrounded app.

## Procedure

Two iOS devices, both on the same build, signed into Game Center.

1. **Baseline run.** Host a match, complete a full run to game over. Record
   both screens.
2. **Health-parity check.** At three points during the run, pause and
   photograph both health bars on the same enemy. Any visible difference fails
   the row.
3. **Wi-Fi interruption.** Mid-run, put the guest device in airplane mode for
   ~5 seconds, then restore it. Neither app may crash; the guest must
   re-converge.
4. **Background interruption.** Mid-run, background the guest app for ~10
   seconds and return. Same expectations.
5. **Host interruption.** Repeat step 3 on the host device.
6. **Long run.** One uninterrupted co-op run of at least five minutes.

## Evidence to attach to the PR

- Screen recordings of both devices for the baseline run.
- The three health-parity photographs.
- For each interruption step: pass/fail, and whether re-convergence was
  visible.
- Build number and iOS versions of both devices.

## When this cannot be run yet

`unity-test.yml` needs `UNITY_LICENSE` before the automated half runs in CI at
all, and the device half needs two provisioned devices. Until both exist the
row stays open — the automated suite passing locally is not the row's
criterion, and ticking it on that basis would be the exact "green lie" the
Cycle 0 test gate was built to prevent.
