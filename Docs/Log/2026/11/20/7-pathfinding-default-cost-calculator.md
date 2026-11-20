# Pathfinding: Default Cost Calculator Was Never Used
**Date**: 2026-11-20
**Session**: 7
**Status**: ✅ Complete
**Priority**: High

---

## Problem

Land units pathed across sea and through terrain any game had marked impassable.
`PathfindingSystem.SetDefaultCostCalculator` stored the calculator in `defaultCostCalculator`,
but nothing read it: `FindPath` builds `PathOptions.Default` (CostCalculator = null) and
`GetEffectiveCostCalculator()` falls back to `UniformCostCalculator`. Every path was uniform-cost
via the Burst path, and StarterKit's `LandUnitCostCalculator` (blocks water) never ran.

## Fix (`Core/Systems/PathfindingSystem.cs`)

- `FindPathWithOptions`: when `options.CostCalculator` is null, use `defaultCostCalculator`
  (non-uniform calculators go through the managed A*).
- **Path cache only for uniform costs.** The cache key is (start, goal). A cost calculator can
  depend on the unit (owner, type) and on changing state (ownership, wars, access), so a cached
  path from one country would be served to another.
- **Start province is no longer checked with `CanTraverse`.** The unit is already there and may
  always leave (e.g. it stands in a country whose access it lost). Non-traversable goals still
  fail (no path found).

## Pattern

GAME movement policy = an `IMovementCostCalculator` (`CanTraverse` + `GetMovementCost`) installed
with `SetDefaultCostCalculator` after StarterKit-style setup (which calls it during init, and
`Pathfinding.Initialize` resets it to uniform). Access rules can use `PathContext.UnitOwnerCountryId`
with `DiplomacySystem` (`AreAllied`, `IsAtWar`, `HasMilitaryAccess`).

## Quick Reference for Future Claude

- A setter whose value is never read is a silent no-op: grep for reads of the field.
- Never cache results keyed on fewer inputs than the computation depends on.
