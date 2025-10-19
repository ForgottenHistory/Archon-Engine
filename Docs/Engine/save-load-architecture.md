# Save/Load System Architecture

**Architecture Type:** Hybrid Snapshot + Command Log
**Primary Goal:** Fast loading with determinism verification
**Key Innovation:** State snapshot for speed, command log for validation

---

## Core Principles

### Principle 1: Single Source of Truth
Each system serializes its own state via `OnSave`/`OnLoad` hooks. SaveManager orchestrates, never duplicates data.

**Pattern:**
```csharp
// System owns its data and knows how to serialize it
class ProvinceSystem {
    public void SaveState(BinaryWriter writer) { /* save logic */ }
    public void LoadState(BinaryReader reader) { /* load logic */ }
}
```

### Principle 2: Hot Data Only
Serialize authoritative state. Rebuild derived data on load (indices, caches, GPU textures).

**Save:**
- ProvinceState (owner, controller, terrain)
- Resources (treasury arrays)
- Buildings (sparse hash maps)
- Modifiers (active modifier lists)

**Rebuild on Load:**
- provincesByOwner indices (derived from ProvinceState)
- Border textures (GPU regeneration)
- Map mode caches (marked dirty)
- Frame-coherent query caches
- Economy income cache

### Principle 3: Deterministic Serialization
Use fixed-point binary serialization. No floating-point, no platform-dependent formats.

**Determinism Requirements:**
- FixedPoint64 → serialize as `long RawValue`
- Commands → serialize with deterministic order
- Checksums → uint32 hash of game state
- Random seeds → saved and restored

### Principle 4: Command Log is Verification, Not Primary Load
Snapshot loads instantly. Command replay verifies determinism optionally (dev/testing).

**Production:** Snapshot only (fast)
**Development:** Snapshot + replay verification (validates determinism)

### Principle 5: Post-Load Finalization via Callbacks
ENGINE layer cannot call GAME/MAP layers directly. Use callback pattern for layer separation.

**Pattern:**
```csharp
// ENGINE (SaveManager)
public System.Action OnPostLoadFinalize;

// GAME (SaveLoadGameCoordinator)
saveManager.OnPostLoadFinalize = OnPostLoadFinalize;
```

---

## Serialization Patterns

### Pattern A: Dense Arrays (ProvinceSystem)
**Storage:** `NativeArray<ProvinceState>`
**Serialization:** Raw memory copy (all provinces)
**Use Case:** Every element has data, no sparsity

### Pattern B: Sparse Hash Maps (BuildingConstructionSystem)
**Storage:** `Dictionary<ushort, HashSet<ushort>>`
**Serialization:** Only provinces with buildings (skip empty)
**Use Case:** Most elements empty, sparse by nature

### Pattern C: Indexed Collections (ResourceSystem)
**Storage:** `Dictionary<ushort, FixedPoint64[]>`
**Serialization:** Country ID + resource array pairs
**Use Case:** All tracked entities, indexed by ID

### Pattern D: Fixed-Point Values (EconomySystem)
**Storage:** `FixedPoint64` arrays
**Serialization:** Serialize as `long[]` (RawValue property)
**Use Case:** Deterministic values requiring exact representation

### Pattern E: Managed Collections (CountryColdData)
**Storage:** Dictionaries, Lists
**Serialization:** Count + elements
**Use Case:** Variable-length collections, rebuild on load

---

## Save File Format

```
SaveFile.sav:
├── Header
│   ├── Magic bytes (4 bytes: "HGSV")
│   ├── Version (int32: major.minor)
│   ├── Save name (string)
│   ├── Save date (DateTime as long ticks)
│   ├── Current tick (int32)
│   ├── Game speed (int32)
│   └── Scenario name (string)
├── System Data (count + key-value pairs)
│   ├── TimeManager
│   ├── ResourceSystem
│   ├── ProvinceSystem
│   ├── ModifierSystem
│   ├── CountrySystem
│   ├── PlayerState (via callback)
│   └── GameSystems[] (via reflection)
├── Command Log (verification)
│   ├── Command count (int32)
│   └── Commands[] (serialized)
└── Checksum (uint32)
```

