# UI Toolkit Principles

**Status**: Production Standard
**Last Updated**: 2025-10-18
**Applies To**: All runtime UI in Archon-based games

## Philosophy

UI Toolkit is Unity's modern UI system, replacing the legacy uGUI (Canvas/RectTransform) system. It uses retained-mode rendering, CSS-like styling, and flexbox-inspired layout - fundamentally better for complex UIs at scale.

## Why UI Toolkit Over uGUI

**Performance**:
- Retained-mode rendering (batches updates efficiently)
- Better for complex UIs with many elements
- Lower overhead for show/hide operations

**Developer Experience**:
- No RectTransform pivot/anchor hell
- Simple absolute/relative positioning via style properties
- Programmatic UI creation scales better than prefab nesting
- CSS-like styling with clear separation of concerns

**Future-Proofing**:
- Unity's recommended path for all new projects
- Active development and improvements
- Better tooling support (UI Builder, debugger)

## Core Architecture

### Component Structure

Every UI Toolkit UI requires:
1. **UIDocument component** - Hosts the UI on a GameObject
2. **PanelSettings asset** - Configuration for resolution, scaling, DPI
3. **VisualElement hierarchy** - The actual UI elements (created programmatically or via UXML)

### Programmatic vs UXML

**Use Programmatic Creation When**:
- UI is simple (panels, labels, buttons)
- Styling needs to be Inspector-editable
- Dynamic content based on game state
- Tight coupling to game logic

**Use UXML When**:
- Complex nested structures
- Designer-driven workflows
- Reusable UI templates
- Minimal runtime modification

**Archon Standard**: Prefer programmatic creation for game UIs. Keeps everything in code, easier to modify, better for Inspector customization.

## Coordinate Systems

### Critical Understanding

**Input.mousePosition**:
- Bottom-left origin (Y=0 at bottom)
- Unity's standard input coordinate system

**UI Toolkit VisualElements**:
- Top-left origin (Y=0 at top)
- Like web browsers and most UI systems

### Screen-to-Panel Conversion

When positioning UI based on mouse/screen coordinates:

1. **Flip Y coordinate**: `Screen.height - mousePosition.y`
2. **Scale to panel resolution**: Multiply by `(panelSize / screenSize)` ratio
3. **Use RuntimePanelUtils** if available, but verify it handles origin correctly

**Gotcha**: Panel size may differ from screen size due to scaling modes (Scale With Screen Size, Constant Physical Size, etc.)

## Positioning Patterns

### Absolute Positioning

For elements that need fixed screen positions:
- Set `position = Position.Absolute`
- Set `left`, `right`, `top`, `bottom` as needed
- Values in pixels or percentages

**Use Cases**:
- Corner-anchored panels (province info, country info)
- Fullscreen overlays (loading screen, country selection)
- Tooltips following mouse cursor

### Flexbox Layout

For centered or flow-based layouts:
- Use `flexDirection` (Row/Column)
- Use `alignItems` and `justifyContent`
- Let the system handle positioning

**Use Cases**:
- Centered content (loading screen text + button)
- Lists and grids
- Responsive layouts

### Hybrid Approach

Common pattern: Absolute-positioned container with flexbox content inside.

## Visibility Management

### Show/Hide Pattern

**NEVER use GameObject.SetActive()** for UI Toolkit elements. The GameObject should stay active.

**Use DisplayStyle instead**:
- Show: `element.style.display = DisplayStyle.Flex`
- Hide: `element.style.display = DisplayStyle.None`

**Why**: UI Toolkit elements exist in the visual tree, not as GameObjects. SetActive() breaks the UI system's update loop.

### Opacity for Fades

For fade animations:
- Use `element.style.opacity` (0.0 to 1.0)
- Animate in Update() or coroutines
- Element still receives input when opacity = 0 (use DisplayStyle.None if you want to block input)

## Styling Strategy

### Inline Styles (Programmatic)

Set styles directly on elements in code:
- Immediate, clear intent
- Inspector-editable via serialized fields
- No external files to manage

**Best for**: Game-specific UIs with dynamic styling needs

### USS Stylesheets (External)

CSS-like files for reusable styles:
- Global theming
- Consistent look across UIs
- Designer-friendly

**Best for**: Large-scale UI systems with many screens

**Archon Standard**: Start with inline styles. Add USS only when you need global theming.

## Color Handling

**Color32 vs Color**:
- UI Toolkit styles use `StyleColor`, which accepts `Color` but not `Color32`
- Explicit cast required: `(Color)color32Value`

**Why**: StyleColor is Unity's wrapper type for optional CSS-like color values.

## Progress Bars

**uGUI way**: Image.fillAmount (0-1 range)
**UI Toolkit way**: VisualElement.style.width (percentage or pixels)

