# HOI4 Shader Loading System — Research Notes

Notes on how Hearts of Iron IV (Clausewitz engine, v1.19.3) loads shaders and how map mods change the game's look. They're written up as inspiration for a moddable map renderer in Unity. The aim is to learn from the design, not to copy it.

Sources: the game's shipped files (`gfx/FX/`), the user shader cache, and decompiling `hoi4.exe` in Ghidra.

---

## Summary

- HOI4 has no shader plugin system. Shaders ship as **plain-text source**, and mods **replace the files by path**.
- The engine **compiles shaders at runtime** with `D3DCompile` (DX11) and **caches the compiled output** on disk, keyed by a hash of the full source.
- Engine code and shaders agree on a small set of **names**: effect names, sampler slots and constant names. Anything inside the shader is up to the modder as long as those names stay the same.

---

## 1. Mods override files by path

All shaders live in `gfx/FX/` (58 files):

| File type | Purpose |
|---|---|
| `*.shader` | A full effect file (e.g. `pdxmap.shader` = terrain, `pdxwater.shader`, `border.shader`, `river.shader`, `tree.shader`, `sky.shader`, `bloom.shader`) |
| `*.fxh` | Shared include headers (`constants.fxh`, `pdxmap.fxh`, `fow.fxh`, `shadow.fxh`, `tiled_pointlights.fxh`, …) |
| `defines_hlsl.fxh`, `defines_hlsl_dx11.fxh`, `defines_glsl.fxh` | Per-backend compatibility macros (`PDX_POSITION`, `PDX_COLOR`, `tex2Dlod0`, `vec3()`, …) |

The engine has a virtual file system. If a mod contains `gfx/FX/pdxmap.shader`, the mod's copy replaces the vanilla file completely. Map/graphics mods usually change three layers:

1. **Shader source**: `gfx/FX/*.shader` / `*.fxh`
2. **Textures**: `map/terrain/*.dds` (`atlas0.dds`, `atlas_normal0.dds`, `border_*.dds`, colormaps, …)
3. **Tuning values**: `common/defines/00_graphics.lua`, plus the `static const` values in `constants.fxh`

Many "look" mods only edit `constants.fxh`, which is all tuning values:

```hlsl
static const float3 DayAmbientMapPosX   = float3(0.1, 0.1, 0.05);
static const float  CITY_LIGHTS_INTENSITY = 5.5;
static const float  TERRAIN_TILE_FREQ     = 128.0f;
static const float  SPECULAR_WIDTH        = 15.0;
```

---

## 2. The `.shader` file format

Paradox wraps HLSL in their own script syntax (the same brace syntax as the rest of their game data). Raw HLSL goes inside `[[ ... ]]` blocks.

Shortened from `pdxmap.shader`:

```
Includes = {
    "constants.fxh"
    "standardfuncsgfx.fxh"
    "pdxmap.fxh"
    "shadow.fxh"
    "fow.fxh"
}

PixelShader =
{
    Samplers =
    {
        TerrainDiffuse = { Index = 0  MagFilter = "Linear" MinFilter = "Linear" MipFilter = "Linear" AddressU = "Wrap" AddressV = "Wrap" }
        HeightNormal   = { Index = 1  ... }
        TerrainIDMap   = { Index = 5  MagFilter = "Point" MinFilter = "Point" MipFilter = "None" AddressU = "Clamp" AddressV = "Clamp" }
        ShadowMap      = { Index = 12 ... Type = "Shadow" }
        ...
    }
}

VertexStruct VS_OUTPUT_TERRAIN
{
    float4 position : PDX_POSITION;
    float2 uv       : TEXCOORD0;
    ...
};

VertexShader =
{
    MainCode VertexShader
    [[
        VS_OUTPUT_TERRAIN main( const VS_INPUT_TERRAIN_NOTEXTURE VertexIn ) { ... }
    ]]
}

PixelShader =
{
    MainCode PixelShaderTerrain
    [[
        float4 main( VS_OUTPUT_TERRAIN Input ) : PDX_COLOR { ... }
    ]]
}

BlendState BlendState
{
    BlendEnable = no
    AlphaTest = no
    WriteMask = "RED|GREEN|BLUE|ALPHA"
}

Effect terrain_low_gfx { VertexShader = "VertexShader" PixelShader = "PixelShaderTerrain" Defines = { "LOW_END_GFX" } }
Effect terrain         { VertexShader = "VertexShader" PixelShader = "PixelShaderTerrain" Defines = { "PDX_IMPROVED_BLINN_PHONG" } }
Effect underwater      { VertexShader = "VertexShader" PixelShader = "PixelShaderUnderwater" }
Effect terrainunlit    { VertexShader = "VertexShader" PixelShader = "PixelShaderTerrainUnlit" }
```