**Binary Format:** Custom binary (not JSON/XML)
**Atomicity:** Temp file → rename (prevents corruption)
**File Extension:** `.sav`

---

## Loading Pipeline

### Phase 1: Validation
1. Read header, verify magic bytes ("HGSV")
2. Check version compatibility
3. Deserialize metadata

### Phase 2: State Restoration
1. Call `LoadState()` for each ENGINE system
2. Call `OnLoad()` for GAME systems (via reflection)
3. Systems deserialize from `SaveGameData.systemData`

### Phase 3: Internal State Restoration
**Critical Pattern:** Systems that use `Clear()` must restore internal counters.

```csharp
// ProvinceSystem.LoadState()
dataManager.Clear();              // Sets provinceCount = 0
// ... load data ...
dataManager.RestoreProvinceCount(activeCount);  // CRITICAL: Restore count
```

**Without restoration:** Queries return empty results (GetAllProvinceIds returns 0 elements).

### Phase 4: Double Buffer Synchronization
Sync double-buffered systems to prevent UI reading stale data on first frame.

```csharp
provinceSystem.SyncBuffersAfterLoad();  // Copy write buffer → read buffer
```

### Phase 5: Post-Load Finalization (Callback Pattern)
**Architecture:** ENGINE calls GAME callback, GAME handles MAP + GAME finalization.

```csharp
// SaveManager (ENGINE)
OnPostLoadFinalize?.Invoke();

// SaveLoadGameCoordinator (GAME)
private void OnPostLoadFinalize() {
    RefreshMapTextures();      // MAP layer: GPU textures
    RebuildEconomyCache();     // GAME layer: derived data
    EmitGameLoadedEvent();     // UI layer: refresh displays
}
```

**Why Callbacks:** ENGINE cannot import GAME/MAP namespaces (architecture violation).

### Phase 6: UI Refresh via Events
Broadcast `GameLoadedEvent` to notify all UI systems.

```csharp
// SaveLoadGameCoordinator emits event
gameState.EventBus.Emit(new GameLoadedEvent { ... });

// UI components subscribe
gameState.EventBus.Subscribe<GameLoadedEvent>(OnGameLoaded);
```

**Subscribed Systems:** PlayerResourceBar, CountryInfoPanel, ProvinceInfoPanel

---

## Layer Separation Patterns

### ENGINE → GAME Communication (Callbacks)
ENGINE exposes delegates, GAME implements logic.

```csharp
// ENGINE (SaveManager)
public System.Action OnPostLoadFinalize;
public System.Func<byte[]> OnSerializePlayerState;
public System.Action<byte[]> OnDeserializePlayerState;

// GAME (SaveLoadGameCoordinator)
saveManager.OnPostLoadFinalize = OnPostLoadFinalize;
saveManager.OnSerializePlayerState = SerializePlayerState;
saveManager.OnDeserializePlayerState = DeserializePlayerState;
```

**Rule:** ENGINE never calls GAME code directly. Always use callbacks.

### GAME → MAP Communication (Direct)
GAME layer can import MAP layer (allowed by architecture).

```csharp
// GAME layer can directly call MAP layer
var ownerDispatcher = FindFirstObjectByType<OwnerTextureDispatcher>();
ownerDispatcher.PopulateOwnerTexture(gameState.ProvinceQueries);
```

### GAME → ENGINE Communication (Direct)
GAME layer uses ENGINE via GameState.

```csharp
// GAME layer accesses ENGINE systems via GameState
var provinceSystem = gameState.Provinces;
provinceSystem.GetProvinceOwner(provinceId);
```

---

## Common Pitfalls & Solutions

### Pitfall 1: Forgetting to Restore Internal State
**Problem:** `Clear()` zeroes counters, load doesn't restore them.

```csharp
// BAD
dataManager.Clear();  // provinceCount = 0
// ... load data ...
// provinceCount still 0! GetAllProvinceIds() returns empty array

// GOOD
dataManager.Clear();
// ... load data ...
dataManager.RestoreProvinceCount(savedCount);  // Restore counter
```

### Pitfall 2: Not Syncing Double Buffers
**Problem:** UI reads from read buffer, load writes to write buffer, UI shows stale data.

