# Hot/Cold Data Engine Separation - Refactoring Plan

**Date**: 2025-10-09
**Status**: Planning Phase
**Priority**: High - Foundational architecture issue

---

## Problem Statement

The current `ProvinceState` struct (Assets/Archon-Engine/Scripts/Core/Data/ProvinceState.cs) contains **game-specific fields** that violate the engine-first architecture principle:

```csharp
// Current ProvinceState (8 bytes)
public struct ProvinceState {
    public ushort ownerID;       // ✅ Engine: generic ownership
    public ushort controllerID;  // ✅ Engine: generic control
    public byte development;     // ❌ GAME-SPECIFIC: EU4-style mechanic
    public byte terrain;         // ✅ Engine: generic terrain type
    public byte fortLevel;       // ❌ GAME-SPECIFIC: fort system
    public byte flags;           // ✅ Engine: generic flags
}
```

**Key Issue**: `development` and `fortLevel` are **Hegemon game mechanics**, not generic engine primitives.

### Why This Matters

From engine-game-separation.md:
> **Engine provides mechanisms (HOW). Game defines policy (WHAT).**

The engine should NOT know about:
- "development" as a concept
- "fortification levels" as a mechanic

A truly reusable engine must be agnostic to game-specific mechanics.

---

## Current Architecture Analysis

### What We Have (Mixed Engine/Game)

**Core/Data/ProvinceState.cs** - Lines 19-22:
```csharp
public byte development;    // EU4-inspired development system
public byte fortLevel;      // EU4-inspired fort mechanics
```

**Core/Data/CountryData.cs** - Lines 15-22:
```csharp
public struct CountryHotData {
    public ushort tagHash;           // ✅ Generic
    public uint colorRGB;            // ✅ Generic
    public byte graphicalCultureId;  // ⚠️ Possibly game-specific
    public byte flags;               // ✅ Generic
}
```

### Hot/Cold Data Confusion

The docs (data-linking-architecture.md) conflate TWO different concepts:

1. **Performance Hot/Cold** (access frequency):
   - Hot: Accessed every frame → NativeArray
   - Cold: Accessed rarely → Dictionary

2. **Architecture Hot/Cold** (reusability):
   - Engine primitives (generic mechanisms)
   - Game logic (specific policies)

---

## Proposed Solution: Three-Layer Data Architecture

### Layer 1: Engine Hot Data (Generic Primitives)

```csharp
// ✅ ArchonEngine/Core/Data/ProvinceState.cs (ENGINE LAYER)
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ProvinceState {
    public ushort ownerID;       // Who owns this province
    public ushort controllerID;  // Who controls it militarily
    public ushort terrainType;   // Terrain type ID (from registry)
    public ushort gameDataSlot;  // Index into game-specific hot data
    // 8 bytes total - ZERO game mechanics
}
```

**Changes**:
- ❌ Remove `development` (game-specific)
- ❌ Remove `fortLevel` (game-specific)
- ✅ Add `gameDataSlot` (index into game layer data)
- ✅ Expand `terrain` from `byte` to `ushort` (supports 65k terrain types vs 256)

### Layer 2: Game Hot Data (Hegemon Mechanics)

```csharp
// ✅ Game/Data/HegemonProvinceData.cs (GAME LAYER)
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct HegemonProvinceData {
    public byte development;     // Hegemon's development system
    public byte fortLevel;       // Hegemon's fortification system
    public byte unrest;          // Hegemon's unrest mechanic
    public byte population;      // Hegemon's population abstraction
    // 4 bytes - game-specific hot data
}
```

**Storage**:
```csharp
// Engine storage
NativeArray<ProvinceState> engineStates;           // 8 bytes × 10k = 80KB

// Game storage
NativeArray<HegemonProvinceData> hegemonData;      // 4 bytes × 10k = 40KB
```

### Layer 3: Cold Data (Both Layers)

```csharp
// Engine cold data (generic presentation)
public class ProvinceColdData {
    public string name;               // Display name
    public Vector2Int mapPosition;    // Position in texture
    public ushort[] neighbors;        // Adjacent province IDs
    public Dictionary<string, object> metadata;  // Generic key-value
}

// Game cold data (Hegemon-specific)
public class HegemonProvinceColdData {
    public List<BuildingId> buildings;      // Buildings list
    public TradeGoodId tradeGood;          // Trade good
    public CultureId culture;              // Culture
    public ReligionId religion;            // Religion
    public CircularBuffer<HistoricalEvent> history;  // Event history
}
```

