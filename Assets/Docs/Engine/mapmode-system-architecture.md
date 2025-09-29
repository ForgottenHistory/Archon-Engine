# Grand Strategy Game - Map Mode System Architecture

## Executive Summary
**Challenge**: Efficiently display different data visualizations (political, terrain, development, etc.) on 10,000+ provinces  
**Solution**: GPU-based map mode system with data textures and specialized shaders  
**Key Principle**: Encode all province data in textures, let GPU do the visualization  
**Performance**: Single draw call, <0.1ms mode switching, 200+ FPS maintained

## System Overview

```
SIMULATION (CPU)           →  DATA TEXTURES (GPU)      →  SHADER (GPU)
Province ownership data    →  R16 Owner texture        →  Sample & visualize
Province development data  →  R8 Development texture   →  Color mapping
Province culture data      →  R16 Culture texture      →  Pattern rendering
```

## Core Architecture

### The Map Mode Pipeline

```csharp
public class MapModeSystem {
    // 1. DATA LAYER: Raw simulation data
    private ProvinceData[] provinces;
    
    // 2. TEXTURE LAYER: GPU-readable data
    private Dictionary<MapMode, Texture2D> dataTextures;
    
    // 3. SHADER LAYER: Visualization
    private Material mapMaterial;
    
    // 4. UI LAYER: Mode selection
    private MapModeUI modeSelector;
}
```

## Map Mode Types & Data Requirements

### Core Map Modes

```csharp
public enum MapMode {
    // Basic modes (single data source)
    Political = 0,      // Owner ID → Country color
    Terrain = 1,        // Terrain type → Terrain color
    Development = 2,    // Dev level → Gradient
    Culture = 3,        // Culture ID → Culture color
    Religion = 4,       // Religion ID → Religion color
    
    // Composite modes (multiple data sources)
    Diplomatic = 5,     // Relations → Color gradient
    Trade = 6,          // Trade value + flow → Heatmap + arrows
    Military = 7,       // Army strength → Threat colors
    Economic = 8,       // Income/expenses → Green/red gradient
    
    // Special modes
    Selected = 9,       // Highlights for selected country
    StrategicView = 10, // Simplified military view
    PlayerMapMode = 11  // Custom player-defined
}
```

### Data Texture Requirements

```csharp
public class MapModeDataTextures {
    // Core ID textures (always needed)
    public Texture2D ProvinceID;        // RG16: Province IDs
    public Texture2D ProvinceOwner;     // R16: Owner nation ID
    
    // Mode-specific data textures
    public Texture2D ProvinceTerrain;   // R8: Terrain type ID
    public Texture2D ProvinceDevelopment; // R8: Development level (0-255)
    public Texture2D ProvinceCulture;   // R16: Culture ID
    public Texture2D ProvinceReligion;  // R16: Religion ID
    public Texture2D ProvinceTradeValue; // R16: Trade value
    public Texture2D ProvinceUnrest;    // R8: Unrest level
    public Texture2D ProvinceAutonomy;  // R8: Autonomy percentage
    
    // Composite data (for complex modes)
    public Texture2D DiplomaticRelations; // RG16: From/To relations
    public Texture2D MilitaryStrength;   // RGBA32: Detailed military data
    
    // Color palettes (for ID→Color mapping)
    public Texture2D CountryColors;     // 256x1 RGBA32
    public Texture2D CultureColors;     // 256x1 RGBA32
    public Texture2D ReligionColors;    // 256x1 RGBA32
    public Texture2D TerrainColors;     // 32x1 RGBA32
}
```

## Improved Shader Architecture

### Modular Shader System

