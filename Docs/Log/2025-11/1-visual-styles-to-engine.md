# Move Visual Styles System to Archon-Engine
**Date**: 2025-11-17
**Session**: 1
**Status**: 🔄 In Progress
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Move visual style system from GAME to ENGINE to enable "drop-in and see a map" workflow

**Secondary Objectives:**
- Simplify visual style system to Flat (2D) and Terrain (3D) modes as defaults
- Make ENGINE ship with working visualization out-of-box
- Keep GAME layer for policy customization (colors, map modes)

**Success Criteria:**
- ENGINE has default shaders (Flat + Terrain) with Political & Terrain map modes
- GAME can customize or override with own shaders
- Namespaces updated (GAME uses `Archon.Engine.Map`)
- System compiles without errors

---

## Context & Background

**Previous Work:**
- Visual styles were entirely in GAME layer (`Assets/Game/VisualStyles/`)
- See: [visual-styles-architecture.md](../Engine/visual-styles-architecture.md)

**Current State:**
- User has working 2D/3D mode switching via material swap
- System uses VisualStyleConfiguration ScriptableObject for parameters
- Two separate shaders: DefaultMapShader (flat) and DefaultMapShaderTessellated (3D)

**Why Now:**
- Archon-Engine should be "drop-in ready" - provide working map with just provinces.bmp + terrain.bmp
- Current system requires GAME to provide all shaders, breaking drop-in workflow
- Simplify to two core modes: Flat (2D) and Terrain (3D with heightmap displacement)

---

## What We Did

### 1. Moved Core C# Classes to ENGINE
**Files Changed:**
- `Assets/Game/VisualStyles/VisualStyleConfiguration.cs` → `Assets/Archon-Engine/Scripts/Map/VisualStyles/VisualStyleConfiguration.cs`
- `Assets/Game/VisualStyles/VisualStyleManager.cs` → `Assets/Archon-Engine/Scripts/Map/VisualStyles/VisualStyleManager.cs`

**Implementation:**
- Updated namespace: `Game.VisualStyles` → `Archon.Engine.Map`
- Fixed using statements: `Map.Rendering`, `Map.MapModes`, `Core` (ENGINE still uses old namespaces)
- Changed `Core.ProvinceQueries` access via `GameState.ProvinceQueries` (not a MonoBehaviour)
- Updated logging subsystem: `"game_hegemon"` → `"map_rendering"`

**Rationale:**
- VisualStyleConfiguration defines visual parameters (mechanism, not policy)
- VisualStyleManager applies styles to rendering system (ENGINE infrastructure)
- GAME can create custom VisualStyleConfiguration assets with different parameters

**Architecture Compliance:**
- ✅ Follows engine-game separation (ENGINE = mechanism, GAME = policy)
- ✅ ScriptableObject pattern for configuration

### 2. Moved Shader Includes to ENGINE
**Files Changed:** All .hlsl files moved to `Assets/Archon-Engine/Shaders/Includes/`

**Core Includes (Rendering Infrastructure):**
- `DefaultCommon.hlsl` - Material properties, texture declarations, samplers
- `DefaultLighting.hlsl` - Normal map lighting calculations
- `DefaultEffects.hlsl` - Overlay textures, fog of war, highlights
- `DefaultDebugModes.hlsl` - Debug visualization modes
- `DefaultMapModes.hlsl` - Map mode dispatcher

**Map Mode Visualization:**
- `MapModeCommon.hlsl` - Province/owner sampling, border rendering, utilities
- `MapModePolitical.hlsl` - Political map visualization
- `MapModeTerrain.hlsl` - Terrain texture visualization
- `MapModeDevelopment.hlsl` - Development gradient (TO BE REMOVED - GAME policy)

**Path Updates:**
- Fixed BezierCurves.hlsl reference: `Assets/Archon-Engine/Shaders/BezierCurves.hlsl` → `../BezierCurves.hlsl`
- DefaultMapModes includes updated to reference local files

**Rationale:**
- Shader includes are reusable rendering utilities (ENGINE mechanism)
- GAME can override by creating custom shaders
- Modular architecture allows selective overrides

### 3. Created ENGINE Default Shaders
**Files Created:**
- `Assets/Archon-Engine/Shaders/DefaultFlatMapShader.shader` - Shader name: `Archon/DefaultFlat`
- `Assets/Archon-Engine/Shaders/DefaultTerrainMapShader.shader` - Shader name: `Archon/DefaultTerrain`

**DefaultFlatMapShader (2D Mode):**
- Standard vertex → fragment pipeline
- No tessellation or heightmap displacement
- Includes: Political & Terrain map modes
- Features: Normal map lighting, borders, fog of war, overlays

**DefaultTerrainMapShader (3D Mode):**
- Tessellation pipeline: vertex → hull → domain → fragment
- Heightmap-based vertex displacement in domain shader
- Distance-based LOD for tessellation factor
- Tri-planar detail texture mapping (keyword: TERRAIN_DETAIL_MAPPING)
- Same fragment shader as Flat (modular architecture)

