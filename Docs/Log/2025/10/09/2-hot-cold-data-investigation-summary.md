# Hot/Cold Data Investigation - Summary

**Date**: 2025-10-09
**Investigator**: Claude (via user question)
**Question**: "Are we actually doing hot/cold data correctly in relation to engine-game separation?"

---

## Short Answer

**NO**, we are NOT doing hot/cold data correctly for engine-first development.

The current `ProvinceState` contains **game-specific fields** (`development`, `fortLevel`) that should belong in the **game layer**, not the **engine layer**.

---

## The Problem

### What Was Found

**File**: `Assets/Archon-Engine/Scripts/Core/Data/ProvinceState.cs` (lines 19-22)

```csharp
public struct ProvinceState {
    public ushort ownerID;       // ✅ Engine: generic
    public ushort controllerID;  // ✅ Engine: generic
    public byte development;     // ❌ GAME-SPECIFIC: Hegemon mechanic
    public byte terrain;         // ✅ Engine: generic
    public byte fortLevel;       // ❌ GAME-SPECIFIC: Hegemon mechanic
    public byte flags;           // ✅ Engine: generic
}
```

### Why This Violates Architecture

From `engine-game-separation.md`:
> **Engine provides mechanisms (HOW). Game defines policy (WHAT).**

But `ProvinceState` defines **WHAT** provinces contain:
- `development` assumes the game has a development system (EU4-style)
- `fortLevel` assumes the game has fortifications

**A truly reusable engine shouldn't know these concepts exist.**

---

## Root Cause: Terminology Confusion

The architecture documents conflate TWO different meanings of "hot/cold":

### 1. Performance Hot/Cold (Access Frequency)
- **Hot data**: Accessed every frame → `NativeArray<T>` for cache locality
- **Cold data**: Accessed rarely → `Dictionary<K,V>` for flexibility

### 2. Architecture Hot/Cold (Reusability)
- **Engine primitives**: Generic mechanisms (ProvinceSystem, TimeManager)
- **Game logic**: Specific policies (EconomySystem, BuildingRules)

**These are DIFFERENT concepts using the SAME terminology!**

---

## Impact Assessment

### What Works ✅

1. **Performance architecture is correct**:
   - 8-byte `ProvinceState` in `NativeArray` ✅
   - Dictionary for cold data ✅
   - Hot/cold separation for access patterns ✅

2. **CountryData separation is better**:
   - `CountryHotData` (8 bytes, generic)
   - `CountryColdData` (class, flexible)
   - Cleaner separation than provinces

### What's Broken ❌

1. **Engine contains game mechanics**:
   - `development` field in engine layer
   - `fortLevel` field in engine layer
   - Cannot build fundamentally different games

2. **Documentation mixes concepts**:
   - `data-linking-architecture.md` shows game-specific ProvinceState as "engine"
   - No clear distinction between performance and architectural layers

