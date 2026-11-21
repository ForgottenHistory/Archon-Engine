# Map Point Under the Cursor
**Date**: 2026-11-21
**Session**: 3
**Status**: ✅ Complete
**Priority**: Medium

---

## Session Goal

**Primary Objective:**
- Give game code the cursor's position on the rendered map surface, the same point province picking uses, so map drawing that follows the cursor (a drag trail, a drawn line) stays under it

**Success Criteria:**
- A line drawn from the returned points sits under the cursor at any zoom and over raised terrain
- Province picking unchanged

---

## Problem

A game drew a dragged line from a ray intersected with the flat `y = 0` plane. The line landed south of the cursor (towards the camera), and the gap grew as the camera zoomed in. The rendered map is displaced by the heightmap but its collider is flat: a flat-plane hit misses the visible surface by the terrain height times the ray's slope. `ProvinceSelector` already corrected for this when picking provinces (`TryIntersectHeightfield`), but kept the corrected point private, so game code reinvented the raycast without the correction.

## Changes

### `ProvinceSelector.TryGetMapPointAtScreenPosition(screen, camera, out worldPoint, out mapUV)`
The flat collider hit, corrected for the heightmap displacement (the same heightfield march as picking). `mapUV` is 0..1 with v from the bottom, the convention of `MapTextureManager.GetProvinceID`. `RaycastForProvince` now goes through the same private `TryRaycastMapPoint`.

---

## Quick Reference for Future Claude

- Anything that follows the cursor on the map: `ProvinceSelector.TryGetMapPointAtScreenPosition`, never a ray against a flat plane. Symptom of getting it wrong: the drawing sits south of (below) the cursor, worse when zoomed in or over mountains.
- Map pixel with y down (texture image rows): `(uv.x * MapWidth, (1 - uv.y) * MapHeight)`.