**Shader Properties (Configurable via VisualStyleConfiguration):**
- Border colors/strengths (country + province)
- Map mode colors (ocean, unowned, development gradient)
- Terrain adjustments (brightness, saturation, detail strength)
- Normal map lighting (strength, ambient, highlight)
- Fog of war (colors, noise, animation)
- Tessellation (height scale, factor, LOD distances)

**Rationale:**
- Two shaders = two render pipelines (tessellation fundamentally different from standard)
- All visual parameters exposed via material (no hardcoded policy)
- Fragment shader logic identical (via includes) = consistent rendering

### 4. Updated GAME Layer to Use ENGINE
**Files Changed:**
- `Assets/Game/HegemonInitializer.cs:6` - Changed `using Game.VisualStyles;` → `using Archon.Engine.Map;`
- `Assets/Game/DebugInputHandler.cs:2` - Changed `using Game.VisualStyles;` → `using Archon.Engine.Map;`

**Rationale:**
- GAME now imports ENGINE types (correct dependency direction)
- Old GAME VisualStyleManager files will be deleted (migration complete)

---

## Decisions Made

### Decision 1: ENGINE Provides Default Political & Terrain Map Modes Only
**Context:** User clarified "Flat & Terrain" means basic visualization, not all map modes

**Options Considered:**
1. **All map modes in ENGINE** (Political, Terrain, Development, Culture, etc.) - Makes ENGINE too opinionated
2. **No map modes in ENGINE** (just texture sampling) - Breaks "drop-in ready" goal
3. **Political & Terrain only** - Universal basics, GAME adds custom modes

**Decision:** Chose Option 3 - Political & Terrain as ENGINE defaults

**Rationale:**
- **Political** = "show who owns what" - universal for all grand strategy games
- **Terrain** = "show terrain texture" - universal visualization
- **Development** = GAME-specific policy (how to calculate/visualize development levels)
- Provides working visualization out-of-box while allowing GAME customization

**Trade-offs:**
- Development map mode moves to GAME layer (more work for game developers)
- BUT: Makes clear separation of ENGINE mechanism vs GAME policy
- Games that don't have "development" don't pay for unused code

**Documentation Impact:**
- Update visual-styles-architecture.md to clarify ENGINE vs GAME map modes
- Add example of how GAME extends with custom map modes

### Decision 2: Keep Material Swapping for 2D/3D Mode Switch
**Context:** Tessellation requires different shader pipeline, can't be runtime toggle

**Options Considered:**
1. **Single shader with keyword** - Tessellation not a keyword feature, fundamentally different pipeline
2. **Material swapping** (current approach) - Requires two materials, instant switch
3. **Geometry shader approach** - More complex, not better performance

**Decision:** Chose Option 2 - Keep material swapping

**Rationale:**
- Tessellation = 5-stage GPU pipeline (vertex/hull/domain/geometry/fragment) vs 2-stage (vertex/fragment)
- Cannot be toggled via keyword or parameter
- Material swap is O(1), texture rebinding handled by MapTextureManager
- Fragment shader identical (via includes) = minimal code duplication

**Trade-offs:**
- Need two separate shader files (DefaultFlat + DefaultTerrain)
- Need two material assets per visual style
- BUT: Clear separation, no runtime overhead, matches Unity's shader model

---

## What Worked ✅

1. **Modular Shader Includes**
   - What: All shader includes moved to ENGINE, referenced by both Flat and Terrain shaders
   - Why it worked: Fragment composition logic identical, only vertex processing differs
   - Reusable pattern: Yes - GAME can create shader variants that include ENGINE utilities

2. **Namespace Migration Strategy**
   - What: Updated using statements in GAME to reference ENGINE classes
   - Why it worked: Clear dependency direction (GAME imports ENGINE, not vice versa)
   - Impact: Compilation errors immediately revealed all dependencies

---

## What Didn't Work ❌

1. **Initial Namespace Assumption**
   - What we tried: Used `Archon.Engine.Map.Rendering` for ENGINE classes
   - Why it failed: ENGINE still uses old namespaces (`Map.Rendering`, not `Archon.Engine.Map.Rendering`)
   - Lesson learned: Check actual namespace with Grep before assuming
   - Don't try this again because: Mixed namespaces cause confusion, ENGINE refactor should be separate task

---

## Problems Encountered & Solutions

### Problem 1: Compilation Errors After Moving VisualStyleManager
**Symptom:**
```
CS0234: The type or namespace name 'Rendering' does not exist in the namespace 'Archon.Engine.Map'
CS0246: The type or namespace name 'MapModeManager' could not be found
CS0311: The type 'Core.Queries.ProvinceQueries' cannot be used as type parameter 'T'
```

**Root Cause:**
- ENGINE classes still use old namespaces (`Map.Rendering`, not `Archon.Engine.Map.Rendering`)
- `MapModes.MapModeManager` should be `MapModeManager` (using already imported)
- `ProvinceQueries` is not a MonoBehaviour, accessed via `GameState.ProvinceQueries`

