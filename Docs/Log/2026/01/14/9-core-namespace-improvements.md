# CORE Namespace Improvements
**Date**: 2026-01-14
**Session**: 9
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Audit CORE namespace for systems needing enhancement
- Improve FixedPoint64/FixedPoint32 with convenience methods
- Add IResourceProvider interface and ResourceCost struct

**Success Criteria:**
- Planning doc with all improvement opportunities
- FixedPoint types have IsZero, Sqrt, Lerp, etc.
- ResourceSystem has interface abstraction, cost validation, EventBus

---

## What We Did

### 1. CORE Namespace Audit

Created comprehensive planning doc identifying improvement opportunities across 13 systems.

**File:** `Docs/Planning/core-namespace-improvements.md`

**Key Findings:**
| Priority | Systems |
|----------|---------|
| High | ResourceSystem, AISystem, PathfindingSystem |
| Medium | FixedPoint64/32, DeterministicRandom, AdjacencySystem, Query System |
| Low | GameSystem, Result Types, Localization |

### 2. FixedPoint64 Enhancements

**File:** `Core/Data/FixedPoint64.cs`

**Added Convenience Properties:**
- `IsZero`, `IsPositive`, `IsNegative`, `IsNonNegative`, `IsNonPositive`
- `Sign` (returns -1, 0, or 1)

**Added Constants:**
- `NegativeOne`, `Ten`, `Hundred`

**Added Math Functions:**
- `Sqrt()` - Newton-Raphson square root
- `Pow(int exponent)` - Binary exponentiation
- `Lerp()`, `LerpClamped()` - Linear interpolation
- `InverseLerp()` - Reverse interpolation
- `Remap()`, `RemapClamped()` - Range mapping
- `MoveTowards()` - Gradual value change
- `Frac()` - Fractional part
- `Percentage()` - Calculate percentage

### 3. FixedPoint32 Parity

**File:** `Core/Data/Math/FixedPointMath.cs`

Brought FixedPoint32 to full parity with FixedPoint64:
- All convenience properties
- All operators (`/`, `%`, `==`, `!=`, unary `-`)
- `IEquatable<T>`, `IComparable<T>` interfaces
- All factory methods (`FromFraction`, `FromFloat`, `FromFixed64`, `ToFixed64`)
- All math functions (Abs, Min, Max, Clamp, Floor, Ceiling, Round)
- All advanced math (Sqrt, Pow, Lerp, Remap, etc.)
- Serialization (`ToBytes`, `FromBytes`)

**Also Enhanced FixedPoint2:**
- `Length` property (using new Sqrt)
- Arithmetic operators, `Dot()`, `Distance()`, `Lerp()`

### 4. ResourceSystem Interface & Enhancements

**New Files:**
- `Core/Resources/IResourceProvider.cs` - Interface for GAME customization
- `Core/Resources/ResourceCost.cs` - Cost validation struct
- `Core/Resources/ResourceEvents.cs` - EventBus events

**Updated:** `Core/Resources/ResourceSystem.cs`

