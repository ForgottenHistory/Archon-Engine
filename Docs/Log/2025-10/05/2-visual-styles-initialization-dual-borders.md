# Visual Styles Initialization & Dual Borders
**Date**: 2025-10-05
**Session**: 2
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Fix initialization flow so GAME layer controls sequence and visual styles apply correctly

**Secondary Objectives:**
- Implement dual borders (country + province) from VisualStyleConfiguration
- Ensure borders appear on startup based on visual style settings
- Properly separate ENGINE mechanism from GAME policy

**Success Criteria:**
- Material from VisualStyleConfiguration applied before map loads
- Dual borders visible on startup
- GAME controls entire initialization sequence via HegemonInitializer
- No ENGINE→GAME imports (clean architecture)

---

## Context & Background

**Previous Work:**
- See: [2025-10-05-province-loading-and-texture-fixes.md](./2025-10-05-province-loading-and-texture-fixes.md)
- Related: [visual-styles-architecture.md](../Engine/visual-styles-architecture.md)

**Current State:**
- Visual styles system existed (VisualStyleConfiguration, VisualStyleManager)
- But borders not applying on startup
- Material being overwritten by MapRenderingCoordinator
- MapInitializer auto-starting when receiving SimulationDataReadyEvent (no GAME control)

**Why Now:**
- User wanted dual borders working on startup
- Material swap timing was causing EU3Classic shader to be overwritten by fallback
- ENGINE was controlling initialization flow (violates GAME-controls-policy principle)

---

## What We Did

### 1. Fixed Material Overwrite Issue in MapRenderingCoordinator

**Files Changed:** `Assets/Archon-Engine/Scripts/Map/Rendering/MapRenderingCoordinator.cs:60-117`

**Problem:** MapRenderingCoordinator was creating a new material with MapCore shader, overwriting the EU3Classic material that VisualStyleManager had just applied.

**Root Cause Analysis:**
```
Logs showed:
[12:50:03.567] MapRenderingCoordinator: Using existing material 'Universal Render Pipeline/Lit' from GAME layer

But VisualStyleConfiguration had EU3Classic shader assigned!
```

**Timing Issue:** MapRenderingCoordinator.SetupMaterial() ran **before** VisualStyleManager.ApplyStyle(), so it read the default Unity URP/Lit material.

**Implementation:**
```csharp
// OLD - Always created new material
if (mapMaterial == null)
{
    mapMaterial = new Material(mapShader);
}
meshRenderer.material = mapMaterial;

// NEW - Check if GAME layer already set material
if (meshRenderer.material != null && meshRenderer.material.shader.name != "Hidden/InternalErrorShader")
{
    // Use existing material from GAME layer
    runtimeMaterial = meshRenderer.material;
    ArchonLogger.LogMapInit($"MapRenderingCoordinator: Using existing material '{runtimeMaterial.shader.name}' from GAME layer");
}
else
{
    // Fallback: Create material if not assigned
    // ... creates MapCore fallback material
}

// Bind ENGINE textures to the material (whether from GAME or fallback)
textureManager.BindTexturesToMaterial(runtimeMaterial);
```

**Also Fixed:** Line 111 null reference error
```csharp
// OLD - mapMaterial could be null
mapMaterial.SetFloat("_HighlightStrength", 1.0f);

// NEW - Use runtimeMaterial with null check
if (runtimeMaterial != null)
{
    runtimeMaterial.SetFloat("_HighlightStrength", 1.0f);
}
```

**Rationale:**
- GAME owns complete material (shader + parameters)
- ENGINE should respect GAME's material choice
- Only create fallback if GAME didn't provide material

**Architecture Compliance:**
- ✅ Follows visual-styles-architecture.md principle: "GAME owns Material+Shader, ENGINE just renders it"

### 2. Implemented GAME-Controlled Initialization Flow

**Files Changed:**
- `Assets/Archon-Engine/Scripts/Map/Core/MapInitializer.cs:121-186`
- `Assets/Game/HegemonInitializer.cs:143-210`

