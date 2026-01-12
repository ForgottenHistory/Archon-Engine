# Debug UI Migration and Console Polish
**Date**: 2025-10-18
**Session**: 2
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Migrate remaining OnGUI debug UIs to UI Toolkit
- Fix UI click-through issues with province selection
- Polish console UI for production use

**Secondary Objectives:**
- Follow ENGINE/GAME layer separation (mechanisms vs policy)
- Ensure dependency injection through HegemonInitializer
- Fix console scrolling and camera interaction issues

**Success Criteria:**
- All debug UIs use UI Toolkit instead of OnGUI
- Province selection doesn't trigger when clicking UI buttons
- Console auto-scrolls and doesn't interfere with camera controls
- All changes follow established architecture patterns

---

## Context & Background

**Previous Work:**
- See: [1-ui-toolkit-migration.md](1-ui-toolkit-migration.md)
- Previous session migrated gameplay UIs (TooltipUI, ProvinceInfoPanel, CountryInfoPanel, LoadingScreenUI, CountrySelectionUI)

**Current State:**
- Two debug UIs still using OnGUI: TimeManager debug controls and MapModeDebugUI
- ConsoleUI had text visibility and Enter key submission issues
- Province selection was triggering when clicking UI buttons
- Country Selection UI had fullscreen blocking issue

**Why Now:**
- Continuing UI Toolkit migration from previous session
- Debug UIs need to respect ENGINE/GAME layer separation
- Console needed production-ready polish for playtesting
- UI click-through blocking critical for usability

---

## What We Did

### 1. TimeDebugPanel Migration
**Files Changed:** `Assets/Game/Debug/TimeDebugPanel.cs:1-231` (created)

**Implementation:**
- Created UI Toolkit debug panel in GAME layer (top-right corner)
- Shows game time, tick count, pause/play/speed controls
- Dependency injected via HegemonInitializer.Initialize()
- Replaced TimeManager's OnGUI debug UI

**Key Features:**
- Play/Pause/Faster/Slower buttons
- Current speed display (1x, 2x, 5x)
- Game time and tick count labels
- Compact layout with proper styling

**Architecture Compliance:**
- ✅ Follows ENGINE/GAME separation
- ✅ Dependency injection pattern
- ✅ UI Toolkit programmatic creation
- ✅ Inspector-editable styling

### 2. MapModeDebugPanel Migration
**Files Changed:** `Assets/Game/Debug/MapModeDebugPanel.cs:1-256` (created)

**Implementation:**
- Created UI Toolkit debug panel in GAME layer (bottom-right corner)
- Quick switch buttons for main map modes (Political, Development, Terrain, Culture)
- Debug mode buttons (Heightmap, Normal Map, Borders, Province IDs)
- Dependency injected via HegemonInitializer.RegisterMapModes()

**Architecture Note:**
- Old MapModeDebugUI was in ENGINE layer (incorrect - GAME policy)
- New panel properly placed in GAME layer
- ENGINE provides MapModeManager mechanism, GAME provides debug UI policy

### 3. Removed OnGUI Debug UIs
**Files Changed:**
- `Assets/Archon-Engine/Scripts/Core/Systems/TimeManager.cs:1-200` (removed OnGUI code)
- `Assets/Archon-Engine/Scripts/Map/Debug/MapModeDebugUI.cs` (deleted)
- `Assets/Archon-Engine/Scripts/Map/Core/MapInitializer.cs:1-516` (removed MapModeDebugUI references)
- `Assets/Archon-Engine/Scripts/Map/Core/MapSystemCoordinator.cs:1-365` (removed Map.Debug namespace)

**Rationale:**
- OnGUI is deprecated, replaced by UI Toolkit
- Debug UIs belong in GAME layer, not ENGINE layer
- Cleaner ENGINE layer with fewer responsibilities

### 4. Fixed Country Selection UI Layout
**Files Changed:** `Assets/Game/UI/CountrySelectionUI.cs:154-177`

