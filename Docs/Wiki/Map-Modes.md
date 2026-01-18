# Map Modes

Map modes change how the map is colored to visualize different data. The ENGINE provides the rendering infrastructure; your GAME defines what data to display.

## Architecture

```
GAME Layer (your code)
    ↓ implements
IMapModeHandler / GradientMapMode
    ↓ registered with
MapModeManager (ENGINE)
    ↓ updates
GPU Textures → Shader
```

- **ENGINE** provides: `GradientMapMode` base class, texture management, shader integration
- **GAME** provides: Data values per province, color gradients, tooltips

## Built-in Map Modes

The ENGINE provides:
- **Political** (ShaderModeID 0) - Country colors
- **Terrain** (ShaderModeID 1) - Terrain types

Custom map modes use ShaderModeID 2+.

## Creating a Custom Map Mode

### Extend GradientMapMode

```csharp
using Map.MapModes;
using Map.Rendering;
using Core.Queries;

namespace MyGame.MapModes
{
    public class FarmDensityMapMode : GradientMapMode
    {
        private readonly BuildingSystem buildingSystem;
        private readonly ushort farmTypeId;

        public FarmDensityMapMode(
            BuildingSystem buildings,
            MapModeManager mapModeManager)
        {
            buildingSystem = buildings;
            farmTypeId = buildings.GetBuildingType("farm")?.ID ?? 0;

            // Register to get a texture array slot
            RegisterWithMapModeManager(mapModeManager);
        }

        // Required properties
        public override MapMode Mode => MapMode.Economic;
        public override string Name => "Farm Density";
        public override int ShaderModeID => 2;  // First custom mode

        // How often to refresh (PerConquest, Monthly, Never)
        public override UpdateFrequency GetUpdateFrequency()
            => UpdateFrequency.PerConquest;

        // Define color gradient (low to high)
        protected override ColorGradient GetGradient()
        {
            return new ColorGradient(
                new Color32(240, 240, 220, 255),  // Cream (no farms)
                new Color32(255, 255, 150, 255),  // Light yellow
                new Color32(255, 200, 50, 255),   // Golden
                new Color32(255, 140, 0, 255),    // Orange
                new Color32(200, 80, 0, 255)      // Dark orange (max)
            );
        }

        // Return value for each province (0-1 normalized, or raw value)
        protected override float GetValueForProvince(
            ushort provinceId,
            ProvinceQueries provinceQueries,
            object gameProvinceSystem)
        {
            ushort owner = provinceQueries.GetOwner(provinceId);
            if (owner == 0)
                return -1f;  // Negative = use ocean color (skip)

            int farms = buildingSystem.GetBuildingCount(provinceId, farmTypeId);
            return farms > 0 ? farms : 0.001f;  // Small positive = "low"
        }

        // Tooltip text
        public override string GetProvinceTooltip(
            ushort provinceId,
            ProvinceQueries provinceQueries,
            CountryQueries countryQueries)
        {
            int farms = buildingSystem.GetBuildingCount(provinceId, farmTypeId);
            return $"Province {provinceId}\nFarms: {farms}";
        }

        // Called when mode becomes active
        public override void OnActivate(
            Material mapMaterial,
            MapModeDataTextures dataTextures)
        {
            OnMapModeActivated();  // Base class handles texture updates
            LogActivation("Showing farm density");
        }

        public override void OnDeactivate(Material mapMaterial)
        {
            LogDeactivation();
        }
    }
}
```

### Register the Map Mode

```csharp
public class MyInitializer : MonoBehaviour
{
    IEnumerator Start()
    {
        // ... wait for ENGINE ...

        var mapModeManager = FindFirstObjectByType<MapModeManager>();

        // Create and register custom map modes
        var farmMode = new FarmDensityMapMode(
            buildingSystem, mapModeManager);

        var terrainCostMode = new TerrainCostMapMode(
            gameState, mapModeManager);
    }
}
```

## Key Concepts

### GetValueForProvince()