**Problem:** MapInitializer auto-started when receiving SimulationDataReadyEvent, preventing GAME from controlling the sequence.

**Previous Flow (ENGINE-controlled):**
```
SimulationDataReadyEvent emitted
  ↓
MapInitializer.OnSimulationDataReady() immediately starts initialization
  ↓
MapRenderingCoordinator reads material (too early - GAME hasn't applied style yet)
  ↓
VisualStyleManager applies style (too late - material already created)
```

**New Flow (GAME-controlled):**
```
1. GameInitializer completes → SimulationDataReadyEvent emitted
2. MapInitializer.OnSimulationDataReady() caches data, waits
3. HegemonInitializer Step 2: VisualStyleManager.ApplyStyle()
4. HegemonInitializer Step 3: MapInitializer.StartMapInitialization()
5. MapRenderingCoordinator reads material (now has EU3Classic!)
```

**MapInitializer Changes:**
```csharp
// OLD - Auto-start on event
private void OnSimulationDataReady(SimulationDataReadyEvent simulationData)
{
    InitializeAllComponents();
    coordinator.HandleSimulationReady(simulationData, ...);
}

// NEW - Cache data, wait for GAME to trigger
private SimulationDataReadyEvent cachedSimulationData;
private bool hasSimulationData = false;

private void OnSimulationDataReady(SimulationDataReadyEvent simulationData)
{
    ArchonLogger.LogMapInit($"MapInitializer: Received simulation data with {simulationData.ProvinceCount} provinces - waiting for GAME layer to trigger initialization");
    cachedSimulationData = simulationData;
    hasSimulationData = true;
}

public void StartMapInitialization()
{
    if (!hasSimulationData)
    {
        ArchonLogger.LogError("MapInitializer: StartMapInitialization called but no simulation data cached!");
        return;
    }

    InitializeAllComponents();
    coordinator.HandleSimulationReady(cachedSimulationData, ...);
}
```

**HegemonInitializer Orchestration:**
```csharp
// Step 2: Apply visual style BEFORE map loads
visualStyleManager.ApplyStyle(activeStyle);
yield return null;

// Step 3: Initialize map (manually triggered)
mapInitializer.StartMapInitialization();
while (!mapInitializer.IsInitialized && elapsed < timeout)
{
    yield return null;
    elapsed += Time.deltaTime;
}
```

**Rationale:**
- GAME layer should control initialization sequence (policy decision)
- ENGINE provides mechanisms, GAME decides when to invoke them
- Proper sequential awaiting prevents race conditions

**Architecture Compliance:**
- ✅ Follows engine-game-separation.md: "GAME controls flow, ENGINE provides mechanisms"
- ✅ Maintains clean dependency: GAME imports ENGINE, never reverse

### 3. Removed Border Generation from MapDataLoader

**Files Changed:** `Assets/Archon-Engine/Scripts/Map/Loading/MapDataLoader.cs:172-186`

**Problem:** MapDataLoader was hardcoding `BorderMode.Dual` and calling `DetectBorders()`, violating GAME policy control.

**Implementation:**
```csharp
// OLD - ENGINE decides border mode
private void GenerateBorders()
{
    if (borderDispatcher != null)
    {
        ArchonLogger.LogMapInit($"MapDataLoader: Generating borders - Mode: {BorderComputeDispatcher.BorderMode.Dual}");
        borderDispatcher.ClearBorders();
        borderDispatcher.SetBorderMode(BorderComputeDispatcher.BorderMode.Dual);
        borderDispatcher.DetectBorders();
        ArchonLogger.LogMapInit($"MapDataLoader: ✓ Generated dual borders using GPU compute shader");
    }
}

// NEW - ENGINE only initializes system, GAME controls borders
private void GenerateBorders()
{
    if (borderDispatcher != null)
    {
        // Note: Border mode and generation is controlled by GAME layer (VisualStyleManager)
        // ENGINE only initializes the border system, GAME decides what borders to show
        ArchonLogger.LogMapInit("MapDataLoader: Border system ready (mode will be set by GAME layer)");
    }
}
```

