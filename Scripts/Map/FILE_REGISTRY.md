# Map Layer File Registry
**Namespace:** `Map.*`
**Purpose:** GPU-accelerated presentation layer - textures, rendering, interaction
**Architecture Rules:**
- ✅ GPU compute shaders for visual processing (NO CPU pixel ops)
- ✅ Single draw call for base map
- ✅ Presentation only (does NOT affect simulation)
- ✅ Reads from Core layer, updates textures

**Status:** ✅ Texture-based rendering system operational

---

## Map/Core/ - Map System Coordination

### **MapInitializer.cs**
- **Purpose:** Initialize all map subsystems in correct order
- **Orchestrates:** MapTextureManager, MapRenderer, ProvinceSelector
- **Status:** ✅ Initialization flow

### **MapSystemCoordinator.cs**
- **Purpose:** Coordinate map subsystems (rendering, interaction, modes)
- **API:** UpdateSystems(), SyncWithSimulation()
- **Status:** ✅ System orchestration

---

## Map/Rendering/ - GPU Rendering Pipeline

### **MapTextureManager.cs** [HOT_PATH] [STABLE]
- **Purpose:** Manage all map textures (IDs, owners, colors, borders)
- **Textures:** ProvinceID (R16G16), Owner (R16), Color (RGBA32), Border (R8)
- **Memory:** ~60MB total for 5632×2048 map
- **API:** SetProvinceID(), SetProvinceOwner(), BindTexturesToMaterial()
- **Status:** ✅ Texture infrastructure
- **Lines:** 522

### **MapRenderer.cs** [HOT_PATH]
- **Purpose:** Single draw call map rendering
- **Pattern:** Quad mesh + texture shader
- **API:** Render(), UpdateMaterial()
- **Status:** ✅ Core rendering

### **MapRenderingCoordinator.cs**
- **Purpose:** Coordinate rendering subsystems
- **Uses:** MapRenderer, BorderComputeDispatcher, TextureUpdateBridge
- **Status:** ✅ Rendering pipeline

### **BorderComputeDispatcher.cs** [GPU] [HOT_PATH]
- **Purpose:** Dispatch compute shader for border generation
- **Performance:** 2ms for 10k provinces on GPU
- **Shader:** Detects province boundaries via neighbor comparison
- **Status:** ✅ GPU border detection

### **OwnerTextureDispatcher.cs** [GPU]
- **Purpose:** Update owner texture from simulation state
- **Pattern:** CPU → GPU texture upload
- **Status:** ✅ Ownership visualization

### **TextureStreamingManager.cs**
- **Purpose:** Stream texture LODs for memory optimization
- **Status:** ✅ Texture streaming

### **TextureUpdateBridge.cs**
- **Purpose:** Bridge simulation state changes to GPU textures
- **Pattern:** Listen to EventBus → Update textures
- **Status:** ✅ State-to-texture sync

### **MapTexturePopulator.cs**
- **Purpose:** Populate textures from loaded map data
- **API:** PopulateFromBitmap(), PopulateFromProvinceSystem()
- **Status:** ✅ Texture population

---

## Map/Interaction/ - User Input & Selection

### **ProvinceSelector.cs** [HOT_PATH]
- **Purpose:** Ultra-fast province selection via texture lookup
- **Performance:** <1ms province detection (no raycasting!)
- **API:** GetProvinceAtMouse() → reads ProvinceID texture
- **Status:** ✅ Texture-based selection
- **Note:** 1000x faster than physics raycasting

### **ParadoxStyleCameraController.cs**
- **Purpose:** Grand strategy camera controls (pan, zoom, edge scrolling)
- **Status:** ✅ Camera control

---

## Map/MapModes/ - Visual Display Modes

### **IMapModeHandler.cs**
- **Purpose:** Interface for map mode implementations
- **API:** UpdateTexture(), OnEnter(), OnExit()
- **Status:** ✅ Map mode interface

### **MapModeManager.cs**
- **Purpose:** Switch between map modes (political, terrain, development)
- **API:** SetMapMode(), GetCurrentMode()
- **Status:** ✅ Mode management

