# Unit Visualization System - 3D Visual Feedback
**Date**: 2025-10-20
**Session**: 1
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Implement 3D visual representation of units on the map so players can see their armies at province centers

**Secondary Objectives:**
- Event-driven updates (no polling)
- Object pooling for performance
- Billboard text that stays readable

**Success Criteria:**
- Units appear as colored cubes at correct province centers
- Count badges show number of units
- Visual updates instantly when units created/moved/destroyed
- Right-click movement integration

---

## Context & Background

**Previous Work:**
- See: [4-unit-system-and-movement.md](../19/4-unit-system-and-movement.md)
- Related: [unit-system-implementation.md](../../Planning/unit-system-implementation.md)

**Current State:**
- Unit system functional (create, disband, move, save/load)
- Console commands working
- UI buttons working (recruit, select, move)
- **Missing:** Visual feedback - can't see units on map

**Why Now:**
- Blind army management is frustrating
- Need visual feedback to understand game state
- Foundation for future features (combat, selection)

---

## What We Did

### 1. ProvinceCenterLookup Utility
**Files Changed:** `Assets/Game/Utils/ProvinceCenterLookup.cs:1-130`

**Implementation:**
Converts province IDs to world positions by:
1. Getting province pixel data from ProvinceMapping
2. Calculating center of mass in texture space
3. Converting texture coordinates → UV → mesh local space → world space
4. Caching results for performance

**Key Code:**
```csharp
// Calculate center from pixels
Vector2 centerPixels = Vector2.zero;
foreach (var pixel in pixels) {
    centerPixels.x += pixel.x;
    centerPixels.y += pixel.y;
}
centerPixels /= pixels.Count;

// Convert to UV, then to world space using mesh bounds
float uvX = 1.0f - (centerPixels.x / textureWidth); // FLIPPED for rotated map
float uvY = centerPixels.y / textureHeight;

float localX = meshBounds.min.x + uvX * meshBounds.size.x;
float localZ = meshBounds.min.z + uvY * meshBounds.size.z;
worldPosition = mapMeshTransform.TransformPoint(new Vector3(localX, 0f, localZ));
```

**Architecture Compliance:**
- ✅ Uses existing ProvinceMapping infrastructure
- ✅ No Engine modifications needed
- ✅ GAME layer utility (not Engine)

### 2. UnitStackVisual Component
**Files Changed:** `Assets/Game/Visualization/UnitStackVisual.cs:1-149`

**Implementation:**
- Cube primitive (0.5 unit size) colored by country
- TextMeshPro count badge above cube
- Billboard rotation (X-axis only, stays upright)
- Reusable via object pooling

**Key Features:**
```csharp
// Billboard effect - tilt to face camera vertically, no Y rotation
Vector3 toCamera = mainCamera.transform.position - countText.transform.position;
float distance = new Vector3(toCamera.x, 0f, toCamera.z).magnitude;
float angle = Mathf.Atan2(toCamera.y, distance) * Mathf.Rad2Deg;
countText.transform.rotation = Quaternion.Euler(angle, 0f, 0f);
```

**Architecture Compliance:**
- ✅ Presentation layer only (no simulation logic)
- ✅ GPU instancing compatible (one material per country)

### 3. UnitVisualizationSystem Manager
**Files Changed:** `Assets/Game/Visualization/UnitVisualizationSystem.cs:1-284`

**Implementation:**
Event-driven system that:
- Subscribes to UnitCreatedEvent, UnitDestroyedEvent, UnitMovedEvent
- Manages object pool (100 initial visuals)
- Creates one visual per province with units (aggregate display)
- Updates on events only (no Update() polling)

**Key Pattern:**
```csharp
private void OnUnitCreated(UnitCreatedEvent evt) {
    UpdateProvinceVisual(evt.ProvinceID);
}

private void UpdateProvinceVisual(ushort provinceID) {
    int unitCount = gameState.Units.GetUnitCountInProvince(provinceID);

    if (unitCount == 0) {
        // Remove and pool visual
    } else {
        // Create or update visual
    }
}
```

**Architecture Compliance:**
- ✅ Event-driven (follows EventBus pattern)
- ✅ Object pooling (no runtime allocations)
- ✅ Scales to thousands of provinces

### 4. Integration & Timing Fix
**Files Changed:**
- `Assets/Game/GameSystemInitializer.cs:346-403`
- `Assets/Game/HegemonInitializer.cs:209-213`

**Problem:** Initially tried to initialize in GameSystemInitializer, but MapSystemCoordinator doesn't exist yet at that point.

**Solution:** Moved initialization to HegemonInitializer after map loads (Step 4.6, right after adjacency scanning).

