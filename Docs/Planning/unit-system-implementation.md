# Unit System Implementation Plan
**Date:** 2025-10-19
**Type:** ENGINE Feature Implementation
**Scope:** Military Pillar - Phase 1 (Unit System)
**Status:** 📋 Planning

---

## OVERVIEW

Implement the foundation for military units in Archon-Engine. This is **Phase 1** of the Military pillar - getting units to exist, be created, and be destroyed. Movement and combat come later.

**Key Principle:** Architect for 3D visualization from day one, but implement simple visualization first. Zero refactoring needed when adding 3D models later.

**Success Criteria:**
- ✅ Can create/disband units via commands
- ✅ Units stored efficiently (8-byte structs, NativeArray)
- ✅ Sparse province→units mapping scales to 10k units
- ✅ Save/Load preserves unit state
- ✅ Deterministic (same commands = same units)
- ✅ Visual confirmation (province counter showing "5 units")
- ✅ 3D rendering possible later with zero engine changes

---

## ARCHITECTURE

### Layer Separation (Critical)

**ENGINE Layer (Core.Units):**
- `UnitState` - 8-byte struct (provinceID, countryID, unitTypeID, strength, morale)
- `UnitSystem` - Manages NativeArray<UnitState>, provides queries
- `UnitCommands` - CreateUnitCommand, DisbandUnitCommand
- Sparse storage - Province → Unit IDs mapping
- No knowledge of visuals, positions, or rendering

**GAME Layer (Game.Units):**
- `UnitDefinition` - JSON5 definitions (cost, stats, **visual metadata for future**)
- `UnitRegistry` - Central registry for unit types
- `IUnitRenderer` - Visualization interface (future-proof abstraction)
- `ProvinceCounterRenderer` - Simple text display (Phase 1 implementation)
- `UnitModel3DRenderer` - 3D models/billboards (Phase 2+ implementation)

**Why This Matters:**
- Renderer is swappable without touching engine
- 3D visualization = drop in new renderer class
- Engine tests work regardless of visualization
- Multiplayer-ready (no rendering in simulation)

---

## DATA STRUCTURES

### UnitState (ENGINE - 8 bytes)

```csharp
// Core.Units.UnitState
// 8 bytes - fits in cache line
struct UnitState {
    ushort provinceID;     // Current location (0-65535)
    ushort countryID;      // Owner (0-65535)
    ushort unitTypeID;     // Infantry/Cavalry/Artillery (0-65535)
    byte strength;         // 0-100 (percentage of max)
    byte morale;           // 0-100 (combat effectiveness)
}
```

**Design Decisions:**
- **8 bytes total** - Cache-friendly, network-friendly
- **provinceID, not position** - Simulation knows provinces, not coordinates
- **Percentage strength** - 0-100 is sufficient granularity, saves space
- **No visual data** - Renderer looks up visual assets separately

### UnitDefinition (GAME - JSON5)

```json5
{
  id: "infantry",
  name: "Infantry",
  category: "land",

  cost: {
    gold: 10,
    manpower: 1000
  },

  maintenance: {
    gold: 0.5  // per month
  },

  stats: {
    max_strength: 1000,  // men per regiment
    attack: 2,
    defense: 2,
    morale: 3,
    speed: 4             // provinces per day
  },

  // Future-proofing: visual data ignored in Phase 1
  visual: {
    type: "model_3d",           // or "billboard"
    asset: "Models/Infantry",    // path to prefab/sprite
    scale: 1.0,
    icon: "Icons/Infantry",
    color_source: "country"      // tint with country color
  }
}
```

**Why Include Visual Data Now:**
- Definitions stable from day one
- No JSON5 changes when adding 3D
- Modders can prepare visual assets early

---

## CORE COMPONENTS

### 1. UnitSystem (ENGINE)

**Purpose:** Central manager for all units in the game

**Storage:**
```csharp
NativeArray<UnitState> units;              // All units (indexed by unitID)
SparseCollectionManager<ushort, ushort> provinceUnits;  // Province → Unit IDs
Dictionary<ushort, UnitColdData> unitColdData;          // Rare data (name, history)
```

**API:**
```csharp
// Creation/Destruction
ushort CreateUnit(ushort provinceID, ushort countryID, ushort unitTypeID);
void DisbandUnit(ushort unitID);

// Queries
UnitState GetUnit(ushort unitID);
NativeArray<ushort> GetUnitsInProvince(ushort provinceID);
NativeArray<ushort> GetCountryUnits(ushort countryID);
int GetUnitCount();

// Modification
void SetUnitStrength(ushort unitID, byte strength);
void SetUnitMorale(ushort unitID, byte morale);
void MoveUnit(ushort unitID, ushort newProvinceID);  // Updates sparse mapping

// Persistence
void SaveState(BinaryWriter writer);
void LoadState(BinaryReader reader);
```

**Performance:**
- Sparse storage scales with actual units (not possible units)
- 10k units × 8 bytes = 80KB hot data
- GetUnitsInProvince() = O(m) where m = units in province (typically 1-10)