```csharp
// BAD
provinceSystem.LoadState(reader);  // Only updates write buffer
// UI reads read buffer → shows old data

// GOOD
provinceSystem.LoadState(reader);
provinceSystem.SyncBuffersAfterLoad();  // Copy write → read
```

### Pitfall 3: ENGINE Calling GAME Code Directly
**Problem:** Architecture violation, breaks layer separation.

```csharp
// BAD (ENGINE → GAME violation)
var economySystem = gameState.GetGameSystem<EconomySystem>();
economySystem.InvalidateAllIncome();

// GOOD (callback pattern)
OnPostLoadFinalize?.Invoke();  // GAME implements finalization
```

### Pitfall 4: Not Refreshing UI After Load
**Problem:** UI shows cached values, doesn't reflect loaded state.

```csharp
// BAD
// Load completes, UI still shows old income values

// GOOD
EmitGameLoadedEvent();  // Broadcast event
// All UI components subscribe and refresh on GameLoadedEvent
```

---

## Serialization Helpers

### FixedPoint64 Serialization
```csharp
SerializationHelper.WriteFixedPoint64(writer, value);
FixedPoint64 value = SerializationHelper.ReadFixedPoint64(reader);
```

### NativeArray Serialization
```csharp
// Allocate new array (returns allocated array)
var array = SerializationHelper.ReadNativeArray<T>(reader, Allocator.Persistent);

// In-place deserialization (writes to existing array)
SerializationHelper.ReadNativeArray(reader, existingArray);
```

### String Serialization
```csharp
SerializationHelper.WriteString(writer, str);
string str = SerializationHelper.ReadString(reader);
```

---

## Performance Characteristics

**Save Time:**
- Small map (1000 provinces): <500ms
- Medium map (3000 provinces): <1000ms
- Large map (5000 provinces): <2000ms

**Load Time:**
- Small map: <1000ms
- Medium map: <2000ms
- Large map: <3000ms

**File Size (Uncompressed):**
- Base snapshot: 50-100 MB
- Command log: 24 KB
- Total: 50-100 MB

**Bottlenecks:**
- I/O (disk write/read)
- NativeArray serialization (raw memory copy)
- Managed collection serialization (CountryColdData)

---

## Command Log & Verification

**Purpose:** Verify simulation determinism.

**Storage:** Ring buffer (last 6,000 commands ≈ 100 ticks × 60 commands/tick)

**Verification Process:**
1. Save state snapshot at tick N
2. Save commands from tick N to N+100
3. Save expected checksum at tick N+100
4. On load: restore snapshot, replay 100 ticks, verify checksum matches

**When to Run:**
- Always in development builds
- Never in release builds (performance cost)
- Always when loading bug report saves

---

## Future-Proofing

### Version Compatibility
Save format version number. Detect incompatible saves gracefully.

```csharp
if (saveData.saveFormatVersion != CURRENT_VERSION) {
    // Attempt migration or reject
}
```

### Migration Path to Full Command Replay
**Current:** Snapshot + recent commands
**Future:** Initial state + all commands

**Migration Effort:** Minimal (file format already supports both patterns)

---

## File Structure Reference

```
Assets/Archon-Engine/Scripts/Core/SaveLoad/
├── SaveManager.cs              # Orchestrates save/load
├── SaveGameData.cs             # Save file data structure
├── SerializationHelper.cs      # Binary serialization utilities
└── CommandLogger.cs            # Command history (future)

Assets/Archon-Engine/Scripts/Core/Events/
└── SaveLoadEvents.cs           # GameLoadedEvent, GameSavedEvent

Assets/Game/
├── SaveLoadGameCoordinator.cs  # GAME layer finalization
└── PlayerState.cs              # Player-specific state (2 bytes)

Core Systems (implement SaveState/LoadState):
├── ProvinceSystem.cs
├── CountrySystem.cs
├── ModifierSystem.cs
├── ResourceSystem.cs
└── TimeManager.cs
```

---

## Related Documentation

- **[master-architecture-document.md](master-architecture-document.md)** - Overall architecture
- **[data-flow-architecture.md](data-flow-architecture.md)** - Command pattern, events
- **[dual-layer-architecture.md](dual-layer-architecture.md)** - ENGINE/GAME/MAP separation

---

*Architecture Document*
*Last Updated: 2025-10-19*
