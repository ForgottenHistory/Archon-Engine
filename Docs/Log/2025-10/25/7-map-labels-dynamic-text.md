# Map Labels - Dynamic Province & Country Text
**Date**: 2025-10-25
**Session**: 7
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Implement dynamic province and country text labels with zoom-based switching

**Secondary Objectives:**
- Dynamic sizing based on territory area
- Dynamic rotation for visual variety
- Text outlines for readability
- Connected landmass detection (no ocean labels)

**Success Criteria:**
- Province labels when zoomed in, country labels when zoomed out
- Smooth fade transitions between label types
- Labels positioned correctly on connected landmasses
- Text readable at all zoom levels

---

## Context & Background

**Previous Work:**
- Farm map mode and fog of war implemented GPU-based rendering
- Movement validator system for pathfinding
- Map rendering uses texture-based approach (no GameObjects per province)

**Current State:**
- Had basic map rendering but no text labels
- Needed human-readable province/country identification
- User requested "visually cool" labels similar to Paradox games

**Why Now:**
- Map is functional but hard to navigate without labels
- User specifically requested country/province text with zoom switching
- Visual polish pass after core mechanics

---

## What We Did

### 1. MapLabelManager - Core Label System
**Files Changed:** `Assets/Game/MapLabels/MapLabelManager.cs` (NEW)

**Implementation:**
- TextMeshPro-based label rendering (not GPU instanced)
- Object pooling via SetActive for performance
- Frustum culling (only render visible labels)
- Zoom-based visibility switching
- Event-driven updates (not update-everything-every-frame)

**Rationale:**
- ~1000 countries + ~4500 provinces = manageable with TMP + pooling
- User confirmed can switch to GPU instancing later if needed
- Orthographic camera = simple rect-based culling
- Single zoom threshold parameter on CameraController

