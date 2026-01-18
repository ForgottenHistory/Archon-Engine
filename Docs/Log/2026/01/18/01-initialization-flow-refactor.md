# Initialization Flow Refactor
**Date**: 2026-01-18
**Session**: 1
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Fix timing issues with map component initialization (terrain showing as ocean T0)
- Consolidate initialization ownership to ArchonEngine

**Secondary Objectives:**
- Remove Awake() usage from map components
- Simplify GameSettings data directories
- Update StarterKit to use ArchonEngine.Instance API

**Success Criteria:**
- Terrain loads correctly without ocean (T0) on land
- Single initialization owner (ArchonEngine)
- No Awake() timing dependencies

---

## Context & Background

**Previous Work:**
- See: [07-fluent-validation-query-builders.md](../17/07-fluent-validation-query-builders.md)

**Current State:**
- Map components used Awake() for initialization, causing race conditions
- GameSettings had 4 separate data directories (confusing)
- MapSystemCoordinator was both initializing components AND coordinating - double initialization
- StarterKit used FindFirstObjectByType<MapSystemCoordinator> instead of ArchonEngine API

**Why Now:**
- Random provinces showing as ocean (T0) indicated timing issues
- Jungle color was white (254,254,254) conflicting with snow

---

## What We Did

### 1. Fixed Jungle Terrain Color
**Files Changed:**
- `Template-Data/map/terrain.json5:69`
- `Template-Data/utils/generate_terrain.py:86`
- `Template-Data/map/terrain.png` (regenerated)

Changed jungle color from `[254,254,254]` (white, conflicting with snow) to `[0,100,0]` (dark green).

Ran `generate_terrain.py` to regenerate terrain.png with correct colors.

### 2. Removed Awake() from Map Components
**Files Changed:**
- `Scripts/Map/MapTextureManager.cs`
- `Scripts/Map/Rendering/OwnerTextureDispatcher.cs`
- `Scripts/Map/Rendering/ProvinceTerrainAnalyzer.cs`
- `Scripts/Map/Rendering/Terrain/TerrainBlendMapGenerator.cs`
- `Scripts/Map/Rendering/Border/BorderDistanceFieldGenerator.cs`

**Pattern:** Each component now has:
```csharp
private bool isInitialized = false;

public void Initialize(/* params if needed */)
{
    if (isInitialized) return;
    isInitialized = true;
    // ... initialization logic
}
```

**Rationale:**
- Awake() runs in undefined order, causing race conditions
- Explicit Initialize() called by ArchonEngine ensures correct order
- Guard flag prevents double initialization

### 3. Simplified GameSettings
**Files Changed:** `Scripts/Core/GameSettings.cs`

Consolidated from 4 directories to 1:
```csharp
// Before (confusing):
public string DataDirectory;
public string TemplateDataDirectory;
public string MapDirectory;
public string DebugOutputDirectory;

// After (simple):
public string DataDirectory = "Assets/Archon-Engine/Template-Data";
```

### 4. Refactored MapSystemCoordinator
**Files Changed:** `Scripts/Map/Core/MapSystemCoordinator.cs`

**Before:** MapSystemCoordinator initialized components AND coordinated runtime
**After:** MapSystemCoordinator only coordinates - receives already-initialized components

```csharp
// New Configure() method receives initialized components
public void Configure(
    Camera camera,
    MeshRenderer mesh,
    GameSettings settings,
    MapTextureManager textures,
    BorderComputeDispatcher borders,
    OwnerTextureDispatcher ownerDispatcher,
    ProvinceTerrainAnalyzer terrain,
    TerrainBlendMapGenerator blendGen)
```

Removed `InitializeComponents()` method entirely.

### 5. Updated ArchonEngine.InitializeMap()
**Files Changed:** `Scripts/Engine/ArchonEngine.cs:396-524`

Clear 4-phase structure:
1. **Phase 1:** Get and initialize ALL map components (single owner)
2. **Phase 2:** Configure MapSystemCoordinator with initialized components
3. **Phase 3:** Configure visual style
4. **Phase 4:** Start map loading (async)

### 6. Added Public API to ArchonEngine
**Files Changed:** `Scripts/Engine/ArchonEngine.cs:131-159`

New properties exposed:
- `GameSettings` - configuration asset
- `MapMeshRenderer` - mesh renderer for map plane
- `BorderDispatcher` - border compute dispatcher
- `MapCamera` - map camera

### 7. Updated StarterKit to Use ArchonEngine.Instance
**Files Changed:**
- `Scripts/StarterKit/Visualization/UnitVisualization.cs:71-94`
- `Scripts/StarterKit/UI/ProvinceInfoUI.cs:79-82`
- `Scripts/StarterKit/Systems/AISystem.cs:49-52`

**Before:**
```csharp
var coordinator = FindFirstObjectByType<MapSystemCoordinator>();
string dataDirectory = coordinator?.DataDirectory;
```

