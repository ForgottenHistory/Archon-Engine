# StarterKit AI Colonization & Double-Buffer Fix
**Date**: 2026-01-11
**Session**: 4
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Add AI colonization to StarterKit (expand before building farms)
- Fix rendering bug where province colors didn't update correctly

**Success Criteria:**
- AI countries colonize unowned neighboring provinces
- Province ownership changes render correctly without desyncs

---

## Context & Background

**Previous Work:**
- See: [3-starterkit-colonization-restrictions.md](3-starterkit-colonization-restrictions.md)

**Current State:**
- Player colonization working with terrain/neighbor restrictions
- AI only built farms, didn't expand territory

---

## What We Did

### 1. Added AI Colonization to StarterKit
**Files Changed:** `StarterKit/AISystem.cs`

Added `TryColonize()` method that:
- Gets owned provinces for AI country
- Finds unowned, ownable neighbor provinces
- Randomly picks one to colonize
- Priority: colonize first, then build farms (expand before develop)

```csharp
private const int COLONIZE_COST = 20;
private TerrainRGBLookup terrainLookup;

// In ProcessAI():
if (aiGold[countryId] >= COLONIZE_COST)
{
    if (TryColonize(countryId, provinceSystem))
    {
        aiGold[countryId] -= COLONIZE_COST;
        continue; // Move to next country after colonizing
    }
}
```

### 2. Fixed GPU Sync Issue (Initial Diagnosis)
**Files Changed:** `Map/Rendering/MapTexturePopulator.cs:247-256`

Initial bug report: Province colors wrong after AI colonizes.

First fix attempt: Added GPU synchronization to `UpdateSimulationData()`:
```csharp
ownerTextureDispatcher.PopulateOwnerTexture(provinceQueries);
var ownerSyncRequest = AsyncGPUReadback.Request(textureManager.ProvinceOwnerTexture);
ownerSyncRequest.WaitForCompletion();
```

**Result:** Didn't fully fix the issue - revealed a deeper problem.

### 3. Discovered Double-Buffer Bug (Root Cause)
**Files Changed:** `Core/GameStateSnapshot.cs`, `Core/Systems/TimeManager.cs`

**Root Cause:** The double-buffer system was losing persistent state on swap.

Flow:
1. User buys province → writes to buffer A (current write buffer)
2. TimeManager.Update() calls `SwapBuffers()` every frame when not paused
3. After swap: Buffer B becomes write buffer (contains OLD data!)
4. All Get/Set operations now use buffer B → data loss

The double-buffer pattern assumed simulation recalculates everything each tick, but ownership is persistent state that doesn't get recalculated.

### 4. Implemented Dirty Tracking (Proper Fix)
**Files Changed:**
- `Core/Systems/Province/ProvinceDataManager.cs` - Added dirty tracking
- `Core/Systems/ProvinceSystem.cs:383-412` - Updated SwapBuffers
- `Core/GameStateSnapshot.cs:102-106` - Reverted to O(1) swap

**ProvinceDataManager changes:**
```csharp
// Track dirty province indices
private NativeHashSet<int> dirtyIndices;

private void MarkDirty(int arrayIndex)
{
    dirtyIndices.Add(arrayIndex);
}

// Called by SetProvinceOwner, SetProvinceTerrain, SetProvinceState
```

**ProvinceSystem.SwapBuffers:**
```csharp
public void SwapBuffers()
{
    using (var dirtyIndices = dataManager.GetDirtyIndices(Allocator.Temp))
    {
        snapshot.SwapBuffers(); // O(1) pointer flip

        // Copy only dirty entries
        if (dirtyIndices.Length > 0)
        {
            var readBuffer = snapshot.GetProvinceReadBuffer();
            var writeBuffer = snapshot.GetProvinceWriteBuffer();
            for (int i = 0; i < dirtyIndices.Length; i++)
            {
                writeBuffer[dirtyIndices[i]] = readBuffer[dirtyIndices[i]];
            }
        }
        dataManager.ClearDirty();
    }
}
```

---

## Problems Encountered & Solutions

### Problem 1: Province Colors Wrong After AI Colonizes
**Symptom:** Player provinces showed terrain colors after AI bought land
**Initial Theory:** GPU async race condition
**Actual Root Cause:** Double-buffer swap losing persistent state

**Solution:** Dirty tracking - only copy modified provinces on buffer swap

### Problem 2: Full Buffer Copy Doesn't Scale
**Symptom:** N/A (preemptive fix)
**Root Cause:** Copying entire buffer O(n) every frame

**Solution:** Dirty tracking - O(dirty) instead of O(all)

| Scenario | Full Copy | Dirty Tracking |
|----------|-----------|----------------|
| 10k provinces, 100 changed | 80KB (~0.1ms) | 800B (~0.001ms) |
| 100k provinces, 100 changed | 800KB (~1ms) | 800B (~0.001ms) |

---

## Architecture Impact

### New Pattern: Dirty Tracking for Double-Buffered Persistent State
**When to use:** Any double-buffered data with persistent state (not recalculated each tick)
**Benefits:** O(changes) instead of O(all), scales to any size
**Implementation:** Track dirty indices in a NativeHashSet, copy only those on swap

### Key Insight: Double-Buffer Assumptions
The double-buffer pattern from Victoria 3 analysis assumes:
- Simulation fully recalculates the write buffer each tick
- UI reads from completed tick's buffer

This breaks for **persistent state** like ownership that accumulates changes over time.

---

## Files Changed Summary

**Modified Files:** 5 files
- `StarterKit/AISystem.cs` - Added colonization logic
- `Map/Rendering/MapTexturePopulator.cs` - Added GPU sync (kept for safety)
- `Core/GameStateSnapshot.cs` - Reverted to O(1) swap with documentation
- `Core/Systems/ProvinceSystem.cs` - Dirty-tracking swap, dispose dataManager
- `Core/Systems/Province/ProvinceDataManager.cs` - Added dirty tracking system

---

## Quick Reference for Future Claude

**Key Files:**
- `ProvinceDataManager.cs:282-341` - Dirty tracking implementation
- `ProvinceSystem.cs:383-412` - SwapBuffers with dirty copy
- `GameStateSnapshot.cs:102-106` - O(1) pointer swap only

**Critical Pattern:**
For ANY double-buffered persistent state:
1. Track dirty entries on modification
2. Swap pointers (O(1))
3. Copy only dirty entries from read→write buffer
4. Clear dirty tracking

**Gotchas:**
- `NativeHashSet.Count` is a property, not a method (no parentheses)
- Double-buffer assumes recalculated state - persistent state needs dirty tracking
- TimeManager calls SwapBuffers every frame when not paused

---

*Session focused on AI expansion and discovering/fixing the double-buffer persistent state bug*
