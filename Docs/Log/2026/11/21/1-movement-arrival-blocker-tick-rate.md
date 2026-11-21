# Unit Movement: Arrival Blocker and Tick Rate
**Date**: 2026-11-21
**Session**: 1
**Status**: ✅ Complete
**Priority**: Medium

---

## Session Goal

**Primary Objective:**
- Let a game stop a unit from entering a province (e.g. an enemy holds it) and let a game move units in finer steps than a day, without the engine knowing about either rule

**Success Criteria:**
- Defaults unchanged for day-based games
- Both are mechanisms; the game supplies the policy

---

## Changes

### `UnitMovementQueue.ArrivalBlocker`
`Func<ushort unit, ushort destination, bool>`. When a movement completes, the queue asks the blocker; `true` keeps the unit at its origin with the movement kept (`ticksRemaining = 0`) and asks again on the next tick. `IsBlocked(unit)` tells UI and game code which units are held. `CancelMovement` clears the flag.

### `UnitMovementQueue.TicksPerDay`
Default 1: units step once per `DailyTickEvent`, as before. A game can set it (once, at startup, before any unit moves) to 24 to step on `HourlyTickEvent`. `UnitSystem` listens to both events and processes the one that matches. Movement state is now counted in ticks (`ticksRemaining`, `totalTicks`); `StartMovement(days)` multiplies by `TicksPerDay`, `StartMovementTicks` takes ticks directly, `DaysRemaining(state)` converts back for display. The save format is unchanged but its values are ticks, so a save made with another `TicksPerDay` is not compatible.

### Multi-hop legs
Later legs of a multi-province path used a hard-coded 2 days. They now take as long as the leg just finished.

### `TimeManager.SetBaseHoursPerSecond`
Game hours per real second at speed 1 (default 24). Lets a game tune the overall pace without changing speed multipliers.

---

## Quick Reference for Future Claude

- Policy (who blocks whom, how fast units step) belongs to the game: set `ArrivalBlocker` and `TicksPerDay` from game code at startup.
- Don't change `TicksPerDay` while units are moving; in-flight movements keep the old scale.
