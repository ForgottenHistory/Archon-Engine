# ProvinceSelector: Hooks for Clickable Map Objects
**Date**: 2026-11-20
**Session**: 4
**Status**: ✅ Complete
**Priority**: Medium

---

## Session Goal

**Primary Objective:**
- Let a game draw clickable objects on the map (unit counters) without a click on them also
  selecting, or hovering, the province underneath

---

## Problem

`ProvinceSelector.Update` turned every left click on the map into `OnProvinceClicked` and every
mouse position into a hovered province. A game-layer unit counter drawn over a province had no way
to take the click first, so selecting units also selected the province, and hovering a counter
showed the province hover highlight under it.

## Solution (`Map/Interaction/ProvinceSelector.cs`)

- **Click interceptors**: `AddClickInterceptor(Func<int, Vector2, bool>)` /
  `RemoveClickInterceptor`. Called with (mouse button 0/1, screen position) before any province
  click handling, in registration order; returning true consumes the click, so no province event
  fires.
- **Pointer blockers**: `AddPointerBlocker(Func<Vector2, bool>)` / `RemovePointerBlocker`. While a
  blocker returns true for the pointer position, no province counts as hovered.
  `OnProvinceHovered(0)` now fires when the hover moves from a province to nothing (off the map or
  onto a blocking object), so hover highlights can clear; before, 0 never fired.
- **`ClearSelection()`**: clears the province selection and fires `OnSelectionCleared`, for when
  the game selects something else on the map (e.g. units).

**Pattern for Future:** map-space objects drawn by the GAME (counters, markers, arrows) hook into
input through these, rather than a second raycast path or disabling the selector.

---

## Quick Reference for Future Claude

- A game's picking function must mirror how its objects are drawn (screen size, anchor, height),
  since the selector only asks "is something under this screen position".
- `OnProvinceHovered` subscribers must handle 0 (pointer left the provinces).
