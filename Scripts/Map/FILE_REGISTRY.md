# Map Layer File Registry
**Namespace:** `Map.*`
**Purpose:** GPU-accelerated presentation layer - textures, rendering, interaction
**Rules:** GPU compute shaders for visuals, single draw call, presentation only (no simulation changes)

---

## Core/
- **Map.Core.MapInitializer** - Initialize all map subsystems in correct order
- **Map.Core.MapSystemCoordinator** - Coordinate map subsystems (rendering, interaction, modes)

---

## Rendering/
- **Map.Rendering.MapTextureManager** - Facade coordinator for all map textures (delegates to texture sets)
- **Map.Rendering.CoreTextureSet** - Core textures: Province ID (RenderTexture), Owner (RenderTexture), Color (Texture2D), Development (RenderTexture UAV-enabled for GPU writes)
- **Map.Rendering.VisualTextureSet** - Visual textures: Terrain, Heightmap, Normal Map, Texture2DArray (27 terrain detail textures)
- **Map.Rendering.DynamicTextureSet** - [BURST] Dynamic textures with mode-aware binding: DistanceField/DualBorder RenderTextures, Highlight, FogOfWar
- **Map.Rendering.PaletteTextureManager** - Color palette texture (256×1 RGBA32) with HSV distribution
- **Map.Rendering.MapRenderer** - Single draw call map rendering
- **Map.Rendering.MapRenderingCoordinator** - Coordinate rendering subsystems
- **Map.Rendering.OwnerTextureDispatcher** - Update owner texture from simulation state
- **Map.Rendering.TextureStreamingManager** - Stream texture LODs for memory optimization
- **Map.Rendering.TextureUpdateBridge** - Bridge simulation state changes to GPU textures via EventBus
- **Map.Rendering.MapTexturePopulator** - Populate textures from loaded map data
- **Map.Rendering.BillboardAtlasGenerator** - Generate texture atlases for billboard rendering
- **Map.Rendering.FogOfWarSystem** - Fog of war rendering system
- **Map.Rendering.InstancedBillboardRenderer** - Instanced rendering for billboards (units, buildings)
- **Map.Rendering.NormalMapGenerator** - Generate normal maps from heightmaps for 3D terrain
- **Map.Rendering.TreeInstanceGenerator** - Generate tree instance positions and types
- **Map.Rendering.TreeInstanceRenderer** - Render trees using GPU instancing

### Rendering/Border/
- **Map.Rendering.BorderComputeDispatcher** - [OPTIMIZED] Orchestrates border rendering: conditionally loads curves only for MeshGeometry mode, skips for shader modes (~0ms for ShaderDistanceField)
- **Map.Rendering.BorderEnums** - Shared enums: BorderMode, BorderRenderingMode (single source of truth)
- **Map.Rendering.BorderCurveExtractor** - [BURST] Extract border curves with 3x Burst jobs: median filter (8x faster), pixel mapping rebuild (14x faster), border pixel detection (2x faster). Only runs for MeshGeometry mode.
- **Map.Rendering.BorderCurveCache** - Cache smooth polyline segments with runtime styles (static geometry + dynamic appearance pattern)
- **Map.Rendering.BorderDistanceFieldGenerator** - [GPU] Generate signed distance field using Jump Flooding Algorithm (JFA) - dual channel R=country, G=province (~14ms for 5.6M pixels)
- **Map.Rendering.Border.BorderShaderManager** - Manage compute shader loading and kernel initialization
- **Map.Rendering.Border.BorderParameterBinder** - Centralize border rendering parameters (thickness, colors, alphas)
- **Map.Rendering.Border.BorderStyleUpdater** - Update border styles based on ownership (country vs province classification)
- **Map.Rendering.Border.BorderDebugUtility** - Debug utilities and benchmarking tools
- **Map.Rendering.Border.MedianFilterProcessor** - [BURST] 3x3 median filter with parallel job (7.3x speedup: 3.9s → 0.5s)
- **Map.Rendering.Border.JunctionDetector** - Detect junction pixels where 3+ provinces meet
- **Map.Rendering.Border.BorderChainMerger** - Merge border chains with U-turn detection
- **Map.Rendering.Border.BorderGeometryUtils** - Geometric utilities (intersection, angles, distances)
- **Map.Rendering.Border.BorderPolylineSimplifier** - RDP simplification, Chaikin smoothing, tessellation
- **Map.Rendering.BorderMeshGenerator** - Generate triangle strip meshes from border curves (MeshGeometry mode only)
- **Map.Rendering.BorderMeshRenderer** - Render border meshes (MeshGeometry mode only)
- **Map.Rendering.BorderTextureDebug** - Debug visualization for border textures

