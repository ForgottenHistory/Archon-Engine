# Event-Driven UI Updates & Toolbar
**Date**: 2026-01-16
**Session**: 06
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Implement event-driven UI updates so buttons enable/disable reactively

**Secondary Objectives:**
- Add save/load support to StarterKit
- Create toolbar with Ledger, Save, Load buttons
- Organize StarterKit folder structure

**Success Criteria:**
- Building/Unit buttons update when gold changes (no polling)
- Buy Land button updates on gold/ownership changes
- Ledger updates when visible and data changes
- Toolbar provides quick access to common actions

---

## Context & Background

**Previous Work:**
- See: [05-ledger-ui.md](05-ledger-ui.md)
- Save/Load existed in ENGINE (SaveManager with F6/F7)
- UI buttons didn't update when game state changed

**Current State:**
- ENGINE has EventBus for cross-system events
- StarterKit UI polled or only updated on click

**Why Now:**
- UI not updating is critical UX bug for grand strategy games
- Save/Load essential for testing and gameplay
- StarterKit should demonstrate best practices

---

## What We Did

### 1. Created StarterKit Events
**Files Changed:** `StarterKit/State/StarterKitEvents.cs` (new)

```csharp
public struct GoldChangedEvent : IGameEvent
{
    public ushort CountryId;
    public int OldValue;
    public int NewValue;
    public float TimeStamp { get; set; }
}

public struct BuildingConstructedEvent : IGameEvent
{
    public ushort ProvinceId;
    public ushort BuildingTypeId;
    public ushort CountryId;
    public float TimeStamp { get; set; }
}
```

### 2. Updated EconomySystem to Emit Events
**Files Changed:** `StarterKit/Systems/EconomySystem.cs`

Added `EmitGoldChanged()` helper called from:
- `CollectIncomeForCountry()` - monthly income
- `AddGoldToCountry()` - cheats/rewards
- `RemoveGoldFromCountry()` - purchases

```csharp
private void EmitGoldChanged(ushort countryId, int oldGold, int newGold)
{
    gameState.EventBus.Emit(new GoldChangedEvent
    {
        CountryId = countryId,
        OldValue = oldGold,
        NewValue = newGold
    });

    // Backward compatibility for C# event subscribers
    if (countryId == playerState.PlayerCountryId)
        OnGoldChanged?.Invoke(oldGold, newGold);
}
```

### 3. Updated UI Components to Subscribe
**Files Changed:**
- `StarterKit/UI/BuildingInfoUI.cs`
- `StarterKit/UI/UnitInfoUI.cs`
- `StarterKit/UI/ProvinceInfoUI.cs`
- `StarterKit/UI/LedgerUI.cs`

**Pattern applied to all:**
```csharp
// In Initialize()
subscriptions = new CompositeDisposable();
subscriptions.Add(gameState.EventBus.Subscribe<GoldChangedEvent>(HandleGoldChanged));

// Handler - only refresh if visible and relevant
private void HandleGoldChanged(GoldChangedEvent evt)
{
    if (selectedProvinceID == 0) return; // Panel not visible
    if (evt.CountryId != playerState.PlayerCountryId) return; // Not player
    RefreshBuildButtons(); // Minimal refresh
}

// In OnDestroy()
subscriptions?.Dispose();
```

**Event subscriptions by UI:**
| UI | Events |
|----|--------|
| BuildingInfoUI | GoldChangedEvent |
| UnitInfoUI | GoldChangedEvent |
| ProvinceInfoUI | GoldChangedEvent, ProvinceOwnershipChangedEvent |
| LedgerUI | GoldChangedEvent, ProvinceOwnershipChangedEvent, UnitCreatedEvent, UnitDestroyedEvent |

### 4. Added Save/Load to StarterKit
**Files Changed:** `StarterKit/Initializer.cs`

Hooked SaveManager callbacks:
- `OnSerializePlayerState` - saves PlayerState, EconomySystem, BuildingSystem
- `OnDeserializePlayerState` - restores all
- `OnPostLoadFinalize` - refreshes map visuals and UI

### 5. Created ToolbarUI
**Files Changed:** `StarterKit/UI/ToolbarUI.cs` (new)

Top-right toolbar with:
- Ledger (L) - toggles LedgerUI
- Save (F6) - calls SaveManager.QuickSave()
- Load (F7) - calls SaveManager.QuickLoad()

