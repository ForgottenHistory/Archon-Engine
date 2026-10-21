# Shader Architecture

Archon provides a modular shader system where the ENGINE handles rendering infrastructure and the GAME controls visual policy. A game map shader is a **thin shell**: Properties, render state, and three includes. The render pass itself (vertex, tessellation, fragment composition) lives in the ENGINE, so engine fixes reach every game automatically.

## Architecture

```
YOUR GAME SHADER (.shader) — Properties + Tags + 3 includes
├── MapShaderSetup.hlsl (Engine) — URP libraries + DefaultCommon.hlsl (globals, material CBUFFER)
├── YourMapModes.hlsl (Game) — defines RenderMapMode()
│   ├── MapModeCommon.hlsl (Engine) — borders, fog, highlights, ID sampling
│   ├── MapModePolitical.hlsl (Game) — your political mode visuals
│   ├── MapModeTerrain.hlsl (Game) — your terrain mode visuals
│   └── MapModeDevelopment.hlsl (Game) — your development mode visuals
└── MapPassFlat.hlsl / MapPassTerrain.hlsl (Engine, #include_with_pragmas)
    └── MapFragment.hlsl (Engine) — debug → ocean → map mode → lighting → borders → highlights → fog → overlay
```

**Key principle:** GAME includes ENGINE, never the other way around. Archon is a separate repository and cannot reference paths outside itself.

## What ENGINE Provides

ENGINE shader includes live in `Assets/Archon-Engine/Shaders/Includes/`:

| File | Purpose |
|------|---------|
| `MapShaderSetup.hlsl` | First include: URP libraries + `DefaultCommon.hlsl` |
| `DefaultCommon.hlsl` | Engine globals (textures, buffers, style values), small per-material CBUFFER |
| `MapPassFlat.hlsl` | Flat map pass: entry points + pragmas (include last, with `#include_with_pragmas`) |
| `MapPassTerrain.hlsl` | Tessellated terrain pass: entry points + pragmas + `TERRAIN_DETAIL_MAPPING` keyword |
| `MapFragment.hlsl` | Pixel composition shared by both passes (calls your `RenderMapMode`) |
| `MapModeCommon.hlsl` | Province ID sampling, owner lookup, border rendering, fog of war, highlights |
| `DefaultLighting.hlsl` | Normal map lighting from heightmap |
| `DefaultEffects.hlsl` | Overlay texture blending (parchment, paper) |
| `DefaultDebugModes.hlsl` | Debug visualizations (province IDs, heightmap, normals, borders) |

ENGINE also provides **complete Default shaders** as working examples:
- `DefaultFlatMapShader.shader` — Flat 2D map
- `DefaultTerrainMapShader.shader` — Tessellated 3D terrain

These serve as your **copy source** (a ~70 line shell), not as shaders to use directly.

## What GAME Provides

GAME creates its own `.shader` files and map mode `.hlsl` files:

| File | Purpose |
|------|---------|
| `YourGame.shader` | Thin shell — Properties, render state, ENGINE setup + your dispatcher + ENGINE pass |
| `MapModePolitical.hlsl` | How political mode looks (colors, blending, unowned provinces) |
| `MapModeTerrain.hlsl` | How terrain mode looks (terrain colors, detail textures) |
| `MapModeDevelopment.hlsl` | How development gradient looks |
| `YourMapModes.hlsl` | Dispatcher — wires ENGINE utilities to GAME map mode renderers |

## Setup

### Step 1: Copy the Default Shader Shells

Copy from ENGINE to your GAME shader folder:

```
Copy: Assets/Archon-Engine/Shaders/DefaultFlatMapShader.shader
  To: Assets/Game/Shaders/YourFlatMap.shader

Copy: Assets/Archon-Engine/Shaders/DefaultTerrainMapShader.shader
  To: Assets/Game/Shaders/YourTerrainMap.shader
```

### Step 2: Rename the Shader

Change the shader name at the top of each file:

```hlsl
// Before
Shader "Archon/DefaultFlat"

// After
Shader "YourGame/FlatMap"
```

### Step 3: Create Your Map Modes Dispatcher

Create `Assets/Game/Shaders/MapModes/YourMapModes.hlsl`:

```hlsl
#ifndef YOUR_MAP_MODES_INCLUDED
#define YOUR_MAP_MODES_INCLUDED

// ENGINE utilities (borders, fog, province sampling)
#include "Assets/Archon-Engine/Shaders/Includes/MapModeCommon.hlsl"

// YOUR visual policy
#include "MapModeTerrain.hlsl"
#include "MapModePolitical.hlsl"
#include "MapModeDevelopment.hlsl"

// Custom GAME map modes via province palette
float4 RenderCustomMapMode(uint provinceID, float2 uv)
{
    if (provinceID == 0) return _OceanColor;

    int rowsPerMode = (_MaxProvinceID + 255) / 256;
    int col = provinceID % 256;
    int row = (provinceID / 256) + (_CustomMapModeIndex * rowsPerMode);

    float2 paletteSize;
    _ProvincePaletteTexture.GetDimensions(paletteSize.x, paletteSize.y);
    float2 paletteUV = float2((col + 0.5) / paletteSize.x, (row + 0.5) / paletteSize.y);

    float4 color = SAMPLE_TEXTURE2D_LOD(_ProvincePaletteTexture,
        sampler_ProvincePaletteTexture, paletteUV, 0);

    return (color.a < 0.01) ? _OceanColor : color;
}

// Main dispatcher
float4 RenderMapMode(int mapMode, uint provinceID, float2 uv, float3 positionWS)
{
    if (mapMode == 0)       return RenderPolitical(provinceID, uv);
    else if (mapMode == 1)  return RenderTerrain(provinceID, uv, positionWS);
    else if (mapMode == 12) return RenderProvinceColors(provinceID, uv);
    else if (mapMode >= 2 && mapMode < 100)
                            return RenderCustomMapMode(provinceID, uv);
    else                    return RenderPolitical(provinceID, uv);
}

#endif
```

### Step 4: Point the Includes at ENGINE and Your Dispatcher

The copied shell uses paths relative to the ENGINE folder. In your copy, use absolute ENGINE paths and swap the dispatcher:

```hlsl
HLSLPROGRAM
#include "Assets/Archon-Engine/Shaders/Includes/MapShaderSetup.hlsl"

// YOUR visual policy (must define RenderMapMode)
#include "MapModes/YourMapModes.hlsl"

// ENGINE pass (MapPassFlat.hlsl or MapPassTerrain.hlsl)
#include_with_pragmas "Assets/Archon-Engine/Shaders/Includes/MapPassTerrain.hlsl"
ENDHLSL
```

Use `#include_with_pragmas` for the pass, or its entry point pragmas are ignored. Render state (`Tags`, `ZWrite`, `Cull`, `FallBack`) stays in your `.shader` file.

### Step 5: Copy Map Mode hlsl Files

Copy the ENGINE's default map mode implementations as your starting point:

```
Copy: Assets/Archon-Engine/Shaders/Includes/MapModePolitical.hlsl
  To: Assets/Game/Shaders/MapModes/MapModePolitical.hlsl

Copy: Assets/Archon-Engine/Shaders/Includes/MapModeTerrain.hlsl
  To: Assets/Game/Shaders/MapModes/MapModeTerrain.hlsl
```

These are now yours to customize. Change colors, blending, terrain rendering — whatever fits your game's visual identity.

### Step 6: Assign Material

In Unity, switch your map material to use `YourGame/FlatMap` or `YourGame/TerrainMap`.

## Available Shaders

### Flat Map (No Tessellation)

2D flat map rendering. Lower GPU requirements, works everywhere.

- ENGINE default: `Archon/DefaultFlat`
- Copy source: `DefaultFlatMapShader.shader`

### Terrain Map (Tessellated)

3D terrain with heightmap displacement. Requires shader model 4.6+.

- ENGINE default: `Archon/DefaultTerrain`
- Copy source: `DefaultTerrainMapShader.shader`
- Extra properties: `_HeightScale`, `_TessellationFactor`, `_TessellationMinDistance`, `_TessellationMaxDistance`
- Keyword: `TERRAIN_DETAIL_MAPPING` for detail texture blending

## Map Mode Rendering Functions

