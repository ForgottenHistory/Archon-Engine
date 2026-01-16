# UI Infrastructure & Panel Refactor
**Date**: 2026-01-16
**Session**: 07
**Status**: ✅ Complete
**Priority**: Medium

---

## Session Goal

**Primary Objective:**
- Reduce UI boilerplate across StarterKit panels

**Secondary Objectives:**
- Create reusable UI infrastructure in ENGINE
- Standardize styling across all panels
- Fix bugs discovered during refactoring

**Success Criteria:**
- All StarterKit UI panels use new base class
- Consistent styling (colors, spacing, fonts)
- Reduced code duplication

---

## Context & Background

**Previous Work:**
- See: [06-event-driven-ui-and-toolbar.md](06-event-driven-ui-and-toolbar.md)
- StarterKit UI had ~2,900 lines across 9 panels
- Each panel duplicated: UIDocument setup, styling, Show/Hide, EventBus subscriptions

**Current State:**
- UI code had 12-18% style boilerplate
- No shared base class for panels
- Manual CompositeDisposable management in each panel

**Why Now:**
- User noticed heavy UI folder after session 06
- Opportunity to establish patterns before more UI is written

---

## What We Did

### 1. Created UIHelper in ENGINE
**Files Changed:** `Core/UI/UIHelper.cs` (new, 287 lines)

Static helper methods for UI Toolkit styling:
- `SetPadding()`, `SetMargin()`, `SetBorderRadius()`, `SetBorderWidth()`
- `SetAbsolutePosition()`, `SetFlexRow()`, `SetFlexColumn()`
- `CreatePanel()`, `CreateLabel()`, `CreateButton()`, `CreateColorIndicator()`
- `Show()`, `Hide()`, `SetVisible()`, `AddHoverEffect()`

### 2. Created BasePanel in ENGINE
**Files Changed:** `Core/UI/BasePanel.cs` (new, 158 lines)

Abstract base class for UI panels:
- Handles UIDocument and rootElement setup
- Manages panelContainer lifecycle
- Provides Show/Hide/Toggle with OnShow/OnHide hooks
- Helper methods: `CreatePanelContainer()`, `PositionPanel()`, `CenterPanel()`

### 3. Created starterkit.uss Stylesheet
**Files Changed:** `StarterKit/UI/Styles/starterkit.uss` (new, 277 lines)

USS stylesheet with CSS variables:
- Colors: `--bg-panel`, `--text-primary`, `--text-gold`, etc.
- Spacing: `--spacing-xs` through `--spacing-lg`
- Typography: `--font-size-normal`, `--font-size-header`, etc.
- Utility classes: `.panel`, `.button`, `.row`, `.flex-row`, `.mt-sm`, etc.

### 4. Created StarterKitPanel Base Class
**Files Changed:** `StarterKit/UI/StarterKitPanel.cs` (new, 189 lines)

StarterKit-specific base extending BasePanel:
- Styling constants matching USS variables
- GameState reference and EventBus integration
- `Subscribe<T>()` helper for auto-disposed subscriptions
- Helper methods: `CreateStyledPanel()`, `CreateHeader()`, `CreateRow()`, `CreateStyledButton()`, etc.

### 5. Refactored All StarterKit UI Panels
**Files Changed:** 8 UI files

| Panel | Before | After | Reduction |
|-------|--------|-------|-----------|
| ToolbarUI | 197 | 109 | 45% |
| LedgerUI | 457 | 340 | 26% |
| ResourceBarUI | 212 | 137 | 35% |
| TimeUI | 241 | 148 | 39% |
| CountrySelectionUI | 284 | 225 | 21% |
| ProvinceInfoUI | 557 | 401 | 28% |
| BuildingInfoUI | 304 | 287 | 6% |
| UnitInfoUI | 453 | 515 | +14%* |

*UnitInfoUI grew due to added ProvinceOwnershipChangedEvent subscription (bug fix).

### 6. Bug Fixes During Refactor
**Files Changed:** Multiple

1. **UnitInfoUI missing ownership event** - Added `ProvinceOwnershipChangedEvent` subscription so "Create Unit" button appears after colonizing
2. **Button text color** - Added `button.style.color = TextPrimary` to `CreateStyledButton()`
3. **Initializer method calls** - Updated `timeUI.Initialize()` signature and `ledgerUI.Show()` call

---

## Decisions Made

### Decision 1: Three-Layer UI Infrastructure
**Context:** How to reduce boilerplate without over-engineering

