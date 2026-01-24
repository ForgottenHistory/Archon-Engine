# Multiplayer Command Synchronization
**Date**: 2026-01-24
**Session**: 4
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Sync game state changes (buildings, units, economy) across multiplayer clients using lockstep architecture

**Secondary Objectives:**
- Fix unit visualization in builds
- Ensure all state changes go through commands

**Success Criteria:**
- Buildings sync across clients
- Units sync across clients (creation and movement)
- Economy stays in sync
- Visualization works in both Editor and Build

---

## Context & Background

**Previous Work:**
- See: [03-multiplayer-lobby-connection.md](03-multiplayer-lobby-connection.md)

**Current State:**
- Lobby working, time syncing implemented
- Buildings weren't syncing between clients

**Why Now:**
- Core gameplay requires synchronized state for multiplayer

---

## What We Did

### 1. Created GameCommandProcessor for GAME Layer Commands
**Files Changed:** `Core/Commands/GameCommandProcessor.cs` (NEW)

**Problem:** Two command systems exist (IProvinceCommand for ENGINE, SimpleCommand for GAME), but only ENGINE commands were networked.

**Solution:** Created `GameCommandProcessor` that:
- Auto-registers command types for deserialization
- Routes commands: clients send to host, host broadcasts after execution
- Uses reflection for type ID mapping (sorted alphabetically for determinism)

```csharp
public bool SubmitCommand<T>(T command, out string resultMessage) where T : ICommand
{
    if (IsMultiplayer && !IsAuthoritative)
    {
        // Client: send to host
        byte[] commandData = SerializeCommand(typeId, command);
        networkBridge.SendCommandToHost(commandData, 0);
        return true;
    }

    // Host: execute locally then broadcast
    bool success = ExecuteLocally(command, out resultMessage);
    if (success && IsMultiplayer && IsAuthoritative)
    {
        networkBridge.BroadcastCommand(commandData, 0);
    }
    return success;
}
```

### 2. Auto-Registration of Commands
**Files Changed:** `StarterKit/Initializer.cs:703-741`

**Problem:** Manual command registration doesn't scale.

**Solution:** Reflection-based auto-discovery:
```csharp
var commandTypes = assembly.GetTypes()
    .Where(t => t.Namespace == "StarterKit.Commands"
             && typeof(BaseCommand).IsAssignableFrom(t)
             && !t.IsAbstract
             && t.GetConstructor(Type.EmptyTypes) != null)
    .OrderBy(t => t.FullName) // Deterministic order
    .ToList();
```

### 3. Created ColonizeCommand
**Files Changed:** `StarterKit/Commands/ColonizeCommand.cs` (NEW)

**Problem:** Colonization directly modified state without commands.

**Solution:** Command that handles both gold deduction AND province ownership:
```csharp
public override void Execute(GameState gameState)
{
    economySystem.RemoveGoldFromCountry(CountryId, COLONIZE_COST);
    provinceSystem.SetProvinceOwner(ProvinceId.Value, CountryId);
}
```

### 4. Fixed AI Running on All Clients
**Files Changed:** `StarterKit/Systems/AISystem.cs:77-80, 109-110`

**Problem:** AI ran independently on each client, causing desync.

**Solution:**
- Only host runs AI: `if (networkInit.IsMultiplayer && !networkInit.IsHost) return;`
- Skip human-controlled countries: `if (networkInit.IsCountryHumanControlled(countryId)) continue;`

Added `IsCountryHumanControlled()` to `NetworkInitializer.cs:55-67`.

### 5. Fixed UnitSystem.CreateUnit Hardcoding PlayerCountryId
**Files Changed:** `StarterKit/Systems/UnitSystem.cs:175-214`

**Problem:** `CreateUnit(provinceId, unitType)` hardcoded `playerState.PlayerCountryId`, breaking multiplayer sync.

**Solution:** Changed signature to require explicit country ID:
```csharp
public ushort CreateUnit(ushort provinceId, string unitTypeStringId, ushort countryId)
public ushort CreateUnit(ushort provinceId, ushort unitTypeId, ushort countryId)
```

Also fixed `DisbandUnitCommand` undo to store and use `previousCountryId`.

### 6. Created QueueUnitMovementCommand
**Files Changed:** `StarterKit/Commands/QueueUnitMovementCommand.cs` (NEW)

**Problem:** Unit movement used direct `movementQueue.StartMovement()` calls.

**Solution:** Command with custom serialization for path list:
```csharp
public override void Serialize(BinaryWriter writer)
{
    writer.Write(UnitId);
    writer.Write(MovementDays);
    writer.Write(CountryId);
    writer.Write((ushort)(Path?.Count ?? 0));
    foreach (var provinceId in Path)
        writer.Write(provinceId);
}
```

### 7. Fixed Shader Not in Build
**Files Changed:**
- Moved shaders to `Assets/Resources/Shaders/`
- `StarterKit/Visualization/UnitVisualization.cs:149-163`

**Problem:** `Shader.Find()` returned null in builds - shader not included.