Your map mode hlsl files must provide these functions:

### Required by Political Mode

```hlsl
float4 RenderPolitical(uint provinceID, float2 uv)
```

### Required by Terrain Mode

```hlsl
float4 RenderTerrain(uint provinceID, float2 uv, float3 positionWS)
float4 RenderTerrain(uint provinceID, float2 uv)  // 2-param overload
float4 RenderProvinceColors(uint provinceID, float2 uv)  // Debug mode 12
```

### Optional (Custom Modes)

Custom map modes (mode 2+) use the province palette system via `GradientMapMode` C# class. No shader code needed — the `RenderCustomMapMode()` function in your dispatcher handles it.

## Stage Overrides

The ENGINE pass composes each pixel in a fixed order: debug modes → ocean → `RenderMapMode` → lighting → borders → highlights → fog → overlay → post composite. To replace one stage, `#define` its macro and write the function **before** the pass include (your dispatcher is the natural place). Everything else stays ENGINE default. Resolution is compile-time, so unused hooks cost nothing.

| Macro | Function you provide |
|-------|----------------------|
| `ARCHON_OVERRIDE_OCEAN` | `float4 ArchonOcean(ArchonMapContext ctx)` |
| `ARCHON_OVERRIDE_LIGHTING` | `float4 ArchonLighting(float4 color, ArchonMapContext ctx)` |
| `ARCHON_OVERRIDE_BORDERS` | `float4 ArchonBorders(float4 color, ArchonMapContext ctx)` |
| `ARCHON_OVERRIDE_HIGHLIGHTS` | `float4 ArchonHighlights(float4 color, ArchonMapContext ctx)` |
| `ARCHON_OVERRIDE_FOG` | `float4 ArchonFog(float4 color, ArchonMapContext ctx)` |
| `ARCHON_OVERRIDE_OVERLAY` | `float4 ArchonOverlay(float4 color, ArchonMapContext ctx)` |
| `ARCHON_POST_COMPOSITE` | `float4 ArchonPostComposite(float4 color, ArchonMapContext ctx)` (extra stage, no default) |

`ArchonMapContext` holds `uv`, `positionWS`, `provinceID`, `mapMode`. Fields may be added later. Signatures won't change.

Example: warmer fog that wraps the ENGINE default.

```hlsl
// In YourMapModes.hlsl
#include "Assets/Archon-Engine/Shaders/Includes/MapModeCommon.hlsl" // defines ApplyFogOfWar

#define ARCHON_OVERRIDE_FOG
float4 ArchonFog(float4 color, ArchonMapContext ctx)
{
    float4 fogged = ApplyFogOfWar(color, ctx.uv);
    return lerp(fogged, fogged * float4(1.1, 0.95, 0.8, 1), 0.5);
}
```

To wrap lighting or overlay, include `DefaultLighting.hlsl` / `DefaultEffects.hlsl` the same way (all include-guarded). Need a whole different pipeline? Write your own pass instead of using `MapPass*.hlsl` (Level 3).

## ENGINE Globals Contract

ENGINE publishes its textures and style values as **shader globals** (`MapShaderGlobals.cs`). Your shader just includes `DefaultCommon.hlsl` and samples them. Consequences:

- **Never** declare an engine global in your Properties block. A material property shadows the global, so the value would be stuck at the material default.
- Engine textures have no `_ST` tiling. Don't use `TRANSFORM_TEX` on them.
- Swapping materials needs no rebinding. Globals survive the swap.
- Per-material values (tessellation, your own tweakables) go in the Properties block **and** the `UnityPerMaterial` CBUFFER. To add GAME fields, `#define ARCHON_GAME_MATERIAL_PROPERTIES` (field declarations) before including `DefaultCommon.hlsl`.

Key textures available:

| Texture | Format | Purpose |
|---------|--------|---------|
| `_ProvinceIDTexture` | ARGB32, Point | Province ID per pixel (RG encoding) |
| `_ProvinceOwnerTexture` | R32_SFloat, Point | Owner country ID per pixel |
| `_CountryColorPalette` | RGBA32 | Country color by ID |
| `_ProvinceTerrainTexture` | RGBA32 | Raw terrain.png colors |
| `_DetailIndexTexture` | RGBA8 | 4 terrain material indices per pixel |
| `_DetailMaskTexture` | RGBA8 | 4 terrain blend weights per pixel |
| `_HeightmapTexture` | R8 | Heightmap for terrain/lighting |
| `_PixelPerfectBorderTexture` | RGBA8, Point | R=country, G=province borders |
| `_DistanceFieldBorderTexture` | RGBA8, Bilinear | R=country, G=province distance |
| `_ProvincePaletteTexture` | RGBA32, Point | Custom map mode colors by province ID |

Full declarations in `DefaultCommon.hlsl`.

## Contract Validation and Custom Parameters

Every map shader value is registered in `MapShaderParameterRegistry` with its type, scope (Global or Material), default, and whether it's required. When a visual style is applied, the material's shader is checked and an error names the shader and property for:

- An engine **global** declared in the Properties block (the material value would shadow it)
- A **required material** property that's missing (e.g. `_HighlightStrength`, `_TriPlanarTightenFactor`)

**Adding GAME parameters** (e.g. a value read by your `ArchonPostComposite` hook):

```csharp
// GAME init, before the visual style is applied
MapShaderParameterRegistry.RegisterGlobal("_CoastGlowStrength", ShaderValue.Float(0.5f), "YourGame");
MapShaderParameterRegistry.ApplyGlobalDefaults(); // or set it later
```

```hlsl
// Your shader code (declare as a global, NOT in Properties)
float _CoastGlowStrength;
```

Set values per style with **Custom Shader Parameters** on `VisualStyleConfiguration` (name, type, value). They're applied by a generic loop after the built-in style. Registered names use their registered scope; unregistered names use the scope set on the entry.

## Style Data Files (Modding)

A visual style can be overridden by a JSON5 file, so mods can change the map look in a shipped build. The path is set per style (`VisualStyleConfiguration.styleDataFile`, default `map/visual_style.json5`) and resolved through `DataFileResolver`, so a file in the override folder wins over the base data. It's applied last on every style apply. In the editor the file is watched: saving it re-applies the style within a second. Toggle with `GameSettings.WatchStyleDataFile`; `WatchStyleDataFileInDevelopmentBuilds` enables it in development builds (never in release builds).

```json5
{
  parameters: {
    _OceanColor: [25, 40, 112],                  // [r,g,b(,a)] 0-1 or 0-255, or "#rrggbb"
    _CountryBorderStrength: 0.9,
    _OverlayTexture: { texture: "gfx/map/parchment.png", filter: "bilinear", wrap: "repeat" },
    _OverlayStrength: 0.3,
    _CoastGlowStrength: { value: 0.5, type: "float", scope: "global" }  // unregistered GAME parameter
  }
}
```

- Keys are registry parameter names. Registered names get their type and scope from the registry.
- Textures are loaded from PNG/JPG at runtime, relative to the data directory.
- Engine-managed values (simulation textures, map mode, border mode, fog toggle) are refused with a warning.
- Errors name the file and key. Bad entries are skipped, the rest still apply.
- In play mode, the `VisualStyleManager` context menu **Export Style To Data File** writes every current value to the override folder as a starting template.

## Anti-Patterns

- **Don't** use ENGINE Default shaders directly for your game — copy the shell
- **Don't** copy the pass code into your shader. Include `MapPass*.hlsl` instead
- **Don't** fork ENGINE hlsl files in place — copy to GAME and customize
- **Don't** duplicate `DefaultCommon.hlsl` — always include from ENGINE path
- **Don't** put game-specific colors or blending in ENGINE shader code
- **Don't** create ENGINE→GAME include dependencies

## Best Practices

- **Do** include ENGINE utilities from absolute path (`Assets/Archon-Engine/Shaders/...`)
- **Do** keep your map mode hlsl files as GAME visual policy
- **Do** use `_BorderRenderingMode` int for border dispatch (not heuristics)
- **Do** keep the render pass in ENGINE (`MapPass*.hlsl`). Pass improvements then reach your game without copying
- **Do** keep your Properties block limited to per-material values (it must match the `UnityPerMaterial` CBUFFER)
