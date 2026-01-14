# Modifier Query API Enhancement
**Date**: 2026-01-14
**Session**: 7
**Status**: ✅ Complete
**Priority**: Medium

---

## Session Goal

**Primary Objective:**
- Add missing query methods to ModifierSystem for UI tooltips and debugging
- Enable source tracking and batch operations

**Success Criteria:**
- Query modifiers by source (what did Building #42 add?)
- Query modifiers by type (what affects production?)
- Iterate with inheritance (province + country + global)
- Batch removal for scope clearing

---

## Context & Background

**Previous Work:**
- See: [6-loader-factory-pattern.md](6-loader-factory-pattern.md)
- ModifierSystem: `Core/Modifiers/ModifierSystem.cs`

**Why Now:**
- UI tooltips need to show modifier breakdown by source
- No way to query "what modifiers come from this building?"
- ForEachLocalModifier only showed direct, not inherited modifiers

---

## What We Did

### 1. Added Query Methods to ActiveModifierList

**New Methods:**
- `ForEachBySource(sourceType, sourceId, action)` - Iterate by source
- `ForEachByModifierType(modifierTypeId, action)` - Iterate by modifier type
- `CountBySource(sourceType, sourceId)` - Count modifiers from source

### 2. Exposed Queries via ScopedModifierContainer

**New Methods:**
- `ForEachLocalModifierBySource()` - Filter local modifiers by source
- `ForEachLocalModifierByType()` - Filter local modifiers by type
- `CountLocalModifiersBySource()` - Count local modifiers by source

### 3. Added System-Level Query API

**New Region in ModifierSystem:**
```csharp
#region Query API
// Inherited iteration
ForEachProvinceModifierWithInheritance(provinceId, countryId, action)
ForEachCountryModifierWithInheritance(countryId, action)

// Filter by modifier type
ForEachProvinceModifierByType(provinceId, countryId, modifierTypeId, action)

// Filter by source
ForEachProvinceModifierBySource(provinceId, sourceType, sourceId, action)
ForEachCountryModifierBySource(countryId, sourceType, sourceId, action)
ForEachGlobalModifierBySource(sourceType, sourceId, action)

// Count queries
CountProvinceModifiersBySource(provinceId, sourceType, sourceId)
CountCountryModifiersBySource(countryId, sourceType, sourceId)
HasProvinceModifiersFromSource(provinceId, sourceType, sourceId)
HasCountryModifiersFromSource(countryId, sourceType, sourceId)
#endregion
```

### 4. Added Batch Removal

**New Region in ModifierSystem:**
```csharp
#region Batch Removal
ClearProvinceModifiers(provinceId)  // Clear all province modifiers
ClearCountryModifiers(countryId)    // Clear all country modifiers
ClearGlobalModifiers()              // Clear all global modifiers
#endregion
```

### 5. Added ModifierScopeLevel Enum

For inherited iteration, callbacks now receive scope level:
```csharp
public enum ModifierScopeLevel : byte
{
    Province = 0,
    Country = 1,
    Global = 2
}
```

---

## Architecture Impact

### API Pattern Established

Query methods follow consistent naming:
- `ForEach...WithInheritance()` - All modifiers including inherited
- `ForEach...BySource()` - Filter by source (local only)
- `ForEach...ByType()` - Filter by modifier type
- `Count...BySource()` - Count queries
- `Has...FromSource()` - Boolean check
- `Clear...Modifiers()` - Batch removal

---

## Files Changed

| File | Changes |
|------|---------|
| `Core/Modifiers/ActiveModifierList.cs` | +ForEachBySource, +ForEachByModifierType, +CountBySource |
| `Core/Modifiers/ScopedModifierContainer.cs` | +ForEachLocalModifierBySource, +ForEachLocalModifierByType, +CountLocalModifiersBySource |
| `Core/Modifiers/ModifierSystem.cs` | +Query API region, +Batch Removal region, +ModifierScopeLevel enum |

---

## Quick Reference for Future Claude

**Query modifiers from a specific building:**
```csharp
modifierSystem.ForEachProvinceModifierBySource(
    provinceId,
    ModifierSource.SourceType.Building,
    buildingId,
    mod => tooltip.AddLine($"{mod.ModifierTypeId}: {mod.Value}")
);
```

**Show all modifiers affecting production (with sources):**
```csharp
modifierSystem.ForEachProvinceModifierByType(
    provinceId, countryId, PRODUCTION_MODIFIER_ID,
    (mod, scope) => tooltip.AddLine($"[{scope}] {mod.Type}[{mod.SourceID}]: {mod.Value}")
);
```

**Clear province when conquered:**
```csharp
modifierSystem.ClearProvinceModifiers(provinceId);
```

---

## Session Statistics

**Files Modified:** 3
**New Methods:** 15+
**New Enum:** ModifierScopeLevel
**Pattern:** Query + Count + Has + Clear

---

## Links & References

### Related Sessions
- [Previous: Loader Factory Pattern](6-loader-factory-pattern.md)

### Code References
- ModifierSystem: `Core/Modifiers/ModifierSystem.cs:256-438`
- ActiveModifierList: `Core/Modifiers/ActiveModifierList.cs:183-228`

---

*Modifier Query API enables UI tooltips and debugging with source tracking, type filtering, inherited iteration, and batch removal.*