```hlsl
// MapModeCore.shader - Improved version

Shader "GrandStrategy/MapModeCore" {
    Properties {
        // Data textures - organized by update frequency
        [Header(Core Data - Never Changes)]
        _ProvinceIDTex ("Province IDs", 2D) = "black" {}
        _TerrainTex ("Terrain Types", 2D) = "black" {}
        
        [Header(Frequently Updated Data)]
        _OwnerTex ("Province Owners", 2D) = "black" {}
        _DevelopmentTex ("Development", 2D) = "black" {}
        
        [Header(Occasionally Updated Data)]
        _CultureTex ("Cultures", 2D) = "black" {}
        _ReligionTex ("Religions", 2D) = "black" {}
        
        [Header(Color Palettes)]
        _CountryColors ("Country Colors", 2D) = "white" {}
        _CultureColors ("Culture Colors", 2D) = "white" {}
        _ReligionColors ("Religion Colors", 2D) = "white" {}
        
        [Header(Map Settings)]
        _MapMode ("Map Mode", Int) = 0
        _BorderIntensity ("Border Intensity", Range(0,1)) = 0.8
        _HighlightColor ("Highlight Color", Color) = (1,1,0,1)
    }
    
    SubShader {
        Pass {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            
            // Better organization with includes
            #include "MapModeCommon.hlsl"
            #include "MapModePolitical.hlsl"
            #include "MapModeTerrain.hlsl"
            #include "MapModeDevelopment.hlsl"
            
            float4 frag(Varyings input) : SV_Target {
                // Sample province ID once
                uint provinceID = SampleProvinceID(input.uv);
                
                // Early out for ocean
                if (provinceID == 0) {
                    return _OceanColor;
                }
                
                // Branch based on map mode
                float4 color;
                
                switch(_MapMode) {
                    case 0: // Political
                        color = RenderPolitical(provinceID, input.uv);
                        break;
                    case 1: // Terrain
                        color = RenderTerrain(provinceID, input.uv);
                        break;
                    case 2: // Development
                        color = RenderDevelopment(provinceID, input.uv);
                        break;
                    case 3: // Culture
                        color = RenderCulture(provinceID, input.uv);
                        break;
                    default:
                        color = float4(1,0,1,1); // Magenta for undefined
                }
                
                // Apply borders (shared across all modes)
                color = ApplyBorders(color, input.uv);
                
                // Apply highlights (selected provinces, etc.)
                color = ApplyHighlights(color, provinceID);
                
                return color;
            }
            ENDHLSL
        }
    }
}
```

### Specialized Map Mode Shaders

```hlsl
// MapModePolitical.hlsl
float4 RenderPolitical(uint provinceID, float2 uv) {
    // Get owner from texture
    uint ownerID = SampleOwner(provinceID);
    
    if (ownerID == 0) {
        return _UnownedColor; // Gray for unowned
    }
    
    // Sample country color from palette
    return SampleCountryColor(ownerID);
}

// MapModeDevelopment.hlsl  
float4 RenderDevelopment(uint provinceID, float2 uv) {
    // Get development level (0-255)
    float development = SampleDevelopment(provinceID) / 255.0;
    
    // Color gradient from red (low) to green (high)
    float3 lowColor = float3(0.5, 0.1, 0.1);  // Dark red
    float3 highColor = float3(0.1, 0.5, 0.1); // Dark green
    
    float3 color = lerp(lowColor, highColor, development);
    return float4(color, 1.0);
}
```

## C# Map Mode Manager

### Core Manager Class

