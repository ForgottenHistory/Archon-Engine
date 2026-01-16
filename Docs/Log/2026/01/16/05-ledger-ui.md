# Ledger UI: Country Statistics Panel
**Date**: 2026-01-16
**Session**: 05
**Status**: ✅ Complete
**Priority**: Medium

---

## Session Goal

**Primary Objective:**
- Create a ledger panel showing data for all countries

**Secondary Objectives:**
- Track gold for all countries (not just player)
- Display provinces, units, gold, income per country
- Sortable columns

**Success Criteria:**
- Press L to toggle ledger
- Shows all countries with provinces
- Sortable by any column

---

## Context & Background

**Previous Work:**
- See: [04-unit-movement-system.md](04-unit-movement-system.md)
- EconomySystem only tracked player gold

**Current State:**
- ENGINE has rich query system (`CountryQueries`, `ProvinceQueries`)
- StarterKit had single-country economy tracking

**Why Now:**
- Showcase feature for StarterKit demo
- Need visibility into AI country status

---

## What We Did

### 1. Expanded EconomySystem for All Countries
**Files Changed:** `StarterKit/EconomySystem.cs`

Changed from single `int gold` to `Dictionary<ushort, int> countryGold`:
- `CollectIncomeForAllCountries()` - Monthly tick processes all countries with provinces
- `GetCountryGold(countryId)` - Query any country's gold
- `GetMonthlyIncome(countryId)` - Query any country's income
- `AddGoldToCountry()` / `RemoveGoldFromCountry()` - Per-country operations
- Backward compatible: `Gold`, `AddGold()`, `RemoveGold()` still work for player

### 2. Created LedgerUI Panel
**Files Changed:** `StarterKit/LedgerUI.cs` (new)

Features:
- Centered overlay panel (600x500 max)
- Columns: Country (with color indicator), Provinces, Units, Gold, Income
- Sortable by clicking column headers (▲/▼ indicators)
- Player row highlighted in green
- Toggle with `L` key or close button
- Scrollable table for many countries

**Key Implementation:**
```csharp
// Get all countries from Core
var countries = gameState.Countries.GetAllCountryIds();
foreach (ushort countryId in countries)
{
    int provinceCount = gameState.CountryQueries?.GetProvinceCount(countryId) ?? 0;
    if (provinceCount == 0) continue; // Skip countries without provinces
    // ... build row data
}
```

### 3. Added Ledger to Initializer
**Files Changed:** `StarterKit/Initializer.cs`

- Added `[SerializeField] private LedgerUI ledgerUI`
- Auto-discovery with `FindFirstObjectByType<LedgerUI>()`
- Initialized after country selection

---

## Decisions Made

### Decision 1: Track Gold for All Countries
**Context:** Original EconomySystem only tracked player gold

**Decision:** Expand to track all countries
**Rationale:**
- Ledger needs to show AI country data
- Enables future AI economic decisions
- Minimal memory overhead (Dictionary vs single int)

### Decision 2: Use Core APIs Directly vs Query Builder
**Context:** Query builder returns NativeList requiring Unity.Collections reference

**Decision:** Use `gameState.Countries.GetAllCountryIds()` directly
**Rationale:**
- Returns NativeArray which works without extra assembly reference
- Simpler code, same functionality
- Filter countries with provinces manually (one extra check)

---

## Problems Encountered & Solutions

### Problem 1: NativeList Assembly Reference
**Symptom:** `CS0012: The type 'NativeList<>' is defined in an assembly that is not referenced`

**Root Cause:** Query.Execute() returns NativeList, StarterKit doesn't reference Unity.Collections

**Solution:** Use `gameState.Countries.GetAllCountryIds()` which returns NativeArray instead of Query builder

### Problem 2: GetCountryUnits Not Found
**Symptom:** `CS1061: 'UnitSystem' does not contain 'GetCountryUnits'`

**Root Cause:** LedgerUI used StarterKit's UnitSystem, but `GetCountryUnits` is on Core's UnitSystem

**Solution:** Access Core's UnitSystem via `gameState.Units.GetCountryUnits()`

---

## Session Statistics

**Files Changed:** 3
- StarterKit/LedgerUI.cs (new, ~415 lines)
- StarterKit/EconomySystem.cs (expanded for all countries)
- StarterKit/Initializer.cs (added LedgerUI)

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- Ledger toggle: `L` key
- Gold for all countries: `economySystem.GetCountryGold(countryId)`
- Income for all countries: `economySystem.GetMonthlyIncome(countryId)`
- Unit count: `gameState.Units.GetCountryUnits(countryId).Count`
- Country queries: `gameState.CountryQueries.GetProvinceCount(countryId)`

**Gotchas for Next Session:**
- StarterKit UnitSystem wraps Core UnitSystem - some methods only on Core
- Use `gameState.Countries.GetAllCountryIds()` not Query builder to avoid NativeList issues
- Remember to dispose NativeArray from GetAllCountryIds()

---

## Links & References

### Code References
- LedgerUI: `StarterKit/LedgerUI.cs`
- Economy tracking: `StarterKit/EconomySystem.cs:75-97` (CollectIncomeForAllCountries)
- Country queries: `Core/Queries/CountryQueries.cs`

### Related Sessions
- Previous: [04-unit-movement-system.md](04-unit-movement-system.md)

---

*Session Duration: ~30 minutes*
