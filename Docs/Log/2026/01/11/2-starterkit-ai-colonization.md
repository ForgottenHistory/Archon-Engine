# StarterKit AI Gold & Province Colonization
**Date**: 2026-01-11
**Session**: 2
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Fix AI gold tracking to properly accumulate and spend gold
- Add province colonization feature ("Buy Land" for 20 gold)

**Success Criteria:**
- AI countries earn gold monthly and only build farms when they can afford them
- Player can click unowned provinces and buy them for 20 gold
- All provinces (including unowned) are registered at startup

---

## Context & Background

**Previous Work:**
- See: [1-starterkit-buildings.md](1-starterkit-buildings.md)

**Current State:**
- AI was building farms every month without paying gold
- Province colonization didn't work because unowned provinces weren't loaded

---

## What We Did

### 1. Fixed AI Gold Tracking
**Files Changed:** `StarterKit/AISystem.cs`

Added proper gold accumulation and spending:
- Pre-allocated `Dictionary<ushort, int> aiGold` at init for zero gameplay allocations
- AI earns income each month (1 gold per province + building bonuses)
- AI only builds farms when `aiGold[countryId] >= farmType.Cost`
- Gold deducted after successful construction

```csharp
// Pre-allocate at init
var countrySystem = gameState.GetComponent<CountrySystem>();
for (ushort i = 1; i <= countrySystem.CountryCount; i++)
{
    aiGold[i] = 0;
}

// Monthly processing
int income = CalculateIncome(countryId, provinceSystem);
aiGold[countryId] += income;

if (farmType != null && aiGold[countryId] >= farmType.Cost)
{
    if (TryBuildFarm(countryId, provinceSystem, farmType.Cost))
    {
        aiGold[countryId] -= farmType.Cost;
    }
}
```

### 2. Added Colonization UI
**Files Changed:** `StarterKit/ProvinceInfoUI.cs`, `StarterKit/Initializer.cs`

- Added "Buy Land" button (20 gold) that shows for unowned provinces
- Button disabled when player can't afford it
- Integrated `EconomySystem` and `PlayerState` references

### 3. Fixed Province Loading (Critical Fix)
**Files Changed:** `Core/Initialization/Phases/ProvinceDataLoadingPhase.cs`, `Core/Systems/ProvinceSystem.cs`

**Problem:** Only 60 provinces loaded (ones with JSON5 files). Clicking province 2155 (valid land) failed because it wasn't registered.

**Root Cause:** `InitializeFromProvinceStates` only loaded provinces with JSON5 history files. Unowned provinces have no JSON5 files to avoid loading unnecessary data.

**Solution:** Load ALL provinces from definition.csv first, then apply ownership from JSON5:

```csharp
// ProvinceDataLoadingPhase.cs
// ALWAYS initialize from definition.csv (4425 provinces)
context.ProvinceSystem.InitializeFromDefinitions(context.ProvinceDefinitions);

// Then apply ownership/terrain from JSON5 (60 provinces with history)
context.ProvinceSystem.ApplyInitialStates(context.ProvinceInitialStates);
```

Added new method to ProvinceSystem:
```csharp
public void ApplyInitialStates(ProvinceInitialStateLoadResult loadResult)
{
    for (int i = 0; i < loadResult.InitialStates.Length; i++)
    {
        var initialState = loadResult.InitialStates[i];
        if (!initialState.IsValid) continue;

        ushort provinceId = (ushort)initialState.ProvinceID;
        if (!dataManager.HasProvince(provinceId)) continue;

        if (initialState.Terrain != 0)
            dataManager.SetProvinceTerrain(provinceId, initialState.Terrain);
    }
}
```

---

## Problems Encountered & Solutions

### Problem 1: AI Building Unlimited Farms
**Symptom:** AI countries built farms every month, many farms appeared across map
**Root Cause:** `ConstructForAI` bypassed gold check entirely
**Solution:** Added gold tracking per AI country, accumulate income, check before building

### Problem 2: Province 2155 Invalid
**Symptom:** `Cannot set owner for invalid province 2155` when clicking land province
**Root Cause:** ProvinceSystem only loaded 60 provinces (from JSON5 files), but map has 4425 provinces in definition.csv

**Investigation:**
- Checked definition.csv: Has 4425 provinces including 2155
- Checked ProvinceDataLoadingPhase: Was calling `InitializeFromProvinceStates` which only loads JSON5 provinces
- Registry had all provinces, but ProvinceSystem (simulation) only had 60

**Solution:** Changed loading order to initialize ALL provinces from definition.csv first, then apply states from JSON5

### Problem 3: Dictionary Allocations During Gameplay
**Symptom:** User concern about Dictionary allocations in AI system
**Solution:** Pre-allocate all country entries at init:
```csharp
for (ushort i = 1; i <= countrySystem.CountryCount; i++)
{
    aiGold[i] = 0;
}
```

---

## Architecture Impact

### Key Insight: Province Loading Architecture
- **definition.csv**: Source of truth for ALL provinces (4425 entries)
- **JSON5 history files**: Only for provinces with owners/history (60 files)
- **ProvinceSystem**: Must load from definition.csv, then apply JSON5 states
- **Registry**: Gets both sources merged in ReferenceLinkingPhase

### New Methods Added
- `ProvinceSystem.ApplyInitialStates()` - Apply JSON5 data to already-registered provinces
- `ProvinceSystem.AddProvince()` - Public method for runtime province addition

---

## Quick Reference for Future Claude

**Key Files:**
- `Core/Initialization/Phases/ProvinceDataLoadingPhase.cs` - Province loading order
- `Core/Systems/ProvinceSystem.cs:221-276` - ApplyInitialStates and AddProvince
- `StarterKit/AISystem.cs` - AI gold tracking
- `StarterKit/ProvinceInfoUI.cs` - Colonization UI

**Critical Pattern:**
Province loading MUST be:
1. Initialize ALL provinces from definition.csv
2. Apply states (ownership/terrain) from JSON5 files

**Gotchas:**
- `ProvinceQueries.Exists()` checks if province is in simulation, not just registry
- Unowned provinces have no JSON5 files by design
- AI gold Dictionary pre-allocated to avoid gameplay allocations
- definition.csv has ~4400 provinces, JSON5 files only exist for ~60 owned provinces

---

## Files Changed Summary

**Modified Files:** 5 files
- `Core/Initialization/Phases/ProvinceDataLoadingPhase.cs` - Load order fix
- `Core/Systems/ProvinceSystem.cs` - Added ApplyInitialStates, AddProvince
- `StarterKit/AISystem.cs` - Gold tracking with pre-allocation
- `StarterKit/ProvinceInfoUI.cs` - Colonization UI
- `StarterKit/Initializer.cs` - Pass economy/player refs to ProvinceInfoUI

---

*Session focused on AI economy fix and province colonization with critical province loading fix*