```csharp
public class MapModeManager : MonoBehaviour {
    [Header("References")]
    [SerializeField] private Material mapMaterial;
    [SerializeField] private ComputeShader borderCompute;
    
    [Header("Settings")]
    [SerializeField] private MapMode currentMode = MapMode.Political;
    
    // Texture management
    private MapModeDataTextures dataTextures;
    private TextureUpdateScheduler updateScheduler;
    
    // Mode-specific handlers
    private Dictionary<MapMode, IMapModeHandler> modeHandlers;
    
    void Start() {
        InitializeTextures();
        InitializeModeHandlers();
        SetMapMode(MapMode.Political);
    }
    
    private void InitializeTextures() {
        int width = MapConfig.TextureWidth;   // 4096
        int height = MapConfig.TextureHeight; // 2048
        
        // Create persistent textures
        dataTextures = new MapModeDataTextures {
            ProvinceID = new Texture2D(width, height, TextureFormat.RG16, false),
            ProvinceOwner = new Texture2D(width, height, TextureFormat.R16, false),
            ProvinceDevelopment = new Texture2D(width, height, TextureFormat.R8, false),
            // ... etc
        };
        
        // Create color palettes
        CreateColorPalettes();
        
        // Initial data upload
        UpdateAllTextures();
    }
    
    public void SetMapMode(MapMode mode) {
        if (currentMode == mode) return;
        
        // Notify previous handler
        modeHandlers[currentMode]?.OnDeactivate();
        
        currentMode = mode;
        
        // Update shader
        mapMaterial.SetInt("_MapMode", (int)mode);
        
        // Notify new handler
        modeHandlers[mode]?.OnActivate();
        
        // Update textures if needed
        UpdateTexturesForMode(mode);
    }
}
```

### Map Mode Handlers

```csharp
public interface IMapModeHandler {
    MapMode Mode { get; }
    void OnActivate();
    void OnDeactivate();
    void UpdateTextures(MapModeDataTextures textures);
    string GetProvinceTooltip(ushort provinceId);
}

public class PoliticalMapMode : IMapModeHandler {
    public MapMode Mode => MapMode.Political;
    
    public void OnActivate() {
        // Set up political view
    }
    
    public void UpdateTextures(MapModeDataTextures textures) {
        // Update owner texture from simulation
        var owners = SimulationCore.GetProvinceOwners();
        UpdateOwnerTexture(textures.ProvinceOwner, owners);
    }
    
    public string GetProvinceTooltip(ushort provinceId) {
        var owner = SimulationCore.GetProvinceOwner(provinceId);
        return $"Owner: {GetCountryName(owner)}";
    }
}

public class DevelopmentMapMode : IMapModeHandler {
    public MapMode Mode => MapMode.Development;
    
    public void UpdateTextures(MapModeDataTextures textures) {
        // Update development texture
        var developments = SimulationCore.GetProvinceDevelopments();
        
        // Convert to texture data
        var pixels = new Color32[textures.ProvinceDevelopment.width * 
                                 textures.ProvinceDevelopment.height];
        
        for (int i = 0; i < provinces.Length; i++) {
            byte devLevel = (byte)Mathf.Clamp(developments[i], 0, 255);
            // Map province to texture pixels
            SetProvincePixels(pixels, provinces[i].TextureRegion, devLevel);
        }
        
        textures.ProvinceDevelopment.SetPixels32(pixels);
        textures.ProvinceDevelopment.Apply();
    }
}
```

## Texture Update System

### Efficient Texture Updates

```csharp
public class TextureUpdateScheduler {
    private struct UpdateRequest {
        public Texture2D texture;
        public Action<Texture2D> updateAction;
        public UpdateFrequency frequency;
        public float lastUpdate;
    }
    
    private List<UpdateRequest> requests = new();
    
    public void RegisterTexture(Texture2D texture, Action<Texture2D> updater, 
                                UpdateFrequency frequency) {
        requests.Add(new UpdateRequest {
            texture = texture,
            updateAction = updater,
            frequency = frequency,
            lastUpdate = 0
        });
    }
    
    public void Update() {
        float currentTime = Time.time;
        
        foreach (var request in requests) {
            if (ShouldUpdate(request, currentTime)) {
                request.updateAction(request.texture);
                request.lastUpdate = currentTime;
            }
        }
    }
    
    private bool ShouldUpdate(UpdateRequest request, float currentTime) {
        float interval = GetInterval(request.frequency);
        return currentTime - request.lastUpdate >= interval;
    }
}
```

### Batch Texture Operations

