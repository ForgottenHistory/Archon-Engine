# Multiplayer Architecture

**Status:** Production Standard

---

## Core Principle

**All state changes flow through commands; commands sync across network; identical execution produces identical state.**

---

## The Problem

Grand strategy games require synchronized state across multiple clients:
- Thousands of provinces with complex ownership
- Multiple game systems (economy, units, diplomacy, buildings)
- AI running alongside human players
- State must be identical on all clients to prevent desync

Traditional approaches fail:
- **State sync** - Too much bandwidth (megabytes per tick)
- **Peer-to-peer without authority** - Conflicts and cheating
- **Server-simulates-all** - Expensive infrastructure

---

## The Solution

**Lockstep Command Synchronization** with player-hosted sessions.

**Architecture Layers:**

| Layer | Responsibility |
|-------|---------------|
| Transport | Raw network I/O (pluggable backends) |
| Network Manager | Peer tracking, message routing, lobby |
| Command Processor | Validate, execute, broadcast commands |
| Game Systems | Execute commands identically on all clients |

**Command Flow:**

1. Client UI creates command with explicit parameters
2. Client sends command to host
3. Host validates and executes locally
4. Host broadcasts confirmed command to all clients
5. All clients execute, producing identical state

**Key Invariants:**
- Commands carry all required data (no local lookups)
- Same command + same state = same result (determinism)
- Host is authoritative for validation
- AI runs only on host

---

## Architecture Decisions

### Decision 1: Player-Hosted Sessions

**Context:** Where does the authoritative server run?

**Decision:** One player acts as host (runs server + client), others connect as clients.

**Rationale:**
- No dedicated server costs
- Matches player expectations (Paradox model)
- Simpler deployment
- Host has authority for conflict resolution

**Trade-off:** Host has latency advantage; acceptable for grand strategy pace.

---

### Decision 2: Dual Command Processors

**Context:** ENGINE provides IProvinceCommand, but GAME layer has its own commands (ICommand).

**Decision:** Two separate processors with different network behavior.

| Processor | Layer | Network Behavior |
|-----------|-------|-----------------|
| CommandProcessor | ENGINE | Local execution only |
| GameCommandProcessor | GAME | Client→Host→Broadcast sync |

**Rationale:**
- ENGINE remains transport-agnostic
- GAME layer controls what syncs
- Clear separation of concerns (Pattern 1)

---

### Decision 3: Explicit Command Parameters

**Context:** Commands need to know which country is acting.

**Decision:** Commands carry explicit CountryId; never reference local player state.

**Rationale:**
- `playerState.PlayerCountryId` differs between clients
- Command must produce same result on any client
- Validation uses command's CountryId, not local state

**Anti-Pattern:**
- Using `playerState.PlayerCountryId` in command execution
- Hardcoding the executing player's perspective

---

### Decision 4: Host-Only AI

**Context:** Where should AI logic run?

**Decision:** AI runs only on host; AI decisions become commands broadcast to all.

**Rationale:**
- AI uses random decisions; running on each client causes divergence
- AI commands validated and broadcast like player commands
- Clients skip AI processing entirely

---

### Decision 5: Automatic Desync Recovery

**Context:** Desyncs will happen despite deterministic design.

**Decision:** Detect via periodic checksums, recover via state resync.

**Rationale:**
- Prevention is impossible (uninitialized memory, edge cases)
- Detection + recovery is player-friendly
- Reuses late-join sync infrastructure
- Brief pause (seconds) vs Paradox-style rehost (minutes)

---

### Decision 6: Unity Transport over High-Level Frameworks

**Context:** Many networking solutions exist: FishNet, Mirror, Netcode for GameObjects, Photon, etc.

**Decision:** Use Unity Transport Package directly with custom messaging.

