# Managed A* on Flat Arrays, Log Levels, ControlVersion, Outline by Members
**Date**: 2026-11-22
**Session**: 6
**Status**: ✅ Complete (all assemblies compile; confirm in a profiled run)
**Priority**: High

---

## Problems Encountered & Solutions

### Problem 1: Managed A* slow outside Burst
**Symptom:** In a deep-profiled hitch, 32 `FindPathManaged` searches took about 2.3 s: 1.7M `NativeList`
reads, 2.9M `AtomicSafetyHandle` checks, a dictionary lookup and a copy for every visited province's neighbours.

**Root Cause:** The managed search (needed whenever a game cost calculator is set) reused the Burst job's
Native containers. Outside Burst every access pays safety checks and a hash.

**Solution:** `FindPathManaged` runs on managed arrays indexed by province id: g-score, parent and closed
arrays cleared by a stamp bump, an array binary heap (ties to the lower id), and the graph from the new
`AdjacencySystem.GetCompactGraph` (CSR offsets and neighbours, rebuilt when `AdjacencySystem.Version` moves).
See `Log/learnings/burst-optimization-flat-storage-pattern.md`, "Native Containers in Managed Hot Loops".

### Problem 2: The log level filtered almost nothing
**Symptom:** A single frame spent 63 ms in `LogStringToConsole` (34 messages). Units created and disbanded,
resource batches, wars, peace, treaties, province owner changes and every step of a border distance-field
pass all logged at Info, and `GameSettings.LogLevel` only applied where a caller checked it by hand.

**Solution:**
- `ArchonLogger.Level` (set from `GameSettings.LogLevel` on registration and on inspector edits) drops
  messages above it, for console and file. `LogLevel` moved to `Utils/LogLevel.cs` (namespace unchanged).
- New `ArchonLogger.LogDebug` for per-event and per-step messages; the runtime ones above moved to it.
- `BorderDistanceFieldGenerator`: per-pass logs removed (the timing line is behind `logPerformance`). The
  ping-pong render textures stay allocated between regenerations instead of a full-map allocation each time,
  and the active render texture is cleared before release.
- Leftover debug in `CountryDataManager` (a `Debug.Log` in `GetCountryColor` for ids 0-4) removed; raw
  `Debug.Log` calls in Core routed through `ArchonLogger`.

### Problem 3: StarterKit demo systems always ran
**Solution:** `StarterKit.Initializer` has `enableEconomy`, `enableBuildings`, `enableAI` and
`enableProvinceHistory` (default on). A game with its own economy and AI turns them off; the systems that
depend on them are already null-safe.

### Problem 4: No change counter for control
**Symptom:** Several game systems each read every province's controller once an hour (whole-map reads through
a NativeArray), with no way to know whether control had changed since the last read.

**Solution:** `ProvinceSystem.ControlVersion`, the controller counterpart of `OwnershipVersion`: up on
`SetProvinceController`, on bulk loads (`BumpOwnershipVersion` now bumps both) and on `SetProvinceState`.
`SetProvinceState` also bumps `OwnershipVersion` now when the owner changes, which it did not before.

### Problem 5: Outlining one province scanned every border arc
**Symptom:** Selecting a province tested every arc on the map (about 41k, twice each) through a predicate.

**Solution:** `IBorderHighlightOutline.SetSecondaryOutline` and `ProvinceHighlighter.HighlightProvinceSet`
take the member provinces instead of a predicate; outlines look only at those provinces' arcs
(`BorderCurveCache.ArcsOf`). Province and country outlines do the same (country members from
`ProvinceSystem.GetCountryProvinces`).

---

## Quick Reference for Future Claude

- `Scripts/Core/Systems/PathfindingSystem.cs`: `FindPathManaged`, heap, `EnsureManagedBuffers`
- `Scripts/Core/Systems/AdjacencySystem.cs`: `GetCompactGraph`, `Version`
- `Scripts/Utils/ArchonLogger.cs`: `Level`, `IsEnabled`, `LogDebug`; `Scripts/Utils/LogLevel.cs`
- `Scripts/Core/Systems/ProvinceSystem.cs`: `ControlVersion`
- `Scripts/Map/Rendering/Border/BorderCurveCache.cs`: `ArcsOf`
- **Pattern:** in the editor every console message costs about 1-2 ms. Anything that can fire per event or
  per step at runtime is `LogDebug`.
- **Pattern:** a whole-map read shared by several systems is cached on `OwnershipVersion`/`ControlVersion`,
  with new arrays on each change so a held reference stays a consistent snapshot.