**Problem:** Fullscreen semi-transparent overlay blocked all map clicks during country selection

**Solution:**
- Removed fullscreen background
- Moved to bottom of screen with 20px margin
- Background only on content container (text + button), not entire screen
- Added padding and rounded corners to content box

**Result:** User can click map to select countries, UI only blocks small area at bottom

### 5. Fixed Province Selection Click-Through
**Files Changed:** `Assets/Archon-Engine/Scripts/Map/Interaction/ProvinceSelector.cs:1-123`

**Problem:** Clicking UI buttons also clicked provinces underneath

**Investigation:**
- Tried: Manual panel.Pick() with coordinate conversion - too complex, didn't work
- Tried: RuntimePanelUtils.ScreenToPanel() - coordinate issues
- Found: EventSystem.current.IsPointerOverGameObject() is official Unity solution

**Solution:**
```csharp
private bool IsPointerOverUI()
{
    // Unity's EventSystem.IsPointerOverGameObject works for both uGUI and UI Toolkit
    return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
}
```

**Why This Works:**
- EventSystem handles both uGUI and UI Toolkit automatically
- Handles all coordinate conversion internally
- Official Unity-recommended approach
- Simple, clean, reliable

**Pattern for Future:** Always use EventSystem for UI pointer detection, not manual picking

### 6. Fixed Console UI Scroll Issues
**Files Changed:** `Assets/Game/Debug/Console/ConsoleUI.cs:154-406`

**Problem 1:** Text clipping - only top of letters visible after 2 commands

**Solution:**
- Added paddingTop/paddingBottom (2px each) to labels
- Increased marginBottom from 2px to 4px
- Set height to StyleKeyword.Auto for proper sizing
- Added unityTextAlign for consistent alignment

**Problem 2:** Console didn't auto-scroll to latest output

**Solution:**
```csharp
private void ScrollToBottom()
{
    outputScrollView.schedule.Execute(() =>
    {
        float maxScroll = Mathf.Max(0,
            outputScrollView.contentContainer.layout.height -
            outputScrollView.contentViewport.layout.height);
        outputScrollView.scrollOffset = new Vector2(0, maxScroll);
    }).ExecuteLater(10); // Delay for layout calculation
}
```

- Set ScrollView to explicit ScrollViewMode.Vertical
- Made vertical scroller always visible
- Improved scroll calculation (content height - viewport height)
- Increased delay from 0ms to 10ms for layout
- Auto-scroll after every LogOutput()

### 7. Fixed Camera Controller Interference
**Files Changed:** `Assets/Game/Debug/Console/ConsoleUI.cs:39,77-81,258-261,283-286`

**Problem:** Arrow keys in console input field also moved camera

**Solution:**
- ConsoleUI finds ParadoxStyleCameraController on Start()
- ShowConsole() disables camera controller component
- HideConsole() re-enables camera controller
- Clean input isolation via component enabled/disabled pattern

**Result:** Arrow keys navigate command history when console open, move camera when closed

---

## Decisions Made

### Decision 1: EventSystem for UI Blocking vs Manual Panel.Pick()
**Context:** ProvinceSelector needed to ignore clicks on UI elements

**Options Considered:**
1. **Manual panel.Pick()** - Manual coordinate conversion and picking
2. **RuntimePanelUtils.ScreenToPanel()** - Unity's built-in conversion helper
3. **EventSystem.IsPointerOverGameObject()** - Event system detection

**Decision:** Chose EventSystem.IsPointerOverGameObject()

**Rationale:**
- Official Unity-recommended approach (confirmed via web search + Unity docs)
- Works for both uGUI and UI Toolkit automatically
- Handles all coordinate conversion internally
- Simplest implementation (one line vs 40+ lines)
- Most reliable

**Trade-offs:** Requires EventSystem in scene (already present)

**Documentation Impact:** Pattern documented in this log for future reference

### Decision 2: Component Enable/Disable for Camera Blocking
**Context:** Console arrow keys interfered with camera movement

