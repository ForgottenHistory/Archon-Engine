# Keyboard Focus Gate for Hotkeys
**Date**: 2026-11-22
**Session**: 10
**Status**: ✅ Complete
**Priority**: Medium

---

## Goal

A game screen with a UI Toolkit text field (naming something) must not trigger hotkeys while the player
types: space pausing, arrow keys moving the camera, letters opening screens, F5 saving.

## What Was Added

- `Utils.KeyboardFocus` (Utils assembly, so every engine and game assembly can use it):
  - `Typing`: a UI Toolkit `TextField` has keyboard focus in any UI document's panel. Worked out once per
    frame; the UI documents are looked up again every 2 real seconds (a presentation throttle).
  - `KeyDown(key)` / `Key(key)`: `Input.GetKeyDown` / `Input.GetKey`, false while typing.
- The engine's own hotkeys read keys through it: camera arrow keys (`BaseCameraController`), the perspective
  toggle, quick save / load (`SaveManager`), the day/night toggle (`MapDayNight`) and the StarterKit UI keys.

## Decisions

### A gate, not an input system
Hotkeys stay plain `Update` checks; they only swap `Input.GetKeyDown` for `KeyboardFocus.KeyDown`. Modifier
checks (Shift, Ctrl held) keep using `Input.GetKey`, since they only change what another key does.

### Detect focus, don't register fields
Asking the panels' focus controllers finds every text field without screens having to register theirs, and
can't get stuck "typing" when a screen forgets to unregister.

### Call with `global::Utils`
`Map.Utils` exists, so code inside `Map.*` namespaces must write `global::Utils.KeyboardFocus`.

## Notes

- A screen with a text field should drop focus (`Blur()`) on Escape, on Enter and on clicks outside the
  field, or hotkeys stay off until the player clicks another focusable element.