**Rationale:**
- Border mode is GAME policy (which borders to show, how they look)
- BorderComputeDispatcher is ENGINE mechanism (how to generate borders)
- Clear separation: ENGINE initializes, GAME configures

**Architecture Compliance:**
- ✅ Removes GAME policy from ENGINE layer
- ✅ Follows visual-styles-architecture.md border system design

### 4. Implemented Two-Phase Visual Style Application

**Files Changed:**
- `Assets/Game/VisualStyles/VisualStyleManager.cs:95-144`
- `Assets/Game/HegemonInitializer.cs:189-210`

**Problem:** VisualStyleManager.ApplyStyle() tried to set border mode, but BorderComputeDispatcher didn't exist yet (created later by MapInitializer).

**Solution: Split into Two Phases**

**Phase 1 (Step 2): Material Swap + Shader Parameters**
```csharp
private void ApplyBorderStyle(VisualStyleConfiguration.BorderStyle borders)
{
    if (runtimeMaterial == null)
    {
        Debug.LogWarning("VisualStyleManager: Cannot apply border style - no runtime material");
        return;
    }

    // Set border parameters on the material
    runtimeMaterial.SetFloat("_CountryBorderStrength", borders.countryBorderStrength);
    runtimeMaterial.SetColor("_CountryBorderColor", borders.countryBorderColor);
    runtimeMaterial.SetFloat("_ProvinceBorderStrength", borders.provinceBorderStrength);
    runtimeMaterial.SetColor("_ProvinceBorderColor", borders.provinceBorderColor);

    // Note: Border mode application deferred until ENGINE components exist
    // Use ApplyBorderConfiguration() after map initialization
}
```

**Phase 2 (Step 4): Border Generation**
```csharp
public void ApplyBorderConfiguration(VisualStyleConfiguration style)
{
    if (borderDispatcher == null)
    {
        borderDispatcher = FindFirstObjectByType<Map.Rendering.BorderComputeDispatcher>();
    }

    if (borderDispatcher != null)
    {
        var engineBorderMode = ConvertBorderMode(style.borders.defaultBorderMode);
        borderDispatcher.SetBorderMode(engineBorderMode);

        if (style.borders.enableBordersOnStartup)
        {
            borderDispatcher.DetectBorders();
            ArchonLogger.Log($"VisualStyleManager: Applied {style.borders.defaultBorderMode} border mode from visual style");
        }
    }
}
```

**HegemonInitializer Sequence:**
```csharp
// Step 2: Apply visual style (material swap, colors set)
VisualStyleManager.ApplyStyle(activeStyle);

// Step 3: Map initialization (BorderDispatcher created)
MapInitializer.StartMapInitialization();
await WaitForInitialized();

// Step 4: Apply border configuration (now BorderDispatcher exists!)
VisualStyleManager.ApplyBorderConfiguration(activeStyle);
```

**Rationale:**
- Can't apply border mode before ENGINE component exists
- But CAN set material parameters (they persist when BorderDispatcher appears)
- Two-phase approach respects component lifecycle

**Result:** Dual borders now appear on startup, reading `defaultBorderMode = Dual` from VisualStyleConfiguration!

**Architecture Compliance:**
- ✅ GAME reads policy from VisualStyleConfiguration
- ✅ ENGINE provides BorderComputeDispatcher mechanism
- ✅ Clean handoff: GAME → ENGINE → GAME

### 5. Updated Initialization Documentation

**Files Changed:**
- `Assets/Game/HEGEMON_INITIALIZATION_SETUP.md:6-231`
- `Assets/Archon-Engine/Docs/Engine/visual-styles-architecture.md:206-260`