### 2. Unit Commands (ENGINE)

**CreateUnitCommand:**
```csharp
class CreateUnitCommand : ICommand {
    ushort provinceID;
    ushort countryID;
    ushort unitTypeID;
    ushort resultUnitID;  // Assigned after execution

    // Validation: province owned by country, has resources, etc.
    // Execution: Deduct resources, create unit, emit event
}
```

**DisbandUnitCommand:**
```csharp
class DisbandUnitCommand : ICommand {
    ushort unitID;

    // Validation: unit exists
    // Execution: Remove unit, refund partial resources, emit event
}
```

**Future Commands (not in Phase 1):**
- `MoveUnitCommand` - Phase 2 (Movement System)
- `MergeUnitsCommand` - Phase 2 (combine damaged units)
- `SplitUnitCommand` - Phase 3 (detach regiments)

### 3. UnitDefinition & Registry (GAME)

**UnitDefinition.cs:**
```csharp
class UnitDefinition : IDefinition {
    public ushort ID { get; set; }
    public string StringID { get; set; }
    public string Name { get; set; }
    public string Category { get; set; }  // land, naval, air

    public ResourceCost Cost { get; set; }
    public ResourceCost Maintenance { get; set; }
    public UnitStats Stats { get; set; }
    public UnitVisualData Visual { get; set; }  // Future use
}
```

**UnitRegistry.cs:**
```csharp
class UnitRegistry {
    Dictionary<string, UnitDefinition> byStringID;
    Dictionary<ushort, UnitDefinition> byID;

    UnitDefinition GetByID(ushort id);
    UnitDefinition GetByStringID(string id);
    ushort GetIDFromStringID(string id);
}
```

---

## VISUALIZATION ARCHITECTURE

### IUnitRenderer Interface (GAME)

**Purpose:** Abstract visualization, allow swapping renderers with zero engine changes

```csharp
interface IUnitRenderer {
    void Initialize(GameState gameState);
    void RenderUnits(Camera camera);  // Called every frame
    void OnUnitCreated(ushort unitID);
    void OnUnitDestroyed(ushort unitID);
    void OnUnitMoved(ushort unitID, ushort oldProvince, ushort newProvince);
    void Dispose();
}
```

### Phase 1: ProvinceCounterRenderer

**Implementation:**
- Count units per province
- Display "5 units" on province (UI text or shader-based)
- Click province → show unit list in panel
- Simple, fast, functional

**No 3D models, no sprites, no positions.**

### Phase 2+: UnitModel3DRenderer (Future)

**Implementation:**
- Reads `UnitDefinition.Visual` for model paths
- Uses GPU instancing (one draw call per unit type)
- LOD system: far = billboard, close = 3D model
- Formation positioning (stack units in province)

**Swapping:**
```csharp
// In GameInitializer or settings
IUnitRenderer renderer;

if (settings.Use3DUnits) {
    renderer = new UnitModel3DRenderer();
} else {
    renderer = new ProvinceCounterRenderer();
}
```

**Zero engine changes. Zero save/load changes. Just drop in.**

---

## INTEGRATION POINTS

### With Existing Systems

**ResourceSystem:**
- CreateUnitCommand validates resources (gold + manpower)
- Deducts costs on unit creation
- Monthly maintenance tick deducts upkeep

**ModifierSystem:**
- Unit stats affected by modifiers (e.g., "Infantry Combat Ability +10%")
- Building modifiers apply (e.g., Barracks: "-10% recruitment cost")

**ProvinceSystem:**
- Units stored by provinceID
- Province ownership changes → units change owner or get destroyed

**EventBus:**
- `UnitCreatedEvent` - UI updates, achievement tracking
- `UnitDestroyedEvent` - UI updates, statistics
- `UnitMovedEvent` - Future, for movement system

**SaveManager:**
- UnitSystem.SaveState/LoadState
- Save sparse collections (province→units mapping)
- Serialize NativeArray<UnitState>

---

## IMPLEMENTATION PHASES

### Phase 1: Foundation (This Implementation)

**ENGINE:**
1. Create `UnitState` struct
2. Create `UnitSystem` (NativeArray storage)
3. Create sparse collection for province→unit mapping
4. Create `CreateUnitCommand`, `DisbandUnitCommand`
5. Add `UnitSystem.SaveState/LoadState`

**GAME:**
6. Create `UnitDefinition` and `UnitRegistry`
7. Create JSON5 unit definitions (infantry, cavalry, artillery)
8. Create `IUnitRenderer` interface
9. Implement `ProvinceCounterRenderer`
10. Add unit creation UI (button in province panel)

**Validation:**
- Create 10k units → verify <100ms creation
- Save/load with units → verify round-trip
- Sparse collection scales (80KB for 10k units, not 5MB)
- Province counter shows correct counts

### Phase 2: Movement (Future)

**Not in this implementation:**
- `MoveUnitCommand`
- Pathfinding (A* on province adjacency)
- Movement queue (deterministic ordering)
- Movement visualization