Return values:
- **Positive (0.0 - 1.0)**: Normalized value for gradient
- **Positive (> 1.0)**: Raw value (base class normalizes)
- **Negative**: Skip province (use ocean color)
- **0.001**: Very small positive = "low" on gradient

### ColorGradient

Define 5 colors from low to high:

```csharp
new ColorGradient(
    color1,  // Lowest value
    color2,
    color3,  // Middle
    color4,
    color5   // Highest value
)
```

### UpdateFrequency

How often the map mode refreshes:

```csharp
public enum UpdateFrequency
{
    Never,        // Static data (terrain)
    PerConquest,  // When provinces change hands
    Monthly,      // Every game month
    Weekly,       // Every game week
    Daily         // Every game day (expensive!)
}
```

### ShaderModeID

Each map mode needs a unique ID:
- 0 = Political (built-in)
- 1 = Terrain (built-in)
- 2+ = Custom modes

```csharp
public override int ShaderModeID => 2;  // First custom
public override int ShaderModeID => 3;  // Second custom
```

## Terrain-Based Map Mode Example

```csharp
public class TerrainCostMapMode : GradientMapMode
{
    private readonly float[] terrainCosts;
    private readonly bool[] terrainIsWater;

    public TerrainCostMapMode(GameState gameState, MapModeManager manager)
    {
        // Cache terrain costs from registry
        var terrains = gameState.Registries.Terrains;
        terrainCosts = new float[maxTerrainId + 1];
        terrainIsWater = new bool[maxTerrainId + 1];

        foreach (var terrain in terrains.GetAll())
        {
            terrainCosts[terrain.TerrainId] = terrain.MovementCost;
            terrainIsWater[terrain.TerrainId] = terrain.IsWater;
        }

        RegisterWithMapModeManager(manager);
    }

    public override MapMode Mode => MapMode.Terrain;
    public override string Name => "Movement Cost";
    public override int ShaderModeID => 3;

    // Terrain doesn't change
    public override UpdateFrequency GetUpdateFrequency()
        => UpdateFrequency.Never;

    protected override ColorGradient GetGradient()
    {
        return new ColorGradient(
            new Color32(50, 180, 50, 255),    // Green (fast)
            new Color32(150, 200, 50, 255),  // Yellow-green
            new Color32(220, 200, 50, 255),  // Yellow
            new Color32(220, 140, 40, 255),  // Orange
            new Color32(180, 60, 40, 255)    // Red (slow)
        );
    }

    protected override float GetValueForProvince(
        ushort provinceId,
        ProvinceQueries provinceQueries,
        object _)
    {
        ushort terrainId = provinceQueries.GetTerrain(provinceId);

        if (terrainIsWater[terrainId])
            return -1f;  // Water = ocean color

        float cost = terrainCosts[terrainId];
        // Normalize to 0-1 range
        return (cost - minCost) / (maxCost - minCost);
    }
}
```

## Switching Map Modes

```csharp
// Via MapModeManager
mapModeManager.SetMapMode(MapMode.Economic);

// Or by ShaderModeID directly
mapModeManager.SetMapModeById(2);
```

## Category Labels

Override `GetValueCategory()` for tooltip descriptions:

```csharp
protected override string GetValueCategory(float value)
{
    int farms = Mathf.RoundToInt(value);
    if (farms >= 3) return "Fully Developed";
    if (farms >= 2) return "Well Developed";
    if (farms >= 1) return "Developing";
    return "Undeveloped";
}
```

## Best Practices

1. **Cache data lookups** - Don't query every province every frame
2. **Use appropriate UpdateFrequency** - Don't update more than needed
3. **Return -1 for skipped provinces** - Ocean/water/unowned
4. **Meaningful gradients** - Colors should convey information
5. **Clear tooltips** - Help player understand the data
6. **Register early** - Before player can switch modes

## StarterKit Examples

- `MapModes/FarmDensityMapMode.cs` - Building count heatmap
- `MapModes/TerrainCostMapMode.cs` - Movement cost visualization

Both demonstrate the `GradientMapMode` pattern with different data sources.