### **PoliticalMapMode.cs**
- **Purpose:** Show province ownership colors
- **Status:** ✅ Political visualization

### **TerrainMapMode.cs**
- **Purpose:** Show terrain types (grassland, mountains, ocean)
- **Status:** ✅ Terrain visualization

### **DevelopmentMapMode.cs**
- **Purpose:** Show province development levels (heatmap)
- **Status:** ✅ Development visualization

### **MapModeDataTextures.cs**
- **Purpose:** Manage textures for different map modes
- **Status:** ✅ Mode texture management

### **TextureUpdateScheduler.cs**
- **Purpose:** Schedule texture updates to avoid frame spikes
- **Pattern:** Distribute updates across multiple frames
- **Status:** ✅ Update throttling

---

## Map/Province/ - Province Data Processing

### **ProvinceIDEncoder.cs** [STABLE]
- **Purpose:** Pack/unpack province IDs in R16G16 texture format
- **API:** PackProvinceID(ushort) → Color32, UnpackProvinceID(Color32) → ushort
- **Format:** R channel (low 8 bits) + G channel (high 8 bits)
- **Status:** ✅ ID encoding/decoding

### **ProvinceNeighborDetector.cs**
- **Purpose:** CPU-based neighbor detection (flood fill algorithm)
- **Status:** ✅ Adjacency detection

### **GPUProvinceNeighborDetector.cs** [GPU]
- **Purpose:** GPU compute shader for parallel neighbor detection
- **Performance:** Faster than CPU for large maps
- **Status:** ✅ GPU neighbor detection

### **ProvinceDataStructure.cs**
- **Purpose:** Data structures for province metadata
- **Status:** ✅ Province data

### **ProvinceMetadataGenerator.cs**
- **Purpose:** Generate metadata (center points, bounds, pixel counts)
- **API:** GenerateMetadata(), CalculateBounds()
- **Status:** ✅ Metadata generation

---

## Map/Loading/ - Map Data Loading

### **IMapDataProvider.cs**
- **Purpose:** Interface for map data sources
- **API:** LoadProvinceMap(), LoadDefinitions()
- **Status:** ✅ Provider interface

### **MapDataLoader.cs**
- **Purpose:** Load map from bitmap files (provinces.bmp, terrain.bmp)
- **API:** LoadFromBitmap(), ParseDefinitions()
- **Status:** ✅ Bitmap loading

---

## Map/Simulation/ - Map-Specific Simulation

### **SimulationMapLoader.cs**
- **Purpose:** Bridge between map loading and simulation layer
- **Status:** ✅ Simulation integration

### **StateValidator.cs**
- **Purpose:** Validate map state consistency
- **API:** ValidateProvinces(), CheckIntegrity()
- **Status:** ✅ State validation

---

## Map/Compatibility/ - Legacy/Migration Code

### **ProvinceMapping.cs** [LEGACY]
- **Purpose:** Old province mapping system
- **Status:** ⚠️ Consider removing after migration

### **Map.Loading.cs** [LEGACY]
- **Purpose:** Old map loading code
- **Status:** ⚠️ Legacy system

---

## Map/Debug/ - Debugging Tools

### **MapModeDebugUI.cs**
- **Purpose:** Debug UI for map mode testing
- **API:** ShowMapModeControls(), LogTextureStats()
- **Status:** ✅ Debug tools

---

## Map/Tests/ - Test Suite

### **Rendering/TextureInfrastructureTests.cs**
- **Purpose:** Test texture creation and management
- **Status:** ✅ Rendering tests

### **Simulation/CommandSystemTests.cs**
- **Purpose:** Test command execution on map
- **Status:** ✅ Command tests

### **Simulation/ProvinceStateTests.cs**
- **Purpose:** Test ProvinceState operations
- **Status:** ✅ State tests

### **Simulation/ProvinceSimulationTests.cs**
- **Purpose:** Test province simulation logic
- **Status:** ✅ Simulation tests