**Updates to HEGEMON_INITIALIZATION_SETUP.md:**
- Corrected initialization flow to show 5 steps with Step 4 as border configuration
- Updated expected console output to match new sequence
- Added Border Generation section explaining GAME control

**Updates to visual-styles-architecture.md:**
- Added "Initialization Flow (GAME Controls Sequence)" section
- Documented two-phase visual style application
- Explained why borders can't apply in Step 2

**Rationale:**
- Documentation must match implementation
- Future Claude sessions need accurate reference
- Users need setup guide that reflects actual flow

---

## Decisions Made

### Decision 1: Two-Phase Visual Style Application vs Callback After MapInitializer

**Context:** VisualStyleManager needs to configure borders, but BorderComputeDispatcher doesn't exist until MapInitializer runs.

**Options Considered:**
1. **Two-phase application** - Split style application into Phase 1 (material) and Phase 2 (borders)
2. Event-driven callback - VisualStyleManager subscribes to MapInitializationCompleteEvent
3. Polling - VisualStyleManager checks for BorderDispatcher in Update()

**Decision:** Chose Option 1 (Two-phase application)

**Rationale:**
- Explicit sequencing is clearer than event-driven for initialization
- HegemonInitializer already orchestrates the sequence, adding callback is redundant
- Polling in Update() is wasteful and less deterministic
- Two-phase matches the natural flow: material swap → map init → border config

**Trade-offs:**
- Requires GAME to call two methods instead of one
- But provides better control over initialization sequence

**Documentation Impact:** Updated visual-styles-architecture.md and HEGEMON_INITIALIZATION_SETUP.md

### Decision 2: GAME Controls Initialization vs ENGINE Auto-Start

**Context:** MapInitializer was auto-starting when receiving SimulationDataReadyEvent, preventing GAME from controlling sequence.

**Options Considered:**
1. **GAME controls via HegemonInitializer** - MapInitializer waits for explicit trigger
2. Keep ENGINE auto-start, add delays - Use coroutines to delay VisualStyleManager
3. Event chain - MapInitializer emits events at each phase

**Decision:** Chose Option 1 (GAME controls via HegemonInitializer)

**Rationale:**
- GAME should control initialization flow (policy decision)
- Explicit sequencing is more predictable than delays
- Event chains become complex for sequential initialization
- Follows architecture principle: GAME owns policy, ENGINE provides mechanism

**Trade-offs:**
- MapInitializer becomes passive (needs explicit trigger)
- But this is correct architecture - ENGINE shouldn't make flow decisions

**Documentation Impact:** Created HEGEMON_INITIALIZATION_SETUP.md, updated visual-styles-architecture.md

### Decision 3: Border Generation in MapDataLoader vs VisualStyleManager

**Context:** Borders need to be generated, question is who decides the mode and triggers generation.

**Options Considered:**
1. **VisualStyleManager controls** - Reads mode from config, triggers generation
2. MapDataLoader hardcodes mode - Always generates Dual borders
3. MapInitializer decides - Reads from GameSettings

**Decision:** Chose Option 1 (VisualStyleManager controls)

**Rationale:**
- Border mode is visual policy (which borders to show)
- VisualStyleConfiguration is the right place for this setting
- Hardcoding violates GAME/ENGINE separation
- GameSettings is for gameplay settings, not visual style