**Decision:** Three separate components
1. UIHelper (ENGINE) - Static utilities, no state
2. BasePanel (ENGINE) - Abstract base, reusable across games
3. StarterKitPanel (StarterKit) - Game-specific styling and EventBus

**Rationale:**
- UIHelper useful for any UI, even non-panels
- BasePanel could be used by other games on Archon-Engine
- StarterKitPanel has StarterKit-specific colors/patterns

### Decision 2: Constants vs USS
**Context:** Should panels use USS classes or C# constants?

**Decision:** Both - C# constants in StarterKitPanel that match USS variables
**Rationale:**
- Programmatic UI creation needs C# values
- USS available for UXML-based UI if needed later
- Single source of truth in StarterKitPanel, mirrored in USS

---

## What Worked ✅

1. **Inheritance for Common Setup**
   - BasePanel handles UIDocument boilerplate
   - OnDestroy cleanup automatic via base class

2. **Subscribe<T>() Helper**
   - One-liner for EventBus subscriptions
   - Auto-disposed, no manual cleanup needed

3. **Helper Methods for Common Elements**
   - `CreateRow()`, `CreateRowEntry()`, `CreateStyledButton()`
   - Consistent styling without repetition

---

## Problems Encountered & Solutions

### Problem 1: Button Text Color Dark Gray
**Symptom:** Buttons had dark gray text instead of white
**Root Cause:** `UIHelper.CreateButton()` didn't set text color
**Solution:** Added `button.style.color = TextPrimary` in `CreateStyledButton()`

### Problem 2: Missing Ownership Event in UnitInfoUI
**Symptom:** "Create Unit" button didn't appear after buying land
**Root Cause:** UnitInfoUI didn't subscribe to `ProvinceOwnershipChangedEvent`
**Solution:** Added subscription and handler to refresh on ownership change

---

## Architecture Impact

### New Pattern: StarterKit UI Panel Architecture
```
Core/UI/
├── UIHelper.cs         # Static styling utilities (ENGINE)
└── BasePanel.cs        # Abstract panel base (ENGINE)

StarterKit/UI/
├── Styles/
│   └── starterkit.uss  # USS stylesheet
├── StarterKitPanel.cs  # Base with EventBus + styling
└── [Panel]UI.cs        # Concrete panels
```

**When to use:**
- New StarterKit panels should extend `StarterKitPanel`
- Use `Subscribe<T>()` for EventBus events
- Use helper methods for consistent styling

---

## Session Statistics

**Files Created:** 4
- `Core/UI/UIHelper.cs` (287 lines)
- `Core/UI/BasePanel.cs` (158 lines)
- `StarterKit/UI/Styles/starterkit.uss` (277 lines)
- `StarterKit/UI/StarterKitPanel.cs` (189 lines)

**Files Refactored:** 8 UI panels + Initializer.cs

**Total Line Reduction:** ~400 lines across panels (average 25% reduction)

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- All StarterKit UI panels extend `StarterKitPanel`
- Use `Subscribe<T>(handler)` for EventBus - auto-disposed
- Use `CreateStyledButton()`, `CreateRow()`, `CreateHeader()` etc.
- Styling constants in StarterKitPanel match USS variables

**Key Files:**
- ENGINE base: `Core/UI/BasePanel.cs`, `Core/UI/UIHelper.cs`
- StarterKit base: `StarterKit/UI/StarterKitPanel.cs`
- Example panel: `StarterKit/UI/ToolbarUI.cs` (simplest)

**Pattern for New Panels:**
```csharp
public class NewUI : StarterKitPanel
{
    public void Initialize(GameState gameStateRef, ...)
    {
        // Store references first
        if (!base.Initialize(gameStateRef)) return;

        // Subscribe to events
        Subscribe<SomeEvent>(HandleEvent);

        Hide(); // Usually hidden initially
    }

    protected override void CreateUI()
    {
        panelContainer = CreateStyledPanel("panel-name");
        PositionPanel(bottom: 10f, left: 10f);
        // Add elements...
    }

    protected override void OnDestroy()
    {
        // Unsubscribe C# events if any
        base.OnDestroy(); // Disposes EventBus subscriptions
    }
}
```

---

## Links & References

### Code References
- UIHelper: `Core/UI/UIHelper.cs`
- BasePanel: `Core/UI/BasePanel.cs`
- StarterKitPanel: `StarterKit/UI/StarterKitPanel.cs`
- USS stylesheet: `StarterKit/UI/Styles/starterkit.uss`

### Related Sessions
- Previous: [06-event-driven-ui-and-toolbar.md](06-event-driven-ui-and-toolbar.md)

---

*Session Duration: ~30 minutes*