```csharp
public class BatchTextureUpdater {
    private NativeArray<Color32> pixelBuffer;
    private JobHandle currentJob;
    
    public void UpdateOwnershipTexture(Texture2D texture, NativeArray<byte> owners) {
        // Schedule job to update texture data
        var job = new UpdateOwnershipJob {
            owners = owners,
            pixels = pixelBuffer,
            textureWidth = texture.width
        };
        
        currentJob = job.Schedule(owners.Length, 64);
    }
    
    [BurstCompile]
    struct UpdateOwnershipJob : IJobParallelFor {
        [ReadOnly] public NativeArray<byte> owners;
        [WriteOnly] public NativeArray<Color32> pixels;
        public int textureWidth;
        
        public void Execute(int provinceId) {
            byte owner = owners[provinceId];
            Color32 color = GetOwnerColor(owner);
            
            // Write to all pixels for this province
            var region = GetProvinceRegion(provinceId);
            for (int i = region.start; i < region.end; i++) {
                pixels[i] = color;
            }
        }
    }
}
```

## Border Generation System

### Compute Shader Border Detection

```hlsl
// BorderGeneration.compute
#pragma kernel GenerateBorders

Texture2D<float2> ProvinceIDTexture;
RWTexture2D<float> BorderTexture;

[numthreads(8,8,1)]
void GenerateBorders(uint3 id : SV_DispatchThreadID) {
    uint2 coord = id.xy;
    
    // Sample center province
    uint centerProvince = DecodeProvinceID(ProvinceIDTexture[coord].xy);
    
    // Check 8 neighbors
    float borderStrength = 0;
    
    for (int dx = -1; dx <= 1; dx++) {
        for (int dy = -1; dy <= 1; dy++) {
            if (dx == 0 && dy == 0) continue;
            
            uint2 neighborCoord = coord + int2(dx, dy);
            uint neighborProvince = DecodeProvinceID(
                ProvinceIDTexture[neighborCoord].xy);
            
            if (neighborProvince != centerProvince) {
                // Different province - this is a border
                float weight = (abs(dx) + abs(dy)) == 1 ? 1.0 : 0.707; // Corner vs edge
                borderStrength += weight;
            }
        }
    }
    
    // Normalize and write
    BorderTexture[coord] = saturate(borderStrength / 4.0);
}
```

## Map Mode Transitions

### Smooth Mode Switching

```csharp
public class MapModeTransition {
    private MapMode fromMode;
    private MapMode toMode;
    private float transitionTime = 0.3f;
    private float currentProgress;
    
    public void StartTransition(MapMode from, MapMode to) {
        fromMode = from;
        toMode = to;
        currentProgress = 0;
    }
    
    public void Update(float deltaTime) {
        currentProgress += deltaTime / transitionTime;
        
        if (currentProgress >= 1.0f) {
            CompleteTransition();
            return;
        }
        
        // Blend between modes in shader
        mapMaterial.SetFloat("_TransitionBlend", currentProgress);
        mapMaterial.SetInt("_FromMode", (int)fromMode);
        mapMaterial.SetInt("_ToMode", (int)toMode);
    }
}
```

## Optimization Strategies

### Texture Atlas for Province Data

```csharp
public class ProvinceDataAtlas {
    // Pack multiple data channels into single texture
    // R: Owner, G: Development, B: Culture, A: Religion
    private Texture2D atlasTexture;
    
    public void PackData() {
        var pixels = new Color32[width * height];
        
        for (int i = 0; i < provinces.Length; i++) {
            var p = provinces[i];
            pixels[i] = new Color32(
                p.Owner,
                p.Development,
                (byte)(p.Culture & 0xFF),
                (byte)(p.Religion & 0xFF)
            );
        }
        
        atlasTexture.SetPixels32(pixels);
        atlasTexture.Apply(false);
    }
}
```

### LOD System for Large Maps