**Initialization Flow:**
```
1. GameSystemInitializer.Initialize()
   ├─ Engine systems
   ├─ Unit registry
   └─ [Skip visualization - map not loaded yet]

2. MapInitializer.StartMapInitialization()
   └─ Creates MapSystemCoordinator + ProvinceMapping

3. HegemonInitializer Step 4.6
   └─ GameSystemInitializer.InitializeUnitVisualization()
      ├─ Gets MapSystemCoordinator.ProvinceMapping
      ├─ Gets texture dimensions
      └─ Initializes ProvinceCenterLookup
```

**Architecture Compliance:**
- ✅ Non-critical initialization (gracefully degrades if map not ready)
- ✅ No Engine modifications needed

### 5. Right-Click Unit Movement
**Files Changed:**
- `Assets/Archon-Engine/Scripts/Map/Interaction/ProvinceSelector.cs:22,106-126`
- `Assets/Game/Input/InputManager.cs:26,58,76,136-148`
- `Assets/Game/UI/ProvinceInfoPanel.cs:125,166,364-375`
- `Assets/Game/Visualization/UnitStackVisual.cs:95-98`

**Implementation:**
Added separate event chain for right-clicks:
- ProvinceSelector fires `OnProvinceRightClicked` (button 1)
- InputManager forwards to UI layer
- ProvinceInfoPanel handles movement when in movement mode

**User Flow:**
1. Left-click province → Opens province info
2. Click "Select Units" button → Enters movement mode
3. Right-click adjacent province → Moves units instantly

**Architecture Compliance:**
- ✅ Engine provides primitive (OnProvinceRightClicked event)
- ✅ Game layer defines policy (unit movement)
- ✅ Clean separation of concerns

---

## Decisions Made

### Decision 1: Aggregate Display vs Individual Units
**Context:** Should we show one icon per unit, or one icon with count badge?

**Options Considered:**
1. Individual units (EU4-style) - 5 units = 5 cubes
   - Pros: More visual detail, see exact composition
   - Cons: Visual clutter, performance cost (thousands of GameObjects)
2. Aggregate count (CK3-style) - 5 units = one cube with "5"
   - Pros: Clean, scales well, one visual per province
   - Cons: Less visual detail
3. Hybrid (show 1-3 individually, then aggregate)
   - Pros: Detail for small stacks, clean for large armies
   - Cons: Complex logic, inconsistent visuals

**Decision:** Chose Option 2 (Aggregate)

**Rationale:**
- Scales to 4,000 provinces with units
- Consistent visuals across all army sizes
- Object pooling is simpler
- Can add detail later with tooltips/UI