**Options Considered:**
1. **Check console visibility in camera controller** - Add UI awareness to ENGINE
2. **Disable component when console open** - Component enabled/disabled pattern
3. **Input priority system** - Complex event handling

**Decision:** Chose disable component pattern

**Rationale:**
- Clean separation - camera controller doesn't know about console
- Simple enable/disable when showing/hiding console
- No ENGINE layer pollution with GAME layer awareness
- Reusable pattern for other input conflicts

**Trade-offs:** None significant

**Documentation Impact:** Pattern available for future UI input conflicts

---

## What Worked ✅

1. **EventSystem.IsPointerOverGameObject() for UI Blocking**
   - What: Unity's built-in pointer-over-UI detection
   - Why it worked: Handles both UI systems, all coordinate conversion automatically
   - Reusable pattern: Yes - use for all UI click blocking needs

2. **Component Enable/Disable for Input Isolation**
   - What: Disable camera controller when console is visible
   - Impact: Perfect input isolation without coupling
   - Reusable pattern: Yes - applies to any input conflicts

3. **ScrollView Layout-Aware Scrolling**
   - What: Calculate maxScroll based on content vs viewport height
   - Why it worked: Accounts for dynamic content size correctly
   - Reusable pattern: Yes - standard pattern for UI Toolkit ScrollViews

4. **Dependency Injection Through HegemonInitializer**
   - What: Debug panels receive references via Initialize() methods
   - Impact: No FindFirstObjectByType in components, clean architecture
   - Reusable pattern: Yes - standard pattern for all GAME components

---

## What Didn't Work ❌

1. **Manual panel.Pick() with Coordinate Conversion**
   - What we tried: Convert mouse position to panel coordinates, pick element
   - Why it failed: Complex coordinate math, multiple coordinate spaces, unreliable
   - Lesson learned: Unity provides EventSystem for this exact purpose
   - Don't try this again because: EventSystem is simpler, more reliable, officially supported

2. **Simple Y Flip for Coordinate Conversion**
   - What we tried: `Screen.height - mousePosition.y` without scaling
   - Why it failed: Doesn't account for panel resolution vs screen resolution
   - Lesson learned: Panel virtual resolution may differ from screen pixels
   - Don't try this again because: Need both Y flip AND scaling factor

---

## Problems Encountered & Solutions

### Problem 1: Province Selection Triggered When Clicking UI
**Symptom:** Clicking buttons on TimeDebugPanel also selected provinces underneath

**Root Cause:**
- ProvinceSelector's Update() checked Input.GetMouseButtonDown(0) without checking if pointer was over UI
- All UI elements were transparent to raycasts

**Investigation:**
- Tried: Manual panel.Pick() with coordinate conversion - too complex, unreliable
- Found: Unity docs recommend EventSystem.IsPointerOverGameObject()
- Searched: Web search confirmed this is correct approach for UI Toolkit

**Solution:**
```csharp
// In ProvinceSelector.Update()
if (Input.GetMouseButtonDown(0) && !IsPointerOverUI())
{
    // Province click handling
}

private bool IsPointerOverUI()
{
    return EventSystem.current != null &&
           EventSystem.current.IsPointerOverGameObject();
}
```

**Why This Works:** EventSystem handles UI detection for both uGUI and UI Toolkit automatically

**Pattern for Future:** Always check IsPointerOverUI() before processing world clicks

### Problem 2: Console Text Clipping at Bottom
**Symptom:** After 2 messages, only tops of letters visible - bottom half cut off

**Root Cause:**
- Labels had no padding or margins
- Height wasn't set to auto
- ScrollView layout didn't account for proper label sizing

**Investigation:**
- Inspected with UI Toolkit Debugger - labels had zero height
- Tried: Setting explicit height - broke with variable text
- Found: StyleKeyword.Auto allows proper height calculation

**Solution:**
```csharp
outputLabel.style.paddingTop = 2f;
outputLabel.style.paddingBottom = 2f;
outputLabel.style.marginBottom = 4f;
outputLabel.style.height = StyleKeyword.Auto;
```

