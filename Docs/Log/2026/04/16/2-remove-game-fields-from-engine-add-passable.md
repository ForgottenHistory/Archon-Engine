# Remove Game-Specific Fields from Engine, Add Passable Property
**Date**: 2026-04-16
**Session**: 2
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Remove all game-specific fields (Culture, Religion, Development, Buildings, etc.) from engine province data structures

**Secondary Objectives:**
- Add per-province `IsPassable` property as an engine-level concept
- Make province history start date configurable instead of hardcoded 1444.11.11
- Add passable toggle to terrain editor UI

**Success Criteria:**
- Engine compiles with no game-specific province fields
- StarterKit loads cleanly with Template-Data
- Passable property loads from json5, queryable via ProvinceQueries, editable in terrain editor

---

## Context & Background

**Previous Work:**
- See: [1-province-terrain-editor-and-data-override-system.md](1-province-terrain-editor-and-data-override-system.md)
- During terrain editor development, discovered engine province data contained EU4/Hegemon-specific fields

**Current State:**
- Engine now clean of game-specific province data
- Game layer (Hegemon) stubbed to compile — needs its own json5 parsing (separate task)

**Why Now:**
- Engine-game separation principle violated — Culture, Religion, Development, BaseTax, etc. are game policy, not engine mechanism
- Passable property needed for province terrain cleanup (impassable provinces)
- Hardcoded 1444.11.11 start date was EU4-specific

---

## What We Did

### 1. Cleaned ProvinceInitialState
`Core/Data/ProvinceInitialState.cs`

**Removed:** Development, Culture, Religion, TradeGood, BaseTax, BaseProduction, BaseManpower, CenterOfTrade, Flags, CalculateDevelopment(), PackFlags(), Unity.Mathematics import

**Added:** `bool IsPassable` (default true in Create())

**Kept:** ProvinceID, IsValid, OwnerID, ControllerID, Terrain, TerrainOverride, OwnerTag, ControllerTag

### 2. Cleaned RawProvinceData
`Core/Data/Json5ProvinceData.cs`

**Added:** `bool passable` + `bool hasPassable` for json5 parsing. Default passable=true in Invalid sentinel.

### 3. Configurable Start Date
`Core/GameSettings.cs`

**Added:** `startYear` (default 0), `startMonth` (default 1), `startDay` (default 1) as serialized fields with public getters. StartYear=0 means no date filtering.

`Core/Loaders/Json5ProvinceConverter.cs:120` — replaced hardcoded `1444, 11, 11` with `GameSettings.Instance.StartYear/Month/Day`. When StartYear=0, skips date filtering entirely (uses raw json as-is).

### 4. Passable Parsing in Province Loader
`Core/Loaders/Json5ProvinceConverter.cs` — parses `passable` field from province json5
`Core/Jobs/ProvinceProcessingJob.cs` — passes `raw.passable` → `state.IsPassable`

### 5. Cleaned ProvinceData (Cold Registry)
`Core/Registries/ProvinceRegistry.cs`

**Removed:** Development, Flags, CultureId, ReligionId, TradeGoodId, BaseTax, BaseProduction, BaseManpower, CenterOfTrade, Buildings

**Added:** `bool IsPassable { get; set; } = true`

### 6. Cleaned Reference Resolution Pipeline
`Core/Linking/ReferenceResolver.cs:44-54` — removed copying of Development, Flags, BaseTax, BaseProduction, BaseManpower, CenterOfTrade. Added `provinceData.IsPassable = rawData.IsPassable`.

`Core/Initialization/Phases/ReferenceLinkingPhase.cs:106-119` — ProvinceData creation now only sets engine fields (RuntimeId, DefinitionId, Name, Terrain, IsPassable). Removed Development-based terrain logic.

### 7. Cleaned Data Validator
`Core/Linking/DataValidator.cs:182-197` — removed Development and Buildings validation. Kept ownership and terrain validation (engine concerns).

### 8. Cleaned Definition Loader
`Core/Loaders/DefinitionLoader.cs:157-168` — removed game fields from default ProvinceData. Now sets only DefinitionId, Name, Terrain, IsPassable.

### 9. ProvinceQueries.IsPassable()
`Core/Queries/ProvinceQueries.cs` — added `ProvinceRegistry` parameter (optional, for cold data queries). Added `IsPassable(ushort provinceId)` method reading from registry cold data.

`Core/GameState.cs:202` — passes `Registries?.Provinces` to ProvinceQueries constructor.