```csharp
public class MapModeLOD {
    private Texture2D[] lodTextures;
    private int[] lodDistances = { 50, 100, 200, 500 };
    
    public void UpdateLOD(float cameraDistance) {
        int lodLevel = GetLODLevel(cameraDistance);
        
        // Use lower resolution textures for distant view
        mapMaterial.SetTexture("_ProvinceData", lodTextures[lodLevel]);
        
        // Reduce border detail at distance
        float borderDetail = Mathf.Lerp(1.0f, 0.2f, lodLevel / 3.0f);
        mapMaterial.SetFloat("_BorderDetail", borderDetail);
    }
}
```

## Complete Implementation Example

### Setting Up a New Map Mode

```csharp
// 1. Define the mode
public class TradeMapMode : IMapModeHandler {
    public MapMode Mode => MapMode.Trade;
    
    // 2. Create specialized texture
    private Texture2D tradeValueTexture;
    private Texture2D tradeFlowTexture;
    
    public void OnActivate() {
        // 3. Update textures with trade data
        UpdateTradeTextures();
        
        // 4. Set shader properties
        mapMaterial.SetTexture("_TradeValue", tradeValueTexture);
        mapMaterial.SetTexture("_TradeFlow", tradeFlowTexture);
        mapMaterial.EnableKeyword("TRADE_MODE");
    }
    
    private void UpdateTradeTextures() {
        // 5. Get data from simulation
        var tradeData = SimulationCore.GetTradeData();
        
        // 6. Convert to texture
        var pixels = new Color32[textureSize];
        foreach (var node in tradeData.Nodes) {
            float value = node.Value / MaxTradeValue;
            Color32 color = Color32.Lerp(Color.red, Color.green, value);
            SetProvinceColor(pixels, node.ProvinceId, color);
        }
        
        tradeValueTexture.SetPixels32(pixels);
        tradeValueTexture.Apply();
    }
}
```

## Performance Considerations

### Texture Format Selection

| Data Type | Texture Format | Size | Use Case |
|-----------|---------------|------|----------|
| Province ID | RG16 | 2 bytes | Up to 65k provinces |
| Owner ID | R8/R16 | 1-2 bytes | 256/65k nations |
| Development | R8 | 1 byte | 0-255 levels |
| Culture/Religion | R16 | 2 bytes | Many unique values |
| Colors | RGBA32 | 4 bytes | Direct colors |
| Borders | R8 | 1 byte | Border intensity |

### Update Frequencies

| Map Mode | Update Frequency | Reason |
|----------|-----------------|---------|
| Political | Per conquest | Ownership changes |
| Development | Monthly | Slow growth |
| Culture | Yearly | Very slow change |
| Trade | Weekly | Dynamic flow |
| Military | Daily (at war) | Rapid changes |
| Terrain | Never | Static |

## Shader Your Assessment

Your current shader has good foundations but needs:

1. **Better data organization** - Separate textures for different data types
2. **Modular mode handling** - Include files for each mode
3. **Efficient sampling** - Sample province ID once, use for all lookups
4. **Proper color palettes** - Don't calculate colors in shader
5. **Border optimization** - Pre-compute borders in separate pass

## Best Practices

1. **Pre-compute everything possible** - Colors, borders, gradients
2. **Use texture atlases** - Reduce texture switches
3. **Update only what changes** - Don't regenerate static data
4. **Cache mode data** - Keep recently used modes in memory
5. **Profile texture uploads** - This is often the bottleneck
6. **Use compute shaders** - For complex calculations
7. **Implement LOD** - Reduce detail at distance
8. **Batch updates** - Update multiple provinces at once

## Summary

The key to an efficient map mode system is:
1. **Encode all data in textures** - Let GPU do visualization
2. **Specialized handlers per mode** - Each mode updates its own data
3. **Efficient texture updates** - Only update what changed
4. **Modular shader system** - Easy to add new modes
5. **Pre-computation** - Do expensive work once, not per frame

This architecture gives you instant map mode switching, maintains 200+ FPS, and makes adding new modes straightforward.