### Key concepts

- **Samplers are declared in data, not code.** Filtering and addressing are set per texture slot in the file, and the engine creates the sampler states from them.
- **`VertexStruct`** declares input/output structs in a backend-neutral way. Semantics like `PDX_POSITION` map to the right backend semantic through the defines headers.
- **`MainCode` blocks** are named entry points. One file can hold several vertex and pixel shaders.
- **`Effect`** is a named combination of one VS, one PS and a list of `#define`s. Quality levels and variants (`terrain` vs `terrain_low_gfx`) are just different Effects over the same code with different defines.
- **`BlendState`** (and depth/rasterizer state) are also data blocks.

---

## 3. The engine/shader contract

The C++ side only knows:

- **Effect names** to draw with (`"terrain"`, `"underwater"`, …)
- **Sampler slot indices and names** to bind textures to (TerrainDiffuse = 0, HeightNormal = 1, TerrainColorTint = 2, …)
- **Constants** it uploads every frame (e.g. `ViewProjectionMatrix`, `ShadowMapTextureMatrix`, `QuadOffset_Scale_IsDetail`, `vSnap[]`, map size values)
- **Vertex input layout** for each mesh type

A modder can rewrite the whole lighting model as long as the effect names, slots, inputs and constants still match. This contract is what makes the system moddable.

---

## 4. Runtime compilation and caching (from decompiling the binary)

The relevant function is at `0x142410b70` in hoi4 1.19.3. It logs errors with the source path `clausewitz\pdx_gfx\gfx_dx11.cpp`.

### Flow

1. The engine parses the `.shader` file and builds **one full HLSL source string per effect and stage**: the backend defines header (`defines_hlsl_dx11.fxh`), then the includes, the effect's `#define`s, the structs, and the `MainCode`.
2. **It computes a cache key** (`FUN_14240eee0`) from:
   - the compile flags (always including `0x8000`)
   - the source length
   - a **32-bit FNV-1a hash** of the full source text (offset basis `0x811c9dc5`, prime `0x1000193`)
3. **It looks in the cache** at
   `Documents/Paradox Interactive/Hearts of Iron IV/shadercache/dx11_win32/<profile>/`
   where `<profile>` is `vs_5_0` or `ps_5_0`. Files are named after the key in hex, e.g. `0008000000111000EF1DA5F5`.
   - `<key>.scache` holds a **copy of the full source text**. The engine checks that the length matches and runs `memcmp` against the current source, so a hash collision can't load the wrong shader.
   - `<key>.bin` holds the **compiled bytecode**.
4. **On a cache miss** it calls `D3DCompile(source, len, ..., profile, flags | 0x8000, ...)`.
   - On success it writes both `.scache` and `.bin` to the cache.
   - On failure it logs `"Compile error:\n <compiler output>"` and the effect isn't usable.
5. There's also a DX9 path (`shadercache/dx9/`, `D3DXCompileShader`) and an OpenGL path (`defines_glsl.fxh`) for other platforms.

### Why this design works for modding

- Modders ship **source**, not compiled binaries, so there's no SDK, no separate per-platform builds, and no engine version lock-in for the compiled output.
- The cache is keyed by **content**, so any edit invalidates it. Modders never clear the cache by hand.
- The source check in `.scache` makes the cache safe even though the hash is only 32 bits.
- On a typical install the cache grows to about 5,000 files, because every effect × defines × stage combination is its own entry.

