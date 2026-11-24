# Province Link Network and Control Change Tick
**Date**: 2026-11-24
**Session**: 1
**Status**: ✅ Complete (compiles; not yet play-tested)
**Priority**: Medium

---

## Goal

A game's railways and supply hubs were first written entirely in the game layer. Two parts of it are
mechanisms any grand strategy game can use, so they moved into the engine:

1. a network of links between provinces with a level per link, and the best-bottleneck path over it;
2. how long a province has been held by its current controller.

## What Was Added

- `Core.Graph.ProvinceLinkNetwork`:
  - Links between two provinces (stored a < b) with a level 1..MaxLevel. `SetLevel(a, b, level)` adds,
    re-levels or (level 0) removes a link; link indices stay stable, so a removed link stays at level 0.
  - `Level(a, b)`, `Find(a, b)`, `BestLevel(p)`, `LinksOf(p)` (allocation-free), `Provinces()`, `Version`.
  - `WidestPaths(source, canUse, result)`: the best bottleneck level from the source to every province
    reachable over links whose ends `canUse` allows. A bucket queue by level, highest first.
  - `WidestPathResult`: `Level` (0 not reached, MaxLevel + 1 the source), `Via` (arriving link, to trace the
    path), `Hops`. Reused across calls: a stamp marks the current call's entries, nothing is cleared.
- `ProvinceSystem.TryGetControlChangeTick(province, out tick)`: the tick the effective controller (controller,
  else owner) last changed. Stamped in `SetProvinceController`, `SetProvinceOwner` (when the province has no
  separate controller) and `SetProvinceState`. Saved and loaded with `ProvinceSystem`'s state.
- `ProvinceSystem.SetTickSource`, wired by `GameState` to `TimeManager.CurrentTick`.

## Decisions

### A link layer, not edge weights on adjacency
Railways (and roads, canals) are sparse and change by construction and capture; adjacency is fixed and dense.
A separate network keeps both simple and lets a game have several (rail and road) without touching adjacency.

### Bucket queue instead of a heap
Levels are small integers, so the widest path settles each province once per level bucket, highest first:
linear time and the same result every run (links are visited in index order).

### Managed, not Burst
A few thousand links, recomputed when something changes (daily at most). A managed version is simpler to
call with a game predicate; it can become a Burst job if a game needs it per hour.

### Tick 0 isn't stamped
The starting setup (loading owners and controllers) happens at tick 0, before time runs. Stamping it would make
every province look freshly captured. So 0 means "not changed since the start", and `TryGet` returns false.

### Save format
The control change ticks are appended to `ProvinceSystem.SaveState`. Older saves can't be read by this version
(the engine is unreleased; no compatibility path).

## Notes

- The game layer that uses this computes conversion as "changed tick + cooldown <= now", re-checked daily, so
  it needs no per-province timers of its own.
- `WidestPaths` finds a widest path, not the shortest of the widest ones: `Hops` is the length of the path it
  found.
