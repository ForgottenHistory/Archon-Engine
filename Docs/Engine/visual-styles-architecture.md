# Visual Styles Architecture

**Last Updated:** 2025-10-15
**Status:** ✅ Implemented and Architecture Compliant

## Problem Statement

Map visualization shaders contained hardcoded GAME policy (colors, gradients, visualization rules) in the ENGINE layer, violating the engine-game separation principle.

## Solution: Complete Material Ownership

**GAME owns the entire Material + Shader, ENGINE just renders it**

### Architecture Flow

**GAME Layer (Policy)**:
- VisualStyleConfiguration.asset (ScriptableObject references complete style)
- EU3MapMaterial.mat (Material with textures bound by ENGINE)
- EU3MapShader.shader (Complete shader with all map modes)
- MapMode*.hlsl (Political, Terrain, Development visualization logic)

**ENGINE Layer (Mechanism)**:
- MapFallback.shader (Minimal pink "missing style" fallback)
- VisualStyleManager (applies GAME material to MeshRenderer)
- MapTextureManager (binds simulation textures to GAME material)
- BorderComputeDispatcher (generates border data via compute shader)

**Key Principle:** ENGINE provides **rendering infrastructure** (textures, compute shaders, mesh renderer), GAME provides **complete visual style** (shader + material + configuration)

### Dependency Flow (Clean)

Clean dependency hierarchy:
- CORE (simulation) - No imports
- MAP (rendering infra) - No imports from GAME
- GAME (visual policy) - Imports Core + Map, provides shaders/materials

**No circular dependencies:** ENGINE never imports from GAME

## Implementation Details

### GAME Layer Components

#### VisualStyleConfiguration.cs (ScriptableObject)
**Purpose:** Complete visual style definition

**Contains:**
- Material mapMaterial - Reference to complete material (REQUIRED)
- BorderStyle borders - Border colors and strengths
- MapModeColors mapModes - Ocean, unowned, development gradient
- DevelopmentGradient - 5-tier color progression
- Terrain adjustments (brightness, saturation)

**Pattern:** ScriptableObject asset, swappable at runtime
**Location:** `Game/VisualStyles/VisualStyleConfiguration.cs`

#### VisualStyleManager.cs (MonoBehaviour)
**Purpose:** Applies visual styles to ENGINE rendering system

**Key Method:** `ApplyStyle(VisualStyleConfiguration style)`
1. Swaps MeshRenderer.material to style's material
2. Sets shader parameters from configuration
3. Configures ENGINE BorderComputeDispatcher

**Runtime:** Supports `SwitchStyle()` for on-the-fly swapping
**Location:** `Game/VisualStyles/VisualStyleManager.cs`

#### EU3MapShader.shader (Complete Shader)
**Shader Name:** `Archon/EU3Classic`

**Includes:** All map mode visualization logic
- MapModeCommon.hlsl - Utilities
- MapModePolitical.hlsl - Political mode
- MapModeTerrain.hlsl - Terrain mode
- MapModeDevelopment.hlsl - Development mode

**Configurable via:** Material parameters (set by VisualStyleConfiguration)
**Location:** `Game/VisualStyles/EU3Classic/EU3MapShader.shader`

#### Map Mode Shaders (.hlsl files)
- MapModeCommon.hlsl - Shared utilities (province ID decoding, owner sampling)
- MapModePolitical.hlsl - Political visualization (country colors)
- MapModeTerrain.hlsl - Terrain visualization (terrain.bmp colors)
- MapModeDevelopment.hlsl - Development gradient (configurable 5-tier)

**Location:** `Game/Shaders/MapModes/`
**Note:** These are GAME POLICY - different games visualize differently

### ENGINE Layer Components

#### MapFallback.shader
**Purpose:** Minimal fallback when no GAME style is configured
**Renders:** Pink/magenta to indicate missing visual style
**Location:** `Archon-Engine/Shaders/MapFallback.shader`
**Status:** Never used if GAME properly configured

#### MapTextureManager (Extension Point)
**Provides:** `BindTexturesToMaterial(Material material)` method

**Binds:** Simulation textures to GAME's material
- ProvinceIDTexture, ProvinceOwnerTexture, BorderTexture, etc.

**Design:** ENGINE doesn't care what shader is used, just binds textures
**Location:** `Archon-Engine/Scripts/Map/MapTextureManager.cs`

#### BorderComputeDispatcher
**Provides:** Border generation via GPU compute shader

**Modes:** Province, Country, Thick, **Dual** (recommended)

**Output:** BorderTexture (RG16: R=country borders, G=province borders)
**Location:** `Archon-Engine/Scripts/Map/Rendering/BorderComputeDispatcher.cs`

### Border System (Dual-Border Implementation)

**Texture Format:** `RG16`
- **R channel:** Country borders (between different owners)
- **G channel:** Province borders (between same-owner provinces)

**Compute Shader:** `BorderDetection.compute` kernel `DetectDualBorders`
- Generates both border types in single GPU pass
- Efficient border generation

**Shader Rendering:** Layered approach
- Province borders first (lighter, configurable)
- Country borders on top (darker, configurable)

**Configuration:** Set via VisualStyleConfiguration (GAME policy)

## Benefits

### Architecture
- Clean separation - No ENGINE→GAME imports
- Single responsibility - ENGINE renders, GAME defines visuals
- Extensible - Add new styles without touching ENGINE

### Gameplay
- Modular graphics - Swap entire visual style via ScriptableObject
- Runtime switching - Change styles in settings menu
- Multiple styles - Ship EU3, Imperator, Modern together
- Modder-friendly - Create shader + material + config asset