---

## 5. Applying this to Unity

### The main limitation

**A Unity player build can't compile shaders at runtime.** Shaders are compiled in the editor for each target graphics API. So the Paradox approach of shipping source and compiling at launch doesn't carry over directly.

### Option A: data-driven look layer (recommended starting point)

Write one (or a few) map shaders with a **fixed, documented set of properties and keywords**, then let mods drive them through data files. This is the Unity version of Paradox's sampler slots, constants and `constants.fxh` tuning.

Example mod file:

```
terrain_material = {
    diffuse_atlas = "terrain/atlas0.png"
    normal_atlas  = "terrain/atlas_normal0.png"
    specular      = 0.4
    snow_tint     = { 0.9 0.95 1.0 }
    city_lights_intensity = 5.5
    features = { CITY_LIGHTS FOW_CLOUDS IMPROVED_BLINN_PHONG }
}
```

Runtime side:

- Textures: `Texture2D.LoadImage` (PNG/JPG) or a DDS loader for compressed formats
- Numbers and colours: `Material.SetFloat` / `SetColor` / `SetVector`
- Features: `Material.EnableKeyword` / `DisableKeyword` with `#pragma multi_compile` or `shader_feature`. This plays the role of Paradox's `Effect { Defines = { ... } }` variants. Keyword variants must be compiled into the build, so if you use `shader_feature`, make sure the variants you need aren't stripped.
- Optional: colour-grading **LUT textures** as a cheap, very moddable way to change the overall look.

This covers most of what HOI4 map mods actually do: retexture, recolour and tune lighting.

### Option B: AssetBundle shaders for advanced modders

Modders write shaders in the Unity editor, build an **AssetBundle** and the game loads it at runtime (`AssetBundle.LoadFromFile`, then `LoadAsset<Shader>` / `<Material>`). RimWorld and Cities: Skylines mods use this approach.

- Publish a **contract** like Paradox's: required property names, pass names / `LightMode` tags, vertex input layout, and global shader values you set each frame (`Shader.SetGlobalTexture`, `SetGlobalMatrix`, …).
- Ship a small **modding SDK project** containing your shared include files (lighting, fog-of-war, borders) so modders can `#include` and override pieces instead of starting from scratch. This matches Paradox's `.fxh` includes.
- Catches: bundles must be built with the **same Unity version, render pipeline (URP/HDRP/Built-in) and target graphics APIs** as the game. Unity upgrades can break mods.

### Option C: true runtime compilation (not recommended at first)

You could replicate Paradox exactly by bundling a shader compiler (DXC or glslang plus SPIRV-Cross) and creating GPU programs through low-level native plugins. It gives the most freedom, but it means fighting Unity's material system and supporting each graphics backend yourself.

### Ideas worth borrowing whichever option you pick

- **Name-based contract** between engine and shader, with everything else open to modders
- **Variants through defines/keywords** declared in data (quality levels, optional features)
- **Sampler and blend state described in data** next to the shader
- **Shared include headers** so mods override small pieces instead of whole shaders
- **Content-hashed caching** for anything expensive you generate from mod data (baked textures, province lookup maps, etc.), with a full-content check to guard against collisions
- **Clear compile/load error logging** that names the mod file at fault

---

## Reference: addresses (hoi4 1.19.3, image base `0x140000000`)

| Address | What it is |
|---|---|
| `0x142410b70` | DX11 shader compile entry: cache lookup → `D3DCompile` → cache write, logs "Compile error" |
| `0x14240eee0` | Cache key builder and lookup (flags + length + FNV-1a), `.scache` source check, `.bin` load |
| `0x142409e20` | Uses the `".shader"` extension string (likely the `.shader` file loader/parser) |
| `0x142b704b0` | String `"shadercache/dx11_win32/"` |
| `0x142b70878` | String `"shadercache/dx9/"` |
