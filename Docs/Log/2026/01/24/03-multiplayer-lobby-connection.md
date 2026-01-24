# Multiplayer Lobby Connection
**Date**: 2026-01-24
**Session**: 3
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Fix multiplayer connection between Editor and Build
- Get lobby UI working with country selection

**Secondary Objectives:**
- Clean up data receive/send bugs in DirectTransport
- Enable province selection only in appropriate contexts

**Success Criteria:**
- Client connects to host successfully
- Players can select countries in lobby
- Host can start game with selected countries

---

## Context & Background

**Previous Work:**
- See: [02-compute-shader-build-fix.md](02-compute-shader-build-fix.md)
- See: [01-multiplayer-network-foundation.md](01-multiplayer-network-foundation.md)

**Current State:**
- Network foundation implemented but untested
- Lobby UI existed but connection never completed

**Why Now:**
- Network code needed real-world testing
- Multiple bugs discovered during actual connection attempts

---

## What We Did

### 1. Fixed Unity Transport Client Event Handling
**Files Changed:** `Scripts/Network/DirectTransport.cs:266-300`

**Problem:** Client stayed in "Connecting" state forever.

**Root Cause:** Client was using `driver.PopEventForConnection()` (server pattern) instead of `connection.PopEvent()` (client pattern).

**Fix:**
```csharp
// Server uses:
driver.PopEventForConnection(connection, out stream)

// Client MUST use:
connection.PopEvent(driver, out stream)
```

This is explicitly documented in Unity Transport but easy to miss.

### 2. Fixed NativeArray Data Copy Bug
**Files Changed:** `Scripts/Network/DirectTransport.cs:328-352`

**Problem:** Host received "Unknown message type 0" for all messages.

**Root Cause:** `HandleReceivedData` created a NativeArray from managed array, called `ReadBytes`, but never copied data back:
```csharp
// BROKEN:
var data = new byte[length];
reader.ReadBytes(new NativeArray<byte>(data, Allocator.Temp));
// data is still all zeros!
```

**Fix:**
```csharp
// CORRECT:
var nativeData = new NativeArray<byte>(length, Allocator.Temp);
reader.ReadBytes(nativeData);
var data = new byte[length];
nativeData.CopyTo(data);
nativeData.Dispose();
```

Also fixed `SendData` to properly allocate and dispose NativeArray.

### 3. Fixed Run in Background Issue
**Problem:** Connections never established between Editor and Build.

**Root Cause:** "Run in Background" was disabled in Build Settings. When Build lost focus, `Update()` stopped being called, so `ScheduleUpdate().Complete()` never ran.

**Solution:** User enabled "Run in Background" in Player Settings.

**Lesson:** Unity Transport requires continuous polling - if Update stops, network stops.

### 4. Redesigned Lobby UI
**Files Changed:** `Scripts/StarterKit/UI/LobbyUI.cs`

Changes:
- Moved panel to left side of screen (320px wide)
- Changed title from "Multiplayer" to "Main Menu"
- Added country selection via map clicking
- Shows country names in player list
- Only host sees "Start Game" button

**Key additions:**
- `ProvinceSelector` integration for map clicks
- `SetNetworkManager()` to sync country selection
- `SelectedCountryId` property for game start

### 5. Added Province Selection Toggle
**Files Changed:**
- `Scripts/Map/Interaction/ProvinceSelector.cs:23-27`
- `Scripts/StarterKit/UI/LobbyUI.cs`
- `Scripts/StarterKit/UI/CountrySelectionUI.cs`

**Problem:** Province selection worked in main menu (shouldn't).

**Solution:** Added `SelectionEnabled` property to ProvinceSelector:
```csharp
public bool SelectionEnabled
{
    get => enableSelection;
    set => enableSelection = value;
}
```

LobbyUI disables selection on init, enables only when in lobby.
CountrySelectionUI enables on Show(), disables on Hide().

### 6. Fixed Multiplayer Game Start Flow
**Files Changed:** `Scripts/StarterKit/Network/NetworkInitializer.cs:301-336`

**Problem:** After lobby, game showed CountrySelectionUI (redundant - already selected in lobby).

**Solution:** Skip CountrySelectionUI in multiplayer, use lobby selection directly:
```csharp
private void HandleGameStarted()
{
    ushort selectedCountry = lobbyUI?.SelectedCountryId ?? 0;
    lobbyUI?.Hide();

    if (selectedCountry > 0)
    {
        // Use lobby selection, emit event directly
        var playerState = Initializer.Instance?.PlayerState;
        playerState?.SetPlayerCountry(selectedCountry);
        gameState?.EventBus.Emit(new PlayerCountrySelectedEvent {...});
    }
    else
    {
        // Fallback to CountrySelectionUI if no selection
        countrySelectionUI?.Show();
    }
}
```

---

## Decisions Made

### Decision 1: Province Selection Control
**Context:** How to prevent map clicks when not appropriate?
**Options:**
1. Check state in click handler (reactive)
2. Disable/enable ProvinceSelector (proactive)

**Decision:** Option 2 - Proactive disable
**Rationale:** Cleaner separation, no wasted click processing

### Decision 2: Lobby Position
**Context:** Where should lobby UI be positioned?
**Decision:** Left side, 320px wide
**Rationale:** Leaves map visible for country selection

---

## Problems Encountered & Solutions

### Problem 1: Client Event Loop Wrong Pattern
**Symptom:** Client stuck in "Connecting" forever
**Root Cause:** Used server pattern for client
**Solution:** Use `connection.PopEvent(driver, out stream)` for client

### Problem 2: NativeArray Data Not Copied
**Symptom:** All received messages had type 0
**Root Cause:** ReadBytes fills NativeArray, but managed array never updated
**Solution:** CopyTo() after ReadBytes, then Dispose

### Problem 3: Background Focus Lost
**Symptom:** Connection intermittently failed
**Root Cause:** Build stopped polling when not focused
**Solution:** Enable "Run in Background" in Player Settings

### Problem 4: Province Selection Active in Menu
**Symptom:** Could select countries before entering lobby
**Root Cause:** ProvinceSelector enabled by default
**Solution:** Added SelectionEnabled property, disabled on init

---

## Architecture Impact

### New Properties Added
- `ProvinceSelector.SelectionEnabled` - Toggle province click handling
- `LobbyUI.SelectedCountryId` - Expose selected country for game start

### Flow Changes
- Multiplayer game start skips CountrySelectionUI
- Province selection controlled by UI state

---

## Code Quality Notes

### Technical Debt
- Debug logging in OnCancelClicked (removed stack trace)
- NetworkSettings added to DirectTransport (faster connection attempts)

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- Unity Transport client uses `connection.PopEvent()`, server uses `driver.PopEventForConnection()`
- NativeArray.CopyTo() required after ReadBytes to get data into managed array
- "Run in Background" MUST be enabled for networking
- ProvinceSelector.SelectionEnabled controls map clicks

**Key Files:**
- DirectTransport client fix: `Scripts/Network/DirectTransport.cs:266-300`
- NativeArray fix: `Scripts/Network/DirectTransport.cs:328-352, 373-387`
- Lobby UI: `Scripts/StarterKit/UI/LobbyUI.cs`

**Gotchas:**
- Unity Transport has DIFFERENT patterns for client vs server event loops
- NativeArray is not a reference to managed array - must copy back
- Background focus affects Update() which affects network polling

---

## Session Statistics

**Files Changed:** 5
**Bugs Fixed:** 4 (client events, data copy, selection state, game start flow)
**Lines Changed:** ~200

---

*Multiplayer lobby now functional. Clients can connect, select countries, and host can start game.*
