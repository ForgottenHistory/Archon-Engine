# StarterKit Diplomacy UI Integration
**Date**: 2026-01-17
**Session**: 05
**Status**: ✅ Complete
**Priority**: Medium

---

## Session Goal

**Primary Objective:**
- Demonstrate ENGINE diplomacy system in StarterKit with functional UI

**Secondary Objectives:**
- Add all treaty actions (Alliance, NAP, Guarantee, Military Access)
- Add Send Gift feature showcasing opinion modifiers with time decay
- Fix button styling consistency across StarterKit panels

**Success Criteria:**
- DiplomacyPanel shows opinion, war status, treaties
- Can declare war, make peace, form/break treaties
- Send Gift costs gold and adds decaying opinion modifier
- Proper button enable/disable based on diplomatic state

---

## Context & Background

**Previous Work:**
- See: [04-terrain-pathfinding-mapmode.md](04-terrain-pathfinding-mapmode.md)
- ENGINE DiplomacySystem already implemented with full API

**Current State:**
- DiplomacySystem existed with opinion, war, treaty management
- No UI to interact with diplomacy features
- DiplomacySystem.Initialize() was never called in GameState

**Why Now:**
- StarterKit needs to demonstrate all ENGINE features
- Diplomacy is core grand strategy feature

---

## What We Did

### 1. Created Planning Document
**Files Changed:** `Docs/Planning/starterkit-diplomacy.md`

Outlined phases:
1. DiplomacyPanel UI (completed this session)
2. War indicators on map (future)
3. Notification system (future)

### 2. Fixed DiplomacySystem Initialization
**Files Changed:** `Core/GameState.cs:203, 334`

**Bug:** DiplomacySystem was created via `AddComponent` but `Initialize()` was never called, so NativeCollections were null.

**Fix:**
```csharp
// Line 203 - Initialize
Diplomacy.Initialize();

// Line 334 - Cleanup
Diplomacy?.Shutdown();
```

### 3. Created DiplomacyPanel
**Files Changed:** `StarterKit/UI/DiplomacyPanel.cs` (NEW - ~560 lines)

Full diplomacy UI panel showing:
- Country name header (using localization lookup)
- Opinion value with color gradient (red -100 to green +100)
- Opinion bar visualization
- War/Peace status
- Treaty status display (Allied, NAP, Guaranteeing, etc.)

**Action Buttons:**
- Declare War / Make Peace
- Form/Break Alliance
- Form/Break NAP
- Guarantee / Revoke Guarantee
- Grant/Revoke Military Access
- Send Gift (10g for +50 opinion, decays over time)

**Key Implementation:**
```csharp
// Opinion color gradient
private Color GetOpinionColor(int opinion)
{
    if (opinion < 0)
    {
        float t = (opinion + 100f) / 100f;
        return Color.Lerp(OpinionHostile, OpinionNeutral, t);
    }
    else
    {
        float t = opinion / 100f;
        return Color.Lerp(OpinionNeutral, OpinionFriendly, t);
    }
}
```

### 4. Button Enable/Disable Logic

**War declaration blocked when:**
- Allied with target
- Have NAP with target

**Treaty formation blocked when:**
- At war with target (can still break existing treaties)

**Declaring war automatically:**
- Revokes guarantee if guaranteeing target
- Revokes military access if granted to target

### 5. Send Gift Feature
**Files Changed:** `StarterKit/UI/DiplomacyPanel.cs:498-547`

Showcases ENGINE opinion modifier system:
```csharp
var giftModifier = new OpinionModifier
{
    modifierTypeID = 1,
    value = FixedPoint64.FromInt(GiftOpinionBonus), // +50
    appliedTick = currentTick,
    decayRate = GiftOpinionBonus * 30 // ~1 per month decay
};
diplomacy.AddOpinionModifier(targetCountryId, playerId, giftModifier, currentTick);
```

- Costs 10 gold
- Adds +50 opinion
- Decays ~1 point per month (full decay after ~50 months)
- Button disabled when can't afford
- Subscribes to GoldChangedEvent for real-time updates

### 6. Wired Up in Initializer
**Files Changed:** `StarterKit/Initializer.cs:300-307`

```csharp
diplomacyPanel.Initialize(gameState, playerState, economySystem);
```

### 7. Added Diplomacy Button to ProvinceInfoUI
**Files Changed:** `StarterKit/UI/ProvinceInfoUI.cs`

- Shows "Diplomacy" button when viewing province owned by another country
- Opens DiplomacyPanel for that country's owner

### 8. Fixed Button Styling Consistency
**Files Changed:**
- `StarterKit/UI/ProvinceInfoUI.cs:148-151`
- `StarterKit/UI/CountrySelectionUI.cs:111-116`
- `StarterKit/UI/LedgerUI.cs:120-123`

Changed `new Button()` to `CreateStyledButton()` for consistency.
Changed close buttons from "Close" text to "✕" symbol.

### 9. Added Localization Keys
**Files Changed:** `Template-Data/localisation/english/ui_l_english.yml`

Added 15 new keys:
- UI_ACTIONS, UI_FORM_ALLIANCE, UI_BREAK_ALLIANCE
- UI_NAP, UI_FORM_NAP, UI_BREAK_NAP
- UI_GUARANTEE, UI_REVOKE_GUARANTEE
- UI_GRANT_ACCESS, UI_REVOKE_ACCESS, UI_GRANTING_ACCESS
- UI_SEND_GIFT

