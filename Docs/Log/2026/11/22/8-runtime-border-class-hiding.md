# Runtime Border Class Hiding
**Date**: 2026-11-22
**Session**: 8
**Status**: ✅ Complete
**Priority**: Low

---

## Goal

A map tool that works on bigger regions (a game placing state-wide buildings) wants the borders inside
those regions gone while the tool is active, without touching the visual style file.

## What Was Added

- `BorderClassStyles.SetHidden(className, hide)` / `IsHidden(className)`: a runtime set of hidden
  border classes. Changing it bumps `BorderClassStyles.Version`, so mesh borders rebuild their draw list
  on the next frame (the same path as a style hot reload; no geometry is regenerated).
- `BorderMeshRenderer.SetMeshes` skips hidden classes, like `visible: false` in the style.
- The set is reset at `SubsystemRegistration`.

## Decisions

### Separate from the style data
The style is data-driven and hot reloaded; a runtime hide stored in it would be lost on every reload
(and would leak into the file if styles were ever saved). A separate set keeps "what the style says"
and "what the game currently wants hidden" apart.
