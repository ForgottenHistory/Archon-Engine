# Engine-Game Data Separation Refactor
**Date**: 2026-01-17
**Session**: 3
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Refactor `CountryData.cs` and `ProvinceColdData.cs` to remove game-specific fields, making ENGINE layer truly generic

**Secondary Objectives:**
- Demonstrate Hot/Cold Data Separation pattern in StarterKit via province ownership history

**Success Criteria:**
- CountryHotData/ColdData contain only generic fields
- ProvinceColdData contains only generic fields
- Game-specific data uses `customData` dictionary extension point
- StarterKit demonstrates hot/cold separation with ProvinceHistorySystem

---

## Context & Background

**Previous Work:**
- See: [02-modifier-system-integration.md](02-modifier-system-integration.md)
- Related: Pattern 1 (Engine-Game Separation) and Pattern 4 (Hot/Cold Data Separation) in CLAUDE.md

**Current State:**
- CountryData had Paradox-specific fields: `historicalIdeaGroups`, `monarchNames`, `revolutionaryColors`, `preferredReligion`
- ProvinceColdData had game-specific: `BuildingType` enum, `Buildings` list, `CalculateTradeValue()`, `CalculateSupplyLimit()`
- Violated Pattern 1: Engine provides mechanisms, Game defines policy

**Why Now:**
- Engine layer should be reusable for any grand strategy game
- Game-specific data structures prevent engine reuse

---

## What We Did

### 1. StarterKit Hot/Cold Data Separation Demo
**Files Created:**
- `StarterKit/Data/ProvinceHistory.cs` - OwnershipRecord struct + ProvinceHistoryData class
- `StarterKit/Systems/ProvinceHistorySystem.cs` - Tracks ownership changes via EventBus

**Files Changed:**
- `StarterKit/UI/ProvinceInfoUI.cs` - Added history section UI, UpdateHistorySection()
- `StarterKit/Initializer.cs` - Creates and wires ProvinceHistorySystem
- `Template-Data/localisation/english/ui_l_english.yml` - Added UI_HISTORY key

**Implementation:**
- Hot data: `ProvinceState.ownerID` (2 bytes, accessed every frame)
- Cold data: `ProvinceHistoryData` (ownership history, loaded on-demand when clicking province)
- Uses CircularBuffer (max 10 entries) to prevent unbounded memory growth

### 2. CountryData Generic Refactor
**Files Changed:** `Core/Data/CountryData.cs`

**CountryHotData (8 bytes) - Before:**
```csharp
public const byte FLAG_HAS_HISTORICAL_IDEAS = 1 << 0;
public const byte FLAG_HAS_HISTORICAL_UNITS = 1 << 1;
public const byte FLAG_HAS_MONARCH_NAMES = 1 << 2;
// ... game-specific flag constants
```

**CountryHotData (8 bytes) - After:**
```csharp
public ushort tagHash;           // 2 bytes
public uint colorRGBA;           // 4 bytes
public byte graphicalCultureId;  // 1 byte
public byte flags;               // 1 byte - generic, game defines meaning

public bool GetFlag(int index) => (flags & (1 << index)) != 0;
public void SetFlag(int index, bool value) { ... }
```

**CountryColdData - Before:**
```csharp
public Color32 revolutionaryColors;
public string preferredReligion;
public List<string> historicalIdeaGroups;
public List<string> historicalUnits;
public Dictionary<string, int> monarchNames;
```

**CountryColdData - After:**
```csharp
public string tag;
public string displayName;
public string graphicalCulture;
public Color32 color;
public Dictionary<string, object> customData;  // Game extension point

public T GetCustomData<T>(string key, T defaultValue = default);
public void SetCustomData(string key, object value);
```

### 3. ProvinceColdData Generic Refactor
**Files Changed:** `Core/Data/ProvinceColdData.cs`

**Removed:**
- `BuildingType` enum (Farm, Market, Fort, Temple, Workshop, Granary, Road, Port)
- `List<BuildingType> Buildings`
- `FixedPoint64 CachedTradeValue`, `CachedSupplyLimit`
- `CalculateTradeValue()`, `CalculateSupplyLimit()` methods
- `AddBuilding()`, `RemoveBuilding()`, `HasBuilding()` methods

