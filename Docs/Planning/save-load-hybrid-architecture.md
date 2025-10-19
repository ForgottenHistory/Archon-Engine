# Save/Load System - Hybrid Architecture
**Status:** 📋 Planning
**Type:** Core System Implementation
**Complexity:** Medium-High

---

## Executive Summary

**Architecture:** Hybrid snapshot + command replay
**Primary Goal:** Fast loading with determinism verification
**Secondary Goals:** Bug reproduction, multiplayer foundation, replay system

**Core Innovation:** State snapshot for speed, command log for verification

---

## Architectural Principles

### Principle 1: Single Source of Truth Per System
Each GameSystem serializes its own state via OnSave/OnLoad hooks. SaveManager orchestrates, never duplicates data.

### Principle 2: Hot Data Only
Serialize authoritative state. Rebuild derived data on load (indices, caches, GPU textures).

**Save:**
- ✅ ProvinceState (owner, controller, terrain)
- ✅ Resources (treasury arrays)
- ✅ Buildings (sparse hash maps)
- ✅ Modifiers (active modifier lists)

**Rebuild on Load:**
- ❌ provincesByOwner (derived from ProvinceState)
- ❌ Border textures (GPU regeneration)
- ❌ Map mode caches (marked dirty)
- ❌ Frame-coherent query caches

### Principle 3: Deterministic Serialization
Use fixed-point binary serialization. No floating-point, no platform-dependent formats.

**Determinism Requirements:**
- FixedPoint64 → serialize as `long RawValue`
- Commands → serialize with deterministic order
- Checksums → uint32 hash of game state
- Random seeds → saved and restored

### Principle 4: Command Log is Verification, Not Primary Load
Snapshot loads instantly. Command replay verifies determinism optionally (dev/testing mode).

**Production Loading:** Snapshot only (fast)
**Development Loading:** Snapshot + replay last 100 ticks + checksum verification (validates determinism)

### Principle 5: Forward Compatibility via Versioning
Save version number. Detect incompatible saves. Allow graceful degradation for minor version changes.

---

## System Components

### SaveManager (Orchestrator)
**Responsibility:** Coordinate save/load across all systems
**Pattern:** Calls GameSystem.OnSave/OnLoad in dependency order
**Location:** `Assets/Archon-Engine/Scripts/Core/SaveLoad/SaveManager.cs`

**Key Methods:**
- `SaveGame(string saveName)` - full state + recent commands
- `LoadGame(string saveName)` - restore snapshot, optional replay verification
- `QuickSave()` - F5 hotkey, saves to "quicksave.sav"
- `AutoSave()` - periodic saves (every N ticks)

### CommandLogger (Command History)
**Responsibility:** Track executed commands for replay/verification
**Pattern:** Ring buffer (last N commands only)
**Location:** `Assets/Archon-Engine/Scripts/Core/Commands/CommandLogger.cs`

**Storage Strategy:**
- Keep last 6,000 commands (100 ticks × 60 avg commands/tick)
- Discard older commands (ring buffer)
- Serialize to save file for verification

### SerializationHelper (Low-Level Serialization)
**Responsibility:** Binary serialization of primitive types and arrays
**Location:** `Assets/Archon-Engine/Scripts/Core/SaveLoad/SerializationHelper.cs`

**Handles:**
- FixedPoint64 serialization (as long RawValue)
- NativeArray serialization (raw memory copy)
- Sparse data structure serialization (skip empty entries)
- Command serialization (type ID + payload)

### ChecksumCalculator (Determinism Verification)
**Responsibility:** Generate deterministic checksum of game state
**Location:** `Assets/Archon-Engine/Scripts/Core/SaveLoad/ChecksumCalculator.cs`

**Algorithm:**
- Hash all ProvinceState fields (XOR fold)
- Hash all system state (resource totals, building counts)
- Combine with deterministic order
- Produce uint32 checksum

---

## Save File Format

```
SaveFile.sav:
├── Header
│   ├── Magic bytes (4 bytes: "HGSV")
│   ├── Version (ushort: major.minor)
│   ├── Checksum header (uint32)
│   └── Metadata length (int32)
├── Metadata
│   ├── Save name (string)
│   ├── Save date (DateTime as long)
│   ├── Current tick (int32)
│   ├── Game speed (byte)
│   └── Scenario name (string)
├── State Snapshot
│   ├── ProvinceSystem state
│   ├── HegemonProvinceSystem state
│   ├── ResourceSystem state
│   ├── EconomySystem state
│   ├── BuildingConstructionSystem state
│   ├── ModifierSystem state
│   └── TimeManager state
├── Command Log (last 100 ticks)
│   ├── Command count (int32)
│   └── Commands[] (serialized)
└── Checksum (expected after replay)
```

