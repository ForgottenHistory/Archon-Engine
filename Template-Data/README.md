# Template-Data

Sample game data for StarterKit demonstration. This is reference data showing the expected structure - games should create their own data directory.

## Overview

Template-Data follows Paradox-style conventions (EU4, CK3, HOI4) for familiarity, however Archon uses it's own formats. The ENGINE loads this data through a phase-based initialization pipeline, while GAME layer can extend with custom loaders.

## Directory Structure

```
Template-Data/
├── common/                    # Definitions (what exists)
│   ├── buildings/            # Building type definitions
│   ├── countries/            # Country definitions
│   └── country_tags/         # Tag registry mapping
├── history/                   # Initial state (what's set up)
│   └── provinces/            # Province ownership, terrain
├── localisation/              # Display strings
│   └── english/              # Language folder
├── map/                       # Map data
│   ├── provinces.png         # Province ID texture
│   ├── terrain.png           # Terrain type texture
│   ├── heightmap.png         # Height data
│   ├── definition.csv        # Province RGB mapping
│   └── terrain.json5         # Terrain definitions
├── textures/                  # Visual assets
└── units/                     # Unit type definitions
```

## Loading Pipeline

The ENGINE loads data through `EngineInitializer` in phases:

### Phase 1: Core Systems (0-5%)
- Creates GameState, EventBus, Registries
- No data files loaded yet

### Phase 2: Static Data (5-15%)
- **Localization** - `localisation/**/*.yml`
- **Loaders via Registry** - Auto-discovers `ILoaderFactory` implementations
- ENGINE loaders: `TerrainLoader` (terrain.json5)
- GAME can add custom loaders via `LoaderMetadata` attribute

