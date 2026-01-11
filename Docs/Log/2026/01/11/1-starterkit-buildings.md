# StarterKit Buildings System
**Date**: 2026-01-11
**Session**: 1
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Implement building system for StarterKit with Farm building

**Success Criteria:**
- Farm costs 100 gold, provides +1 gold/month
- Building UI panel shows buildings and build button
- Economy system includes building bonuses in income
- Hide build button for unowned provinces

---

## Context & Background

**Previous Work:**
- See: [3-starterkit-military-units.md](../10/3-starterkit-military-units.md)

**Current State:**
- StarterKit had units, economy, province selection
- GAME layer had full building system but too complex for StarterKit
- Unit badges weren't scaling properly (badge size stuck regardless of setting)

---

## What We Did

### 1. Fixed Unit Badge Scaling
**Files Changed:** `Shaders/Instancing/InstancedAtlasBadge.shader`, `StarterKit/UnitVisualization.cs`

**Problem:** `badgeScale` parameter had no effect - badges stayed same size.

**Root Cause:** Shader did custom billboarding that bypassed matrix scale.

**Solution:** Added per-instance `_Scale` property to shader:
```hlsl
UNITY_DEFINE_INSTANCED_PROP(float, _Scale)
// ...
float scale = UNITY_ACCESS_INSTANCED_PROP(Props, _Scale);
float3 localPos = input.positionOS.xyz * scale;
```

C# passes scale via `propertyBlock.SetFloatArray("_Scale", scaleValues)`.

### 2. Created Building Definition
**Files Created:** `Template-Data/buildings/farm.json5`

```json5
{
  id: "farm",
  name: "Farm",
  cost: { gold: 100 },
  modifiers: { gold_output: 1 },
  max_per_province: 3
}
```

### 3. Created BuildingSystem
**Files Created:** `StarterKit/BuildingSystem.cs`

Simple building system with:
- `BuildingType` class - definition loaded from JSON5
- `BuildingSystem` class - manages construction and province buildings
- `CanConstruct()` / `Construct()` API
- `GetProvinceGoldBonus()` for economy integration
- Uses `EconomySystem` for gold deduction

### 4. Created BuildingInfoUI
**Files Created:** `StarterKit/BuildingInfoUI.cs`

Building info panel with:
- Shows total gold bonus from buildings
- Lists buildings per province with counts
- Build buttons for each building type (disabled if can't afford/max reached)
- Hidden for unowned provinces

### 5. Integrated Buildings into Economy
**Files Changed:** `StarterKit/EconomySystem.cs`

Added building bonus to income calculation:
```csharp
int baseIncome = provinceCount;
int buildingBonus = CalculateBuildingBonus(countryId);
int income = baseIncome + buildingBonus;
```

`SetBuildingSystem()` method to link systems (avoids circular dependency).

### 6. Renamed Classes (Removed Redundant Prefix)
**Files Renamed:**
- `StarterKitBuildingSystem.cs` → `BuildingSystem.cs`
- `StarterKitUnitSystem.cs` → `UnitSystem.cs`
- `StarterKitUnitVisualization.cs` → `UnitVisualization.cs`
- `StarterKitInitializer.cs` → `Initializer.cs`

**Classes Renamed:**
- `StarterKitBuildingSystem` → `BuildingSystem`
- `StarterKitBuildingType` → `BuildingType`
- `StarterKitUnitSystem` → `UnitSystem`
- `StarterKitUnitType` → `UnitType`
- `StarterKitUnitVisualization` → `UnitVisualization`
- `StarterKitInitializer` → `Initializer`

**Rationale:** Namespace `StarterKit` already provides context; prefix was redundant.

---

## Problems Encountered & Solutions

### Problem 1: Badge Scale Not Working
**Symptom:** Changing `badgeScale` in Inspector had no effect
**Root Cause:** Shader extracted position from matrix but ignored scale for billboarding
**Solution:** Added explicit `_Scale` per-instance property, pass via `SetFloatArray`

### Problem 2: Json5Cleaner Not Found
**Symptom:** Build error - `Json5Cleaner` doesn't exist
**Solution:** Use `Json5Loader.LoadJson5File()` instead (correct API)

### Problem 3: CountrySystem.Treasury Not Found
**Symptom:** Build error - can't access treasury via CountrySystem
**Root Cause:** StarterKit uses its own `EconomySystem`, not Core's CountrySystem for gold
**Solution:** Use `economySystem.Gold` and `economySystem.RemoveGold()` instead

### Problem 4: Income Not Updating After Building
**Symptom:** Built farm but income stayed at 10 instead of 11
**Root Cause:** EconomySystem didn't know about BuildingSystem
**Solution:** Added `SetBuildingSystem()` method, called from Initializer after both systems created

---

## Architecture Impact

### New Files in ENGINE
- `Template-Data/buildings/farm.json5` - Farm building definition
- `StarterKit/BuildingSystem.cs` - Building management
- `StarterKit/BuildingInfoUI.cs` - Building UI panel

### Shader Changes
- `InstancedAtlasBadge.shader` - Added `_Scale` per-instance property

### Renamed Files
- 4 files renamed to remove `StarterKit` prefix
- All references updated across 6+ files

---

## Quick Reference for Future Claude

**Key Files:**
- `StarterKit/BuildingSystem.cs` - Building management
- `StarterKit/BuildingInfoUI.cs` - Building UI panel
- `StarterKit/EconomySystem.cs` - Includes building bonuses
- `Template-Data/buildings/farm.json5` - Building definition

**Class Names (no StarterKit prefix):**
- `BuildingSystem`, `BuildingType`
- `UnitSystem`, `UnitType`
- `UnitVisualization`
- `Initializer`

**Gotchas:**
- EconomySystem needs `SetBuildingSystem()` called after both systems created
- Badge scale uses per-instance `_Scale` property, not matrix scale
- Use `Json5Loader.LoadJson5File()` for loading JSON5 files

**Scene Setup:**
| GameObject | Components |
|------------|------------|
| BuildingInfoUI | `UIDocument` + `BuildingInfoUI` |

---

## Files Changed Summary

**New Files:** 3 files
- `Template-Data/buildings/farm.json5`
- `StarterKit/BuildingSystem.cs`
- `StarterKit/BuildingInfoUI.cs`

**Renamed Files:** 4 files
- `StarterKitBuildingSystem.cs` → `BuildingSystem.cs`
- `StarterKitUnitSystem.cs` → `UnitSystem.cs`
- `StarterKitUnitVisualization.cs` → `UnitVisualization.cs`
- `StarterKitInitializer.cs` → `Initializer.cs`

**Modified Files:** 7 files
- `Shaders/Instancing/InstancedAtlasBadge.shader` - Per-instance scale
- `StarterKit/EconomySystem.cs` - Building bonus integration
- `StarterKit/Initializer.cs` - Building system init
- `StarterKit/UnitInfoUI.cs` - Class name updates
- `StarterKit/BuildingInfoUI.cs` - Class name updates
- `StarterKit/UnitVisualization.cs` - Scale property, class name

---

*Session focused on building system implementation and class naming cleanup*