### Rendering/Terrain/
- **Map.Rendering.Terrain.TerrainBlendMapGenerator** - [GPU] Imperator Rome-style 4-channel blend map generation: samples ProvinceIDTexture in configurable radius (default 5x5), counts terrain types via ProvinceTerrainBuffer, outputs DetailIndexTexture (RGBA8: 4 indices) + DetailMaskTexture (RGBA8: 4 weights). Configurable sample radius and blend sharpness. (~50-100ms at load time)
- **Map.Rendering.Terrain.ProvinceTerrainAnalyzer** - [GPU] Analyze terrain.bmp per province, generate ProvinceTerrainBuffer (65536 entries, uint per province). Feeds TerrainBlendMapGenerator.

---

## Interaction/
- **Map.Interaction.ProvinceSelector** - Ultra-fast province selection via texture lookup (<1ms)
- **Map.Interaction.ProvinceHighlighter** - Province highlighting system

---

## MapModes/
- **Map.MapModes.IMapModeHandler** - Interface for map mode implementations
- **Map.MapModes.MapModeManager** - Switch between map modes (political, terrain, development)
- **Map.MapModes.DebugMapModeHandler** - Generic handler for debug visualization modes
- **Map.MapModes.MapModeDataTextures** - Manage textures for different map modes
- **Map.MapModes.TextureUpdateScheduler** - Schedule texture updates to avoid frame spikes
- **Map.MapModes.ColorGradient** - Color gradient utilities for map modes
- **Map.MapModes.GradientMapMode** - [GPU] Base class for gradient-based map modes (GPU compute shader, ~1ms per update)
- **Map.MapModes.GradientComputeDispatcher** - [GPU] Dispatch gradient colorization compute shader (manages ComputeBuffers)

---

## Province/
- **Map.Province.ProvinceIDEncoder** - Pack/unpack province IDs in R16G16 texture format
- **Map.Province.ProvinceNeighborDetector** - CPU-based neighbor detection (flood fill)
- **Map.Province.GPUProvinceNeighborDetector** - GPU compute shader for parallel neighbor detection
- **Map.Province.ProvinceDataStructure** - Data structures for province metadata
- **Map.Province.ProvinceMetadataGenerator** - Generate metadata (center points, bounds, pixel counts)

---

## VisualStyles/
- **Map.VisualStyles.VisualStyleConfiguration** - ScriptableObject defining complete visual style (material, borders, fog of war, colors)
- **Map.VisualStyles.VisualStyleManager** - Applies styles to renderer, binds textures, F5 reload support

---

## Loading/
- **Map.Loading.IMapDataProvider** - Interface for map data sources
- **Map.Loading.MapDataLoader** - Load map from bitmap files (provinces.bmp, terrain.bmp, heightmap.bmp, world_normal.bmp). Orchestrates terrain blend map generation after terrain analysis.
- **Map.Loading.ProvinceMapProcessor** - Process loaded province map data
- **Map.Loading.DetailTextureArrayLoader** - Load terrain detail textures into Texture2DArray: scans Assets/Data/textures/terrain_detail/ for {index}_{name}.jpg/png files, supports 0-255 indices, missing textures filled with neutral gray (128,128,128), 512x512 per texture with mipmaps
- **Map.Loading.NoiseTextureGenerator** - Generate noise textures for terrain variation
- **Map.Loading.TerrainTypeTextureGenerator** - Generate terrain type textures from province data