### Phase 3: Province Data (15-40%)
- **definition.csv** - Creates all provinces (ID, RGB, name)
- **history/provinces/*.json5** - Sets ownership, terrain per province
- Provinces without history files exist but are unowned

### Phase 4: Country Data (40-60%)
- **country_tags/*.txt** - Maps TAG → filename
- **common/countries/*.json5** - Country definitions

### Phase 5: Reference Linking (60-65%)
- Resolves string references (e.g., owner: "RED" → countryId: 1)
- Validates cross-references

### Phase 6: Scenario Loading (65-75%)
- Optional scenario overrides

### Phase 7: Systems Warmup (75-100%)
- Cache warming, index building

## File Formats

### JSON5 (*.json5)
Structured data with comments and trailing commas allowed.

```json5
// This is a comment
{
  key: "unquoted keys work",
  trailing: "commas are fine",
}
```

### YML Localization (*_l_english.yml)
Paradox-style format. Key format: `KEY:version "text"`

```yaml
l_english:
 RED:0 "Red Empire"
 RED_ADJ:0 "Red"
 UI_CLOSE:0 "Close"
```

The `:0` is a version number (for mod compatibility in Paradox games, we always use 0).

### CSV Definition (definition.csv)
Province ID to RGB color mapping. Semicolon-separated.

```csv
province;red;green;blue;name;x
1;17;31;157;Province_1;
2;34;62;164;Province_2;
```

The `x` column is unused (Paradox compatibility).

### TXT Tag Registry (*.txt)
Maps 3-letter tags to country files. Hash comments.

```txt
# Country tags
RED = "countries/RedEmpire.json5"
BLU = "countries/BlueKingdom.json5"
```

## Data Files

### map/terrain.json5
Terrain type definitions. ENGINE reads: color, movement_cost, is_water.

```json5
{
  categories: {
    grasslands: {
      color: [86, 124, 27],
      movement_cost: 1.0
    },
    ocean: {
      color: [8, 31, 130],
      movement_cost: 1.0,
      is_water: true
    }
  }
}
```

### common/countries/*.json5
Country definitions. One file per country.

```json5
{
  tag: "RED",
  graphical_culture: "westerngfx",
  color: [200, 50, 50]
}
```

### history/provinces/*.json5
Province initial state. Filename format: `{id}-{name}.json5`

```json5
{
  owner: "RED",      // Country tag
  controller: "RED"  // Usually same as owner
}
```

### units/*.json5
Unit type definitions (loaded by GAME layer).

```json5
{
  id: "infantry",
  name: "Infantry",
  category: "land",
  cost: { gold: 20 },
  stats: { speed: 2 }
}
```

### localisation/english/*_l_english.yml
Localization files by category:
- `countries_l_english.yml` - Country names (TAG → display name)
- `provinces_l_english.yml` - Province names
- `terrain_l_english.yml` - Terrain names
- `ui_l_english.yml` - UI strings

## ENGINE vs GAME Loading

### ENGINE Loads (Core layer)
- `map/terrain.json5` - via `TerrainLoader`
- `map/definition.csv` - via `DefinitionLoader`
- `history/provinces/*.json5` - via `BurstProvinceHistoryLoader`
- `common/countries/*.json5` - via `BurstCountryLoader`
- `common/country_tags/*.txt` - via `CountryTagLoader`
- `localisation/**/*.yml` - via `LocalizationManager`

### GAME Loads (StarterKit/Game layer)
- `units/*.json5` - via custom `UnitDefinitionLoader`
- `common/buildings/*.json5` - via custom loader
- Any custom data directories

### Adding Custom Loaders

GAME layer can add loaders using the `LoaderMetadata` attribute. Loaders are auto-discovered via reflection and executed in priority order.

**Example: Adding Religion Data**

1. **Create data files** in your game's data directory:

```
YourGame-Data/
└── common/
    └── religions/
        └── religions.json5
```

```json5
// religions.json5
{
  religions: [
    {
      id: "roman_pantheon",
      name: "Roman Pantheon",
      color: [180, 140, 60],
      tenets: ["sacrifice", "augury"]
    },
    {
      id: "druidism",
      name: "Druidism",
      color: [60, 140, 80],
      tenets: ["nature_worship"]
    }
  ]
}
```

2. **Create a loader** with the `LoaderMetadata` attribute:

```csharp
[LoaderMetadata("religions",
    Description = "Load religion definitions",
    Priority = 25,      // After terrain (10), buildings (20)
    Required = false)]  // Game works without religions
public class ReligionLoader : ILoaderFactory
{
    public void Load(LoaderContext context)
    {
        string path = Path.Combine(context.DataPath, "common/religions");

        foreach (var file in Directory.GetFiles(path, "*.json5"))
        {
            var religions = Json5Parser.ParseArray<ReligionDefinition>(file);
            foreach (var religion in religions)
            {
                ReligionRegistry.Register(religion);
            }
        }
    }
}
```

3. **Create a registry** for runtime access:

```csharp
public static class ReligionRegistry
{
    private static Dictionary<string, ReligionDefinition> _byId = new();

    public static void Register(ReligionDefinition religion)
    {
        _byId[religion.Id] = religion;
    }

    public static ReligionDefinition Get(string id) => _byId.GetValueOrDefault(id);
    public static IEnumerable<ReligionDefinition> GetAll() => _byId.Values;
}
```

**LoaderContext** provides:
- `DataPath` - Root data directory path
- `GameState` - Access to game systems
- `EventBus` - For emitting load events

**Priority Guidelines:**
- 10: Core data (terrain)
- 20: Game mechanics (buildings)
- 25-30: Content data (religions, cultures)
- 40+: Data that depends on other loaders

## Graceful Degradation

The loader handles missing data gracefully:
- **No definition.csv**: Map-only mode (0 provinces)
- **No history files**: Provinces exist but unowned
- **No terrain.json5**: Default terrains created
- **No localization**: Keys shown as-is

## Creating Game Data

1. Copy Template-Data structure to your game's data folder
2. Set path in `GameSettings.DataDirectory`
3. Replace sample data with your content
4. Add custom loaders for game-specific data

## Map Textures

### provinces.png
- RGB encodes province ID (R + G*256 + B*65536)
- Point filtering (no interpolation)
- Must match definition.csv RGB values exactly

### terrain.png
- RGB matches terrain.json5 color values
- Used to assign terrain types to provinces

### heightmap.png
- Grayscale height data for 3D rendering
- Optional for flat map display