---

## Refactoring Plan

### Phase 1: Create Game Data Layer (Week 1)

#### Step 1.1: Create HegemonProvinceData

**File**: `Assets/Game/Data/HegemonProvinceData.cs`

```csharp
using System.Runtime.InteropServices;

namespace Game.Data
{
    /// <summary>
    /// GAME LAYER - Hegemon-specific province hot data
    /// Contains mechanics unique to Hegemon game
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct HegemonProvinceData {
        public byte development;     // 1 byte - EU4-style development (0-255)
        public byte fortLevel;       // 1 byte - Fortification level (0-255)
        public byte unrest;          // 1 byte - Province unrest/stability
        public byte population;      // 1 byte - Abstract population level
        // TOTAL: 4 bytes

        public static HegemonProvinceData CreateDefault() {
            return new HegemonProvinceData {
                development = 1,
                fortLevel = 0,
                unrest = 0,
                population = 10
            };
        }

        public static HegemonProvinceData FromLegacyProvinceState(ProvinceState legacy) {
            return new HegemonProvinceData {
                development = legacy.development,  // Extract from old struct
                fortLevel = legacy.fortLevel,      // Extract from old struct
                unrest = 0,                        // New field
                population = 10                    // New field
            };
        }
    }
}
```

#### Step 1.2: Create HegemonProvinceSystem

**File**: `Assets/Game/Systems/HegemonProvinceSystem.cs`

```csharp
using Core;
using Core.Data;
using Unity.Collections;
using Game.Data;

namespace Game.Systems
{
    /// <summary>
    /// GAME LAYER - Manages Hegemon-specific province data
    /// Bridges engine ProvinceState with game HegemonProvinceData
    /// </summary>
    public class HegemonProvinceSystem : IDisposable {
        private NativeArray<HegemonProvinceData> hegemonData;
        private ProvinceSystem provinceSystem;  // Engine system reference
        private bool isInitialized;

        public void Initialize(ProvinceSystem provinceSystem, int maxProvinces) {
            this.provinceSystem = provinceSystem;
            hegemonData = new NativeArray<HegemonProvinceData>(
                maxProvinces,
                Allocator.Persistent
            );
            isInitialized = true;
        }

        // Game-specific accessors
        public byte GetDevelopment(ushort provinceId) {
            return hegemonData[provinceId].development;
        }

        public void SetDevelopment(ushort provinceId, byte value) {
            var data = hegemonData[provinceId];
            data.development = value;
            hegemonData[provinceId] = data;
        }

        public byte GetFortLevel(ushort provinceId) {
            return hegemonData[provinceId].fortLevel;
        }

        public void SetFortLevel(ushort provinceId, byte value) {
            var data = hegemonData[provinceId];
            data.fortLevel = value;
            hegemonData[provinceId] = data;
        }

        // NEW: Hegemon-specific methods
        public byte GetUnrest(ushort provinceId) {
            return hegemonData[provinceId].unrest;
        }

        public void SetUnrest(ushort provinceId, byte value) {
            var data = hegemonData[provinceId];
            data.unrest = value;
            hegemonData[provinceId] = data;
        }

        public void Dispose() {
            if (isInitialized && hegemonData.IsCreated) {
                hegemonData.Dispose();
            }
        }
    }
}
```

#### Step 1.3: Create HegemonProvinceColdData

**File**: `Assets/Game/Data/HegemonProvinceColdData.cs`

```csharp
using System.Collections.Generic;
using Core.Data;

namespace Game.Data
{
    /// <summary>
    /// GAME LAYER - Hegemon-specific province cold data
    /// Rarely accessed, loaded on-demand
    /// </summary>
    public class HegemonProvinceColdData {
        public List<BuildingId> buildings;              // Buildings in province
        public TradeGoodId tradeGood;                   // What it produces
        public CultureId culture;                       // Dominant culture
        public ReligionId religion;                     // Dominant religion
        public CircularBuffer<HistoricalEvent> history; // Recent events (100 max)

        public HegemonProvinceColdData() {
            buildings = new List<BuildingId>();
            history = new CircularBuffer<HistoricalEvent>(100);
        }
    }
}
```

---

### Phase 2: Refactor Engine ProvinceState (Week 2)

#### Step 2.1: Update ProvinceState Struct

