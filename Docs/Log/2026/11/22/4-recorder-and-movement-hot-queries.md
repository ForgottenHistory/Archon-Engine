# Session Recorder and Movement Queue: Hot Per-Tick Queries
**Date**: 2026-11-22
**Session**: 4
**Status**: ✅ Complete (compiles; to be confirmed in a profiled run)
**Priority**: Medium

---

## Problems Encountered & Solutions

### Problem 1: ProvinceControlSource cost every tick
**Symptom:** In a deep profile of a busy hour (about 30 wars), `ProvinceControlSource.Record` was the
largest recorder cost: 40,616 `GetProvinceController` and as many `GetProvinceOwner` calls for 4 ticks.

**Root Cause:** Each getter is a `NativeHashMap` lookup (province id to array index) plus a buffer read,
two per province per tick on a 10,000-province map.

**Solution:** `Begin` looks up each included province's state-array index once; `Record` reads
`GetNativeData().provinceStates[index]` directly. Same comparison, same rows.

**Why not event-driven:** `EventBus` delivers once per frame. At high game speed one frame covers several
ticks, so event-driven rows would carry the wrong tick, and recording tools match control changes to
battles by tick.

### Problem 2: GetFullPath allocates for a simple question
**Symptom:** "Where is this unit marching to?" was answered with `GetFullPath(id)` and its last element:
a new list per call. Asked for every unit every tick (recorder, battle plans), it allocated about
155 KB per tick.

**Solution:** `UnitMovementQueue.GetFinalDestination(unitID)`: the last queued waypoint, else the current
hop's destination, 0 when not moving. No allocation. `GetFullPath` stays for drawing paths.

---

## Quick Reference for Future Claude

- `Scripts/Core/Recording/SessionRecorder.cs`: `ProvinceControlSource.stateIndex`
- `Scripts/Core/Units/UnitMovementQueue.cs`: `GetFinalDestination`
- **Pattern:** for per-tick scans over every province, read the state array by cached index, not the
  id-keyed getters. For per-tick queries over every unit, avoid APIs that build lists.
