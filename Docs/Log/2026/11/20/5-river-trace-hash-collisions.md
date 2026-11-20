# River Trace: long Hash Collisions Made It Quadratic
**Date**: 2026-11-20
**Session**: 5
**Status**: ✅ Complete
**Priority**: High

---

## Problem

`MapRiverRenderer` took ~6.4 s at startup on a 5632x2048 rivers image with only ~88k river pixels
(875 arcs, median 79 px). Timing split (now in its load log line): decode 74 ms, scan 34 ms,
smooth 134 ms, **walk 6165 ms**.

## Cause

`RiverGraph.Build` keeps visited edges in a `HashSet<long>`, key = `(a << 32) | b` for two
neighbouring pixel indices. `long.GetHashCode()` is `(int)x ^ (int)(x >> 32)`, i.e. `a ^ b`.
Neighbours differ by 1, W or W±1, so `a ^ b` takes only a few distinct values: nearly every edge
lands in a handful of buckets and each lookup walks a chain of thousands. Quadratic.

## Fix

`HashSet<long>(MixedLongComparer.Instance)`: same keys, hash = upper 32 bits of
`x * 0x9E3779B97F4A7C15` (Fibonacci hashing). Walk: 6165 ms → 28 ms. Rivers total 6.4 s → 0.28 s;
total engine + game startup ~12 s → ~6 s. Output unchanged.

`MapRiverRenderer` now logs `decode / trace [scan, walk, smooth] / mesh / setup`.

---

## Quick Reference for Future Claude

- Never key a managed `HashSet<long>` / `Dictionary<long, …>` on packed pairs of correlated ints
  (pixel neighbours, adjacent IDs) with the default comparer: the hi^lo hash collapses. Use a
  mixing `IEqualityComparer<long>`, or avoid hashing (flat arrays / per-pixel bit flags).
- Unity's `NativeParallelHashSet<ulong>` hashes differently and is not affected.
- When a step is slow but the algorithm counts cheap (prototype the op count in Python), suspect
  hashing before the algorithm.
