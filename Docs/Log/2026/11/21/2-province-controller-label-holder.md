# Province Controller Setter and Label Holder Hook
**Date**: 2026-11-21
**Session**: 2
**Status**: ✅ Complete
**Priority**: Medium

---

## Session Goal

**Primary Objective:**
- Let a game change who controls a province without changing its owner (occupation), and let the map labels follow that choice, without the engine defining occupation rules

**Success Criteria:**
- Defaults unchanged: labels follow owners
- The game supplies the policy (when control changes, what counts as occupied)

---

## Changes

### `ProvinceSystem.SetProvinceController(province, controller)`
`ProvinceState` already stored `controllerID` but nothing could set it. The setter writes the snapshot buffer, marks the province dirty and emits `ProvinceControllerChangedEvent { ProvinceId, OldController, NewController, TimeStamp }`. No-op if unchanged. `controllerID` can be 0 for provinces whose loader never set it, so games should treat 0 as "the owner".

### `IMapLabelSource.GetLabelHolder(province, owner, gameState)`
Which country's label a province counts towards. `DefaultMapLabelSource` returns the owner. `MapLabelRenderer` builds its province-to-country array from it, and re-places the affected countries on both `ProvinceOwnershipChangedEvent` and `ProvinceControllerChangedEvent` (after the usual settle delay).

---

## Quick Reference for Future Claude

- The owner texture and borders still follow ownership. A game that colours by controller needs its own texture (province-indexed, updated from `ProvinceControllerChangedEvent`).
- Units cannot enter water, so a game that wants lakes to follow occupation must set their controller itself.