**Trade-offs:**
- Requires two-phase application (can't apply before BorderDispatcher exists)
- But enforces clean architecture

**Documentation Impact:** Updated visual-styles-architecture.md border system section

---

## What Worked ✅

1. **Caching SimulationDataReadyEvent**
   - What: MapInitializer caches event data instead of immediately processing
   - Why it worked: Allows GAME to control when map initialization starts
   - Reusable pattern: Yes - use for any ENGINE component that needs GAME sequencing

2. **Two-Phase Visual Style Application**
   - What: Phase 1 (material swap) → Phase 2 (border generation)
   - Why it worked: Respects component lifecycle (BorderDispatcher doesn't exist in Phase 1)
   - Impact: Dual borders now work on startup via configuration

3. **Material Detection in MapRenderingCoordinator**
   - What: Check if meshRenderer.material already set before creating fallback
   - Why it worked: Respects GAME's material choice, only falls back if needed
   - Reusable pattern: Yes - ENGINE should always check for GAME-provided resources first

---

## What Didn't Work ❌

1. **Applying Borders in VisualStyleManager.ApplyStyle()**
   - What we tried: Call borderDispatcher.DetectBorders() during ApplyStyle()
   - Why it failed: BorderComputeDispatcher doesn't exist yet (created by MapInitializer)
   - Lesson learned: Can't configure components that haven't been created
   - Solution: Split into two phases - defer border config until after MapInitializer

2. **Quick Fix: Adding Delays**
   - What we tried: Initial suggestion to add delays between steps
   - User feedback: "Proper fix please" (rejected quick fixes)
   - Why it failed: Delays are non-deterministic, hide architectural issues
   - Lesson learned: Fix architecture problems properly, don't band-aid with delays

---

## Problems Encountered & Solutions

### Problem 1: Material Showing as 'Universal Render Pipeline/Lit' Instead of 'Archon/EU3Classic'

**Symptom:** Logs showed `MapRenderingCoordinator: Using existing material 'Universal Render Pipeline/Lit' from GAME layer` even though EU3ClassicStyle had EU3Classic shader assigned.

**Root Cause:** MapRenderingCoordinator.SetupMaterial() ran **before** VisualStyleManager.ApplyStyle() due to MapInitializer auto-starting.

**Investigation:**
```
Timeline from logs:
[12:50:03.566] MapRenderingCoordinator: Setting up map rendering system...
[12:50:03.570] HegemonInitializer [2/5]: Applying visual style...
```
MapRenderingCoordinator started 4ms before VisualStyleManager!

**Solution:**
1. Made MapInitializer wait for explicit trigger (doesn't auto-start)
2. HegemonInitializer applies visual style BEFORE triggering map initialization
3. MapRenderingCoordinator now finds EU3Classic material already set

**Why This Works:**
- Sequential initialization with proper awaiting
- GAME controls the flow, ensures correct ordering
- Material set before ENGINE reads it

**Pattern for Future:** When initialization order matters, use explicit sequencing via GAME coordinator, not event-driven auto-start.

### Problem 2: Dual Borders Not Appearing on Startup

**Symptom:** User configured `defaultBorderMode = Dual` in VisualStyleConfiguration but borders didn't appear.

**Root Cause:** MapDataLoader removed border generation (correct), but VisualStyleManager wasn't applying border configuration because BorderComputeDispatcher didn't exist.

**Investigation:**
- MapDataLoader.GenerateBorders() changed to not generate borders
- VisualStyleManager.ApplyStyle() tried to call borderDispatcher.DetectBorders()
- But borderDispatcher was null (not created yet)

**Solution:**
Created two-phase visual style application:
- Phase 1 (Step 2): Material swap, shader parameters set
- Phase 2 (Step 4): Border generation (after BorderComputeDispatcher exists)

**Why This Works:**
- Respects component creation order
- GAME can still control border policy via VisualStyleConfiguration
- Clean handoff: GAME → ENGINE creates component → GAME configures component

**Pattern for Future:** For settings that require ENGINE components, defer application until after component creation.

### Problem 3: Null Reference Error at MapRenderingCoordinator.cs:111

**Symptom:** Exception during map initialization: `Object reference not set to an instance of an object` at line 111.

**Root Cause:** Line 111 used `mapMaterial.SetFloat()` but mapMaterial could be null when using GAME's material.

**Investigation:**
```csharp
// Line 111 - crashed here
mapMaterial.SetFloat("_HighlightStrength", 1.0f);
```
When GAME provides material, `mapMaterial` is never assigned (only `runtimeMaterial` is set).

**Solution:**
```csharp
// Use runtimeMaterial with null check
if (runtimeMaterial != null)
{
    runtimeMaterial.SetFloat("_HighlightStrength", 1.0f);
}
```

**Why This Works:**
- `runtimeMaterial` is always set (either GAME's or fallback)
- Null check prevents crash if neither exists
- Uses correct material instance

**Pattern for Future:** When code paths create different material instances, use common variable (runtimeMaterial) instead of conditional logic.

---

## Architecture Impact

### Documentation Updates Required
- [x] Updated [HEGEMON_INITIALIZATION_SETUP.md](../../Game/HEGEMON_INITIALIZATION_SETUP.md) - Corrected initialization flow and console output
- [x] Updated [visual-styles-architecture.md](../Engine/visual-styles-architecture.md) - Added initialization flow section and two-phase application

### New Patterns Discovered

**Pattern: GAME-Controlled Sequential Initialization**
- When to use: Complex initialization requiring specific ordering
- Structure:
  ```csharp
  // GAME Coordinator (HegemonInitializer)
  Step 1: GameInitializer.StartInitialization()
  Wait: await gameInitializer.IsComplete
  Step 2: VisualStyleManager.ApplyStyle()
  Wait: yield return null
  Step 3: MapInitializer.StartMapInitialization()
  Wait: await mapInitializer.IsInitialized
  Step 4: VisualStyleManager.ApplyBorderConfiguration()
  ```
- Benefits:
  - Explicit ordering (no race conditions)
  - GAME controls policy decisions
  - Proper awaiting between phases
- Add to: master-architecture-document.md initialization section

**Pattern: Two-Phase Component Configuration**
- When to use: Configuring components that don't exist yet
- Structure:
  ```csharp
  Phase 1: Set parameters that persist (material properties)
  Phase 2: Configure components after creation (border mode)
  ```
- Benefits:
  - Respects component lifecycle
  - Allows early configuration of persistent state
  - Clean separation of responsibilities
- Add to: visual-styles-architecture.md

**Pattern: Material Ownership Check**
- When to use: ENGINE rendering code that might receive GAME-provided material
- Implementation:
  ```csharp
  if (meshRenderer.material != null && meshRenderer.material.shader.name != "Hidden/InternalErrorShader")
  {
      runtimeMaterial = meshRenderer.material; // Use GAME's material
  }
  else
  {
      runtimeMaterial = CreateFallbackMaterial(); // ENGINE fallback
  }
  ```
- Benefits:
  - Respects GAME's material choice
  - Provides fallback if GAME didn't configure
  - Clear visual indicator (pink) when misconfigured
- Add to: visual-styles-architecture.md

### Architectural Decisions That Changed

**Changed:** Initialization control ownership
**From:** ENGINE components auto-start when receiving events
**To:** GAME coordinator explicitly triggers ENGINE components in sequence
**Scope:** Affects MapInitializer, all initialization flow
**Reason:** GAME should control policy (when things happen), ENGINE provides mechanism (how things work)

**Changed:** Border generation trigger
**From:** MapDataLoader hardcodes BorderMode.Dual and calls DetectBorders()
**To:** VisualStyleManager reads defaultBorderMode from VisualStyleConfiguration and triggers generation
**Scope:** Affects MapDataLoader, VisualStyleManager, BorderComputeDispatcher
**Reason:** Border mode is GAME policy, should be configurable via visual style

---

## Code Quality Notes

### Performance
- **Measured:** Initialization time unchanged (~2.77s total)
- **Target:** <5s initialization (from CLAUDE.md)
- **Status:** ✅ Meets target
- **Impact:** Two-phase visual style application adds negligible overhead (<1ms)

### Testing
- **Manual Tests:**
  - ✅ Dual borders appear on startup
  - ✅ Material is EU3Classic (not URP/Lit)
  - ✅ Border colors match VisualStyleConfiguration settings
  - ✅ No errors during initialization
- **Visual Verification:**
  - ✅ Black country borders (strength 1.0)
  - ✅ Gray province borders (strength 0.5)
  - ✅ Both border types visible simultaneously

### Technical Debt
- **Created:** None - all changes follow architecture patterns
- **Paid Down:**
  - Removed ENGINE hardcoding of border mode (was technical debt)
  - Fixed material overwrite issue (was causing visual bugs)
- **Future Work:**
  - Consider runtime style switching (partially implemented, needs testing)
  - Add visual style switching UI

---

## Next Session

### Immediate Next Steps (Priority Order)
1. Test runtime style switching (VisualStyleManager.SwitchStyle()) - verify it works
2. Implement Imperator Rome visual style - validate modular system works
3. Add style switching UI - dropdown in settings menu

### Questions to Resolve
1. Should border strengths animate when switching styles? - visual polish consideration
2. How should style switching handle ongoing gameplay? - pause, continue, or reload?
3. Do we need per-map-mode border settings? - e.g., no borders in terrain mode

---

## Session Statistics

**Duration:** ~2 hours
**Files Changed:** 6 files
  - Modified: `MapInitializer.cs` (+50 lines)
  - Modified: `HegemonInitializer.cs` (+20 lines)
  - Modified: `VisualStyleManager.cs` (+30 lines)
  - Modified: `MapRenderingCoordinator.cs` (+10 lines)
  - Modified: `MapDataLoader.cs` (-15 lines)
  - Modified: `MapSystemCoordinator.cs` (+5 lines error logging)
  - Updated: `HEGEMON_INITIALIZATION_SETUP.md`
  - Updated: `visual-styles-architecture.md`
**Lines Added/Removed:** +115/-15
**Bugs Fixed:** 3 (material overwrite, null reference, borders not applying)
**Documentation Updated:** 2 architecture/setup docs

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- Initialization: `HegemonInitializer.cs:57-223` controls entire sequence
- Material check: `MapRenderingCoordinator.cs:63-98` respects GAME material
- Border config: `VisualStyleManager.cs:119-144` applies borders in Phase 2
- Two-phase style: Phase 1 (Step 2) material swap, Phase 2 (Step 4) borders

**What Changed Since Last Doc Read:**
- Architecture: GAME now controls initialization sequence (was ENGINE auto-start)
- Implementation: Visual styles apply in two phases (material → borders)
- Borders: Configured from VisualStyleConfiguration.defaultBorderMode (was hardcoded)

**Gotchas for Next Session:**
- Watch out for: Component creation order - can't configure before creation
- Don't forget: Visual style application is two-phase (material, then borders)
- Remember: GAME controls flow via HegemonInitializer, ENGINE components are passive

---

## Links & References

### Related Documentation
- [visual-styles-architecture.md](../Engine/visual-styles-architecture.md)
- [engine-game-separation.md](../Engine/engine-game-separation.md)
- [HEGEMON_INITIALIZATION_SETUP.md](../../Game/HEGEMON_INITIALIZATION_SETUP.md)

### Related Sessions
- [2025-10-05-province-loading-and-texture-fixes.md](./2025-10-05-province-loading-and-texture-fixes.md)

### Code References
- GAME initialization: `HegemonInitializer.cs:57-223`
- Material check: `MapRenderingCoordinator.cs:60-117`
- Two-phase style: `VisualStyleManager.cs:50-144`
- Border generation: `BorderComputeDispatcher.cs` (ENGINE mechanism)

---

## Notes & Observations

- User insisted on "proper fix" when I suggested quick fixes - good architectural discipline
- Two-phase visual style application is elegant solution to component lifecycle issue
- HegemonInitializer pattern (GAME coordinator) should be template for future complex initialization
- Border system architecture is clean: ENGINE provides mechanism, GAME controls policy
- Dual borders working perfectly on startup validates entire visual styles system

---

*Session completed successfully - all objectives met ✅*
*Dual borders working, GAME controls initialization, clean ENGINE/GAME separation maintained*