**File**: `Assets/Archon-Engine/Scripts/Core/Data/ProvinceState.cs`

```csharp
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ProvinceState
{
    public ushort ownerID;       // 2 bytes - country that owns this province
    public ushort controllerID;  // 2 bytes - country controlling militarily
    public ushort terrainType;   // 2 bytes - terrain type ID (expanded from byte)
    public ushort gameDataSlot;  // 2 bytes - index into game-specific data
    // TOTAL: exactly 8 bytes

    // ❌ REMOVED: public byte development;
    // ❌ REMOVED: public byte fortLevel;
    // ✅ ADDED: public ushort gameDataSlot;
    // ✅ CHANGED: byte terrain → ushort terrainType (more terrain types)

    /// <summary>
    /// Create a default province state (unowned, undeveloped)
    /// </summary>
    public static ProvinceState CreateDefault(ushort terrainType = 1, ushort gameSlot = 0)
    {
        return new ProvinceState
        {
            ownerID = 0,
            controllerID = 0,
            terrainType = terrainType,
            gameDataSlot = gameSlot
        };
    }

    /// <summary>
    /// Create province state with initial owner
    /// </summary>
    public static ProvinceState CreateOwned(ushort owner, ushort terrainType = 1, ushort gameSlot = 0)
    {
        return new ProvinceState
        {
            ownerID = owner,
            controllerID = owner,
            terrainType = terrainType,
            gameDataSlot = gameSlot
        };
    }

    // Remove flag-based system - move to separate byte if needed
    // Flags were mixing presentation concerns with simulation
}
```

**Breaking Changes**:
- ❌ `development` field removed → Use `HegemonProvinceSystem.GetDevelopment()`
- ❌ `fortLevel` field removed → Use `HegemonProvinceSystem.GetFortLevel()`
- ❌ `flags` field removed → Move to separate system if needed
- ⚠️ `terrain` changed from `byte` to `ushort` (backwards compatible in storage, not in API)

#### Step 2.2: Update ProvinceState API Surface

**Remove these methods**:
```csharp
// ❌ REMOVE - game-specific
public bool HasFlag(ProvinceFlags flag);
public void SetFlag(ProvinceFlags flag);
public void ClearFlag(ProvinceFlags flag);
```

**Keep these methods** (generic):
```csharp
// ✅ KEEP - generic simulation
public bool IsOwned => ownerID != 0;
public bool IsOccupied => controllerID != ownerID && ownerID != 0;
public byte[] ToBytes();
public static ProvinceState FromBytes(byte[] bytes);
public override int GetHashCode();
```

---

### Phase 3: Migration Bridge (Week 2)

Create compatibility layer to minimize immediate breakage:

**File**: `Assets/Game/Compatibility/ProvinceStateExtensions.cs`

```csharp
using Core.Data;
using Game.Systems;

namespace Game.Compatibility
{
    /// <summary>
    /// TEMPORARY: Compatibility extensions for gradual migration
    /// TODO: Remove after all systems migrated to HegemonProvinceSystem
    /// </summary>
    public static class ProvinceStateExtensions {
        // Compatibility getters that delegate to game layer
        [System.Obsolete("Use HegemonProvinceSystem.GetDevelopment() instead")]
        public static byte GetDevelopment(this ProvinceState state, HegemonProvinceSystem hegemonSystem, ushort provinceId) {
            return hegemonSystem.GetDevelopment(provinceId);
        }

        [System.Obsolete("Use HegemonProvinceSystem.GetFortLevel() instead")]
        public static byte GetFortLevel(this ProvinceState state, HegemonProvinceSystem hegemonSystem, ushort provinceId) {
            return hegemonSystem.GetFortLevel(provinceId);
        }
    }
}
```

---

### Phase 4: Update All Usages (Week 3)

#### Step 4.1: Find All ProvinceState Usages

**Search patterns**:
```
provinceState.development
provinceState.fortLevel
GetDevelopment(
SetDevelopment(
province.development
```

#### Step 4.2: Update Each System

**Pattern to follow**:

```csharp
// ❌ OLD (directly accessing ProvinceState)
var province = provinceSystem.GetProvinceState(id);
byte dev = province.development;

// ✅ NEW (using game layer)
var engineState = provinceSystem.GetProvinceState(id);  // Engine data
byte dev = hegemonProvinceSystem.GetDevelopment(id);    // Game data
```