---

## Decisions Made

### Decision 1: Country Name from Localization vs ColdData
**Context:** DiplomacyPanel showed tag instead of country name

**Solution:** Use same pattern as ProvinceInfoPresenter:
```csharp
string ownerTag = gameState.CountryQueries.GetTag(targetCountryId);
string countryName = LocalizationManager.Get(ownerTag);
```

### Decision 2: War Breaks Guarantee and Military Access
**Context:** What happens to existing treaties when declaring war?

**Decision:** Automatically revoke guarantee and military access when declaring war. Alliance and NAP must be broken first (button disabled).

**Rationale:** Realistic diplomacy - you can't guarantee someone you're at war with.

### Decision 3: Gift Button Updates on Gold Change
**Context:** Gift button didn't update when gold changed elsewhere

**Solution:** Subscribe to GoldChangedEvent, update button state on player's gold change.

---

## What Worked ✅

1. **StarterKitPanel Base Class**
   - CreateStyledButton(), CreateRow(), etc. made UI consistent
   - Subscribe<T>() pattern for event handling

2. **EventBus Pattern**
   - Easy to subscribe to diplomacy and economy events
   - Auto-cleanup via CompositeDisposable

3. **Facade Pattern for DiplomacySystem**
   - Clean API: `diplomacy.AreAllied()`, `diplomacy.DeclareWar()`
   - All complexity hidden behind simple methods

---

## Problems Encountered & Solutions

### Problem 1: NullReferenceException in DiplomacySystem
**Symptom:** Crash when opening DiplomacyPanel - NativeParallelHashMap.TryGetValue null

**Root Cause:** DiplomacySystem.Initialize() never called, NativeCollections never allocated

**Solution:** Add `Diplomacy.Initialize()` to GameState.InitializeSystems()

### Problem 2: Country Name Showing Tag Only
**Symptom:** Header showed "ROM" instead of "Rome"

**Root Cause:** Used `coldData.displayName` which was empty

**Solution:** Use `LocalizationManager.Get(tag)` like other UI

### Problem 3: Button Styling Inconsistent
**Symptom:** ProvinceInfoUI close button looked different from others

**Root Cause:** Used `new Button()` instead of `CreateStyledButton()`

**Solution:** Updated all panels to use CreateStyledButton

---

## Architecture Impact

### ENGINE-GAME Separation Demonstrated
- ENGINE: DiplomacySystem provides mechanism (opinion calc, war state, treaties)
- GAME: DiplomacyPanel provides policy (UI, when to allow actions)

### New Files
- `StarterKit/UI/DiplomacyPanel.cs` - Full diplomacy UI
- `Docs/Planning/starterkit-diplomacy.md` - Implementation plan

### Modified Files
- `Core/GameState.cs` - Initialize/Shutdown DiplomacySystem
- `StarterKit/Initializer.cs` - Wire up DiplomacyPanel
- `StarterKit/UI/ProvinceInfoUI.cs` - Diplomacy button, close button fix
- `StarterKit/UI/CountrySelectionUI.cs` - Button styling
- `StarterKit/UI/LedgerUI.cs` - Button styling

---

## Next Session

### Remaining Diplomacy Tasks (from planning doc)
1. **War indicators** - Show "At War" in province tooltip for enemy provinces
2. **Notification system** - Toast notifications for diplomatic events

### Potential Enhancements
- Opinion modifier breakdown tooltip
- Treaty duration/expiration
- AI diplomacy decisions

---

## Quick Reference for Future Claude

**Key Implementation:**
- DiplomacyPanel: `StarterKit/UI/DiplomacyPanel.cs`
- Initialization fix: `Core/GameState.cs:203`
- Wiring: `StarterKit/Initializer.cs:300-307`

**Pattern:**
```
ProvinceInfoUI → "Diplomacy" button → DiplomacyPanel.ShowForCountry(ownerId)
                                            ↓
                                    gameState.Diplomacy.* API calls
```

**Button Enable/Disable Logic:**
- At war → Can make peace, can break treaties, cannot form treaties
- Allied/NAP → Cannot declare war (must break first)
- Declaring war → Auto-revokes guarantee and military access

**Gotchas:**
- DiplomacySystem.Initialize() MUST be called or NativeCollections are null
- Use LocalizationManager.Get(tag) for country names, not coldData.displayName
- Subscribe to GoldChangedEvent for buttons that depend on treasury

---

## Files Changed Summary

| File | Changes |
|------|---------|
| `Core/GameState.cs` | Initialize/Shutdown DiplomacySystem |
| `StarterKit/UI/DiplomacyPanel.cs` | **NEW** - Full diplomacy UI (~560 lines) |
| `StarterKit/UI/ProvinceInfoUI.cs` | Diplomacy button, close button styling |
| `StarterKit/UI/CountrySelectionUI.cs` | Button styling fix |
| `StarterKit/UI/LedgerUI.cs` | Button styling fix |
| `StarterKit/Initializer.cs` | Wire up DiplomacyPanel |
| `Docs/Planning/starterkit-diplomacy.md` | **NEW** - Implementation plan |
| `Template-Data/localisation/english/ui_l_english.yml` | 15 new diplomacy keys |

---

*Session Duration: ~90 minutes*
