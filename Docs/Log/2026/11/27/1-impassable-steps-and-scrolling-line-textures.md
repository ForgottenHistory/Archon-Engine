# Impassable Steps in Pathfinding, Scrolling Line Textures
**Date**: 2026-11-27
**Session**: 1
**Status**: ✅ Complete (compiles; not yet play-tested)
**Priority**: Medium

---

## Goal

Let a game forbid single steps between provinces (not whole provinces) in pathfinding, and let a map line's
texture move along the line.

## Problems Found

### `FixedPoint64.MaxValue` from a cost calculator was not impassable
`IMovementCostCalculator.GetMovementCost` documents "return `FixedPoint64.MaxValue` to indicate impassable", but the
A* loop in `PathfindingSystem` added the raw value to the running cost. The sum overflowed to a negative number, so
the forbidden step became the cheapest one on the map.

`CanTraverse` only answers for a province. A rule about the step itself needs the cost call: a land unit that may
enter a sea province only from a province with a port, and leave the sea only into one, cannot be expressed per
province.

### Line textures could not move
Line classes (borders, rivers, `MapLineLayer`) tile a texture along the line, but always still. Flow arrows and
routes (supply, trade) read better when the pattern travels in the direction of flow.

## What Changed

- `PathfindingSystem`: a neighbour whose movement cost is `FixedPoint64.MaxValue` is skipped, before the avoid
  penalty is applied. The documented contract now holds.
- `BorderClassStyle.Scroll` (visual style file: `scroll` on a line class): texture repeats per second moving along
  the line, from its first point to its last. 0 or left out is a still texture. `BorderMeshCore.hlsl` subtracts
  `_Time.y * _BorderScroll` from U; `BorderMeshRenderer` sets `_BorderScroll` per class material. Shaders built on
  `BorderMeshCore.hlsl` get it for free.

## Notes

- Scroll speed is in repeats, so with `tile` repeats per map pixel the pattern moves `scroll / tile` map pixels a
  second.
- The scroll uses shader time (real seconds): it is presentation and does not follow game speed or pause.
- The path cache is unaffected: it was already only used for uniform costs.
