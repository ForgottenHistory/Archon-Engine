# Map Rendering System

The map rendering system provides texture-based province rendering with GPU compute shaders, map modes, and single draw call efficiency.

## Architecture

```
Map Rendering Pipeline
├── MapRenderer           - Single quad mesh for entire map
├── CoreTextureSet        - Province ID, Owner, Color textures
├── VisualTextureSet      - Terrain, heightmap, normal textures
├── MapModeDataTextures   - Per-mode data textures
├── IMapModeHandler       - Map mode interface
└── BorderRenderer        - Province/country borders

Data Flow:
Simulation → GPU Textures → Shader → Single Draw Call
```

**Key Principles:**
- Single draw call for entire map (texture-based)
- GPU compute shaders for all pixel processing
- Point filtering on province textures (no interpolation)
- Dirty flag updates (only update changed data)

## Core Textures

### Province ID Texture

Encodes which province each pixel belongs to:

```csharp
// RenderTexture with R8G8B8A8_UNorm format
// Point filtering required - no interpolation
// Province ID = R + G*256 (16-bit encoding)
```

### Province Owner Texture

Maps each pixel to owning country:

```csharp
// RenderTexture with R32_SFloat format
// Updated when ownership changes
// Country ID stored as float for GPU precision
```

### Province Color Texture

Current visual color per pixel:

```csharp
// Texture2D with RGBA32 format
// Updated by map mode handlers
// Final display color for each province
```

## Map Modes

### Interface

```csharp
public interface IMapModeHandler
{
    MapMode Mode { get; }
    string Name { get; }
    int ShaderModeID { get; }
    bool RequiresFrequentUpdates { get; }

    void OnActivate(Material mapMaterial, MapModeDataTextures dataTextures);
    void OnDeactivate(Material mapMaterial);

    void UpdateTextures(
        MapModeDataTextures dataTextures,
        ProvinceQueries provinceQueries,
        CountryQueries countryQueries,
        ProvinceMapping mapping,
        object gameProvinceSystem = null
    );

    string GetProvinceTooltip(ushort provinceId, ...);
    UpdateFrequency GetUpdateFrequency();
}
```

### Available Map Modes

```csharp
public enum MapMode
{
    // Basic modes
    Political = 0,      // Owner → Country color
    Terrain = 1,        // Terrain type → Terrain color
    Development = 2,    // Development → Gradient
    Culture = 3,        // Culture → Culture color
    Religion = 4,       // Religion → Religion color

    // Composite modes
    Diplomatic = 5,     // Relations → Gradient
    Trade = 6,          // Trade value → Heatmap
    Military = 7,       // Army strength → Threat colors
    Economic = 8,       // Income → Green/red gradient

    // Special modes
    Selected = 9,       // Selection highlights
    StrategicView = 10, // Simplified military
    PlayerMapMode = 11, // Custom defined
    ProvinceColors = 12, // Provinces.bmp original colors

    // Debug modes (100+)
    BorderDebug = 100,
    ProvinceIDDebug = 101,
    HeightmapDebug = 102,
    NormalMapDebug = 103
}
```

### Update Frequency

```csharp
public enum UpdateFrequency
{
    Never = 0,        // Static (terrain)
    Yearly = 1,       // Very slow (culture)
    Monthly = 2,      // Slow (development)
    Weekly = 3,       // Regular (trade)
    Daily = 4,        // Fast (military during war)
    PerConquest = 5,  // Event-driven (political)
    RealTime = 6      // Continuous (selection)
}
```

### Implementing a Map Mode

```csharp
public class DevelopmentMapModeHandler : BaseMapModeHandler
{
    public override MapMode Mode => MapMode.Development;
    public override string Name => "Development";
    public override int ShaderModeID => 2;

    public override void OnActivate(Material material, MapModeDataTextures textures)
    {
        DisableAllMapModeKeywords(material);
        EnableMapModeKeyword(material, "MAP_MODE_DEVELOPMENT");
        SetShaderMode(material, ShaderModeID);
        LogActivation();
    }

    public override void OnDeactivate(Material material)
    {
        LogDeactivation();
    }

    public override void UpdateTextures(
        MapModeDataTextures textures,
        ProvinceQueries provinces,
        CountryQueries countries,
        ProvinceMapping mapping,
        object gameSystem = null)
    {
        // Update development texture via GPU compute shader
        // Use GradientComputeDispatcher for gradient colorization
    }

    public override UpdateFrequency GetUpdateFrequency()
        => UpdateFrequency.Monthly;

    public override string GetProvinceTooltip(ushort provinceId, ...)
        => $"Development: {GetDevelopment(provinceId)}";
}
```

