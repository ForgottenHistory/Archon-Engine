# Startup: Border Graph Cache, Single Border Build, Linking Log Spam
**Date**: 2026-11-20
**Session**: 6
**Status**: ✅ Complete
**Priority**: Medium

---

## Context

Follows [5-river-trace-hash-collisions.md](5-river-trace-hash-collisions.md). On a 5632x2048 map
with 13,413 provinces, engine + game startup was ~6 s after the river fix. Three more wastes:

## What We Did

### 1. Border graph disk cache (`BorderGraphCache`)
`MeshGeometryBorderRenderer.BuildGraph` traced + smoothed the border graph (41k arcs, 250k points)
on every start: ~506 ms. Now cached as `provinces.png.borders` (next to the province image,
via `DataFileResolver`).
- **Key = 64-bit hash of the province ID readback** (+ map size + smoothing settings), not the
  file timestamp: valid exactly while the province pixels are, whatever produced them (base data,
  mods, overrides). The readback (~47 ms) is still needed; the hash costs ~3 ms.
- Binary format: header, then per arc `ProvinceA, ProvinceB, Closed, n, n * Vector2`. ~2.4 MB.
- Written to `.tmp` then moved; any read error = miss (retrace and save).
- `ForceRegenerate` goes through the same path (hash changes when pixels change).
- **Result:** 506 ms → 13 ms load.

### 2. Border meshes built once at startup
`VisualStyleManager.ApplyBorderConfiguration` called `SetBorderMode` (auto-regenerates) and later
`DetectBorders()` after populating the owner texture: two full mesh builds (~230 + ~390 ms), the
first one classified against an empty owner texture. `SetBorderMode` and
`SetPixelPerfectParameters` take `regenerate = true`; startup passes false when the explicit
`DetectBorders()` follows (`enableBordersOnStartup`).

### 3. Per-entity linking logs removed
`ReferenceResolver` logged one line per province and per country ("Resolved references for ...").
13k+ file log lines per start. Removed; the phase summary remains. Linking 720 ms → 76 ms.

## Result

Startup ~12 s (session start) → ~6 s (river fix) → **~4.6 s**.

## Quick Reference for Future Claude

- Derived data that only depends on province pixels: cache keyed on a hash of the ID readback.
- Never log per province/country in load paths; log a summary.
- Setters that auto-regenerate need a `regenerate` opt-out for batch configuration.
- Remaining big items: province pixel index (~0.5 s, needed by OwnerTextureDispatcher indexed
  updates), JSON5 province loading (~0.6 s), border mesh build (~0.23 s).