**Binary Format:** Custom binary (not JSON/XML for size/speed)
**Compression:** Optional zlib/gzip compression layer
**File Extension:** `.sav`

---

## System Serialization Patterns

### Pattern A: Dense Arrays (ProvinceSystem)
**Storage:** NativeArray<ProvinceState>
**Serialization:** Raw memory copy (all provinces)
**Rationale:** Every province has state, no sparsity

### Pattern B: Sparse Hash Maps (BuildingConstructionSystem)
**Storage:** Dictionary<ushort, HashSet<ushort>>
**Serialization:** Only provinces with buildings (skip empty)
**Rationale:** Most provinces have 0-3 buildings, sparse by nature

### Pattern C: Indexed Collections (ResourceSystem)
**Storage:** Dictionary<ushort, FixedPoint64[]>
**Serialization:** Country ID + resource array pairs
**Rationale:** All countries tracked, but indexed by ID

### Pattern D: Fixed-Point Values (EconomySystem)
**Storage:** FixedPoint64 arrays
**Serialization:** Serialize as long[] (RawValue property)
**Rationale:** Deterministic across platforms

---

## Loading Pipeline

### Phase 1: Validation
1. Read header, verify magic bytes
2. Check version compatibility
3. Validate checksum header
4. Deserialize metadata

### Phase 2: State Restoration
1. Call OnLoad for each system (dependency order via SystemRegistry)
2. Systems deserialize their state from save data
3. No derived data yet (indices empty)

### Phase 3: Derived Data Rebuild
1. Rebuild provincesByOwner indices (ProvinceSystem)
2. Regenerate border textures (BorderComputeDispatcher)
3. Mark all map modes dirty (MapModeManager)
4. Clear frame-coherent caches

### Phase 4: Verification (Optional - Dev Mode Only)
1. Calculate checksum before replay
2. Replay last 100 ticks of commands
3. Calculate checksum after replay
4. Compare with saved checksum
5. Log warning if mismatch (determinism break detected)

### Phase 5: Finalization
1. Update UI (resource displays, info panels)
2. Position camera (restore last camera position)
3. Resume game loop

---

## Command Replay Verification

**Purpose:** Prove simulation is deterministic

**Process:**
1. Save state snapshot at tick N
2. Save commands from tick N to N+100
3. Save checksum at tick N+100
4. On load: restore snapshot, replay 100 ticks, verify checksum matches

**Benefit:** Catches non-determinism bugs early (float usage, uninitialized state, race conditions)

**When to Run:**
- Always in development builds
- Never in release builds (performance)
- Always when loading saves from bug reports

---

## Migration Path to Full Command Replay

**Current (Hybrid):**
- Save: Snapshot + recent commands
- Load: Restore snapshot (fast)

**Future (Full Replay):**
- Save: Initial state reference + all commands
- Load: Load initial state + replay all commands (slower, smaller files)

**Migration Effort:**
1. Save initial scenario state once (e.g., "scenario_1444.initial")
2. Change save format: store reference to initial state instead of snapshot
3. Change save format: store all commands instead of last 100
4. Change load: replay all instead of last 100

**Compatibility:** Hybrid files can be converted to full replay (snapshot becomes checkpoint)

---

## Save File Location

**Platform-Specific Paths:**
- Windows: `%APPDATA%/Hegemon/Saves/`
- macOS: `~/Library/Application Support/Hegemon/Saves/`
- Linux: `~/.config/Hegemon/Saves/`

**File Naming:**
- `quicksave.sav` - F5 quicksave (overwrites)
- `autosave_YYYY-MM-DD_HH-mm.sav` - periodic autosaves
- `manual_SaveName_YYYY-MM-DD.sav` - manual saves

**Save Slots:** No artificial limit, filesystem-limited

---

## Performance Targets

**Save Time:**
- Small map (1000 provinces): <500ms
- Medium map (3000 provinces): <1000ms
- Large map (5000 provinces): <2000ms

**Load Time:**
- Small map: <1000ms
- Medium map: <2000ms
- Large map: <3000ms

**File Size:**
- Snapshot: 50-100 MB (uncompressed)
- Command log (100 ticks): 24 KB
- Total uncompressed: 50-100 MB
- Total compressed (zlib): 10-20 MB

**Checksum Calculation:** <50ms (all systems)

---

## Error Handling

### Save Failures
**Disk full:** Show error, offer to delete old saves
**Permission denied:** Show error, offer alternative location
**Corruption during write:** Atomic writes (temp file → rename)

### Load Failures
**File not found:** Show error, return to menu
**Version incompatible:** Show warning, attempt migration or reject
**Checksum mismatch:** Log warning (determinism break), allow load
**Corrupted data:** Show error, offer to load last autosave