**Files to update** (estimated):
1. ProvinceQueries.cs - Add overloads or deprecate development queries
2. Map display systems - Update to use HegemonProvinceSystem
3. UI panels - Update to query HegemonProvinceSystem
4. Loaders - Split loading between engine and game data
5. Commands - Update to modify both layers appropriately

---

### Phase 5: Update Loaders (Week 4)

#### Step 5.1: Split Province Loading

**Current** (BurstProvinceHistoryLoader.cs):
```csharp
// Loads everything into ProvinceState
provinceState.development = parsedData.development;
provinceState.fortLevel = parsedData.fortLevel;
```

**New** (Two-phase loading):

**Engine Loader** (BurstProvinceHistoryLoader.cs):
```csharp
// Phase 1: Load engine data
provinceState.ownerID = parsedData.ownerID;
provinceState.controllerID = parsedData.controllerID;
provinceState.terrainType = parsedData.terrainType;
provinceState.gameDataSlot = provinceId;  // 1:1 mapping for now
```

**Game Loader** (HegemonProvinceHistoryLoader.cs - NEW):
```csharp
// Phase 2: Load Hegemon game data
hegemonData.development = parsedData.development;
hegemonData.fortLevel = parsedData.fortLevel;
hegemonData.unrest = parsedData.unrest ?? 0;
hegemonData.population = parsedData.population ?? 10;
```

#### Step 5.2: Update Linking Phase

**ReferenceLinkingPhase.cs** needs to:
1. Resolve engine-layer references (owner, controller, terrain)
2. Pass game-specific data to HegemonProvinceHistoryLoader
3. Coordinate both phases

---

### Phase 6: Update Documentation (Week 4)

#### Files to update:

1. **data-linking-architecture.md**:
   - Clarify hot/cold means PERFORMANCE, not architecture
   - Update ProvinceState example (lines 291-308)
   - Add HegemonProvinceData example
   - Document three-layer architecture

2. **engine-game-separation.md**:
   - Update to show correct ProvinceState (no game fields)
   - Add HegemonProvinceData example
   - Document gameDataSlot pattern

3. **FILE_REGISTRY.md** (Core and Game):
   - Add HegemonProvinceData.cs
   - Add HegemonProvinceSystem.cs
   - Update ProvinceState.cs description

---

## Architecture Validation

### ✅ Engine Layer (Generic)

```csharp
// Can build ANY grand strategy game with this
public struct ProvinceState {
    public ushort ownerID;       // Who owns it
    public ushort controllerID;  // Who controls it
    public ushort terrainType;   // What terrain
    public ushort gameDataSlot;  // Game-specific index
}
```

**Reusability test**: Can you build...
- ✅ EU4-style game → Yes (Hegemon proves this)
- ✅ CK3-style game → Yes (different HegemonProvinceData)
- ✅ Stellaris-style game → Yes (space provinces, different mechanics)
- ✅ Total War-style → Yes (different unit system)

### ❌ Previous Design (Game-Specific)

```csharp
// Hardcoded for EU4-style games only
public struct ProvinceState {
    public byte development;  // Assumes development mechanic
    public byte fortLevel;    // Assumes fort mechanic
}
```