**Pattern**:
- Background element (full width)
- Fill element inside (dynamic width)
- Animate width from 0% to 100%

## Button Handling

**uGUI way**: Button.onClick.AddListener()
**UI Toolkit way**: Button.clicked += callback OR new Button(callback)

**Enable/Disable**:
- uGUI: `button.interactable = false`
- UI Toolkit: `button.SetEnabled(false)`

## Panel Settings Sharing

**One PanelSettings asset can be shared** across multiple UIDocuments:
- Same resolution/scaling behavior
- Reduces asset clutter
- Consistent look across UIs

**When to create separate PanelSettings**:
- Different render orders (overlays vs world-space)
- Different scaling modes
- Different target displays

## Performance Considerations

### Dirty Flagging

UI Toolkit automatically batches updates. Don't worry about calling style setters every frame - it's smart about what actually changed.

### Layout Recalculation

Setting style properties triggers layout. For smooth animations:
- Animate properties that don't affect layout (opacity, scale)
- Or accept the layout cost for properties that do (width, height, margins)

### Draw Calls

UI Toolkit batches aggressively. Multiple UIDocuments with the same PanelSettings = single draw call in most cases.

## Common Pitfalls

### Pitfall: Using gameObject.SetActive() for visibility
**Solution**: Use `element.style.display`

### Pitfall: Assuming screen coordinates = panel coordinates
**Solution**: Account for scaling and coordinate origin differences

### Pitfall: Forgetting to initialize UI before using it
**Solution**: Call InitializeUI() in Awake(), use lazy initialization patterns

### Pitfall: Setting styles before elements are added to hierarchy
**Solution**: Add elements to parent first, then set styles (or set styles before adding - both work)

### Pitfall: Mixing uGUI and UI Toolkit on same GameObject
**Solution**: Don't. Pick one system per GameObject.

## Mutual Exclusion Pattern

When multiple panels should never be visible simultaneously:

**Old uGUI way** (WRONG):
```
// DON'T DO THIS
otherPanel.gameObject.SetActive(false);
```

**UI Toolkit way** (CORRECT):
```
// Expose public Show/Hide methods
otherPanel.HidePanel();
```

**Why**: Calling methods is clearer, testable, and respects encapsulation.

## Testing Strategy

### Context Menu Helpers

Add `[ContextMenu("Test - Show Panel")]` methods for quick testing in editor.

### Runtime Logs

Log initialization, show/hide operations during development. Remove or gate behind debug flags for production.

### Visual Debugging

UI Toolkit Debugger (Window → UI Toolkit → Debugger) shows:
- Element hierarchy
- Computed styles
- Layout boxes
- Event propagation

## Migration from uGUI Checklist

When converting existing uGUI:

1. **Remove old components**: Canvas, CanvasGroup, Image, TextMeshProUGUI, Button
2. **Add UIDocument**: Assign PanelSettings, leave Source Asset empty for programmatic
3. **Rewrite UI creation**: Labels instead of TextMeshProUGUI, VisualElements instead of Images
4. **Fix visibility**: gameObject.SetActive() → element.style.display
5. **Fix colors**: Color32 casts to Color where needed
6. **Fix buttons**: onClick.AddListener() → clicked += or new Button(callback)
7. **Fix positioning**: RectTransform → style.position/left/top/etc
8. **Test thoroughly**: Coordinate systems often need adjustment

## Best Practices Summary

✅ **DO**:
- Use programmatic creation for game UIs
- Share PanelSettings across UIDocuments when possible
- Use DisplayStyle for show/hide
- Expose styling options in Inspector via SerializedFields
- Use absolute positioning for fixed-position panels
- Use flexbox for centered/responsive layouts
- Log initialization and state changes during development
- Add Context Menu helpers for testing

❌ **DON'T**:
- Use gameObject.SetActive() for UI Toolkit visibility
- Assume screen coordinates = panel coordinates
- Mix uGUI and UI Toolkit on same GameObject
- Over-engineer with USS files unless you need global theming
- Forget to handle coordinate system differences
- Ignore the UI Toolkit Debugger when things go wrong

## Resources

**Unity Docs**:
- UI Toolkit Manual (comprehensive official docs)
- UI Toolkit API Reference (all classes and properties)
- UI Builder Guide (visual editor)

**Internal References**:
- `Assets/Game/UI/TooltipUI.cs` - Mouse-following tooltip with smart positioning
- `Assets/Game/UI/ProvinceInfoPanel.cs` - Corner-anchored panel with color indicators
- `Assets/Game/UI/LoadingScreenUI.cs` - Fullscreen overlay with progress bar
- `Assets/Game/UI/CountrySelectionUI.cs` - Fullscreen overlay with button

## UI Presenter Pattern for Complex Panels

