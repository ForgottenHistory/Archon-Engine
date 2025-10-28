# Map Layer File Registry
**Namespace:** `Map.*`
**Purpose:** GPU-accelerated presentation layer - textures, rendering, interaction
**Rules:** GPU compute shaders for visuals, single draw call, presentation only (no simulation changes)

---

## Core/
**Map.Core.MapInitializer** - Initialize all map subsystems in correct order
**Map.Core.MapSystemCoordinator** - Coordinate map subsystems (rendering, interaction, modes)

---

## Rendering/
**Map.Rendering.MapTextureManager** - Facade coordinator for all map textures (delegates to texture sets)
**Map.Rendering.CoreTextureSet** - Core textures: Province ID (RenderTexture), Owner (RenderTexture), Color (Texture2D), Development (RenderTexture UAV-enabled for GPU writes)
**Map.Rendering.VisualTextureSet** - Visual textures: Terrain, Heightmap, Normal Map
**Map.Rendering.DynamicTextureSet** - Dynamic textures: Border, BorderMask (R8 sparse mask), Highlight RenderTextures
**Map.Rendering.PaletteTextureManager** - Color palette texture (256×1 RGBA32) with HSV distribution
**Map.Rendering.MapRenderer** - Single draw call map rendering
**Map.Rendering.MapRenderingCoordinator** - Coordinate rendering subsystems
**Map.Rendering.BorderComputeDispatcher** - Orchestrates border rendering: initializes curve extraction/cache/renderer, uploads GPU data, generates border mask & distance fields
**Map.Rendering.BorderCurveExtractor** - Extract border pixel chains from province pairs, chain into polylines, merge chains (uses AdjacencySystem)
**Map.Rendering.BezierCurveFitter** - Fit curves to pixel chains (currently: polyline approach - straight segments between pixels)
**Map.Rendering.BorderCurveCache** - Cache Bézier/polyline segments with metadata (type, provinces, colors)
**Map.Rendering.BorderCurveRenderer** - Upload curve segments to GPU, manage curve buffers, spatial grid
**Map.Rendering.SpatialHashGrid** - Spatial acceleration structure (88×32 grid, 64px cells) for O(nearby) curve lookup
**Map.Rendering.BorderDistanceFieldGenerator** - Generate signed distance field for borders using jump flooding algorithm
**Map.Rendering.OwnerTextureDispatcher** - Update owner texture from simulation state
**Map.Rendering.TextureStreamingManager** - Stream texture LODs for memory optimization
**Map.Rendering.TextureUpdateBridge** - Bridge simulation state changes to GPU textures via EventBus
**Map.Rendering.MapTexturePopulator** - Populate textures from loaded map data
**Map.Rendering.BillboardAtlasGenerator** - Generate texture atlases for billboard rendering
**Map.Rendering.FogOfWarSystem** - Fog of war rendering system
**Map.Rendering.InstancedBillboardRenderer** - Instanced rendering for billboards (units, buildings)

---

## Interaction/
**Map.Interaction.ProvinceSelector** - Ultra-fast province selection via texture lookup (<1ms)
**Map.Interaction.ProvinceHighlighter** - Province highlighting system

---

## MapModes/
**Map.MapModes.IMapModeHandler** - Interface for map mode implementations
**Map.MapModes.MapModeManager** - Switch between map modes (political, terrain, development)
**Map.MapModes.DebugMapModeHandler** - Generic handler for debug visualization modes
**Map.MapModes.MapModeDataTextures** - Manage textures for different map modes
**Map.MapModes.TextureUpdateScheduler** - Schedule texture updates to avoid frame spikes
**Map.MapModes.ColorGradient** - Color gradient utilities for map modes
**Map.MapModes.GradientMapMode** - [GPU] Base class for gradient-based map modes (GPU compute shader, ~1ms per update)
**Map.MapModes.GradientComputeDispatcher** - [GPU] Dispatch gradient colorization compute shader (manages ComputeBuffers)

---

## Province/
**Map.Province.ProvinceIDEncoder** - Pack/unpack province IDs in R16G16 texture format
**Map.Province.ProvinceNeighborDetector** - CPU-based neighbor detection (flood fill)
**Map.Province.GPUProvinceNeighborDetector** - GPU compute shader for parallel neighbor detection
**Map.Province.ProvinceDataStructure** - Data structures for province metadata
**Map.Province.ProvinceMetadataGenerator** - Generate metadata (center points, bounds, pixel counts)

---

## Loading/
**Map.Loading.IMapDataProvider** - Interface for map data sources
**Map.Loading.MapDataLoader** - Load map from bitmap files (provinces.bmp, terrain.bmp, heightmap.bmp, world_normal.bmp)
**Map.Loading.ProvinceMapProcessor** - Process loaded province map data

### Loading/Bitmaps/
**Map.Loading.Bitmaps.BitmapTextureLoader** - Base bitmap texture loading utilities
**Map.Loading.Bitmaps.HeightmapBitmapLoader** - Load heightmap bitmap (R8 grayscale)
**Map.Loading.Bitmaps.NormalMapBitmapLoader** - Load normal map bitmap (RGB24)
**Map.Loading.Bitmaps.TerrainBitmapLoader** - Load terrain bitmap

### Loading/Data/
**Map.Loading.Data.TerrainColorMapper** - Map terrain colors to terrain types

---

## Simulation/
**Map.Simulation.SimulationMapLoader** - Bridge between map loading and simulation layer
**Map.Simulation.StateValidator** - Validate map state consistency

---

## Integration/
**Map.Integration.MapDataIntegrator** - Coordinate province data integration (high-level orchestrator)
**Map.Integration.ProvinceDataConverter** - Convert ProvinceMapLoader.LoadResult to ProvinceDataManager format
**Map.Integration.ProvinceTextureSynchronizer** - Synchronize CPU province data with GPU textures
**Map.Integration.ProvinceMetadataManager** - Manage province metadata (neighbors, terrain flags, coastal status)

---

## Compatibility/
**Map.Compatibility.ProvinceMapping** - Old province mapping system (legacy)
**Map.Compatibility.Map.Loading** - Old map loading code (legacy)

---

## Tests/
**Map.Tests.MapDataIntegratorTests** - Test map data integration
**Map.Tests.ProvinceIDEncoderTests** - Test ID encoding/decoding
**Map.Tests.ProvinceMapLoaderTests** - Test province map loading

### Tests/Rendering/
**Map.Tests.Rendering.TextureInfrastructureTests** - Test texture creation and management

### Tests/Simulation/
**Map.Tests.Simulation.CommandSystemTests** - Test command execution on map
**Map.Tests.Simulation.ProvinceStateTests** - Test ProvinceState operations
**Map.Tests.Simulation.ProvinceSimulationTests** - Test province simulation logic

---

## Root/
**Map.MapRenderer** - Single draw call map rendering
**Map.MapRendererSetup** - Setup and configure MapRenderer
**Map.MapTextureManager** - Facade for all map texture management
**Map.FastAdjacencyScanner** - Fast province adjacency scanning
**Map.Color32Comparer** - Color comparison for province detection
**Map.MapDataIntegrator** - High-level map data integration coordinator

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

*Updated: 2025-10-27 - Added BezierCurveFitter, BorderDistanceFieldGenerator to registry*