**After:**
```csharp
var engine = Engine.ArchonEngine.Instance;
string dataDirectory = engine?.GameSettings?.DataDirectory;
```

---

## Decisions Made

### Decision 1: Keep MapSystemCoordinator as Internal Implementation
**Context:** Considered removing MapSystemCoordinator entirely and moving logic to ArchonEngine
**Options Considered:**
1. Remove MapSystemCoordinator, put everything in ArchonEngine
2. Keep MapSystemCoordinator as internal coordinator

**Decision:** Keep MapSystemCoordinator
**Rationale:**
- Moving all logic to ArchonEngine would bloat it
- MapSystemCoordinator handles map data loading (substantial logic)
- Now that initialization is fixed, it serves a clear purpose
**Trade-offs:** Extra indirection, but cleaner separation

### Decision 2: Map Layer Can Use MapSystemCoordinator
**Context:** Bitmap loaders in Map.Loading.Images needed mesh renderer reference
**Options Considered:**
1. Import Engine namespace into Map layer (wrong!)
2. Use MapSystemCoordinator (same Map layer)
3. Pass material as parameter

**Decision:** Use MapSystemCoordinator
**Rationale:**
- Map.Core.MapSystemCoordinator is IN the Map layer
- Map → Map is allowed (same layer)
- Map → Engine is NOT allowed (violates architecture)

---

## What Worked ✅

1. **Explicit Initialize() Pattern**
   - What: Guard flag + explicit method instead of Awake()
   - Why it worked: Deterministic initialization order
   - Reusable pattern: Yes - use for all ENGINE components

2. **Single Owner of Initialization**
   - What: ArchonEngine owns all component initialization
   - Impact: No more timing issues, clear dependency flow

---

## What Didn't Work ❌

1. **Importing Engine into Map Layer**
   - What we tried: `using Engine;` in Map/Loading/Images/*.cs
   - Why it failed: Violates architecture (Map cannot import Engine)
   - Lesson learned: Check layer boundaries before importing
   - Don't try this again because: Architecture violation

---

## Problems Encountered & Solutions

### Problem 1: Terrain Showing as Ocean (T0)
**Symptom:** Random provinces on land showing as ocean terrain type
**Root Cause:** Multiple issues:
1. Jungle color `[254,254,254]` almost identical to snow `[255,255,255]`
2. Awake() running before GameSettings registered
3. terrain.bmp had old colors, terrain.json5 had new colors

**Solution:**
1. Changed jungle to `[0,100,0]` (dark green)
2. Removed Awake(), use explicit Initialize()
3. Regenerated terrain.png from terrain.json5

### Problem 2: Double Initialization
**Symptom:** Components initialized twice - by ArchonEngine AND MapSystemCoordinator
**Root Cause:** Both classes were calling Initialize() on same components

**Solution:**
- ArchonEngine: Single owner of initialization
- MapSystemCoordinator: Receives already-initialized components via Configure()
- Removed InitializeComponents() from MapSystemCoordinator

---

## Architecture Impact

### New Patterns Discovered
**Pattern:** Explicit Initialize() with Guard
```csharp
private bool isInitialized = false;
public void Initialize() {
    if (isInitialized) return;
    isInitialized = true;
    // ...
}
```
- When to use: Any MonoBehaviour that needs controlled initialization
- Benefits: Deterministic order, prevents double init
- Add to: CLAUDE.md initialization section

### Anti-Pattern Confirmed
**Anti-Pattern:** Using Awake() for initialization that depends on other systems
- What not to do: Initialize in Awake() when you need GameSettings or other components
- Why it's bad: Undefined execution order causes race conditions
- Already documented in CLAUDE.md

---

## Next Session

### Immediate Next Steps
1. Test StarterKit thoroughly - verify all functionality works
2. Consider removing unused EngineMapInitializer.cs (predates ArchonEngine)

### Questions to Resolve
1. Should EngineMapInitializer be removed? It's unused but still exists

---

## Session Statistics

**Files Changed:** ~15 in ENGINE
**Key Changes:**
- Removed Awake() from 5 components
- Added 4 properties to ArchonEngine public API
- Updated 3 StarterKit files to use ArchonEngine.Instance
- Fixed terrain colors

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- ArchonEngine is SINGLE OWNER of component initialization
- Map components use Initialize() not Awake()
- StarterKit uses `Engine.ArchonEngine.Instance` for API access
- Map layer CAN use MapSystemCoordinator (same layer)
- Map layer CANNOT import Engine namespace

**Gotchas for Next Session:**
- Don't add Awake() to map components
- Don't import Engine into Map layer
- MapSystemCoordinator is internal - external code uses ArchonEngine.Instance

---

## Links & References

### Related Sessions
- [Previous Session](../17/07-fluent-validation-query-builders.md)

### Code References
- Initialization flow: `ArchonEngine.cs:396-524`
- MapSystemCoordinator.Configure(): `MapSystemCoordinator.cs:100-127`
- ArchonEngine public API: `ArchonEngine.cs:93-161`