**Solution:** Created material asset referencing shader, load material instead:
```csharp
var baseMaterial = Resources.Load<Material>("Shaders/InstancedAtlasBadgeMaterial");
badgeMaterial = new Material(baseMaterial);
```

### 8. Fixed Province Selection Disabled After Game Start
**Files Changed:** `StarterKit/Initializer.cs:314-318`

**Problem:** `CountrySelectionUI.Hide()` disabled province selection, never re-enabled.

**Solution:** Re-enable in `PlayerCountrySelectedEvent` handler:
```csharp
if (selector != null)
{
    selector.SelectionEnabled = true;
}
```

### 9. Fixed GPU Instancing Only Showing First Unit
**Files Changed:** `StarterKit/Visualization/UnitVisualization.cs:235`

**Problem:** Only first unit badge rendered, subsequent units invisible.

**Root Cause:** `MaterialPropertyBlock` accumulated old data.

**Solution:** Clear property block before setting new values:
```csharp
propertyBlock.Clear();
propertyBlock.SetFloatArray("_DisplayValue", displayValues.ToArray());
propertyBlock.SetFloatArray("_Scale", scaleValues.ToArray());
```

---

## Decisions Made

### Decision 1: Commands for All State Changes
**Context:** How to sync state across clients?
**Decision:** All state modifications MUST go through commands
**Rationale:** Lockstep requires identical command execution on all clients
**Impact:** UI and AI cannot directly modify state

### Decision 2: Host-Only AI
**Context:** Where should AI logic run?
**Decision:** Only host runs AI, commands broadcast to clients
**Rationale:** Prevents divergent AI decisions causing desync

### Decision 3: Explicit Country ID in System Methods
**Context:** Should system methods use `playerState.PlayerCountryId`?
**Decision:** No - require explicit country ID parameter
**Rationale:** Commands carry country ID; using playerState would give wrong country on receiving client

---

## Problems Encountered & Solutions

### Problem 1: Buildings Not Syncing
**Symptom:** Host built farm, client didn't see it
**Root Cause:** Two command systems, GAME commands not networked
**Solution:** Created GameCommandProcessor bridging GAME commands to network

### Problem 2: Economy Desync
**Symptom:** Client showed different gold than host
**Root Cause:** AI running independently on each client
**Solution:** Make AI host-only, use commands for all changes

### Problem 3: Units Created with Wrong Country
**Symptom:** Command synced but validation failed on client
**Root Cause:** `CreateUnit()` used `playerState.PlayerCountryId` instead of command's CountryId
**Solution:** Changed API to require explicit country ID

### Problem 4: Shader Missing in Build
**Symptom:** Unit badges not visible in build
**Root Cause:** `Shader.Find()` only finds shaders referenced by assets
**Solution:** Create material asset in Resources, load material instead of shader

### Problem 5: Only First Unit Visible
**Symptom:** First unit badge renders, subsequent ones don't
**Root Cause:** `MaterialPropertyBlock` not cleared between frames
**Solution:** Call `propertyBlock.Clear()` before setting arrays

---

## Architecture Impact

### Pattern Reinforced: Command Pattern for Sync
ALL state changes must go through commands for multiplayer:
- UI → Command → Execute → Event → Visualization
- Never: UI → Direct State Change

### New Files Created
- `Core/Commands/GameCommandProcessor.cs` - GAME layer command networking
- `StarterKit/Commands/ColonizeCommand.cs` - Province colonization
- `StarterKit/Commands/QueueUnitMovementCommand.cs` - Unit movement paths
- `Assets/Resources/Shaders/InstancedAtlasBadgeMaterial.mat` - Shader reference for builds

### API Changes
- `UnitSystem.CreateUnit()` now requires explicit `countryId` parameter
- `NetworkInitializer.IsCountryHumanControlled(countryId)` added

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- All GAME state changes MUST use commands (not direct state modification)
- Commands are auto-registered via reflection (sorted alphabetically for determinism)
- AI runs ONLY on host in multiplayer
- System methods should take explicit country ID, not use playerState

**Key Pattern:**
```csharp
// WRONG - uses local player, breaks on receiving client
units.CreateUnit(provinceId, unitType); // uses playerState.PlayerCountryId

// RIGHT - explicit country from command
units.CreateUnit(provinceId, unitType, command.CountryId);
```

**Build Gotchas:**
- Shaders loaded via `Shader.Find()` must be in "Always Included Shaders" OR referenced by material in Resources
- `MaterialPropertyBlock` must be cleared each frame when array sizes change

**Key Files:**
- Command processor: `Core/Commands/GameCommandProcessor.cs`
- Auto-registration: `StarterKit/Initializer.cs:703-741`
- Unit visualization fix: `StarterKit/Visualization/UnitVisualization.cs:235`

---

## Session Statistics

**Files Changed:** ~15
**Files Created:** 4
**Bugs Fixed:** 5 (sync, shader, instancing, selection, country ID)
**Lines Changed:** ~500

---

*Multiplayer now syncs buildings, units, and economy. All state changes flow through commands.*