### 10. Terrain Editor Passable Toggle
`StarterKit/UI/ProvinceTerrainEditorUI.cs` — passable toggle button in province info section. Updates cold data live, tracks pending changes, saves to json5 via PatchPassableField.

`StarterKit/UI/ProvinceTerrainFilePatcher.cs` — added `PatchPassableField(string filePath, bool passable)` with regex patching.

### 11. Hegemon Stub
`Game/Loaders/HegemonScenarioLoader.cs` — stubbed to compile with default values. TODO comment for game layer to implement its own json5 parsing.

---

## Decisions Made

### Decision 1: Passable on ProvinceData (cold) not ProvinceState (hot)
**Context:** Where to store per-province passability
**Decision:** Cold data (`ProvinceData.IsPassable`) queried via `ProvinceQueries`
**Rationale:** Passability rarely changes at runtime. Pathfinding caches paths and recalculates on map changes, so it's checked at path build time, not every tick. No need to bloat the 8-byte hot struct.

### Decision 2: Start date on GameSettings, not TimeManager
**Context:** Where to configure the scenario start date for province history filtering
**Decision:** `GameSettings` ScriptableObject with StartYear/Month/Day
**Rationale:** Accessible everywhere via `GameSettings.Instance`, set per-project in inspector. TimeManager's start date is for simulation clock; GameSettings' is for data loading.

### Decision 3: Engine-only cleanup, Hegemon stubbed
**Context:** Hegemon game layer references removed engine fields
**Decision:** Stub Hegemon to compile, don't fix properly
**Rationale:** User plans to rewrite Hegemon scripts. Fixing them now would be wasted work.

---

## What Worked

1. **Clean separation** — all game fields removed without breaking engine or StarterKit
2. **Optional registry parameter** — `ProvinceQueries` constructor accepts `ProvinceRegistry` as optional param, backward compatible
3. **Default-true passable** — provinces are passable unless explicitly set false, no data migration needed

---

## Architecture Impact

### Fields Removed from Engine Province Data
| Field | Was In | Game Layer Should Handle |
|-------|--------|------------------------|
| Development | ProvinceInitialState, ProvinceData | HegemonProvinceData |
| BaseTax/BaseProduction/BaseManpower | Both | Game-specific economy |
| CenterOfTrade | Both | Game-specific trade |
| Culture/CultureId | ProvinceInitialState, ProvinceData | Game-specific culture system |
| Religion/ReligionId | ProvinceInitialState, ProvinceData | Game-specific religion system |
| TradeGood/TradeGoodId | ProvinceInitialState, ProvinceData | Game-specific trade |
| Buildings | ProvinceData | Game-specific building system |
| Flags (isCity, isHRE) | Both | Game-specific flags |

### Engine Province Data Now Contains Only
- **ProvinceState (hot, 8 bytes):** ownerID, controllerID, terrainType, gameDataSlot
- **ProvinceInitialState:** ProvinceID, IsValid, OwnerID, ControllerID, Terrain, IsPassable, TerrainOverride, OwnerTag, ControllerTag
- **ProvinceData (cold):** RuntimeId, DefinitionId, Name, OwnerId, ControllerId, Terrain, IsPassable, IsCoastal, NeighborProvinces

---

## Next Session

### Immediate Next Steps
1. Hegemon game layer: implement own json5 parsing for Culture, Religion, Development, etc.
2. Use passable property for the 334 unclassified impassable provinces in Hegemon data
3. Wire remaining loaders to DataFileResolver (MapConfigLoader, country loaders)

---

## Session Statistics

**Files Changed:** 13
**Fields Removed:** 10 game-specific fields across 6 engine files
**Fields Added:** IsPassable (3 structs + query + UI toggle)

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- Engine province data is now game-field-free. Game layer must parse its own fields from json5.
- `ProvinceQueries.IsPassable()` reads from `ProvinceRegistry` cold data (optional param)
- `GameSettings.StartYear = 0` means no date filtering in province loader
- Hegemon's `HegemonScenarioLoader` is stubbed — all provinces get default values until rewritten

**Gotchas for Next Session:**
- Hegemon gameplay is non-functional (all development/culture/religion = 0) until game layer parsing is implemented
- `ProvinceQueries` constructor changed — any code creating it directly needs the new optional param
- `GameSettings.StartYear` defaults to 0 — Hegemon needs to set it to 450 (or whatever its start date is) in the inspector

---

*Session logged: 2026-04-16*