### Determinism Breaks
**Checksum verification fails:** Log detailed diff, allow load with warning
**Command replay crashes:** Catch exception, log state, skip verification

---

## Implementation Phases

### Phase 1: State Snapshot Foundation
**Deliverables:**
- SaveGameData structure
- SerializationHelper (FixedPoint64, NativeArray)
- SaveManager (save/load orchestration)
- OnSave/OnLoad for core systems (Province, Economy, Resource, Building, Modifier)

**Validation:** Round-trip test (save → load → verify identical state)

### Phase 2: Command Logging
**Deliverables:**
- CommandLogger (ring buffer)
- Command serialization for all existing commands
- Integration with CommandProcessor (log all executed commands)

**Validation:** Commands serialize/deserialize correctly

### Phase 3: Replay Verification
**Deliverables:**
- ChecksumCalculator (deterministic state hash)
- Replay logic (execute command log)
- Verification mode (compare checksums)

**Validation:** Replay 100 ticks produces identical checksum

### Phase 4: UI Integration
**Deliverables:**
- Save/Load menu UI
- Quicksave (F5 hotkey)
- Autosave system (periodic saves)
- Save file browser (list saves with metadata)

**Validation:** User can save/load via UI

### Phase 5: Polish & Optimization
**Deliverables:**
- Compression (zlib)
- Async save/load (background thread)
- Progress indicators
- Save file cleanup (auto-delete old autosaves)

**Validation:** Performance targets met

---

## Testing Strategy

### Unit Tests
- SerializationHelper: FixedPoint64 round-trip
- SerializationHelper: NativeArray round-trip
- CommandLogger: Ring buffer behavior
- ChecksumCalculator: Deterministic output

### Integration Tests
- Round-trip: Save → Load → verify state identical
- Command replay: Replay 100 ticks → verify checksum
- System isolation: Each system OnSave/OnLoad works independently

### Stress Tests
- Large map: 5000 provinces, save/load performance
- Long game: 10 hours played, command log size
- Rapid saves: Save every tick for 1000 ticks (stress test I/O)

### Determinism Tests
- Replay 1000 ticks → verify checksum
- Load save twice → play 100 ticks → verify identical state
- Multiply save/load cycles → verify no state drift

---

## Dependencies

**Required Systems (must be implemented first):**
- ✅ GameSystem base class (already exists)
- ✅ SystemRegistry (already exists)
- ✅ Command pattern (already exists)
- ✅ ProvinceSystem (already exists)
- ✅ ResourceSystem (already exists)
- ✅ EconomySystem (already exists)
- ✅ BuildingConstructionSystem (already exists)
- ✅ ModifierSystem (already exists)

**Optional Dependencies:**
- ⏸️ Compression library (zlib/gzip)
- ⏸️ Async I/O (background thread saving)

---

## Success Metrics

**Functional Requirements:**
- ✅ Can save game at any point
- ✅ Can load save and continue gameplay
- ✅ State after load is identical to state before save
- ✅ Quicksave (F5) works
- ✅ Autosave works (periodic)

**Non-Functional Requirements:**
- ✅ Save time <2 seconds (large maps)
- ✅ Load time <3 seconds (large maps)
- ✅ File size <100 MB uncompressed
- ✅ Determinism verification passes (replay produces identical checksum)

**Quality Requirements:**
- ✅ No data loss on power failure (atomic writes)
- ✅ Version incompatibility detected gracefully
- ✅ Corruption detected and reported
- ✅ All tests pass (unit, integration, stress, determinism)

---

## Future Extensions

### Save Compression
Add zlib compression layer. Reduces file size 5-10x. Minimal load time impact.

### Async Save/Load
Save/load on background thread. No frame stutter. Requires careful thread safety.

### Cloud Saves
Upload/download saves to cloud storage. Requires authentication, conflict resolution.

### Full Command Replay
Switch from snapshot to initial state + all commands. Smaller files, slower load, better verification.

### Replay System
Export command log for replay viewer. Watch game playback. Requires replay UI.

### Multiplayer Sync
Use command log for network synchronization. Commands are already serialized.

---

## Related Documentation

- **[master-architecture-document.md](../Engine/master-architecture-document.md)** - Overall architecture principles
- **[data-flow-architecture.md](../Engine/data-flow-architecture.md)** - Command pattern, event system
- **[sparse-data-structures-design.md](../Engine/sparse-data-structures-design.md)** - Sparse data serialization patterns
- **[fixed-point-determinism.md](../Log/decisions/fixed-point-determinism.md)** - Deterministic math requirements

---

*Planning Document Created: 2025-10-19*
*Status: Ready for Implementation*