**Status**: Production Standard (2025-10-25)
**Use For**: Interactive panels with actions, events, and complex display logic
**Decision Doc**: `Docs/Log/decisions/ui-presenter-pattern-for-panels.md`

### When to Use

**Use UI Presenter Pattern When**:
- Panel exceeds 500 lines
- Multiple user actions (3+ button handlers)
- Complex display logic (conditional visibility, formatting)
- Event-driven updates (monthly tick, resource changes)
- Need testability (separate logic from UI)

**Don't Use When**:
- Simple display panels (<200 lines)
- Read-only information
- Single action or no actions
- Temporary/debug UI

### Architecture

**Four-Component Structure** (simple panels):

1. **View (MonoBehaviour)** - Pure view coordination
   - UI Toolkit element creation (~150 lines inline)
   - Show/hide panel
   - Route button clicks to handler
   - Delegate display updates to presenter
   - Manage event subscriber lifecycle

2. **Presenter (Static Class)** - Stateless presentation logic
   - Query game state for display data
   - Format data (strings, colors, visibility)
   - Determine button states
   - Update UI element properties
   - All methods static (no state)

3. **ActionHandler (Static Class)** - Stateless user actions
   - Validate user actions
   - Execute commands
   - Return success/failure
   - All methods static (no state)

4. **EventSubscriber (Class)** - Event lifecycle management
   - Subscribe to all events
   - Unsubscribe on destroy
   - Route events to callbacks
   - Owned by view

**Five-Component Structure** (complex panels with >150 lines UI creation):

1. **View (MonoBehaviour)** - Pure view coordination
   - Minimal UI initialization (~40 lines delegates to UIBuilder)
   - Show/hide panel
   - Route button clicks to handler
   - Delegate display updates to presenter
   - Manage event subscriber lifecycle

2-4. **Presenter/Handler/Subscriber** - Same as 4-component

5. **UIBuilder (Static Class)** - UI element creation
   - Static BuildUI() method returns UIElements container
   - Creates all UI Toolkit elements programmatically
   - Applies styling from StyleConfig struct
   - Wires up button callbacks
   - Keeps view clean and focused
   - Use when UI creation exceeds 150 lines

### Benefits

**Scalability**:
- View remains stable (~200 lines for UI creation)
- Presenter grows linearly with display complexity
- Handler grows linearly with action complexity
- Grand strategy games have tons of UI - pattern scales

**Testability**:
- Presenter: Pass data, verify formatted output
- Handler: Mock dependencies, verify commands
- View: Test UI creation separately
- Stateless components trivial to test

**Maintainability**:
- Each file has single responsibility
- Clear separation: display vs actions vs events
- Easy to find logic (focused files)
- Pattern consistency across all panels

### Pattern vs Facade

**UI Presenter (for panels)**:
- View delegates to stateless helpers
- MonoBehaviour view owns UI state
- Focused on display and user interaction

**Facade (for systems)**:
- Facade owns runtime state
- Delegates operations to managers
- Focused on gameplay logic

**Key Difference**: UI is stateless helpers, Systems have stateful facades

### Examples

**Production Implementation (4 components)**:
- `Assets/Game/UI/ProvinceInfoPanel.cs` (553 lines) - View
- `Assets/Game/UI/ProvinceInfoPresenter.cs` (304 lines) - Presenter
- `Assets/Game/UI/ProvinceActionHandler.cs` (296 lines) - Handler
- `Assets/Game/UI/ProvinceEventSubscriber.cs` (116 lines) - Subscriber

**Production Implementation (5 components)**:
- `Assets/Game/UI/CountryInfoPanel.cs` (503 lines) - View
- `Assets/Game/UI/CountryInfoPresenter.cs` (258 lines) - Presenter
- `Assets/Game/UI/CountryActionHandler.cs` (147 lines) - Handler
- `Assets/Game/UI/CountryEventSubscriber.cs` (186 lines) - Subscriber
- `Assets/Game/UI/CountryUIBuilder.cs` (217 lines) - UIBuilder

**Future Candidates**:
- DiplomacyPanel (when implemented) → 5-component pattern
- Any interactive panel >500 lines → Use UI Presenter pattern
- Any panel with >150 lines UI creation → Consider UIBuilder (5-component)

## Future Considerations

### When to Consider USS Stylesheets

If you find yourself:
- Copy-pasting style code across multiple UIs
- Wanting global color/font changes
- Working with designers who prefer visual tools
- Building a UI system with dozens of screens

### When to Consider UXML Templates

If you need:
- Complex nested layouts that are hard to read in code
- Designer-driven UI workflows
- Heavy reuse of UI patterns
- Separation between structure and logic

---

*This document is intentionally timeless - no code examples, just principles. Code evolves, principles endure.*
