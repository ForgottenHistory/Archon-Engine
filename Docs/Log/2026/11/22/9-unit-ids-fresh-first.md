# Unit IDs: Fresh Before Recycled, Marches End With the Unit
**Date**: 2026-11-22
**Session**: 9
**Status**: ✅ Complete
**Priority**: High

---

## Problems Encountered & Solutions

### Problem 1: A destroyed unit's ID went straight to the next new unit
**Symptom:** Once a game created units during play (recruitment), a unit destroyed in battle had its ID
handed to a unit created a few days later, of another country. Game systems that still held the old ID
(plan membership, borrowed attackers, queued moves; pruned lazily, often once an hour) could have acted
on the new unit.

**Root Cause:** `UnitSystem.AllocateUnitID` took a recycled ID before a fresh one, and picked it from a
`HashSet` (the first in its enumeration order).

**Solution:** Fresh IDs first (`nextID` up to `capacity`); recycled IDs only when those run out, lowest
first, from a `SortedSet` (an explicit, deterministic order instead of a hash set's). Saves keep the same
format (`availableIDs` written as before).

### Problem 2: A disbanded unit's march stayed queued
**Symptom:** "Cannot start movement for non-existent unit N" warnings after battles.

**Root Cause:** `DisbandUnit` cleared the unit but left its entry in `UnitMovementQueue` (moving units,
paths). With the old ID reuse a new unit could have inherited the dead unit's march.

**Solution:** `DisbandUnit` calls `MovementQueue.CancelMovement` first.

---

## Decisions

### Delay reuse rather than never reuse
IDs are `ushort` and index a dense array of `capacity` slots, so they can't grow forever; delaying reuse
until the fresh range is spent makes aliasing practically impossible without changing the ID type.
Systems holding unit IDs should still drop them on `UnitDestroyedEvent` or check `HasUnit` before
acting; note the event is frame-queued, so within one simulation tick only `HasUnit` helps.
