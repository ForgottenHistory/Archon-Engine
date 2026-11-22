# Ownership/Relations Change Versions; Movement Logs at Debug Level
**Date**: 2026-11-22
**Session**: 5
**Status**: ✅ Complete (Core compiles; confirm in a profiled run)
**Priority**: Medium

---

## Problems Encountered & Solutions

### Problem 1: Path searches dominated by per-node lookups
**Symptom:** In a profiled frame of a large war, 42 managed A* searches (about 130 nodes each) took most of
the simulation time. Per node, a game cost calculator did a terrain lookup (hash map), a water check, an
impassable check, an owner lookup (hash map) and three diplomacy queries.

**Root Cause:** Nothing told the calculator when owner- or relation-derived data could have changed, so it
asked again for every node.

**Solution:** Two change counters, a generic mechanism for caches of derived data:
- `ProvinceSystem.OwnershipVersion`: up on every owner change (`ProvinceDataManager.SetProvinceOwner`) and on
  `LoadProvinceInitialStates` / `LoadState`.
- `DiplomacySystem.RelationsVersion`: up on war, peace, alliance, non-aggression pact, guarantee and
  military-access changes, and on load.

A cost calculator can keep an owner snapshot and an access table and rebuild them only when a version
moves. The answers are the same as asking each time. The versions are not saved: compare them only within a
run.

### Problem 2: A console line per unit move
**Symptom:** 277 `Debug.Log` calls in one frame cost about 70 ms; `UnitMovementQueue` logged every order,
hop, arrival and cancel.

**Solution:** These per-unit lines now log only at `GameSettings.LogLevel` Debug (`LogMoves`), like
`CommandProcessor` and `TextureUpdateBridge` already did. Setting the level to Info turns them off.

---

## Quick Reference for Future Claude

- `Scripts/Core/Systems/ProvinceSystem.cs`: `OwnershipVersion`
- `Scripts/Core/Systems/Province/ProvinceDataManager.cs`: `OwnershipVersion`, `BumpOwnershipVersion`
- `Scripts/Core/Diplomacy/DiplomacySystem.cs`: `RelationsVersion`
- `Scripts/Core/Units/UnitMovementQueue.cs`: `LogMoves`
- **Pattern:** per-event logs go behind `GameSettings.Instance?.ShouldLog(LogLevel.Debug)`; a console line
  per unit per hour is a frame-time cost in a war.
- **Pattern:** a cache of state-derived data checks a version counter instead of re-reading the state.