### Performance
- Dual borders - Both types in single compute pass
- Material swapping - Instant style changes
- Configurable params - No shader recompilation needed

## How to Create a New Visual Style

### Example: "Imperator Rome" Style

1. **Create folder structure:** `Assets/Game/VisualStyles/ImperatorRome/`
2. **Copy EU3 shader as base:** Copy EU3MapShader.shader to ImperatorMapShader.shader
3. **Customize shader:** Change shader name, modify map mode logic, add custom effects
4. **Create Material in Unity:** Create material asset, assign shader
5. **Create VisualStyleConfiguration asset:** Create asset, assign mapMaterial, configure colors
6. **Use in scene:** Assign to VisualStyleManager.activeStyle or swap at runtime

### What Makes a Valid Visual Style

**Required:**
- Shader that accepts ENGINE textures (ProvinceIDTexture, etc.)
- Material referencing that shader
- VisualStyleConfiguration with material reference

**Optional:**
- Custom map mode logic (different visualization algorithms)
- Custom effects (terrain blending, lighting, post-processing)
- Custom color schemes (parameter-based or shader-based)

**ENGINE Guarantees:**
- Will bind simulation textures to your material
- Will call your shader with correct geometry
- Will provide border data via BorderTexture

## Integration with Existing Systems

### Initialization Flow (GAME Controls Sequence)

**HegemonInitializer** (GAME layer) orchestrates the entire flow:

**Step 1:** Load simulation data
**Step 2:** Apply visual style BEFORE map loads
- Material swapped to EU3Classic
- Border colors/strengths set
- Border mode NOT applied yet (BorderDispatcher doesn't exist)

**Step 3:** Initialize map
- BorderComputeDispatcher created by ENGINE
- Border system initialized (but no borders generated)
- Textures bound to EU3Classic material

**Step 4:** Apply border configuration
- Reads defaultBorderMode from VisualStyleConfiguration
- Calls BorderComputeDispatcher.SetBorderMode(Dual)
- Calls BorderComputeDispatcher.DetectBorders()
- Dual borders generated and visible

**Key Principle:** Visual style applied in TWO phases:
1. **Phase 1:** Material swap + shader parameters (before ENGINE components exist)
2. **Phase 2:** Border generation (after ENGINE BorderDispatcher exists)

### Map Modes (C# Side)
- **PoliticalMapMode.cs** - Updates CountryColorPalette texture
- **TerrainMapMode.cs** - Uses static ProvinceColorTexture
- **DevelopmentMapMode.cs** - Updates ProvinceDevelopmentTexture
- These work with **any** visual style (ENGINE textures are universal)

### Material Binding Flow

ENGINE MapRenderingCoordinator.SetupMaterial():
- Uses GAME's material if available
- Fallback to MapFallbackShader if not (should never happen if GAME properly configured)

ENGINE MapTextureManager.BindTexturesToMaterial():
- Binds simulation textures to the runtime material

### Runtime Style Switching

Settings menu can call `visualStyleManager.SwitchStyle(newStyle)` for instant visual style changes. Borders regenerate automatically.

## File Locations

### GAME Layer
```
Assets/Game/
├── VisualStyles/
│   ├── VisualStyleConfiguration.cs
│   ├── VisualStyleManager.cs
│   └── EU3Classic/
│       ├── EU3MapShader.shader
│       └── EU3MapMaterial.mat (created in Unity)
├── Shaders/
│   └── MapModes/
│       ├── MapModeCommon.hlsl
│       ├── MapModePolitical.hlsl
│       ├── MapModeTerrain.hlsl
│       └── MapModeDevelopment.hlsl
```

### ENGINE Layer
```
Assets/Archon-Engine/
├── Shaders/
│   └── MapFallback.shader
├── Scripts/Map/
│   ├── MapTextureManager.cs (BindTexturesToMaterial)
│   └── Rendering/BorderComputeDispatcher.cs
```

## Testing Checklist

When implementing/testing visual styles:

- ENGINE MapFallback.shader renders pink (missing style indicator)
- GAME EU3MapShader compiles without errors
- Material created and shader assigned correctly
- VisualStyleConfiguration has material reference
- VisualStyleManager applies style successfully
- Map renders with correct colors/borders
- Dual borders working (country + province)
- Runtime style switching works
- No ENGINE→GAME imports in codebase

## Future Extensions

### Planned Visual Styles
- **EU3 Classic** (Implemented) - Clean borders, simple colors
- **Imperator Rome** (Planned) - Soft borders, terrain blending
- **Modern/Minimal** (Planned) - Flat colors, thin borders
- **High Contrast** (Planned) - Accessibility-focused

### Potential Features
- Custom border shaders - Animated, glowing, thickness-variable
- Terrain blending - Smooth province color transitions
- Lighting effects - Height-based shading, directional lighting
- Seasonal variations - Summer/winter color palettes
- Fog of War styles - Different unexplored area visualizations

## Related Documentation

- **Engine-Game Separation Audit:** `Archon-Engine/Docs/engine-game-separation-audit.md`
- **Master Architecture:** `Archon-Engine/Docs/Engine/master-architecture-document.md`
- **GAME Layer Registry:** `Game/FILE_REGISTRY.md`
- **MAP Layer Registry:** `Archon-Engine/Scripts/Map/FILE_REGISTRY.md`

---

**Architecture Status:** ✅ COMPLIANT
**Implementation Status:** ✅ COMPLETE
**Separation Level:** GAME owns complete visual policy, ENGINE provides rendering infrastructure

---

*Last Updated: 2025-10-15*
