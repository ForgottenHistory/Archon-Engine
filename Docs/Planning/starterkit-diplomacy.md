# StarterKit Diplomacy Integration
**Created**: 2026-01-17
**Status**: 🔄 In Progress

## Goal
Demonstrate ENGINE diplomacy system in StarterKit with minimal but functional UI.

## ENGINE Features Available
- DiplomacySystem facade with full API
- Opinion system (base + modifiers, time-decay)
- War management (declare/peace)
- Treaties (alliance, NAP, guarantee, military access)
- Events via EventBus

## Implementation Plan

### Phase 1: DiplomacyPanel UI
**Priority**: High

Basic panel showing relations with a selected country:
- Country name/flag
- Opinion value with color gradient (red -100 to green +100)
- Relationship status (At War, Allied, NAP, etc.)
- Action buttons: Declare War, Make Peace

**Files to create:**
- `StarterKit/UI/DiplomacyPanel.cs`

**Integration:**
- Open from CountryInfoPanel or right-click on country
- Subscribe to diplomacy events for updates

### Phase 2: War Indicators
**Priority**: Medium

Visual feedback for war state:
- Countries at war with player highlighted on map (red tint or border)
- War icon in country tooltip

**Files to modify:**
- `StarterKit/UI/ProvinceInfoUI.cs` - Show war status in tooltip
- Consider: Political map mode could show enemy countries differently

### Phase 3: Notifications
**Priority**: Low

Toast notifications for diplomatic events:
- "X has declared war on Y"
- "Peace established between X and Y"
- "Alliance formed between X and Y"

**Files to create:**
- `StarterKit/UI/NotificationSystem.cs` (or integrate into existing UI)

## UI Design

### DiplomacyPanel Layout
```
┌─────────────────────────────┐
│ Relations with [Country]    │
├─────────────────────────────┤
│ Opinion: +45 [██████░░░░]   │
│ Status: At Peace            │
│ Treaties: Alliance, NAP     │
├─────────────────────────────┤
│ [Declare War]  [Close]      │
└─────────────────────────────┘
```

When at war:
```
┌─────────────────────────────┐
│ Relations with [Country]    │
├─────────────────────────────┤
│ Opinion: -80 [██░░░░░░░░]   │
│ Status: AT WAR              │
├─────────────────────────────┤
│ [Make Peace]   [Close]      │
└─────────────────────────────┘
```

## Architecture Notes

### ENGINE-GAME Separation
- ENGINE: DiplomacySystem provides mechanism (war state, opinion calc)
- GAME: StarterKit provides policy (when to allow war, peace conditions)

### Event Subscriptions
```csharp
gameState.EventBus.Subscribe<DiplomacyWarDeclaredEvent>(OnWarDeclared);
gameState.EventBus.Subscribe<DiplomacyPeaceMadeEvent>(OnPeaceMade);
```

### Accessing Diplomacy
```csharp
var diplomacy = gameState.Diplomacy;
bool atWar = diplomacy.IsAtWar(playerCountryId, targetCountryId);
var opinion = diplomacy.GetOpinion(playerCountryId, targetCountryId, currentTick);
```

## Task Checklist

### Phase 1: DiplomacyPanel
- [ ] Create DiplomacyPanel.cs extending StarterKitPanel
- [ ] Opinion display with color gradient
- [ ] War/peace status display
- [ ] Treaty status display (icons or text)
- [ ] Declare War button (with confirmation?)
- [ ] Make Peace button
- [ ] Wire up in Initializer
- [ ] Add way to open panel (from province info? hotkey?)

### Phase 2: War Indicators
- [ ] Show "At War" in province tooltip when viewing enemy province
- [ ] Consider map mode or highlight for enemy territories

### Phase 3: Notifications
- [ ] Simple toast/notification system
- [ ] Subscribe to war/peace events
- [ ] Display notification on screen

## Localization Keys Needed
- UI_DIPLOMACY
- UI_OPINION
- UI_AT_WAR
- UI_AT_PEACE
- UI_ALLIED
- UI_DECLARE_WAR
- UI_MAKE_PEACE
- UI_RELATIONS_WITH
- UI_TREATIES