**Why This Works:** Auto height allows label to size to content, padding prevents clipping

**Pattern for Future:** Always set height to Auto for dynamic text content in UI Toolkit

### Problem 3: Console Not Auto-Scrolling
**Symptom:** After sending command, had to manually scroll to see output

**Root Cause:**
- ScrollToBottom() executed before layout was calculated
- Used contentContainer.layout.height which was 0 at scroll time
- No delay for layout calculation

**Investigation:**
- Tried: Immediate scroll - layout.height was 0
- Tried: NextFrame() - still too early
- Found: 10ms delay allows layout calculation to complete

**Solution:**
```csharp
outputScrollView.schedule.Execute(() =>
{
    float maxScroll = Mathf.Max(0,
        outputScrollView.contentContainer.layout.height -
        outputScrollView.contentViewport.layout.height);
    outputScrollView.scrollOffset = new Vector2(0, maxScroll);
}).ExecuteLater(10);
```

**Why This Works:** 10ms delay ensures layout is calculated, maxScroll accounts for viewport size

**Pattern for Future:** Always delay scroll operations after content changes in UI Toolkit

### Problem 4: Arrow Keys Moved Camera While Typing in Console
**Symptom:** Navigating command history with up/down arrows also moved camera

**Root Cause:**
- ParadoxStyleCameraController.HandleArrowKeys() always processes arrow inputs
- Console input and camera controller both responding to same keys
- No input priority system

**Investigation:**
- Tried: Adding console awareness to camera controller - violates layer separation
- Found: Component enable/disable pattern cleanly isolates input

**Solution:**
```csharp
// In ConsoleUI
private void ShowConsole()
{
    if (cameraController != null)
    {
        cameraController.enabled = false;
    }
}

private void HideConsole()
{
    if (cameraController != null)
    {
        cameraController.enabled = true;
    }
}
```

**Why This Works:** Disabled component doesn't execute Update(), no arrow key processing

**Pattern for Future:** Use component enable/disable for clean input isolation

---

## Architecture Impact

### Documentation Updates Required
- [x] Session log created (this document)
- [ ] UI-TOOLKIT-PRINCIPLES.md - Add EventSystem pattern for click blocking
- [ ] engine-game-separation.md - Add example of debug UI placement

### New Patterns Discovered
**New Pattern: EventSystem for UI Click Blocking**
- When to use: Preventing world interaction when clicking UI
- Benefits: Works for both uGUI and UI Toolkit, handles all coordinate conversion
- Add to: UI-TOOLKIT-PRINCIPLES.md

**New Pattern: Component Enable/Disable for Input Isolation**
- When to use: Conflicting input between systems
- Benefits: Clean isolation without coupling
- Add to: ENGINE architecture docs (input handling patterns)

### New Anti-Patterns Discovered
**Anti-Pattern: Manual panel.Pick() for UI Detection**
- What not to do: Manually convert coordinates and pick elements
- Why it's bad: Complex, error-prone, EventSystem does this automatically
- Add warning to: UI-TOOLKIT-PRINCIPLES.md

### Architectural Decisions That Changed
- **Changed:** Debug UI location
- **From:** ENGINE layer (Map/Debug, Core/Systems OnGUI)
- **To:** GAME layer (Game/Debug)
- **Scope:** TimeDebugPanel, MapModeDebugPanel
- **Reason:** Debug UIs are GAME policy, not ENGINE mechanisms

---

## Code Quality Notes

### Performance
- **Measured:** Visual inspection (no frame drops)
- **Target:** UI updates should not affect gameplay performance
- **Status:** ✅ Meets target (UI Toolkit is efficient)

### Testing
- **Tests Written:** 0 (manual testing only)
- **Coverage:** All UI components manually tested in-game
- **Manual Tests:**
  - TimeDebugPanel shows correct values, buttons work
  - MapModeDebugPanel switches modes correctly
  - Country Selection UI only blocks bottom area
  - Province selection doesn't trigger on UI clicks
  - Console auto-scrolls properly
  - Arrow keys don't move camera when console is open

