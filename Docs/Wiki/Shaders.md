# Shader Architecture

Archon provides a modular shader system where the ENGINE handles rendering infrastructure and the GAME controls visual policy. Games **copy** the Default shaders as a starting point and customize from there.

## Architecture

```
YOUR GAME SHADER (.shader)
├── DefaultCommon.hlsl (Engine) — CBUFFER, texture declarations
├── YourMapModes.hlsl (Game) — replaces DefaultMapModes.hlsl
│   ├── MapModeCommon.hlsl (Engine) — borders, fog, highlights, ID sampling
│   ├── MapModePolitical.hlsl (Game) — your political mode visuals
│   ├── MapModeTerrain.hlsl (Game) — your terrain mode visuals
│   └── MapModeDevelopment.hlsl (Game) — your development mode visuals
├── DefaultLighting.hlsl (Engine) — normal map lighting
├── DefaultEffects.hlsl (Engine) — overlay texture
└── DefaultDebugModes.hlsl (Engine) — debug visualizations
```

**Key principle:** GAME includes ENGINE, never the other way around. Archon is a separate repository and cannot reference paths outside itself.

## What ENGINE Provides

ENGINE shader includes live in `Assets/Archon-Engine/Shaders/Includes/`:

| File | Purpose |
|------|---------|
| `DefaultCommon.hlsl` | All texture/buffer declarations, CBUFFER, SRP Batcher compatibility |
| `MapModeCommon.hlsl` | Province ID sampling, owner lookup, border rendering, fog of war, highlights |
| `DefaultLighting.hlsl` | Normal map lighting from heightmap |
| `DefaultEffects.hlsl` | Overlay texture blending (parchment, paper) |
| `DefaultDebugModes.hlsl` | Debug visualizations (province IDs, heightmap, normals, borders) |

ENGINE also provides **complete Default shaders** as working examples:
- `DefaultFlatMapShader.shader` — Flat 2D map
- `DefaultTerrainMapShader.shader` — Tessellated 3D terrain

These serve as your **copy source**, not as shaders to use directly.

## What GAME Provides

GAME creates its own `.shader` files and map mode `.hlsl` files:

| File | Purpose |
|------|---------|
| `YourGame.shader` | Main shader file — includes ENGINE + GAME hlsl |
| `MapModePolitical.hlsl` | How political mode looks (colors, blending, unowned provinces) |
| `MapModeTerrain.hlsl` | How terrain mode looks (terrain colors, detail textures) |
| `MapModeDevelopment.hlsl` | How development gradient looks |
| `YourMapModes.hlsl` | Dispatcher — wires ENGINE utilities to GAME map mode renderers |

## Setup: Copy-and-Customize

### Step 1: Copy Default Shaders

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

### Step 4: Replace the Include

In your copied `.shader` file, replace the ENGINE map modes include with your own:

```hlsl
// Before (ENGINE default)
#include "Includes/DefaultMapModes.hlsl"

// After (YOUR visual policy)
#include "MapModes/YourMapModes.hlsl"
```

Keep all other ENGINE includes unchanged:

```hlsl
// ENGINE mechanism (keep these)
#include "Assets/Archon-Engine/Shaders/Includes/DefaultCommon.hlsl"

// YOUR visual policy (replace this one)
#include "MapModes/YourMapModes.hlsl"

// ENGINE mechanism (keep these)
#include "Assets/Archon-Engine/Shaders/Includes/DefaultLighting.hlsl"
#include "Assets/Archon-Engine/Shaders/Includes/DefaultEffects.hlsl"
#include "Assets/Archon-Engine/Shaders/Includes/DefaultDebugModes.hlsl"
```

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

## ENGINE Texture Contract

All textures are bound by ENGINE C# code. Your shader just declares and samples them. Key textures available:

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

## Anti-Patterns

- **Don't** use ENGINE Default shaders directly for your game — copy them
- **Don't** fork ENGINE hlsl files in place — copy to GAME and customize
- **Don't** duplicate `DefaultCommon.hlsl` — always include from ENGINE path
- **Don't** put game-specific colors or blending in ENGINE shader code
- **Don't** create ENGINE→GAME include dependencies

## Best Practices

- **Do** include ENGINE utilities from absolute path (`Assets/Archon-Engine/Shaders/...`)
- **Do** keep your map mode hlsl files as GAME visual policy
- **Do** use `_BorderRenderingMode` int for border dispatch (not heuristics)
- **Do** update your copies when ENGINE adds new features you want
- **Do** keep Properties blocks in sync with `DefaultCommon.hlsl` CBUFFER
