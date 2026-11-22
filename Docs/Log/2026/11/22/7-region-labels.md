# Region Labels
**Date**: 2026-11-22
**Session**: 7
**Status**: ✅ Complete (needs a Unity compile to verify in play)
**Priority**: Medium

---

## Goal

Map labels for groups of provinces a game defines (states, areas), not only countries: a map tool may
want the names of the regions it works on, shown when zoomed in.

## What Was Added

- `IMapRegionLabelSource` (`MapLabelSource.cs`): `GetRegion(province)` (0 = none) and
  `GetRegionText(region)`. Set on `MapRendererRegistry.RegionLabelSource` (null by default, reset
  every play session like `LabelSource`).
- `MapRegionLabels.Shown`: runtime switch the game turns on while a tool needs region labels
  (reset at `SubsystemRegistration`).
- `MapLabelRenderer.Regions`: region mode, set before `Initialize`. The holder of a province is its
  region; ownership/control events are not subscribed (regions don't change hands); placement runs
  once in the background at startup; drawing only while `Shown`.
- `ArchonEngine` adds a second renderer, child object `RegionLabels` of the map generator, in region
  mode when a game has set `RegionLabelSource`.
- Style: `regionLabels` section in the visual style (same fields as `labels`), `MapLabelStyles.Region`
  / `SetRegion`. Defaults: `fade [3, 5]` with `fadeIn`, `maxScale 1.5`, `maxHeight 24`.
- `fadeIn` (`MapLabelStyle.FadeIn`, `_LabelFade.z` in `Archon/MapLabel`): the zoom fade runs the other
  way, hidden when zoomed out and visible when zoomed in.

## Decisions

### A second renderer instance, not a second layer inside one
Placement, font atlas, mesh and material are all per renderer; a region renderer reuses the whole
pipeline (curved placement, SDF glyphs, terrain draping) with a different holder function and style.
Each renderer loads its own font asset; that costs one more atlas only when a game uses regions.

### Placed at startup, not on first show
First version placed region labels the first time they were shown, to skip the work in sessions that
never need them. In play the labels then popped in about a second after the tool opened. Placement is
a background task anyway, so it now runs at startup and the labels are ready when first shown.