**Trade-offs:** Less visual detail for large armies (can't see unit types at a glance)

### Decision 2: ProvinceMapping vs MapDataIntegrator
**Context:** How to get province center positions?

**Options Considered:**
1. MapDataIntegrator.TryGetProvinceMetadata()
   - Pros: Designed for this purpose, has CenterOfMass pre-calculated
   - Cons: Not in the scene, not actually used in initialization
2. ProvinceMapping.GetProvincePixels() + calculate center
   - Pros: Already exists, guaranteed to be available
   - Cons: Calculate center every time (solved with caching)

**Decision:** Chose Option 2 (ProvinceMapping)

**Rationale:**
- MapDataIntegrator is legacy code, not actively used
- ProvinceMapping already created during map load
- Calculation is cheap + cached, so no performance issue

**Trade-offs:** Slight CPU cost for first province accessed (then cached)

### Decision 3: Billboard Rotation Strategy
**Context:** Text should be readable, but what rotation constraints?

**Options Considered:**
1. Full billboard (always face camera)
   - Pros: Always perfectly readable
   - Cons: Text rotates and tilts - disorienting
2. Y-axis rotation only (spin to face, stay upright)
   - Pros: No tilt, text stays level
   - Cons: Less readable from steep camera angles
3. X-axis rotation only (tilt to face, no spin)
   - Pros: Tilts naturally with camera height
   - Cons: Text sideways if camera is side-on

**Decision:** Chose Option 3 (X-axis only)

**Rationale:**
- Top-down camera means we mostly look down
- Tilting up/down feels natural
- No spinning as we pan left/right

**Trade-offs:** Text might be hard to read from very low camera angles (not a common case)

---

## What Worked ✅

1. **Event-Driven Architecture**
   - What: Subscribe to UnitCreated/Destroyed/Moved events
   - Why it worked: Zero polling overhead, updates exactly when needed
   - Reusable pattern: Yes - any system that reacts to unit changes

2. **Object Pooling**
   - What: Pre-create 100 visuals, reuse them
   - Why it worked: Zero runtime allocations, instant visual updates
   - Reusable pattern: Yes - standard Unity optimization

3. **ProvinceMapping as Data Source**
   - What: Use existing pixel data to calculate centers
   - Why it worked: Already available, no new dependencies
   - Reusable pattern: Yes - any province-position lookups

4. **Separate Right-Click Event**
   - What: OnProvinceRightClicked alongside OnProvinceClicked
   - Why it worked: Clean separation, no breaking existing left-click behavior
   - Reusable pattern: Yes - any multi-button input handling

---

## What Didn't Work ❌

1. **Assuming 10-Unit Quad Size**
   - What we tried: Hardcoded `localX = (uvX - 0.5f) * 10f`
   - Why it failed: Unity quad is 1×1 in local space, then scaled
   - Lesson learned: Always use mesh.bounds, never hardcode sizes
   - Don't try this again because: Mesh dimensions vary by setup

2. **MapDataIntegrator Dependency**
   - What we tried: Initialize using MapDataIntegrator.TryGetProvinceMetadata()
   - Why it failed: Component doesn't exist in scene (legacy code)
   - Lesson learned: Check what's actually in the scene before depending on it
   - Don't try this again because: MapDataIntegrator not actively used

3. **Full Billboard Rotation**
   - What we tried: `Quaternion.LookRotation(countText.position - camera.position)`
   - Why it failed: Text spins and tilts as camera moves - disorienting
   - Lesson learned: Constrain billboard rotation to natural camera movement
   - Don't try this again because: 3D rotation feels wrong for 2D text labels

---

## Problems Encountered & Solutions

### Problem 1: Units Spawning Across The Map
**Symptom:** Created unit in Vancouver, cube appeared in Siberia

**Root Cause:** Map mesh has 180° Y-axis rotation (horizontal flip). Texture pixel coordinates don't match world space without flipping X.

**Investigation:**
- Tried: Using mesh bounds directly - still wrong
- Checked: ProvinceSelector code uses `hit.textureCoord` (Unity handles flip automatically)
- Found: Need to manually flip when going from pixels → world

**Solution:**
```csharp
// IMPORTANT: Map is horizontally flipped (180° Y rotation)
uvX = 1.0f - uvX;
```

**Why This Works:** Reverses the X-axis to match the rotated mesh coordinate system.

**Pattern for Future:** Any pixel → world conversion needs this flip. Document in map-system-architecture.md.

### Problem 2: Initialization Timing (MapSystemCoordinator Not Found)
**Symptom:** Log warning "MapSystemCoordinator not found - unit visualization disabled"

**Root Cause:** GameSystemInitializer runs BEFORE map loads. MapSystemCoordinator doesn't exist yet.

**Investigation:**
- Tried: Making it non-critical (just log warning) - better, but still doesn't work
- Checked: HegemonInitializer sequence - map loads at Step 4, systems init at Step 2
- Found: Need to init visualization AFTER Step 4

**Solution:**
```csharp
// In HegemonInitializer.cs after map loads:
if (gameSystemInitializer != null) {
    gameSystemInitializer.InitializeUnitVisualization(gameState);
}
```

**Why This Works:** Guarantees MapSystemCoordinator exists before we try to access it.

**Pattern for Future:** Any map-dependent system must init AFTER `InitializeMap()` completes.

### Problem 3: Text Billboard Rotation
**Symptom:** Text spinning and tilting as camera moves - hard to read

**Root Cause:** `LookRotation()` rotates on all axes to face target perfectly.

**Investigation:**
- Tried: Y-axis only (spin to face, stay upright) - text sideways from side views
- Tried: Constrain rotation to plane - complex math
- Found: X-axis rotation (pitch) is most natural for top-down camera

**Solution:**
```csharp
// Calculate angle to tilt up/down only
float distance = new Vector3(toCamera.x, 0f, toCamera.z).magnitude;
float angle = Mathf.Atan2(toCamera.y, distance) * Mathf.Rad2Deg;
countText.transform.rotation = Quaternion.Euler(angle, 0f, 0f);
```

**Why This Works:** Only tilts text toward camera vertically, no spinning left/right.

**Pattern for Future:** Billboard text in top-down games should constrain rotation to match camera movement.

---

## Architecture Impact

### Documentation Updates Required
- [x] ~~Update map-system-architecture.md - add coordinate flip note~~
- [x] ~~Update unit-system-implementation.md - mark Phase 4 as complete~~
- [ ] Add visualization section to ARCHITECTURE_OVERVIEW.md

### New Patterns Discovered
**Pattern: Aggregate Visuals with Event-Driven Updates**
- When to use: Large-scale entities (thousands of units across hundreds of provinces)
- Benefits: Scales well, no polling overhead, clean visuals
- Implementation: One visual per location, count badge, update on events only
- Add to: Game layer patterns doc (if we create one)

**Pattern: Deferred Initialization for Map-Dependent Systems**
- When to use: System needs MapSystemCoordinator or ProvinceMapping
- Benefits: Graceful degradation, no initialization order bugs
- Implementation: Public `Initialize()` method called from HegemonInitializer after map loads
- Add to: Initialization patterns section

### Anti-Patterns Discovered
**Anti-Pattern: Hardcoded Mesh Dimensions**
- What not to do: Assume quad is 10×10 units in local space
- Why it's bad: Breaks if mesh scale changes, not portable
- Solution: Always use `meshFilter.sharedMesh.bounds`
- Add warning to: map-system-architecture.md coordinate systems section

---

## Code Quality Notes

### Performance
- **Measured:** ~100 visuals in pool, sub-millisecond update times
- **Target:** Zero allocations during gameplay, instant visual updates
- **Status:** ✅ Meets target

### Testing
- **Tests Written:** 0 (manual testing only)
- **Coverage:** All event types (create, destroy, move) tested manually
- **Manual Tests:**
  - Create unit → cube appears
  - Create multiple → count updates
  - Move unit → both provinces update
  - Disband → cube disappears

### Technical Debt
- **Created:**
  - Debug logging still active (first 3 provinces)
  - Country colors use golden ratio hue instead of actual palette
  - No GPU instancing material setup yet
- **Paid Down:** None
- **TODOs:**
  - Get actual country colors from CountryColorPalette
  - Create GPU instancing material for better performance
  - Remove debug logs once stable

---

## Next Session

### Immediate Next Steps (Priority Order)
1. **Movement Points System (Phase 2B)** - Units should take multiple turns to move long distances
2. **Combat System (Phase 3)** - Handle units meeting in same province
3. **Country Colors Integration** - Use actual palette instead of hue calculation
4. **GPU Instancing Material** - Optimize rendering for thousands of units

### Questions to Resolve
1. **Movement speed:** Instant, per-turn, or continuous? (Need gameplay decision)
2. **Unit stacking:** Allow unlimited units per province, or cap? (Balance question)
3. **Combat initiation:** Automatic on movement, or separate "attack" command? (UX decision)

### Docs to Read Before Next Session
- [unit-system-implementation.md](../../Planning/unit-system-implementation.md) - Review Phase 2B/3 specs
- [combat-system-design.md](../../Planning/combat-system-design.md) - If it exists

---

## Session Statistics

**Files Created:** 3
- ProvinceCenterLookup.cs
- UnitStackVisual.cs
- UnitVisualizationSystem.cs

**Files Modified:** 4
- GameSystemInitializer.cs
- HegemonInitializer.cs
- ProvinceSelector.cs
- InputManager.cs
- ProvinceInfoPanel.cs

**Lines Added:** ~600
**Tests Added:** 0 (manual only)
**Bugs Fixed:** 3 (coordinate flip, timing, billboard)

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- Key implementation: `UnitVisualizationSystem.cs:41-172` (event handlers + visual updates)
- Critical decision: Aggregate display (one visual per province with units)
- Active pattern: Event-driven updates via EventBus subscription
- Current status: Fully functional, needs country color integration

**What Changed Since Last Doc Read:**
- Architecture: Added visualization layer (GAME layer, not Engine)
- Implementation: Units now visible at province centers
- Constraints: Must initialize AFTER map loads (timing dependency)

**Gotchas for Next Session:**
- Watch out for: Coordinate flip (uvX = 1.0f - uvX) for any new pixel→world conversions
- Don't forget: ProvinceMapping is the source of truth for province positions
- Remember: Billboard rotation constrained to X-axis only (top-down camera)

---

## Links & References

### Related Documentation
- [map-system-architecture.md](../../Engine/map-system-architecture.md)
- [unit-system-implementation.md](../../Planning/unit-system-implementation.md)

### Related Sessions
- [4-unit-system-and-movement.md](../19/4-unit-system-and-movement.md) - Previous unit work

### Code References
- Event handlers: `UnitVisualizationSystem.cs:144-171`
- Coordinate conversion: `ProvinceCenterLookup.cs:101-130`
- Billboard rotation: `UnitStackVisual.cs:114-131`
- Right-click integration: `ProvinceSelector.cs:106-126`

---

## Notes & Observations

- User feedback: "Works perfectly", "Dead right in the middle of province"
- Billboard X-axis rotation feels natural with top-down camera
- Right-click for movement is intuitive (RTS-style)
- Aggregate display keeps map clean even with many units
- Object pooling eliminates GC spikes during unit operations
- Province pixel data calculation is fast enough to not need pre-computation

---

*Session completed 2025-10-20, ~3 hours total*