**New Features:**
- Implements `IResourceProvider` interface
- Cost validation: `CanAfford(countryId, costs)`, `TrySpend(countryId, costs)`
- Bulk operations: `AddResourceToAll()`, `SetResourceForAll()`, `TransferResource()`
- Batch mode: `BeginBatch()` / `EndBatch()` for loading
- Query: `GetAllResourcesForCountry(countryId)`
- EventBus integration (removed legacy C# event)

**Events:**
- `ResourceChangedEvent` - Single resource change
- `ResourceTransferredEvent` - Transfer between countries
- `ResourceBatchCompletedEvent` - Batch mode ended
- `ResourceSystemInitializedEvent`, `ResourceRegisteredEvent`

### 5. GAME Layer Updates

**Updated Files:**
- `Game/Initialization/GameSystemResourcePhaseHandler.cs` - Pass EventBus to Initialize
- `Game/UI/PlayerResourceBar.cs` - Subscribe via EventBus
- `Game/Systems/Economy/EconomyTreasuryBridge.cs` - Subscribe via EventBus
- `Game/Systems/EconomySystem.cs` - Pass EventBus to bridge

---

## Architecture Impact

### ResourceSystem Pattern

```
IResourceProvider (interface - ENGINE)
├── GetResource, AddResource, RemoveResource, SetResource
├── CanAfford, TrySpend (cost validation)
├── AddResourceToAll, TransferResource (bulk ops)
├── BeginBatch, EndBatch (event suppression)
└── GetAllResourcesForCountry (queries)

ResourceSystem : IResourceProvider (default impl)
└── Emits ResourceChangedEvent via EventBus

ResourceCost (struct)
├── ResourceId, Amount
└── Scale(), Add(), extension methods
```

### Key Design Decisions

1. **No legacy compatibility** - Removed C# event, EventBus only
2. **Atomic spending** - `TrySpend()` checks all costs before spending any
3. **Batch mode** - Suppresses events during loading for performance
4. **Interface abstraction** - GAME layer can provide custom implementations

---

## Files Changed

| File | Changes |
|------|---------|
| `Core/Data/FixedPoint64.cs` | +165 lines - convenience props, math functions |
| `Core/Data/Math/FixedPointMath.cs` | Rewritten - FixedPoint32 parity, FixedPoint2 enhanced |
| `Core/Resources/IResourceProvider.cs` | NEW - interface |
| `Core/Resources/ResourceCost.cs` | NEW - cost struct |
| `Core/Resources/ResourceEvents.cs` | NEW - EventBus events |
| `Core/Resources/ResourceSystem.cs` | Major update - interface, features |
| `Docs/Planning/core-namespace-improvements.md` | NEW - improvement roadmap |
| `Game/Initialization/GameSystemResourcePhaseHandler.cs` | Pass EventBus |
| `Game/UI/PlayerResourceBar.cs` | EventBus subscription |
| `Game/Systems/Economy/EconomyTreasuryBridge.cs` | EventBus subscription |
| `Game/Systems/EconomySystem.cs` | Pass EventBus to bridge |

---

## Quick Reference for Future Claude

**Cost Validation Usage:**
```csharp
var costs = new[] {
    ResourceCost.Create(goldId, 100),
    ResourceCost.Create(manpowerId, 50)
};
if (resourceSystem.TrySpend(countryId, costs)) { /* success */ }
```

**Batch Mode Usage:**
```csharp
resourceSystem.BeginBatch();
// ... thousands of SetResource calls (no events)
resourceSystem.EndBatch(); // single ResourceBatchCompletedEvent
```

**FixedPoint Convenience:**
```csharp
if (value.IsZero) { }
if (value.IsPositive) { }
var sqrt = FixedPoint64.Sqrt(value);
var lerped = FixedPoint64.Lerp(a, b, t);
```

---

## Session Statistics

**Files Created:** 4
**Files Modified:** 7
**Commits:** 2 (FixedPoint enhancements, ResourceSystem improvements)

---

## Links & References

### Related Sessions
- [Previous: Calendar System Refactor](8-calendar-system-refactor.md)

### Code References
- IResourceProvider: `Core/Resources/IResourceProvider.cs`
- ResourceCost: `Core/Resources/ResourceCost.cs`
- ResourceEvents: `Core/Resources/ResourceEvents.cs`
- FixedPoint64 math: `Core/Data/FixedPoint64.cs:85-180`
- FixedPoint32: `Core/Data/Math/FixedPointMath.cs`

### Planning
- [CORE Improvements Roadmap](../../Planning/core-namespace-improvements.md)

---

*FixedPoint types enhanced with convenience methods and math functions. ResourceSystem refactored with IResourceProvider interface, ResourceCost for validation, and EventBus integration.*