### Technical Debt
- **Created:** None
- **Paid Down:** Removed all OnGUI debug UIs (legacy code)
- **TODOs:** None critical

---

## Next Session

### Immediate Next Steps (Priority Order)
1. Test console commands extensively in play mode
2. Verify all UI positioning at different resolutions
3. Consider adding more debug commands to console

### Blocked Items
None

### Questions to Resolve
1. Should we add USS stylesheets for consistent debug UI theming?
2. Do we need keyboard shortcuts for map mode switching?
3. Should console persist command history between sessions?

### Docs to Read Before Next Session
- UI Toolkit event system documentation
- Input handling best practices in Unity

---

## Session Statistics

**Duration:** ~2 hours
**Files Changed:** 8
**Lines Added/Removed:** +809/-322
**Tests Added:** 0
**Bugs Fixed:** 5 (Country Selection blocking, province click-through, console clipping, console scroll, camera interference)
**Commits:** 4 (2 in Archon-Engine, 2 in Hegemon)

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- All debug UIs now in GAME layer using UI Toolkit
- EventSystem.IsPointerOverGameObject() is the correct way to block UI clicks
- Component enable/disable pattern used for input isolation
- Console auto-scrolls, properly styled, and disables camera when active

**What Changed Since Last Doc Read:**
- Architecture: Debug UIs moved from ENGINE to GAME layer
- Implementation: OnGUI completely removed, replaced with UI Toolkit
- Constraints: Must use EventSystem for UI click detection

**Gotchas for Next Session:**
- Watch out for: UI Toolkit layout timing (need delays for scroll operations)
- Don't forget: EventSystem required in scene for UI click blocking
- Remember: Component enable/disable is cleaner than input priority systems

---

## Links & References

### Related Documentation
- [UI-TOOLKIT-PRINCIPLES.md](../../Engine/ui-architecture.md)
- [engine-game-separation.md](../../Engine/engine-game-separation.md)

### Related Sessions
- [Previous: UI Toolkit Migration](1-ui-toolkit-migration.md)

### External Resources
- [Unity UI Toolkit FAQ - Input and Events](https://docs.unity3d.com/Manual/UIE-faq-event-and-input-system.html)
- [Unity EventSystem Documentation](https://docs.unity3d.com/Manual/EventSystem.html)
- [Stack Overflow: Unity UI Toolkit Click Through](https://stackoverflow.com/questions/78211570/how-to-ignore-mouse-input-with-unitys-new-input-system-while-clicking-on-an-ui)

### Code References
- EventSystem pattern: `Assets/Archon-Engine/Scripts/Map/Interaction/ProvinceSelector.cs:110-115`
- ScrollView auto-scroll: `Assets/Game/Debug/Console/ConsoleUI.cs:395-406`
- Component disable pattern: `Assets/Game/Debug/Console/ConsoleUI.cs:258-261,283-286`
- TimeDebugPanel: `Assets/Game/Debug/TimeDebugPanel.cs:1-231`
- MapModeDebugPanel: `Assets/Game/Debug/MapModeDebugPanel.cs:1-256`

---

## Notes & Observations

- EventSystem.IsPointerOverGameObject() is criminally underutilized - should be used everywhere for UI click blocking
- UI Toolkit's layout timing requires careful consideration - can't scroll immediately after adding content
- Component enable/disable pattern is extremely clean for input isolation
- UI Toolkit Debugger is invaluable for diagnosing layout issues
- The ENGINE/GAME separation for debug UIs makes perfect sense - debug UIs are policy decisions
- Console is now production-ready for playtesting and debugging
- All UI migrations from this and previous session complete - project now 100% UI Toolkit

---

*Session completed 2025-10-18 - All debug UIs migrated to UI Toolkit, console polished, UI click blocking fixed*