### **Simulation/SimulationMapLoaderTests.cs**
- **Purpose:** Test map loading integration
- **Status:** ✅ Loader tests

### **Integration/TextureSystemIntegrationTests.cs**
- **Purpose:** Test full texture pipeline
- **Status:** ✅ Integration tests

### **ProvinceMapLoaderTests.cs**
- **Purpose:** Test province map loading
- **Status:** ✅ Loader tests

### **MapDataIntegratorTests.cs**
- **Purpose:** Test map data integration
- **Status:** ✅ Integration tests

### **ProvinceIDEncoderTests.cs**
- **Purpose:** Test ID encoding/decoding
- **Status:** ✅ Encoder tests

---

## Map/ - Root Level

### **MapDataIntegrator.cs**
- **Purpose:** Integrate map data from multiple sources
- **API:** IntegrateMapData(), MergeSources()
- **Status:** ✅ Data integration

### **MapRendererSetup.cs**
- **Purpose:** Setup and configure MapRenderer
- **Status:** ✅ Renderer configuration

### **FastAdjacencyScanner.cs**
- **Purpose:** Fast province adjacency scanning
- **Pattern:** Optimized flood fill for large maps
- **Status:** ✅ Adjacency optimization

### **Color32Comparer.cs**
- **Purpose:** Color comparison for province detection
- **API:** Equals(Color32, Color32) with tolerance
- **Status:** ✅ Utility

---

## Key Patterns & Conventions

### GPU vs CPU
- **GPU-only operations:** Border generation, neighbor detection (large scale)
- **CPU operations:** Province selection (texture read), metadata generation
- **Rule:** NEVER process millions of pixels on CPU

### Texture Update Flow
```
Simulation Change → EventBus → TextureUpdateBridge → MapTextureManager → GPU
```

### Map Mode Flow
```
User Input → MapModeManager → IMapModeHandler → Update Textures → Render
```

### Province Selection Flow
```
Mouse Click → ScreenToMapUV → Texture.GetPixel() → UnpackProvinceID → Province
```

---

## Performance Critical Paths

### [HOT_PATH] Files - Profile Before Changing
- **MapTextureManager.cs** - Texture updates happen every frame
- **MapRenderer.cs** - Single draw call (keep it that way!)
- **ProvinceSelector.cs** - Called on every mouse move
- **BorderComputeDispatcher.cs** - GPU compute shader dispatch

### [GPU] Files - Compute Shader Territory
- **BorderComputeDispatcher.cs** - Border detection shader
- **GPUProvinceNeighborDetector.cs** - Neighbor detection shader
- **OwnerTextureDispatcher.cs** - Ownership update shader

---

## Quick Reference by Use Case

**Need to update province visual?**
→ `TextureUpdateBridge` listens to events → Updates `MapTextureManager`

**Need to add new map mode?**
→ Implement `IMapModeHandler` → Register in `MapModeManager`

**Need to select province at mouse?**
→ Use `ProvinceSelector.GetProvinceAtMouse()` (texture lookup)

**Need to generate borders?**
→ Use `BorderComputeDispatcher` (GPU compute shader)

**Need to load map from file?**
→ Use `MapDataLoader.LoadFromBitmap()`

**Need province neighbors?**
→ Use `GPUProvinceNeighborDetector` for large maps (parallel)

---

## Architecture Compliance

### ✅ DO
- Use GPU compute shaders for visual processing
- Keep rendering in single draw call
- Read from Core layer via events
- Cache expensive calculations per frame

### ❌ DON'T
- Process millions of pixels on CPU
- Create GameObjects for provinces
- Modify Core simulation state from Map layer
- Use multiple draw calls for base map

---

## Integration with Core Layer

### Data Flow: Core → Map
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

### NO Reverse Flow!
- Map layer is **presentation only**
- Map layer **cannot modify** Core simulation state
- All state changes go through Core.Commands

---

*Last Updated: 2025-09-30*
*Total Files: 44 scripts*
*Status: GPU-accelerated texture-based rendering operational*
