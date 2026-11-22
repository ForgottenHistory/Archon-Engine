# Map Labels: Maximum Wait for Pending Changes
**Date**: 2026-11-22
**Session**: 2
**Status**: ✅ Complete
**Priority**: Medium

---

## Problems Encountered & Solutions

### Problem 1: Country labels very late to follow conquests in large wars
**Symptom:** With many wars running, country labels kept their old place and size long after the
borders had moved; they caught up only when the fighting paused.

**Root Cause:** `MapLabelRenderer` placed labels only once ownership/control changes had settled
(`ChangeSettleSeconds` 0.3 s with no new change). Every change reset that timer. With control
changing every game hour, at high game speed a change arrives nearly every frame, so the changes
never settled.

**Solution:** Debounce with a ceiling: also place once the oldest pending change has waited
`ChangeMaxWaitSeconds` (1 s), tracked in `firstChangeTime` (set by `MarkChanged`, reset when a
placement job starts). Placement runs on a background task, so this costs no frame time; at most one
job runs at a time (unchanged).

---

## Decisions

### Real seconds, not game time
Labels are presentation: they never feed back into the simulation, so they need not be deterministic.
Game time would be wrong here:
- it stops while paused, so changes made while paused (a peace deal, a ceded state) would never
  place until unpaused;
- it scales with game speed, so a game-time wait is no wait at top speed and seconds of stale labels
  at slow speed.
The wait limits a real-time cost (background placement jobs) and visible flicker, so it is measured
in unscaled real time, like the border texture batching (`TextureUpdateBridge`).

The ceiling stays even though only one job runs at a time: without it, a slow placement could make a
new job start the moment the previous one ends, continuously.

---

## Quick Reference for Future Claude

- `Scripts/Map/Rendering/Labels/MapLabelRenderer.cs`: `ChangeSettleSeconds`, `ChangeMaxWaitSeconds`,
  `MarkChanged`, the start condition in `Update`.
- **Pattern:** a debounce on a stream of events needs a maximum wait, or a steady stream starves it.
- **Rule of thumb:** game time and fixed point for anything that changes game state; unscaled real
  time for presentation throttles.