### Phase 3: Combat (Future)

**Not in this implementation:**
- `CombatSystem`
- Battle resolution (damage calculation)
- Morale breaks and retreats
- Combat modifiers

### Phase 4: 3D Visualization (Future)

**Not in this implementation:**
- `UnitModel3DRenderer`
- Billboard sprites or 3D models
- GPU instancing
- LOD system
- Formation positioning

---

## FILE STRUCTURE

```
Assets/Archon-Engine/Scripts/Core/
  Units/
    UnitState.cs                    ← 8-byte struct
    UnitSystem.cs                   ← NativeArray manager
    UnitCommands.cs                 ← CreateUnit, DisbandUnit
    UnitEvents.cs                   ← UnitCreatedEvent, UnitDestroyedEvent
    UnitColdData.cs                 ← Rare unit data (name, history)

Assets/Game/
  Data/
    UnitDefinition.cs               ← JSON5 definition class
    UnitRegistry.cs                 ← Central registry
    UnitStats.cs                    ← Stats struct (attack, defense, etc.)
    UnitVisualData.cs               ← Visual metadata (future use)

  Loaders/
    UnitDefinitionLoader.cs         ← Load JSON5 files

  Commands/Factories/
    CreateUnitCommandFactory.cs     ← Console command factory
    DisbandUnitCommandFactory.cs    ← Console command factory

  Visualization/
    IUnitRenderer.cs                ← Interface
    ProvinceCounterRenderer.cs      ← Simple implementation
    UnitModel3DRenderer.cs          ← 3D implementation (Phase 4)

  UI/
    UnitListPanel.cs                ← Show units in selected province
    UnitRecruitmentPanel.cs         ← Recruit units UI

  Definitions/Units/
    infantry.json5                  ← Unit definitions
    cavalry.json5
    artillery.json5
```

---

## VALIDATION CRITERIA

### Functional Requirements
- ✅ Can create units via command (console or UI)
- ✅ Can disband units via command
- ✅ Units deduct resources (gold + manpower)
- ✅ Units visible in province (counter or list)
- ✅ Save/load preserves units
- ✅ Deterministic (same seed + commands = same units)

### Performance Requirements
- ✅ Create 10k units in <100ms
- ✅ Sparse storage: 10k units = ~80KB hot data
- ✅ GetUnitsInProvince() in <1ms for provinces with <100 units
- ✅ Save/load with 10k units in <500ms

### Architecture Requirements
- ✅ Engine layer has zero rendering code
- ✅ IUnitRenderer interface allows swapping visualization
- ✅ UnitDefinition.Visual exists but unused (future-proof)
- ✅ Save/load format stable (won't change when adding 3D)
- ✅ Command pattern used (multiplayer-ready)

---

## RISKS & MITIGATIONS

### Risk 1: Performance at Scale
**Issue:** 10k units might slow down province queries
**Mitigation:** Sparse collections tested in building system, proven to scale
**Validation:** Benchmark GetUnitsInProvince() with 10k units

### Risk 2: Save/Load Size
**Issue:** 10k units × 8 bytes = 80KB just for hot data
**Mitigation:** Acceptable size, similar to province data
**Validation:** Test save file size with 10k units

### Risk 3: 3D Visualization Refactoring
**Issue:** Adding 3D might require engine changes
**Mitigation:** IUnitRenderer interface prevents engine coupling
**Validation:** Verify UnitDefinition.Visual supports all needed data

### Risk 4: Unit ID Exhaustion
**Issue:** ushort only allows 65535 units
**Mitigation:** Sufficient for grand strategy (HOI4 has ~10k divisions)
**Validation:** If needed later, change to uint (4 bytes)

---

## SUCCESS METRICS

**Phase 1 Complete When:**
- ✅ Units created via console command work
- ✅ Units created via UI button work
- ✅ Province panel shows "X units in this province"
- ✅ Clicking province shows unit list
- ✅ Disband unit removes it
- ✅ Save/load preserves units
- ✅ 10k units perform well
- ✅ Tests pass for UnitSystem

**Future Phase Readiness:**
- ✅ UnitDefinition.Visual has all needed fields
- ✅ IUnitRenderer interface supports all operations
- ✅ No engine code knows about rendering

---

## NEXT STEPS AFTER THIS PLAN

1. **Review this plan** - Confirm approach before implementation
2. **Create UnitState struct** - Foundation for everything
3. **Implement UnitSystem** - NativeArray storage, sparse collections
4. **Implement Commands** - CreateUnit, DisbandUnit
5. **Add JSON5 Definitions** - Infantry, cavalry, artillery
6. **Simple Visualization** - Province counter renderer
7. **UI Integration** - Recruit button in province panel
8. **Save/Load** - UnitSystem serialization
9. **Validation** - 10k unit test, save/load test
10. **Session Log** - Document implementation

---

*Planning Document Created: 2025-10-19*
*Priority: ENGINE validation - Military Pillar Phase 1*
*Note: 3D visualization architecture included but deferred to future phase*