**Kept:**
- Presentation: `ProvinceID`, `Name`, `IdentifierColor`, `CenterPoint`, `PixelCount`, `Bounds`
- History: `CircularBuffer<HistoricalEvent> RecentHistory`
- Modifiers: `Dictionary<string, FixedPoint64> Modifiers`
- Cache: `int CacheFrame`

**Added:**
```csharp
public Dictionary<string, object> CustomData { get; private set; }

public T GetCustomData<T>(string key, T defaultValue = default);
public void SetCustomData(string key, object value);
public bool RemoveCustomData(string key);
public bool HasCustomData(string key);
```

### 4. Updated Dependent Code
**Files Changed:**
- `Core/Systems/Country/CountryDataManager.cs` - `colorRGB` → `colorRGBA`
- `Core/Systems/CountrySystem.cs` - Serialization uses customData instead of specific fields
- `Core/Loaders/BurstCountryLoader.cs` - Stores game-specific data in customData
- `Core/Jobs/CountryProcessingJob.cs` - Removed game-specific flag setting (flags = 0)

---

## Decisions Made

### Decision 1: customData as Dictionary<string, object>
**Context:** Need extension point for game-specific data without engine knowing types
**Options:**
1. Strongly-typed game-specific classes - Engine must know about them
2. `Dictionary<string, object>` - Flexible but loses type safety
3. Generic interface system - Complex, over-engineered

**Decision:** `Dictionary<string, object>` with typed helper methods
**Rationale:** Simple, flexible, helper methods provide type safety at call sites
**Trade-offs:** Runtime type checking vs compile-time; acceptable for cold data

### Decision 2: Engine Sets flags = 0
**Context:** CountryProcessingJob was setting game-specific flags
**Options:**
1. Engine sets flags based on raw data properties
2. Engine sets flags = 0, game layer sets meaningful flags

**Decision:** Engine sets flags = 0
**Rationale:** Flags are policy (what they mean), not mechanism. Game layer decides.

---

## What Worked ✅

1. **customData Extension Pattern**
   - What: `Dictionary<string, object>` with `GetCustomData<T>()` / `SetCustomData()`
   - Why it worked: Clean separation, game can store anything without engine changes
   - Reusable pattern: Yes - apply to any engine data structure needing game extensions

2. **Hot/Cold Demo in StarterKit**
   - What: ProvinceHistorySystem demonstrates on-demand cold data access
   - Why it worked: Clear contrast between hot (ownerID every frame) and cold (history on click)

---

## Architecture Impact

### Patterns Reinforced
- **Pattern 1 (Engine-Game Separation):** Engine provides customData mechanism, game defines what to store
- **Pattern 4 (Hot/Cold Data):** StarterKit now explicitly demonstrates this with ProvinceHistorySystem

### Documentation Updates Required
- [ ] Update FILE_REGISTRY.md for new StarterKit files

---

## Code Quality Notes

### Technical Debt
- **Paid Down:** Removed all Paradox-specific data from Engine layer
- **Created:** None

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- CountryHotData flags are generic - game defines meaning via indices 0-7
- CountryColdData.customData stores game-specific data
- ProvinceColdData.CustomData stores game-specific data
- ProvinceHistorySystem in StarterKit demonstrates hot/cold pattern

**Gotchas for Next Session:**
- If loading Paradox data, game layer must populate customData with parsed fields
- Serialization only persists string values in customData - complex data rebuilt on load

---

## Links & References

### Code References
- CountryData customData: `Core/Data/CountryData.cs:79-107`
- ProvinceColdData customData: `Core/Data/ProvinceColdData.cs:95-134`
- ProvinceHistorySystem: `StarterKit/Systems/ProvinceHistorySystem.cs`
- ProvinceHistory data: `StarterKit/Data/ProvinceHistory.cs`

### Related Sessions
- [02-modifier-system-integration.md](02-modifier-system-integration.md) - Previous session

---

*Template Version: 1.0 - Created 2025-09-30*
