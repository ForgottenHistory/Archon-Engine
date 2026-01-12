# Save/Load Architecture

**Status:** Production Standard

---

## Core Principle

**State snapshot for speed. Command log for verification. Single source of truth per system.**

---

## Hybrid Architecture

### Why Hybrid?
- **Snapshot-only:** Fast loading, but can't verify determinism
- **Command-log-only:** Verifiable, but slow (must replay from start)
- **Hybrid:** Fast loading with optional determinism verification

### Components
- **Snapshot:** Full state for instant loading (production)
- **Command Log:** Recent commands for verification (development)
- **Post-Load Rebuild:** Derived data rebuilt from authoritative state

---

## Core Principles

### Single Source of Truth
Each system serializes its own state. SaveManager orchestrates, never duplicates data.

### Hot Data Only
Serialize authoritative state. Rebuild derived data on load:
- **Save:** Province states, resources, modifiers
- **Rebuild:** Reverse indices, GPU textures, caches

### Deterministic Serialization
Fixed-point binary format. No floats, no platform-dependent formats.

### Layer Separation
ENGINE cannot call GAME directly. Use callbacks for finalization.

---

## Serialization Patterns

### Dense Arrays
All entities have data (province states). Raw memory copy.

### Sparse Collections
Most entities empty (buildings per province). Skip empties, save only populated.

### Fixed-Point Values
Serialize as raw integer representation, not floating-point.

### Managed Collections
Count + elements pattern for variable-length data.

---

## Loading Pipeline

### Phase 1: Validation
- Verify file format (magic bytes)
- Check version compatibility
- Reject corrupted files

### Phase 2: State Restoration
- Each system deserializes its own data
- Systems own their load logic

### Phase 3: Internal State Restoration
Systems that use Clear() must restore internal counters (e.g., entity counts).

### Phase 4: Buffer Synchronization
Double-buffered systems sync read/write buffers to prevent stale UI data.

### Phase 5: Post-Load Finalization
ENGINE triggers callback → GAME rebuilds derived data → MAP refreshes textures.

### Phase 6: UI Refresh
Broadcast GameLoadedEvent → All UI components refresh displays.

---

## Layer Communication

### ENGINE → GAME (Callbacks)
ENGINE exposes Action delegates. GAME implements finalization logic.

### GAME → MAP (Direct)
GAME can import MAP. Direct calls to refresh textures.

### GAME → ENGINE (Direct)
GAME uses ENGINE via GameState. Standard API access.

---

## Common Pitfalls

**Forgetting to restore internal state:**
Clear() zeroes counters. Must explicitly restore after load.

**Not syncing double buffers:**
UI reads from read buffer. Load writes to write buffer. Must sync.

**ENGINE calling GAME directly:**
Architecture violation. Use callbacks.

**Not refreshing UI after load:**
UI shows cached values. Emit GameLoadedEvent.

---

## Command Log Usage

### Purpose
Verify simulation determinism, not primary load mechanism.

### Storage
Ring buffer of recent commands (bounded memory).

### Verification Process
1. Save snapshot at tick N
2. Save commands N to N+100
3. Save expected checksum at N+100
4. On load: restore, replay, verify checksum

### When to Verify
- Development builds: Always
- Release builds: Never (performance)
- Bug reports: Always

---

## Atomicity

**Temp file → rename** prevents corruption on crash.

Never write directly to save file. Write to temp, then atomic rename.

---

## Version Compatibility

Save format includes version number. On load:
- Same version: Load normally
- Compatible version: Migrate if possible
- Incompatible version: Reject with clear message

---

## Key Trade-offs

| Aspect | Snapshot | Command Log |
|--------|----------|-------------|
| Load speed | Fast (instant) | Slow (replay) |
| Determinism | Not verified | Verified |
| File size | Larger | Smaller |
| Bug reproduction | State only | Full replay |

**Hybrid uses both:** Snapshot for production, command log for development/debugging.

---

## Related Patterns

- **Pattern 2 (Command Pattern):** Commands enable replay verification
- **Pattern 14 (Hybrid Save/Load):** This architecture
- **Pattern 17 (Single Source of Truth):** Each system owns its serialization

---

*Snapshot for speed. Command log for verification. Rebuild derived data on load.*