**Rationale:**
- **Lockstep doesn't need state sync** - High-level frameworks optimize for server-authoritative state replication; we sync commands, not state
- **No NetworkBehaviour overhead** - We don't need networked transforms, RPCs, or SyncVars
- **Full control over serialization** - Command serialization is already handled by our command pattern
- **Minimal dependencies** - Unity Transport is lightweight, Burst-compatible, maintained by Unity
- **Steam-ready** - Easy to swap DirectTransport for SteamTransport later

**What we don't need from frameworks:**
- NetworkObject/NetworkBehaviour spawning
- Automatic variable synchronization
- Client-side prediction with rollback (our simulation is deterministic)
- Interest management / relevancy (all players see same state)

**Trade-off:** More code for lobby/connection management; worth it for architectural simplicity.

---

## Anti-Patterns

| Don't | Do Instead |
|-------|------------|
| Reference local player state in commands | Include explicit CountryId in command |
| Run AI on all clients | Run AI only on host |
| Send state snapshots every tick | Send commands only |
| Ignore desyncs | Detect and auto-recover |
| Use float math in simulation | Use FixedPoint64 (Pattern 5) |
| Let systems modify state directly | All changes through commands (Pattern 2) |

---

## Trade-offs

| Aspect | Benefit | Cost |
|--------|---------|------|
| Lockstep sync | Minimal bandwidth, no conflicts | Slowest player affects all |
| Player-hosted | No server costs | Host has latency advantage |
| Host-only AI | No AI divergence | Host CPU load higher |
| Command sync | Deterministic, replayable | All changes must be commands |
| Auto desync recovery | Seamless player experience | Brief pause during resync |

---

## Integration Points

### With Command System (Pattern 2)
- Commands are the unit of synchronization
- Serialize/Deserialize for network transmission
- Validation runs on host before broadcast

### With Event System (Pattern 3)
- Events fire after command execution
- UI subscribes to events, not commands
- Events are local (not synchronized)

### With AI System
- AI checks `IsMultiplayer && !IsHost` to skip processing
- AI checks `IsCountryHumanControlled(countryId)` to skip human countries
- AI uses same command pattern as players

### With Save/Load (Pattern 14)
- Late-join uses same serialization as save files
- State snapshot sent to joining players
- Derived data rebuilt after load

---

## Key Constraints

1. **Determinism Required** - Same commands on same state must produce identical results
2. **No Local State in Commands** - Commands carry all needed data
3. **Command-Only State Changes** - Direct modifications break sync
4. **Host Authority** - Host validates and orders all commands
5. **Time Alignment** - Game time synchronized across all clients

---

## When to Use

**Use lockstep when:**
- Turn-based or pausable real-time (grand strategy)
- State is large but changes are small
- Determinism achievable
- Latency tolerance acceptable (>100ms OK)

**Consider alternatives when:**
- Twitch gameplay requiring <50ms response
- Massive player counts (>16)
- State changes faster than network round-trip

---

## Summary

1. **Lockstep synchronization** - Commands sync, not state
2. **Player-hosted sessions** - One host with authority
3. **Dual command processors** - ENGINE local, GAME synced
4. **Explicit parameters** - Commands carry all required data
5. **Host-only AI** - Prevents divergent decisions
6. **Automatic recovery** - Detect desync, resync gracefully
7. **Transport abstraction** - Pluggable backends
8. **Determinism enforced** - FixedPoint64, no floats
9. **Command pattern essential** - All state changes through commands
10. **Event-driven UI** - Subscribe to events, not network

---

## Related Patterns

- **Pattern 2 (Command Pattern)** - Foundation for network sync
- **Pattern 3 (Event-Driven)** - UI receives local events after sync
- **Pattern 5 (Fixed-Point)** - Determinism requirement
- **Pattern 12 (Pre-Allocation)** - Network buffers pre-allocated
- **Pattern 14 (Save/Load)** - State sync reuses save infrastructure
- **Pattern 21 (Auto-Discovery)** - Commands auto-registered for sync

---

*Multiplayer is determinism + commands + graceful failure handling.*