**Reusability test**: Can you build...
- ❌ Space game → No (no "development" in space)
- ❌ Ancient warfare → No (forts don't make sense)

---

## Performance Impact Analysis

### Memory Usage

**Before**:
```
ProvinceState: 8 bytes × 10k = 80KB
```

**After**:
```
ProvinceState:         8 bytes × 10k = 80KB  (same size)
HegemonProvinceData:   4 bytes × 10k = 40KB  (new)
----------------------------------------
Total:                               120KB  (+40KB, +50% memory)
```

**Verdict**: ✅ Acceptable - 40KB is negligible on modern hardware

### CPU Cache Performance

**Before**: Single 8-byte struct (cache-friendly)
**After**: Two separate arrays (requires two cache lines)

**Impact**:
- ⚠️ Slight performance hit when accessing BOTH engine and game data
- ✅ Better when accessing ONLY engine data (common in multiplayer sync)
- ✅ Better separation allows future optimizations (Burst jobs on each independently)

### Access Pattern

```csharp
// Scenario 1: Only need engine data (multiplayer sync, map rendering)
var state = engineSystem.Get(id);  // ✅ Single cache line
// No game data needed!

// Scenario 2: Need both (UI display, gameplay logic)
var state = engineSystem.Get(id);         // First cache line
var gameData = hegemonSystem.Get(id);     // Second cache line
// ⚠️ Two cache lines, but rare in hot paths
```

**Verdict**: ✅ Net positive - most hot paths only need engine data

---

## Migration Strategy

### Option A: Big Bang Refactoring (NOT RECOMMENDED)
- Update everything at once
- ❌ High risk of breaking everything
- ❌ Difficult to test incrementally

### Option B: Gradual Migration (RECOMMENDED)

**Week 1**: Create game layer alongside existing
**Week 2**: Refactor engine ProvinceState
**Week 3**: Add compatibility bridge
**Week 4**: Migrate systems one-by-one
**Week 5**: Remove compatibility layer
**Week 6**: Update documentation

**Benefits**:
- ✅ Test each change in isolation
- ✅ Can revert if issues found
- ✅ Game remains playable throughout

---

## Testing Strategy

### Unit Tests

**New test files**:
1. `HegemonProvinceDataTests.cs`
   - Test struct size (4 bytes)
   - Test default creation
   - Test legacy conversion

2. `HegemonProvinceSystemTests.cs`
   - Test get/set development
   - Test get/set fort level
   - Test new fields (unrest, population)

3. `ProvinceStateRefactoredTests.cs`
   - Test struct size still 8 bytes
   - Test gameDataSlot usage
   - Test terrain type expansion

### Integration Tests

1. **Loading test**: Load scenario, verify both layers populated
2. **Serialization test**: Save/load, verify data preserved
3. **UI test**: Province panel shows correct data from game layer
4. **Multiplayer test**: Engine data syncs, game data separate

---

## Rollback Plan

If refactoring causes issues:

1. **Keep compatibility bridge** until all systems stable
2. **Maintain parallel systems**: Both old and new ProvinceState patterns
3. **Feature flag**: `USE_SEPARATED_PROVINCE_DATA` toggle

```csharp
#if USE_SEPARATED_PROVINCE_DATA
    byte dev = hegemonSystem.GetDevelopment(id);
#else
    byte dev = provinceState.development;  // Old way
#endif
```

---

## Success Criteria

### ✅ Architecture Goals

1. **Engine is generic**: ProvinceState has zero game mechanics
2. **Game defines policy**: HegemonProvinceData contains all game logic
3. **Clear separation**: No mixing of engine/game concerns
4. **Reusable**: Can build different games with same engine

### ✅ Performance Goals

1. **Memory**: <150MB total (currently 80KB engine + 40KB game = 120KB, well within)
2. **Frame time**: <5ms for 10k provinces (no degradation)
3. **Load time**: <5s for scenario loading (no degradation)

### ✅ Code Quality Goals

1. **All tests pass**: Unit + integration tests green
2. **Documentation updated**: All docs reflect new architecture
3. **No warnings**: Clean compile
4. **FILE_REGISTRY updated**: Both Core and Game registries reflect changes

---

## Timeline

| Week | Phase | Deliverable |
|------|-------|-------------|
| 1 | Create Game Layer | HegemonProvinceData, HegemonProvinceSystem, HegemonProvinceColdData |
| 2 | Refactor Engine | Updated ProvinceState, compatibility bridge |
| 3 | Migrate Systems | Update all ProvinceState usages |
| 4 | Update Loaders | Split loading, update linking |
| 5 | Testing | Full test suite, integration tests |
| 6 | Documentation | Update all architecture docs |

**Total Time**: 6 weeks (can be parallelized with game development)

---

## Related Documents

- [engine-game-separation.md](../../Engine/engine-game-separation.md) - Architecture principles
- [data-linking-architecture.md](../../Engine/data-linking-architecture.md) - Hot/cold data patterns
- [FILE_REGISTRY.md](../../../Scripts/Core/FILE_REGISTRY.md) - Core layer catalog
- [performance-architecture-guide.md](../../Engine/performance-architecture-guide.md) - Performance patterns

---

## Next Steps

1. **Review this plan** with team/stakeholders
2. **Create GitHub issue** tracking this refactoring
3. **Branch**: `feature/engine-game-separation-provinces`
4. **Start with Phase 1**: Create game data layer (low risk)
5. **Test incrementally**: Each phase must pass tests before proceeding

---

*This refactoring establishes true engine-game separation and makes the engine reusable for different grand strategy games.*