3. **Reusability is compromised**:
   - Cannot build space strategy (no "development" concept)
   - Cannot build ancient warfare (forts don't exist yet)
   - Engine is actually "EU4-style game framework," not generic

---

## Recommended Solution

### Three-Layer Architecture

```
┌─────────────────────────────────────────────────────────┐
│ Layer 1: Engine Hot Data (Generic Primitives)          │
│ ProvinceState: ownerID, controllerID, terrainType,     │
│                gameDataSlot                             │
│ 8 bytes - ZERO game mechanics                          │
└─────────────────────────────────────────────────────────┘
                          ↓ gameDataSlot index
┌─────────────────────────────────────────────────────────┐
│ Layer 2: Game Hot Data (Hegemon Mechanics)             │
│ HegemonProvinceData: development, fortLevel, unrest,   │
│                      population                         │
│ 4 bytes - game-specific hot data                       │
└─────────────────────────────────────────────────────────┘
                          ↓ accessed rarely
┌─────────────────────────────────────────────────────────┐
│ Layer 3: Cold Data (Both Layers)                       │
│ ProvinceColdData: name, position, neighbors (engine)   │
│ HegemonProvinceColdData: buildings, trade, culture     │
└─────────────────────────────────────────────────────────┘
```

### Example Usage

```csharp
// Access engine data (multiplayer sync, map rendering)
var engineState = provinceSystem.GetProvinceState(id);
ushort owner = engineState.ownerID;

// Access game data (gameplay logic, UI)
var gameData = hegemonProvinceSystem.GetData(id);
byte development = gameData.development;
byte fortLevel = gameData.fortLevel;
```

---

## Files Investigated

### Core Files
1. ✅ `Assets/Archon-Engine/Scripts/Core/FILE_REGISTRY.md` - Complete catalog
2. ✅ `Assets/Archon-Engine/Scripts/Core/Data/ProvinceState.cs` - Found violations (lines 19-22)
3. ✅ `Assets/Archon-Engine/Scripts/Core/Data/CountryData.cs` - Better separation

### Map Files
1. ✅ `Assets/Archon-Engine/Scripts/Map/FILE_REGISTRY.md` - Complete catalog
2. ✅ Presentation layer correctly separated (no violations found)

### Architecture Docs
1. ✅ `Assets/Archon-Engine/Docs/Engine/engine-game-separation.md` - Philosophy correct, example wrong
2. ✅ `Assets/Archon-Engine/Docs/Engine/data-linking-architecture.md` - Shows game-specific ProvinceState (line 291-308)

---

## Decision Points

### Option 1: Accept Game-Specific Engine (Pragmatic)

**Description**: Keep current design, acknowledge engine is "EU4-style framework"

**Pros**:
- ✅ No refactoring needed
- ✅ Works for current game (Hegemon)
- ✅ Still reusable for EU4-like games (CK3, Imperator, Victoria)

**Cons**:
- ❌ Not truly generic
- ❌ Cannot build fundamentally different grand strategy games
- ❌ Violates stated architecture principles

**Recommendation**: ❌ Not recommended if "engine-game separation is the most important"

---

### Option 2: Refactor to Generic Engine (Recommended)

**Description**: Remove game mechanics from engine, create game data layer

**Pros**:
- ✅ True engine-game separation
- ✅ Can build ANY grand strategy game
- ✅ Aligns with stated architecture goals
- ✅ Better long-term reusability

**Cons**:
- ⚠️ Requires 6-week refactoring effort
- ⚠️ +40KB memory per 10k provinces (negligible)
- ⚠️ Slight complexity increase (two data arrays)

**Recommendation**: ✅ **Strongly recommended** given "engine-first" priority

---

## Next Actions

1. **Review** this investigation and refactoring plan
2. **Decide** on Option 1 (pragmatic) vs Option 2 (pure separation)
3. **If Option 2**: Review detailed plan in `hot-cold-data-engine-separation-refactoring-plan.md`
4. **Create branch**: `feature/engine-game-separation-provinces`
5. **Start Phase 1**: Create game data layer (low-risk first step)

---

## Key Insights

### What "Hot/Cold" Should Mean

| Context | Hot | Cold |
|---------|-----|------|
| **Performance** | Accessed every frame → NativeArray | Accessed rarely → Dictionary |
| **Architecture** | Engine primitives (generic) | Game logic (specific) |

**These are ORTHOGONAL concepts** - don't conflate them!

### Engine-First Principle

> If the engine knows about "development" or "forts," it's not an engine - it's a game framework.

A truly generic engine provides:
- ✅ Ownership tracking (generic)
- ✅ Terrain types (generic)
- ✅ State management (generic)
- ❌ Development levels (game-specific)
- ❌ Fortification (game-specific)

---

## Conclusion

The current hot/cold data architecture works well for **performance** but violates **engine-game separation** by embedding game mechanics in the engine layer.

**Recommendation**: Proceed with refactoring plan to achieve true separation.

**Estimated effort**: 6 weeks (can overlap with game development)

**Risk**: Low (gradual migration with compatibility bridge)

**Benefit**: True engine reusability for different grand strategy games

---

## Related Documents

- ✅ [hot-cold-data-engine-separation-refactoring-plan.md](hot-cold-data-engine-separation-refactoring-plan.md) - Detailed implementation plan
- [engine-game-separation.md](../../Engine/engine-game-separation.md) - Architecture principles
- [data-linking-architecture.md](../../Engine/data-linking-architecture.md) - Current (flawed) architecture
- [FILE_REGISTRY.md](../../../Scripts/Core/FILE_REGISTRY.md) - Core files catalog

---

*Investigation complete. Awaiting decision on refactoring approach.*
