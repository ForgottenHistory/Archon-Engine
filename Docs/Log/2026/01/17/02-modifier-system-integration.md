# Modifier System Integration & EventBus Cleanup
**Date**: 2026-01-17
**Session**: 2
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Integrate ModifierSystem into StarterKit for building bonuses (farm provides +25% local income, +1% country-wide)

**Secondary Objectives:**
- Convert all C# events to EventBus (Pattern 3 compliance)
- Add income caching to fix monthly tick performance

**Success Criteria:**
- Farm building provides flat gold + percentage bonuses via ModifierSystem
- No C# events in StarterKit (all use EventBus)
- Monthly tick runs smoothly with income caching

---

## Context & Background

**Previous Work:**
- See: [01-gpu-gradient-map-modes.md](01-gpu-gradient-map-modes.md)
- Related: Pattern 3 (Event-Driven Architecture) in CLAUDE.md

**Current State:**
- BuildingSystem had simple GoldOutput property
- EconomySystem calculated income without modifiers
- Multiple C# events existed alongside EventBus usage

**Why Now:**
- StarterKit needs to demonstrate ModifierSystem pattern
- C# events are tech debt that violates Pattern 3

---

## What We Did

### 1. ModifierType Enum for StarterKit
**Files Changed:** `StarterKit/Data/ModifierType.cs` (new file)

**Implementation:**
- Created enum with LocalIncomeAdditive, LocalIncomeModifier, CountryIncomeModifier
- Helper methods: FromKey(), ToKey(), IsMultiplicative(), IsCountryWide()
- IDs 1-99 are province-local, 100+ are country-wide

### 2. BuildingSystem Modifier Integration
**Files Changed:** `StarterKit/Systems/BuildingSystem.cs`