**Solution:**
```csharp
// VisualStyleManager.cs
using Map.Rendering;  // Not Archon.Engine.Map.Rendering
using Map.MapModes;
using Core;  // For GameState

// Access ProvinceQueries via GameState
var gameState = FindFirstObjectByType<GameState>();
if (gameState != null && gameState.ProvinceQueries != null)
{
    ownerDispatcher.PopulateOwnerTexture(gameState.ProvinceQueries);
}
```

**Why This Works:**
- Matches actual ENGINE namespace structure
- Correct access pattern for non-MonoBehaviour systems

**Pattern for Future:** Always grep actual namespace before assuming structure

---

## Architecture Impact

### Documentation Updates Required
- [ ] Update visual-styles-architecture.md - ENGINE default map modes (Political & Terrain only)
- [ ] Remove Development from ENGINE includes - Move to GAME layer as example
- [ ] Add "How to extend with custom map modes" section

### New Patterns/Anti-Patterns Discovered
**Pattern: ScriptableObject Configuration with Material Reference**
- When to use: Visual/rendering configuration that needs both parameters and shader selection
- Benefits: Inspector-friendly, swappable at runtime, modder-friendly
- Already documented in: visual-styles-architecture.md

**Anti-Pattern: Mixed Namespaces in ENGINE**
- What not to do: Assume `Archon.Engine.X` namespace without checking
- Why it's bad: Causes compilation errors, confusion
- Current state: ENGINE uses `Map.X`, `Core.X` (no Archon prefix)

---

## Next Session

### Immediate Next Steps (Priority Order)
1. **Remove Development map mode from ENGINE** - Move `MapModeDevelopment.hlsl` to GAME, update DefaultMapModes.hlsl
2. **Remove Development config from VisualStyleConfiguration** - Move DevelopmentGradient to GAME-specific config
3. **Update scene to use ENGINE VisualStyleManager component** - Remove GAME component, add ENGINE component
4. **Create default materials in Unity** - DefaultFlatMapMaterial + DefaultTerrainMapMaterial
5. **Create DefaultVisualStyle.asset** - ENGINE ScriptableObject with sensible defaults
6. **Test drop-in workflow** - Verify map renders with ENGINE defaults only

### Questions to Resolve
1. Should Development gradient stay in ENGINE VisualStyleConfiguration for backward compatibility?
2. Where should GAME-specific map modes live? (New `Game/MapModes/` folder?)
3. Should we create example "How to add custom map mode" in GAME layer?

---

## Session Statistics

**Files Changed:** 13
- Moved: 2 C# files, 9 HLSL files
- Created: 2 shader files
- Updated: 2 GAME initializers

**Lines Added/Removed:** ~3500 lines moved to ENGINE
**Tests Added:** 0 (manual testing pending)
**Commits:** 0 (user controls git)

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- Visual style system moved to ENGINE for "drop-in ready" workflow
- Two default shaders: DefaultFlat (2D) + DefaultTerrain (3D tessellated)
- Default map modes: Political & Terrain only (Development is GAME policy)
- Material swapping required for 2D/3D switch (tessellation = different pipeline)

**What Changed Since Last Doc Read:**
- Architecture: VisualStyleConfiguration + VisualStyleManager now in ENGINE namespace
- Implementation: Shader includes moved to `Archon-Engine/Shaders/Includes/`
- Constraints: ENGINE uses old namespaces (`Map.X`, not `Archon.Engine.Map.X`)

**Gotchas for Next Session:**
- Watch out for: ENGINE still uses old namespaces, don't assume `Archon.Engine.X`
- Don't forget: ProvinceQueries accessed via GameState, not FindFirstObjectByType
- Remember: User wants Political & Terrain as ENGINE defaults only, Development stays GAME

---

## Links & References

### Related Documentation
- [visual-styles-architecture.md](../Engine/visual-styles-architecture.md) - Current system architecture
- [engine-game-separation.md](../Engine/engine-game-separation.md) - ENGINE vs GAME principles

### Code References
- ENGINE C# files: `Archon-Engine/Scripts/Map/VisualStyles/*.cs`
- ENGINE shaders: `Archon-Engine/Shaders/Default{Flat|Terrain}MapShader.shader`
- ENGINE includes: `Archon-Engine/Shaders/Includes/*.hlsl`
- GAME references: `Game/HegemonInitializer.cs:6`, `Game/DebugInputHandler.cs:2`

---

## Notes & Observations

- User has clear vision: "drop-in and see a map" with ENGINE defaults
- Political & Terrain are "good start" defaults, not comprehensive
- Development is explicitly GAME policy, not ENGINE mechanism
- Material swapping approach validated by user (2D/3D mode switch working)
- Next session: Remove Development from ENGINE, test drop-in workflow

---

*Session logged: 2025-11-17 - Visual Styles Migration (In Progress)*