## Texture Sets

### CoreTextureSet

Core gameplay textures:

```csharp
var coreTextures = new CoreTextureSet(mapWidth, mapHeight);
coreTextures.CreateTextures();
coreTextures.BindToMaterial(mapMaterial);

// Update colors
coreTextures.SetProvinceColor(x, y, color);
coreTextures.ApplyChanges();

// Cleanup
coreTextures.Release();
```

### VisualTextureSet

Visual/display textures:

```csharp
// Terrain texture - loaded from BMP
// Heightmap texture - loaded from BMP
// Normal map - generated from heightmap
// Detail textures - terrain-specific details
```

## Border Rendering

### Border Types

```csharp
// Province borders - thin lines between provinces
// Country borders - thick lines between countries
// Selection borders - highlight borders
```

### Border Renderers

Multiple implementations available:
- `PixelPerfectBorderRenderer` - Crisp pixel borders
- `DistanceFieldBorderRenderer` - Smooth scaled borders
- `MeshGeometryBorderRenderer` - 3D mesh borders
- `NoneBorderRenderer` - No borders (performance)

## Map Renderer

### Setup

```csharp
// MapRenderer creates subdivided quad mesh
// Bottom-left pivot with 0-1 UV mapping
// Configurable subdivisions for tessellation
[SerializeField] private int subdivisions = 100;
[SerializeField] private Vector2 mapSize = new Vector2(10f, 10f);
```

### Configuration

```csharp
// Set map dimensions
mapRenderer.SetMapSize(new Vector2(5632, 2048));

// Set material
mapRenderer.SetMaterial(mapMaterial);
```

## Shader Keywords

Map modes use shader keywords for branching:

```csharp
material.EnableKeyword("MAP_MODE_POLITICAL");
material.EnableKeyword("MAP_MODE_TERRAIN");
material.EnableKeyword("MAP_MODE_DEVELOPMENT");
// etc.
```

## GPU Compute Shaders

### Gradient Colorization

```csharp
// GradientComputeDispatcher applies gradient to development texture
var gradient = new ColorGradient(lowColor, highColor);
dispatcher.ApplyGradient(developmentTexture, gradient, minValue, maxValue);
```

### Border Detection

```csharp
// GPU compute shader detects province boundaries
// Parallel processing of all pixels
// Neighbor comparison for edge detection
```

## Integration Example

```csharp
public class MapModeManager
{
    private Dictionary<MapMode, IMapModeHandler> handlers;
    private IMapModeHandler activeHandler;
    private Material mapMaterial;
    private MapModeDataTextures dataTextures;

    public void SetMapMode(MapMode mode)
    {
        // Deactivate current
        activeHandler?.OnDeactivate(mapMaterial);

        // Activate new
        activeHandler = handlers[mode];
        activeHandler.OnActivate(mapMaterial, dataTextures);

        // Initial update
        activeHandler.UpdateTextures(dataTextures, ...);
    }

    public void OnMonthlyTick()
    {
        // Update if needed
        if (activeHandler.GetUpdateFrequency() <= UpdateFrequency.Monthly)
        {
            activeHandler.UpdateTextures(dataTextures, ...);
        }
    }
}
```

## Performance

- Single draw call for entire map
- GPU texture updates (no CPU pixel processing)
- Dirty flag optimization (only changed provinces)
- Point filtering (no interpolation overhead)
- Shader keyword branching (compiled variants)

## Best Practices

1. **Point filtering on province textures** - Never use bilinear/trilinear
2. **GPU compute for bulk operations** - Never process pixels on CPU
3. **Dirty flag updates** - Only update changed data
4. **Appropriate update frequency** - Don't update static data
5. **RenderTexture for GPU access** - Use for compute shader writes

## API Reference

- [MapRenderer](~/api/Map.Rendering.MapRenderer.html) - Quad mesh renderer
- [CoreTextureSet](~/api/Map.Rendering.CoreTextureSet.html) - Core textures
- [IMapModeHandler](~/api/Map.MapModes.IMapModeHandler.html) - Map mode interface
- [MapMode](~/api/Map.MapModes.MapMode.html) - Mode enumeration
- [UpdateFrequency](~/api/Map.MapModes.UpdateFrequency.html) - Update timing