### Loading/Bitmaps/
- **Map.Loading.Bitmaps.BitmapTextureLoader** - Base bitmap texture loading utilities
- **Map.Loading.Bitmaps.HeightmapBitmapLoader** - Load heightmap bitmap (R8 grayscale)
- **Map.Loading.Bitmaps.NormalMapBitmapLoader** - Load normal map bitmap (RGB24)
- **Map.Loading.Bitmaps.TerrainBitmapLoader** - Load terrain bitmap

### Loading/Data/
- **Map.Loading.Data.TerrainColorMapper** - Map terrain colors to terrain types

---

## Simulation/
- **Map.Simulation.SimulationMapLoader** - Bridge between map loading and simulation layer
- **Map.Simulation.StateValidator** - Validate map state consistency

---

## Integration/
- **Map.Integration.MapDataIntegrator** - Coordinate province data integration (high-level orchestrator)
- **Map.Integration.ProvinceDataConverter** - Convert ProvinceMapLoader.LoadResult to ProvinceDataManager format
- **Map.Integration.ProvinceTextureSynchronizer** - Synchronize CPU province data with GPU textures
- **Map.Integration.ProvinceMetadataManager** - Manage province metadata (neighbors, terrain flags, coastal status)

---

## Compatibility/
- **Map.Compatibility.ProvinceMapping** - Old province mapping system (legacy)
- **Map.Compatibility.Map.Loading** - Old map loading code (legacy)

---

## Tests/
- **Map.Tests.MapDataIntegratorTests** - Test map data integration
- **Map.Tests.ProvinceIDEncoderTests** - Test ID encoding/decoding
- **Map.Tests.ProvinceMapLoaderTests** - Test province map loading

### Tests/Rendering/
- **Map.Tests.Rendering.TextureInfrastructureTests** - Test texture creation and management

### Tests/Simulation/
- **Map.Tests.Simulation.CommandSystemTests** - Test command execution on map
- **Map.Tests.Simulation.ProvinceStateTests** - Test ProvinceState operations
- **Map.Tests.Simulation.ProvinceSimulationTests** - Test province simulation logic

---

## Root/
- **Map.MapRenderer** - Single draw call map rendering
- **Map.MapRendererSetup** - Setup and configure MapRenderer
- **Map.MapTextureManager** - Facade for all map texture management
- **Map.FastAdjacencyScanner** - Fast province adjacency scanning
- **Map.Color32Comparer** - Color comparison for province detection
- **Map.MapDataIntegrator** - High-level map data integration coordinator
- **Map.SubdividedPlaneMeshGenerator** - Generate subdivided plane meshes for GPU tessellation support

---

## Quick Reference
**Update province visual?** → TextureUpdateBridge listens to events → Updates MapTextureManager
**Add new map mode?** → Implement IMapModeHandler → Register in MapModeManager
**Select province at mouse?** → ProvinceSelector.GetProvinceAtMouse() (texture lookup)
**Generate borders?** → BorderComputeDispatcher (GPU compute shader)
**Load map from file?** → MapDataLoader.LoadFromBitmap()
**Province neighbors?** → GPUProvinceNeighborDetector for large maps

---

## Data Flow
```
Core.ProvinceSystem (state change)
    ↓
Core.EventBus (ProvinceOwnershipChangedEvent)
    ↓
Map.TextureUpdateBridge (listens)
    ↓
Map.MapTextureManager (updates texture)
    ↓
Map.MapRenderer (renders)
```

**IMPORTANT:** Map layer is presentation only and CANNOT modify Core simulation state. All state changes go through Core.Commands.

---

*Updated: 2025-11-17*
*Added: VisualStyles system, NormalMapGenerator, Tree rendering, Terrain texture loaders, SubdividedPlaneMeshGenerator*
*Fixed: All sections now use proper markdown formatting (dashes for line breaks)*