**Architecture Compliance:**
- ✅ GAME layer (uses ENGINE's Core + Map layers)
- ✅ Uses ProvinceCenterLookup for world positions
- ✅ Uses AdjacencySystem for landmass detection
- ✅ Zero allocations after initialization (pre-allocated dictionaries)

### 2. Dynamic Text Sizing
**Files Changed:** `Assets/Game/MapLabels/MapLabelManager.cs:119-180`

**Implementation:**
```csharp
// Province sizing by pixel area
int provinceArea = centerLookup.GetProvincePixelCount(provinceId);
float normalizedSize = (float)(provinceArea - minArea) / (maxArea - minArea);
float fontSize = Mathf.Lerp(minFontSize, maxFontSize, normalizedSize);

// Country sizing by bounding box area (per landmass)
float size = width * height;
float normalizedSize = (float)(size - minSize) / (maxSize - minSize);
float fontSize = Mathf.Lerp(minCountryFontSize, maxCountryFontSize, normalizedSize);
```

**Rationale:**
- Larger territories = bigger text (more readable, fits space better)
- Normalized across all provinces/countries for consistency
- Min/max ranges prevent unreadable tiny text or oversized labels

**User Feedback:**
- Initial range (0.3-1.2) too small → increased to (2-20) for provinces, (2-30) for countries
- Final default (10-25) for provinces works well at actual map scale

### 3. Connected Landmass Detection
**Files Changed:** `Assets/Game/MapLabels/MapLabelManager.cs:214-268`

**Implementation:**
```csharp
// Flood-fill through AdjacencySystem to find connected territories
var frontier = new Queue<ushort>();
frontier.Enqueue(startProvince);

while (frontier.Count > 0)
{
    ushort current = frontier.Dequeue();
    currentLandmass.Add(current);

    var neighbors = gameState.Adjacencies.GetNeighbors(current, Allocator.Temp);
    foreach (ushort neighbor in neighbors)
    {
        if (gameState.Provinces.GetProvinceOwner(neighbor) == countryId &&
            !visitedProvinces.Contains(neighbor))
        {
            visitedProvinces.Add(neighbor);
            frontier.Enqueue(neighbor);
        }
    }
    neighbors.Dispose();
}
```

**Rationale:**
- Solves Norway + Iceland problem (no ocean labels)
- Each connected landmass gets separate label
- Uses existing AdjacencySystem (no new dependencies)

**User Feedback:**
- "Woah! Actually so much better already. Fixed most issues."
- Norway mainland and Iceland now separate labels

### 4. Dynamic Text Rotation
**Files Changed:** `Assets/Game/MapLabels/MapLabelManager.cs:349-402` (countries), `:204-212` (provinces)

**Implementation:**
```csharp
// Countries: Find farthest apart provinces for true orientation
for (int i = 0; i < landmass.Count; i++)
{
    for (int j = i + 1; j < landmass.Count; j++)
    {
        float dist = Vector3.Distance(pos1, pos2);
        if (dist > maxDistance)
        {
            maxDistance = dist;
            point1 = pos1;
            point2 = pos2;
        }
    }
}

Vector3 direction = point2 - point1;
rotationAngle = Mathf.Atan2(direction.z, direction.x) * Mathf.Rad2Deg;

// Prevent upside-down
if (rotationAngle > 90f) rotationAngle -= 180f;
if (rotationAngle < -90f) rotationAngle += 180f;

// Provinces: Pseudo-random but deterministic
float pseudoRandom = (provinceId * 37) % 100 / 100f;
rotationAngle = (pseudoRandom - 0.5f) * 2f * maxRotationAngle;
```

**Rationale:**
- Countries: True landmass orientation (Norway's north-south axis)
- Provinces: Simpler pseudo-random (same province = same angle always)
- Both clamped to ±maxRotationAngle (user can limit)
- Z-axis rotation (tilts text left/right, never upside-down)

**User Feedback:**
- "Norway has the proper angle" - rotation based on farthest provinces works
- Initially tried bounding box orientation → all 0° (failed)
- Initially tried Y-axis rotation → no effect (wrong axis)

### 5. Text Outline & Curvature
**Files Changed:**
- `Assets/Game/MapLabels/MapLabelManager.cs:495-506`
- `Assets/Game/MapLabels/CurvedText.cs` (NEW)

**Implementation:**
```csharp
// Outline via TextMeshPro shader
textMesh.fontMaterial = new Material(textMesh.fontMaterial);
textMesh.fontMaterial.EnableKeyword("OUTLINE_ON");
textMesh.fontMaterial.SetFloat("_OutlineWidth", outlineWidth);
textMesh.fontMaterial.SetColor("_OutlineColor", Color.black);

// Curvature via vertex manipulation
float curveOffset = -curveAmount * (normalizedX * normalizedX - 0.25f);
vertices[vertexIndex + 0].y += curveOffset;
```

**Rationale:**
- Black outline improves readability over varying map colors
- Curvature gives classic cartographic label appearance
- Material keyword required for TMP outline to work

**User Feedback:**
- "nice, I see it now" (after adding EnableKeyword and material properties)
- Curvature direction initially inverted → flipped sign
- User found curvature "ew" with 3-letter tags → skipped for now

### 6. Width/Height Filtering
**Files Changed:** `Assets/Game/MapLabels/MapLabelManager.cs:340-345`

**Implementation:**
```csharp
// Calculate bounding box dimensions
float width = maxX - minX;
float height = maxZ - minZ;

// Skip landmasses below minimum dimensions
if (width < minCountryWidth || height < minCountryHeight)
{
    skipCount++;
    continue;
}
```

**Rationale:**
- Area-based filtering misleading (thin country could have high area)
- Width AND height ensures territory occupies screen space in both dimensions
- More intuitive than total pixel area

**User Feedback:**
- "Minimum province count to show country label is completely flawed metric"
- Switched from province count → total area → bounding box dimensions
- Final approach much more effective at filtering small territories

### 7. Initialization Flow
**Files Changed:** `Assets/Game/Initialization/HegemonUIPhaseHandler.cs:176-270`

**Implementation:**
- Dynamic GameObject creation (no prefab)
- Reflection-based camera/controller reference injection
- Called after map fully initialized
- Uses ProvinceCenterLookup for world position conversion

**Rationale:**
- Fits HegemonInitializer pattern (all init through phase handlers)
- No Unity scene dependencies
- Camera references set via reflection (clean MonoBehaviour)

---

## Decisions Made

### Decision 1: TextMeshPro vs GPU Instancing
**Context:** 1000 countries + 4500 provinces needs label rendering
**Options Considered:**
1. TextMeshPro + object pooling - Simple, Unity built-in
2. Custom GPU instancing - Maximum performance, complex
3. UI Canvas - Easy but limited to screen space

**Decision:** TextMeshPro + pooling
**Rationale:**
- User confirmed scale is manageable (~5500 total labels)
- Can switch to GPU instancing later if needed (won't break refactor)
- TMP provides outline, curvature, font features for free
- Orthographic camera makes culling trivial

**Trade-offs:**
- Not as performant as GPU instancing at extreme scale
- Some GC from TMP mesh updates (acceptable for now)

### Decision 2: Connected Landmass Detection
**Context:** Countries with disconnected territories (Norway + Iceland)
**Options Considered:**
1. One label per country (at bounding box center) - Simple, ocean labels
2. One label per landmass (flood-fill) - Complex, accurate positioning
3. Manual override system - Tedious, not scalable

**Decision:** Flood-fill via AdjacencySystem
**Rationale:**
- Solves ocean label problem automatically
- Works for any disconnected territory (Rome + Sardinia, etc.)
- Small disconnected islands filtered independently
- Uses existing AdjacencySystem (no new dependencies)

**Trade-offs:**
- O(N²) for finding farthest provinces (acceptable at init time)
- More labels created (but most filtered by size threshold)

### Decision 3: Province Rotation - Pseudo-Random
**Context:** Need rotation for provinces but shape analysis too expensive
**Options Considered:**
1. Actual shape analysis (PCA/bounding box) - Accurate, expensive
2. Pseudo-random based on ID - Simple, consistent, fast
3. No rotation - Boring, grid-like

**Decision:** Pseudo-random via province ID
**Rationale:**
- Province shape less important than country orientation
- `(provinceId * 37) % 100` gives deterministic variety
- Same province always same angle (no flicker on reload)
- Much simpler than shape analysis

**Trade-offs:**
- Not based on actual province shape
- Some provinces might have "wrong" angle
- Acceptable for visual variety goal

---

## What Worked ✅

1. **AdjacencySystem Flood-Fill for Landmasses**
   - What: Breadth-first search through adjacent provinces
   - Why it worked: Reused existing adjacency data, perfect for connectivity
   - Reusable pattern: Yes (any connected region detection)

2. **Farthest Province Pair for Country Rotation**
   - What: O(N²) search for max distance between provinces
   - Why it worked: Captures true territorial orientation (Norway north-south)
   - Reusable pattern: Yes (orientation of irregular shapes)

3. **Width/Height Filtering**
   - What: Require minimum bounding box width AND height
   - Why it worked: Prevents thin/small territories from cluttering map
   - Reusable pattern: Yes (any spatial filtering)

4. **Material Keyword for TMP Outline**
   - What: `EnableKeyword("OUTLINE_ON")` + material property setting
   - Why it worked: TMP outlines require shader keyword activation
   - Reusable pattern: Yes (any TMP outline usage)

---

## What Didn't Work ❌

1. **Bounding Box Aspect Ratio for Rotation**
   - What we tried: Calculate rotation from bbox width/height ratio
   - Why it failed: Norway's bbox is squarish despite elongated shape
   - Lesson learned: Bounding box != actual shape orientation
   - Don't try this again because: Bbox includes empty space, not shape axis

2. **Y-Axis Rotation for Text**
   - What we tried: `Quaternion.Euler(90, rotationAngle, 0)`
   - Why it failed: Text already facing down (X=90), Y-axis spins horizontally
   - Lesson learned: Z-axis rotation needed for tilt on flat surface
   - Don't try this again because: Wrong axis for orthographic top-down camera

3. **Province Count Filtering**
   - What we tried: Minimum province count to show country label
   - Why it failed: 1 huge province != 5 tiny provinces
   - Lesson learned: Territory size more important than province count
   - Don't try this again because: Count doesn't reflect actual map footprint

4. **Initial Font Size Ranges (0.3-1.2)**
   - What we tried: Small font sizes for province labels
   - Why it failed: "0.3078001! Way too small. holy"
   - Lesson learned: Must test at actual map scale, not default scene
   - Don't try this again because: TextMeshPro world units != expected scale

---

## Problems Encountered & Solutions

### Problem 1: Labels Not Appearing in Scene
**Symptom:** User reports "no text objects" despite successful compilation
**Root Cause:** MapRenderer component not found via `FindFirstObjectByType`
**Investigation:**
- Added comprehensive debug logging to trace initialization
- Logs showed "Map plane not found"
- GameObject named "Map Plane" not "MapRenderer"

**Solution:**
```csharp
// Try finding by GameObject name
var mapPlaneObj = GameObject.Find("Map Plane");
if (mapPlaneObj != null)
{
    mapPlane = mapPlaneObj.transform;
}
```

**Why This Works:** Unity dynamically creates "Map Plane" GameObject, component-based search failed
**Pattern for Future:** When dealing with dynamic HegemonInitializer objects, use GameObject.Find() with known names

### Problem 2: All Rotation Angles Calculating to 0°
**Symptom:** Debug logs show "rotation: 0.0°" for all landmasses
**Root Cause:** Bounding box edge points had same Z coordinate
**Investigation:**
```csharp
Vector3 leftmost = new Vector3(minX, 0, (minZ + maxZ) / 2f);
Vector3 rightmost = new Vector3(maxX, 0, (minZ + maxZ) / 2f);
// direction.z always 0 → Atan2(0, x) = 0
```

**Solution:**
```csharp
// Find two most distant provinces in landmass
for (int i = 0; i < landmass.Count; i++)
{
    for (int j = i + 1; j < landmass.Count; j++)
    {
        float dist = Vector3.Distance(pos1, pos2);
        if (dist > maxDistance)
        {
            maxDistance = dist;
            point1 = pos1;
            point2 = pos2;
        }
    }
}
Vector3 direction = point2 - point1;
rotationAngle = Mathf.Atan2(direction.z, direction.x) * Mathf.Rad2Deg;
```

**Why This Works:** Uses actual province positions, not synthetic bbox edge midpoints
**Pattern for Future:** For shape orientation, use real data points not geometric constructs

### Problem 3: Text Outlines Not Visible
**Symptom:** "I still dont see outlines on text btw. I have it maxxed"
**Root Cause:** TMP outline requires shader keyword and material properties
**Investigation:**
- TMP has `outlineWidth`/`outlineColor` properties but not working
- Shader needs explicit outline feature enabled
- Material properties also need direct setting

**Solution:**
```csharp
textMesh.fontMaterial = new Material(textMesh.fontMaterial);
textMesh.fontMaterial.EnableKeyword("OUTLINE_ON");
textMesh.fontMaterial.SetFloat("_OutlineWidth", outlineWidth);
textMesh.fontMaterial.SetColor("_OutlineColor", Color.black);
```

**Why This Works:** TMP shader feature flags + material properties both required
**Pattern for Future:** TMP advanced features need both component properties AND material/shader setup

### Problem 4: GameState.Adjacency Property Not Found
**Symptom:** Compilation error `'GameState' does not contain definition for 'Adjacency'`
**Root Cause:** Property is named `Adjacencies` (plural) not `Adjacency`
**Investigation:**
- Grepped GameState.cs for Adjacency properties
- Found: `public AdjacencySystem Adjacencies { get; private set; }`

**Solution:**
```csharp
var neighbors = gameState.Adjacencies.GetNeighbors(current, Allocator.Temp);
```

**Why This Works:** Correct property name
**Pattern for Future:** Always grep for actual property names, don't assume singular/plural

---

## Architecture Impact

### New Components Created
- `Assets/Game/MapLabels/MapLabelManager.cs` - Main label coordination
- `Assets/Game/MapLabels/CurvedText.cs` - TMP vertex manipulation for curvature
- `Assets/Game/Utils/ProvinceCenterLookup.cs:159-166` - Added GetProvincePixelCount()

### Dependencies Added
- MapLabelManager → ProvinceCenterLookup (GAME utility)
- MapLabelManager → AdjacencySystem (CORE)
- MapLabelManager → GameState (CORE)
- MapLabelManager → ParadoxStyleCameraController (GAME)

### Performance Characteristics
- Initialization: O(N²) for country landmass farthest province detection (acceptable, ~633 landmasses)
- Runtime: O(V) where V = visible labels (frustum culling)
- Memory: ~5500 labels × (GameObject + TextMeshPro + CurvedText) ≈ 2-3MB
- GC: Zero allocations after init (pre-allocated dictionaries, object pooling via SetActive)

---

## Code Quality Notes

### Performance
- **Measured:** Initialization completes in <1s, 20 country labels + 4500 province labels created
- **Target:** Not specified in architecture docs, but initialization time acceptable
- **Status:** ✅ Meets expectations for scale

### Testing
- **Manual Tests:** Verified at multiple zoom levels, rotation angles, label sizes
- **User Verification:** "Works fine!" - all features working as expected
- **Edge Cases:** Norway + Iceland (disconnected), small islands (filtered)

### Technical Debt
- **Created:**
  - CurvedText uses LateUpdate() for vertex manipulation (every frame)
  - Debug logging still enabled (should remove or gate behind flag)
  - Country labels show tags not names (TODO: get actual names)
- **TODOs in Code:**
  - CurvedText.cs:509 - "Full curvature requires custom vertex manipulation"
  - MapLabelManager - Remove debug logs before production

---

## Next Session

### Immediate Next Steps
1. Update FILE_REGISTRY.md with new MapLabel components
2. Git commit map label implementation
3. Optional: Add country name lookup (tags → full names)
4. Optional: Performance profiling with all labels visible

### Questions to Resolve
1. Should province labels use actual province names (from data files)?
2. Curvature amount - disable by default or keep at 10?
3. Should debug logging be removed or gated behind compile flag?

---

## Session Statistics

**Files Changed:** 4 new, 2 modified
- NEW: `Assets/Game/MapLabels/MapLabelManager.cs` (~515 lines)
- NEW: `Assets/Game/MapLabels/CurvedText.cs` (~75 lines)
- NEW: `Assets/Game/MapLabels.meta` (Unity folder)
- MODIFIED: `Assets/Game/Initialization/HegemonUIPhaseHandler.cs` (+95 lines)
- MODIFIED: `Assets/Game/Utils/ProvinceCenterLookup.cs` (+8 lines)
- MODIFIED: `Assets/Game/Camera/ParadoxStyleCameraController.cs` (+4 lines)

**Tests Added:** 0 (manual testing only)
**Bugs Fixed:** 5 (see Problems Encountered)

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- MapLabelManager at `Assets/Game/MapLabels/MapLabelManager.cs:25-540`
- Connected landmass detection uses AdjacencySystem flood-fill
- Country rotation based on farthest province pair (not bounding box)
- Province rotation is pseudo-random deterministic
- TMP outline requires `EnableKeyword("OUTLINE_ON")` + material properties

**What Changed Since Last Doc Read:**
- Architecture: New GAME layer component for map labels
- Implementation: TextMeshPro-based labels (not GPU instanced)
- Constraints: Font sizes must be 10+ for readability at map scale

**Gotchas for Next Session:**
- Watch out for: TMP outline requires shader keyword + material setup
- Don't forget: GameState.Adjacencies (plural) not Adjacency
- Remember: Z-axis rotation for text tilt, not Y-axis

---

## Links & References

### Related Documentation
- [HegemonInitializer Pattern](../../Game/HEGEMON_INITIALIZATION_SETUP.md)
- [Engine-Game Separation](../Engine/engine-game-separation.md)

### Related Sessions
- [6-farm-map-mode-gpu-refactor.md](6-farm-map-mode-gpu-refactor.md) - Previous session

### Code References
- MapLabelManager: `Assets/Game/MapLabels/MapLabelManager.cs:1-540`
- Landmass detection: `MapLabelManager.cs:214-268`
- Country rotation: `MapLabelManager.cs:349-402`
- Province rotation: `MapLabelManager.cs:204-212`
- CurvedText: `Assets/Game/MapLabels/CurvedText.cs:1-75`

---

*Session completed 2025-10-25*