Hidden until player selects country.

### 6. Organized Folder Structure
**Folders Created:**
- `StarterKit/Systems/` - AISystem, BuildingSystem, EconomySystem, UnitSystem
- `StarterKit/UI/` - All UI components
- `StarterKit/State/` - PlayerState, PlayerEvents, StarterKitEvents
- `StarterKit/Visualization/` - UnitVisualization

Final structure:
```
StarterKit/
├── Initializer.cs
├── Commands/
├── State/
├── Systems/
├── UI/
└── Visualization/
```

### 7. Updated README
**Files Changed:** `StarterKit/README.md`

Added:
- Folder structure section
- Event-driven UI pattern with code example
- Save/Load section
- "Add New UI Panel" guide

---

## Decisions Made

### Decision 1: EventBus vs C# Events for UI
**Context:** C# events already existed (e.g., `OnGoldChanged`)

**Decision:** Use EventBus as primary, keep C# events for backward compatibility
**Rationale:**
- EventBus is the ENGINE pattern (Pattern 3)
- Zero-allocation struct events
- Consistent with other systems
- C# events kept for simple cases

### Decision 2: Granular vs Full Refresh
**Context:** When gold changes, refresh entire panel or just buttons?

**Decision:** Minimal refresh (just affected elements)
**Rationale:**
- `UpdateColonizeButton()` vs `UpdatePanel()`
- `RefreshBuildButtons()` vs `RefreshBuildingsList()`
- Better performance, less flicker

### Decision 3: Visibility Check in Handlers
**Context:** Should hidden panels respond to events?

**Decision:** Check visibility before refreshing
**Rationale:**
- `if (selectedProvinceID == 0) return;`
- `if (!isVisible) return;`
- Avoids wasted work on hidden UI
- Panel refreshes on show anyway

---

## What Worked ✅

1. **Event-Driven Pattern**
   - Clean separation: emit events in systems, subscribe in UI
   - CompositeDisposable for cleanup
   - Reusable pattern for all UI

2. **Minimal Refresh**
   - Only refresh what changed
   - Only refresh if visible
   - No polling in Update()

---

## Architecture Impact

### New Pattern: Event-Driven UI Updates
```csharp
// 1. Subscribe in Initialize()
subscriptions.Add(gameState.EventBus.Subscribe<EventType>(Handler));

// 2. Check visibility and relevance
if (!isVisible || evt.CountryId != playerCountryId) return;

// 3. Minimal refresh
UpdateSpecificElement();

// 4. Dispose in OnDestroy()
subscriptions?.Dispose();
```

**Add to:** StarterKit README (done), consider adding to CLAUDE.md Pattern 3

---

## Session Statistics

**Files Changed:** 10
- New: StarterKitEvents.cs, ToolbarUI.cs
- Modified: EconomySystem, BuildingSystem, BuildingInfoUI, UnitInfoUI, ProvinceInfoUI, LedgerUI, Initializer, README

**Files Moved:** 18 (folder reorganization)

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- Event-driven UI pattern: subscribe → check visibility → minimal refresh → dispose
- GoldChangedEvent emitted from EconomySystem on any gold change
- ProvinceOwnershipChangedEvent already exists in Core (ProvinceEvents.cs)
- UnitCreatedEvent/UnitDestroyedEvent in Core (UnitEvents.cs)

**StarterKit Folder Structure:**
- Systems/ - game systems
- UI/ - all UI components
- State/ - player state and events
- Visualization/ - visual rendering
- Commands/ - console commands

**Key Files:**
- Event definitions: `StarterKit/State/StarterKitEvents.cs`
- UI pattern example: `StarterKit/UI/BuildingInfoUI.cs:78-93`
- Toolbar: `StarterKit/UI/ToolbarUI.cs`

---

## Links & References

### Code References
- StarterKit events: `StarterKit/State/StarterKitEvents.cs`
- Event emission: `StarterKit/Systems/EconomySystem.cs:236-251`
- UI subscription pattern: `StarterKit/UI/BuildingInfoUI.cs:78-93`
- Core province events: `Core/Systems/Province/ProvinceEvents.cs`
- Core unit events: `Core/Units/UnitEvents.cs`

### Related Sessions
- Previous: [05-ledger-ui.md](05-ledger-ui.md)

---

*Session Duration: ~45 minutes*
