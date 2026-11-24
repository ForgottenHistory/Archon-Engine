# Map Line Layer
**Date**: 2026-11-24
**Session**: 2
**Status**: ✅ Complete (play-tested)
**Priority**: Medium

---

## Goal

Let a game draw its own lines on the map (railways first) with the same look and tooling as borders and rivers:
textured strips draped on the terrain, styled from the visual style file, without writing a renderer.

## What Was Added

- `Map.Rendering.Lines.MapLineLayer` (MonoBehaviour): `Initialize(textureManager, mapPlane, renderQueue, withJunctionCaps)`,
  then `SetLines(IReadOnlyList<MapLine>)`. A `MapLine` is a polyline in map pixels (x right, y down) and a line class
  name. The layer builds one mesh per class with `BorderMeshGenerator` and draws it with `BorderMeshRenderer`, like
  `MapRiverRenderer`, and re-applies styles when the style file reloads (`BorderClassStyles.Version`).
- `BorderMeshGenerator.GenerateMeshes(..., junctionCaps)`: false leaves out the fans where arcs of a class meet.

## Decisions

### Line classes live in the "borders" section
The style parser already accepts any class name there with every strip field (texture, width, minScreenWidth, tile,
color, fade, order, softness). A game names its classes (for example one per railway level) and the layer resolves
them like borders do. No new style section.

### The game owns the geometry and the rebuild timing
The layer only rebuilds on `SetLines`. A game that changes a few lines often should split them into a second layer
(each layer is its own set of meshes on its own render queue): a small overlay rebuilds cheaply while the big static
layer stays.

### Queue between rivers and borders
Default base queue 2893 (rivers 2890, borders 2900+). Each class takes one queue step in style order, so a few
classes fit below the borders; an overlay layer passes a higher queue.

### No junction caps by default
Caps fan the texture radially, which suits a border profile but not a track or road texture: such lines overlap at
their shared ends instead.

## Notes

- `MapRiverRenderer` could be rewritten on top of `MapLineLayer` (rivers are traced from an image, then drawn the
  same way); left as is for now.
