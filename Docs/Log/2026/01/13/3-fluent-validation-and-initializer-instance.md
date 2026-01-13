# Fluent Validation & Initializer.Instance
**Date**: 2026-01-13
**Session**: 3
**Status**: ✅ Complete
**Priority**: Medium

---

## Session Goal

**Primary Objective:**
- Create fluent validation infrastructure for command validation
- Fix StarterKit's slow `FindFirstObjectByType` pattern

**Success Criteria:**
- Chainable validation API: `Validate.For(gs).Country(id).Province(id).Result(out reason)`
- StarterKit commands use `Initializer.Instance` instead of FindFirstObjectByType

---

## Context & Background

**Previous Work:**
- See: [2-scoped-event-subscriptions.md](2-scoped-event-subscriptions.md)
- Identified validation boilerplate as friction point

**Current State:**
- Command validation is repetitive across commands
- StarterKit commands call `FindFirstObjectByType<Initializer>()` in both Validate and Execute

---

## What We Did

### 1. Created Fluent Validation Infrastructure

**Files Created in `Core/Validation/`:**

| File | Purpose |
|------|---------|
| `Validate.cs` | Static entry point: `Validate.For(gameState)` |
| `ValidationBuilder.cs` | Chainable builder with core validators |

**Core Validators Provided:**
- `Country(countryId)` - validates country exists
- `Province(provinceId)` - validates province ID in range
- `ProvinceOwnedBy(provinceId, countryId)` - validates ownership
- `ProvinceUnowned(provinceId)` - validates unowned
- `NotSameCountry(a, b)` - validates different countries
- `NotSameProvince(a, b)` - validates different provinces
- `ProvincesAdjacent(a, b)` - validates adjacency
- `Check(condition, reason)` - custom validation
- `Fail(reason)` - explicit failure

**Usage Pattern:**
```csharp
public override bool Validate(GameState gs) =>
    Validate.For(gs)
            .Country(countryId)
            .Province(provinceId)
            .ProvinceOwnedBy(provinceId, countryId)
            .Result(out var reason);
```

**Extensibility:**
GAME layer can add validators via extension methods:
```csharp
public static ValidationBuilder HasGold(this ValidationBuilder v, ushort countryId, int amount) { ... }
```

### 2. Added Initializer.Instance to StarterKit

**Problem:** `FindFirstObjectByType<Initializer>()` is slow and called repeatedly.

**Solution:** Static instance pattern (same as `GameState.Instance`).

**Changes to `Initializer.cs`:**
```csharp
public static Initializer Instance { get; private set; }

void Awake()
{
    Instance = this;
}

void OnDestroy()
{
    // ... dispose systems ...
    if (Instance == this)
        Instance = null;
}
```

### 3. Updated StarterKit Commands

All 5 commands updated to use `Initializer.Instance`:

| Command | Before | After |
|---------|--------|-------|
| AddGoldCommand | `Object.FindFirstObjectByType<Initializer>()` | `Initializer.Instance` |
| CreateUnitCommand | `Object.FindFirstObjectByType<Initializer>()` | `Initializer.Instance` |
| MoveUnitCommand | `Object.FindFirstObjectByType<Initializer>()` | `Initializer.Instance` |
| DisbandUnitCommand | `Object.FindFirstObjectByType<Initializer>()` | `Initializer.Instance` |
| ConstructBuildingCommand | `Object.FindFirstObjectByType<Initializer>()` | `Initializer.Instance` |

Also removed unused `using UnityEngine;` from all command files.

---

## Problems Encountered & Solutions

### Problem 1: Wrong API names in ValidationBuilder
**Symptom:** Compile errors for `ProvinceQueries.ProvinceCount` and `AreAdjacent`
**Solution:**
- `ProvinceCount` → `GetTotalProvinceCount()`
- `AreAdjacent` → `IsAdjacent`

---

## Quick Reference for Future Claude

**Key Files:**
- `Core/Validation/Validate.cs` - Static entry point
- `Core/Validation/ValidationBuilder.cs` - Builder with validators
- `StarterKit/Initializer.cs` - Now has static `Instance`

**Fluent Validation Usage:**
```csharp
Validate.For(gs)
        .Country(countryId)
        .Province(provinceId)
        .Result(out var reason);
```

**StarterKit System Access:**
```csharp
var economy = Initializer.Instance?.EconomySystem;
var units = Initializer.Instance?.UnitSystem;
var buildings = Initializer.Instance?.BuildingSystem;
```

---

## Links & References

### Related Sessions
- [Previous: Scoped Event Subscriptions](2-scoped-event-subscriptions.md)
- [Previous: SimpleCommand Infrastructure](1-simple-command-infrastructure.md)

---

*Fluent validation reduces repetitive validation code. Initializer.Instance eliminates slow FindFirstObjectByType calls.*
