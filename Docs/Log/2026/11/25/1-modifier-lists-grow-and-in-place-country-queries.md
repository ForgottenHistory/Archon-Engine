# Modifier Lists Grow, Country Queries In Place
**Date**: 2026-11-25
**Session**: 1
**Status**: ✅ Complete (compiles, EditMode tests added; not yet play-tested)
**Priority**: Medium

---

## Goal

Make the ModifierSystem fit a game that keeps every researched technology and every idea as a country modifier
source, and queries country modifiers daily.

## Problems Found

### A full list dropped modifiers
`ActiveModifierList` had a fixed capacity (64). Past it, `Add` logged a warning and returned false: the modifier was
silently not applied. A country with a few dozen researched technologies, each with one to three modifiers, plus its
laws and ideas, passes 64 early.

### Every country query copied the scope twice
`GetCountryModifier` read the country's `ScopedModifierContainer` out of the NativeArray, queried it and wrote it back.
The container holds the cached `ModifierSet` (two fixed arrays of 512 longs, about 8 KB), so each query copied 16 KB.
The province path already avoided this with pointers (`GetProvinceModifierFast`).

## What Changed

- `ActiveModifierList.Add` doubles the list when it is full (it remembers its allocator), keeping every slot where it
  is. Callers holding a copy of the struct must write it back after `Add`; every ModifierSystem path already does.
- `ModifierSystem.GetCountryModifier` and `EnsureCountryScopeClean` share a pointer path (`CleanCountryScope`): the
  country scope is rebuilt in place when its dirty flag (or its own `isDirty`) is set, with the global scope as
  parent, then read from its cache. Global changes still mark every country dirty, so inheritance is unchanged.
- Tests: `Tests/EditMode/ModifierSystemTests.cs` (a list three times past its starting capacity sums and removes
  correctly; country queries follow adds, removals and global changes).

## Notes for Later

- Memory: every scope carries a full `ModifierSet` (about 8 KB). With the default 65536 province and 4096 country
  scopes that is over 500 MB allocated up front, mostly for provinces that never get a modifier. Sizing the province
  scopes to the map's real province count, or allocating a province's set on its first modifier, would cut it.
- 512 modifier types per scope is a hard cap. A game with more modifier keys in its data than that should give ids only
  to the keys it reads.