**Implementation:**
- Added `BuildingModifier` struct with Type, Value, IsMultiplicative, IsCountryWide
- Updated `BuildingType` to store `List<BuildingModifier>` instead of GoldOutput
- `LoadBuildingTypeFromFile()` parses modifiers from JSON5
- `ApplyBuildingModifiers()` adds to ModifierSystem on construction
- Emits `BuildingConstructedEvent` via EventBus (removed C# event)

### 3. EconomySystem with Modifiers and Caching
**Files Changed:** `StarterKit/Systems/EconomySystem.cs`

**Implementation:**
Income formula per province:
```
(baseIncome + additiveBonus) * (1 + localModifier) * (1 + countryModifier)
```

Added income caching (Pattern 11: Dirty Flag System):
```csharp
private readonly FixedPoint64[] cachedCountryIncome = new FixedPoint64[MAX_COUNTRIES];
private readonly bool[] incomeNeedsRecalculation = new bool[MAX_COUNTRIES];

private FixedPoint64 GetCachedIncome(ushort countryId)
{
    if (incomeNeedsRecalculation[countryId])
    {
        cachedCountryIncome[countryId] = CalculateCountryIncome(countryId);
        incomeNeedsRecalculation[countryId] = false;
    }
    return cachedCountryIncome[countryId];
}
```

Cache invalidation via EventBus:
- Subscribes to `BuildingConstructedEvent` → invalidate owner
- Subscribes to `ProvinceOwnershipChangedEvent` → invalidate old/new owner

### 4. AISystem Uses Shared EconomySystem
**Files Changed:** `StarterKit/Systems/AISystem.cs`

**Implementation:**
- Removed `aiGold` dictionary (AI was tracking gold separately)
- Now uses `economySystem.GetCountryGoldInt()` and `economySystem.RemoveGoldFromCountry()`
- AI benefits from same modifiers as player (no desync)

### 5. EventBus Migration (Pattern 3 Compliance)
**Files Changed:**
- `StarterKit/Systems/BuildingSystem.cs` - Removed `OnBuildingConstructed` C# event
- `StarterKit/Systems/UnitSystem.cs` - Removed `OnUnitCreated/Destroyed/Moved` C# events
- `StarterKit/UI/BuildingInfoUI.cs` - Subscribe via EventBus
- `StarterKit/UI/UnitInfoUI.cs` - Subscribe to Core.Units events via EventBus
- `StarterKit/Initializer.cs` - Subscribe via EventBus for map mode dirty flag

### 6. Updated farm.json5
**Files Changed:** `Template-Data/common/buildings/farm.json5`

```json5
modifiers: {
  local_income_additive: 1,      // +1 flat gold
  local_income_modifier: 0.25,   // +25% local income
  country_income_modifier: 0.01  // +1% country-wide
}
```

---

## Decisions Made

### Decision 1: Income Caching vs Recalculate Every Tick
**Context:** Monthly tick was slow due to modifier lookups for every province
**Options:**
1. Recalculate every tick (simple, slow)
2. Cache with dirty flags (fast, more code)

**Decision:** Cache with dirty flags (Pattern 11)
**Rationale:** Event-driven invalidation is precise and efficient
**Trade-offs:** Must remember to invalidate on all relevant changes

### Decision 2: AI Uses Shared EconomySystem
**Context:** AI had separate gold tracking that could desync
**Options:**
1. Keep separate tracking (simpler, risk of desync)
2. Use shared EconomySystem (consistent, AI benefits from modifiers)

**Decision:** Shared EconomySystem
**Rationale:** Single source of truth, AI gets same modifiers as player

### Decision 3: Remove All C# Events
**Context:** StarterKit had mix of C# events and EventBus
**Options:**
1. Keep C# events for internal use
2. Convert everything to EventBus

**Decision:** Convert everything to EventBus
**Rationale:** Pattern 3 compliance, zero tech debt, consistent API

---

## What Worked ✅

1. **Event-Driven Cache Invalidation**
   - What: Subscribe to BuildingConstructedEvent, invalidate owner's income
   - Why it worked: Precise invalidation, no redundant recalculation
   - Reusable pattern: Yes - any cached value can use this

2. **Core.Units Events Already Exist**
   - What: UnitCreatedEvent, UnitDestroyedEvent, UnitMovedEvent in Core
   - Why it worked: StarterKit UnitSystem was redundantly re-emitting
   - Lesson: Check Core for existing events before creating new ones

---

## Problems Encountered & Solutions

### Problem 1: Monthly Tick Lag After Modifier Integration
**Symptom:** Game chugged on every monthly tick
**Root Cause:** ModifierSystem.GetProvinceModifier() called for every province, every country
**Solution:** Income caching with dirty flags
**Pattern for Future:** Cache expensive calculations, invalidate via events

### Problem 2: Farm Not Providing Gold
**Symptom:** Farm only gave percentage bonus, no flat gold
**Root Cause:** Removed GoldOutput when converting to modifiers
**Solution:** Added `local_income_additive` modifier type for flat bonuses

---

## Architecture Impact

### Documentation Updates Required
- [x] StarterKitEvents.cs - Added note about Core.Units events

### Patterns Reinforced
- **Pattern 3 (EventBus):** All cross-system communication via EventBus
- **Pattern 11 (Dirty Flags):** Income caching with event-driven invalidation
- **Pattern 17 (Single Source of Truth):** AI uses same EconomySystem as player

---

## Code Quality Notes

### Performance
- **Before:** Modifier lookups every province, every tick = lag
- **After:** Cached income, recalculate only when dirty = smooth
- **Status:** ✅ Meets target

### Technical Debt
- **Paid Down:** Removed all C# events from StarterKit
- **Created:** None

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- Income caching: `EconomySystem.cs:39-42` (cache arrays), `:169-182` (GetCachedIncome)
- Cache invalidation: `EconomySystem.cs:80-84` (OnBuildingConstructed)
- No C# events in StarterKit - all use EventBus

**Gotchas for Next Session:**
- Must call InvalidateCountryIncome() when anything affects income
- Core.Units already emits unit events - don't duplicate in StarterKit
- farm.json5 is in `common/buildings/` not root `buildings/`

---

## Links & References

### Code References
- ModifierType enum: `StarterKit/Data/ModifierType.cs`
- Income caching: `StarterKit/Systems/EconomySystem.cs:39-104`
- Building modifiers: `StarterKit/Systems/BuildingSystem.cs:379-416`
- Farm definition: `Template-Data/common/buildings/farm.json5`

### Related Sessions
- [01-gpu-gradient-map-modes.md](01-gpu-gradient-map-modes.md) - Previous session

---

*Template Version: 1.0 - Created 2025-09-30*
