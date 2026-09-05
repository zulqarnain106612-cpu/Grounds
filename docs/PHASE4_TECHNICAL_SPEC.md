# JetFighter — Phase 4 Technical Spec (2-Player P2P)

Co-op survival (per your confirmed assumption), host-authoritative for enemy state, GameKit `GKMatch` under an internal abstraction so the transport can be swapped later without touching gameplay code.

**Dependency note:** Apple's GameKit has no built-in Unity bridge — use Apple's official `com.apple.unityplugin.gamekit` package (or an equivalent maintained wrapper) for the native `GKMatch` calls. Confirm this package's current state before starting `phase4/gamekit-transport`; if it's unmaintained, that's a real risk item to resolve before this phase, not during it.

---

## 1. Folder Additions

```
Assets/_Game/Scripts/
  Network/
    INetworkTransport.cs
    GameKitTransport.cs
    LocalLoopbackTransport.cs
    NetworkMatchmaker.cs
    HostAuthority.cs
    NetworkedEnemyState.cs
    NetworkedPlayerState.cs
```

## 2. Class List

### `Network/INetworkTransport.cs` — interface
`Connect()`, `SendState(byte[] payload)`, `event Action<byte[]> OnStateReceived`, `Disconnect()`. Everything gameplay-side talks to this interface, never to GameKit directly — this is the swap seam for Photon/PlayFab later, per the roadmap's "won't corner us" requirement.

### `Network/LocalLoopbackTransport.cs` — implements `INetworkTransport`
A same-process mock transport for local development/testing without needing two physical iOS devices for every iteration. Built first so Phase 4's gameplay logic (state sync, host authority) can be developed and tested before the GameKit dependency is even wired in.

### `Network/GameKitTransport.cs` — implements `INetworkTransport`
Wraps `GKMatch` send/receive via the native plugin.

### `Network/NetworkMatchmaker.cs` — MonoBehaviour
UI hook to start matchmaking (wraps `GKMatchmakerViewController` or programmatic matching), resolves to a connected `INetworkTransport` instance on success.

### `Network/HostAuthority.cs` — static/service
Elects one peer authoritative for enemy spawn/health state (e.g., match-creator = host — simplest deterministic rule, avoids an election protocol for a 2-player match).

### `Network/NetworkedEnemyState.cs` — MonoBehaviour
Host: serializes `EnemySpawner`/`EnemyHealth` state and sends via the transport. Guest: applies received state, does not run its own independent enemy simulation — this is what prevents enemy-health desync between the two devices.

### `Network/NetworkedPlayerState.cs` — MonoBehaviour (one per remote player)
Each device is authoritative for its **own** jet (no need to sync your own physics to yourself). Broadcasts own jet position/rotation/fire-events at a fixed rate (10–20Hz is sufficient for a short P2P session — deliberately not building client-side prediction/interpolation for this MVP; that's real complexity that isn't justified until playtesting shows it's needed).

---

## 3. Branch Sequence

1. `phase4/network-abstraction` — `INetworkTransport`, `LocalLoopbackTransport` for local dev.
2. `phase4/player-state-sync` — `NetworkedPlayerState`, tested entirely over the loopback transport first.
3. `phase4/host-authoritative-enemies` — `HostAuthority`, `NetworkedEnemyState`, also tested over loopback first.
4. `phase4/gamekit-transport` — `GameKitTransport`, `NetworkMatchmaker`; swap the transport implementation, gameplay code should require **zero changes** at this step if the abstraction (step 1) was done correctly — that's the acceptance test for the abstraction itself.
5. `phase4/network-soak-test` — no new classes; dedicated branch for two-physical-device soak testing (latency tolerance, disconnect/reconnect handling).

## 4. Acceptance Criteria

| Branch | Criteria |
|---|---|
| network-abstraction | Loopback transport round-trips a message with zero gameplay-code awareness of the transport's implementation. |
| player-state-sync | Two local instances (loopback) see each other's jet move correctly at the target sync rate. |
| host-authoritative-enemies | Both instances (loopback) show identical enemy health/position at all times — zero divergence, since guest never simulates independently. |
| gamekit-transport | Swapping `LocalLoopbackTransport` → `GameKitTransport` requires no changes outside `Network/` — proves the abstraction actually held. |
| network-soak-test | Two physical iOS devices complete a full co-op run with no visible desync; brief network interruption doesn't crash either client. |

## 5. `knowledge_update` Pattern

`phase: ["4"]`, e.g.:
```json
{"op":"add_node","id":"phase4_network_abstraction","type":"system","label":"INetworkTransport Abstraction + GameKit Impl","tags":["network","phase4"],"phase":["4"],"symbols":["INetworkTransport","GameKitTransport","HostAuthority"]}
```
