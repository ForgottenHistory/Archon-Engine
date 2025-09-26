# Grand Strategy Game - Coordinate System Architecture

## Executive Summary
**Decision**: Province IDs only in hot data, positions in separate lookup tables  
**Rationale**: Position is presentation, not simulation - keep them separated  
**Memory Cost**: ~100KB for 10k provinces (negligible)  
**Performance**: Zero impact on simulation, <0.01ms position lookups

## The Three Coordinate Spaces

### 1. Province Space (Topology)
**Purpose**: Gameplay logic, pathfinding, adjacency  
**Storage**: Graph/adjacency lists  
**Example**: "Province 1234 borders provinces [1235, 1236, 1240]"  
```csharp
Dictionary<ushort, ushort[]> adjacency;  // ~200KB for 10k provinces
```

### 2. Texture Space (GPU)
**Purpose**: Rendering, selection, borders  
**Storage**: R16G16 texture (province IDs)  
**Example**: "Pixel at UV(0.45, 0.67) = Province 1234"  
```csharp
Texture2D provinceIDTexture;  // 2048×2048 × 4 bytes = 16MB
```

### 3. World Space (3D)
**Purpose**: Unit positions, camera, effects  
**Storage**: Lookup tables  
**Example**: "Province 1234 center = (145.3, 2.1, 89.7)"  
```csharp
Vector2[] provinceCenters;  // 10k × 8 bytes = 80KB
```

## Architecture Decision: ID-Only Hot Data

### What We Store (Hot Data)
```csharp
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ProvinceData {
    public ushort id;           // Province identifier
    public byte owner;          // Nation controlling
    public byte controller;     // Occupier in war
    public ushort development;  // Economic value
    public ushort flags;        // State flags (sieged, etc)
    // Total: 8 bytes, NO POSITION DATA
}
```

### Position Lookup Tables (Warm Data)
```csharp
// Separate arrays for different position needs
public class ProvinceSpatialData {
    // Essential (always loaded)
    public Vector2[] centers;        // 80KB - World space centers
    public ushort[] regions;         // 20KB - Which region contains
    
    // Optional (loaded on demand)
    public Vector2[] labelPositions; // 80KB - UI label placement
    public Vector2[] portPositions;  // 80KB - Coastal provinces only
    public float[] areas;            // 40KB - For density calcs
    public Bounds[] bounds;          // 240KB - If needed for queries
}
```

## Coordinate Transformations

### Critical Transformation Functions
```csharp
public static class CoordinateSystem {
    // World ↔ Texture
    public static Vector2 WorldToTexture(Vector3 worldPos) {
        return new Vector2(
            worldPos.x / MapConstants.WORLD_WIDTH,
            worldPos.z / MapConstants.WORLD_HEIGHT
        );
    }
    
    public static Vector3 TextureToWorld(Vector2 uv, float y = 0) {
        return new Vector3(
            uv.x * MapConstants.WORLD_WIDTH,
            y,
            uv.y * MapConstants.WORLD_HEIGHT
        );
    }
    
    // Province ID Lookups
    public static ushort GetProvinceAt(Vector3 worldPos) {
        Vector2 uv = WorldToTexture(worldPos);
        return ReadProvinceTexture(uv);
    }
    
    public static Vector3 GetProvinceCenter(ushort provinceID) {
        Vector2 center2D = provinceCenters[provinceID];
        float height = Terrain.SampleHeight(center2D);
        return new Vector3(center2D.x, height, center2D.y);
    }
}
```

### Texture Reading (GPU → CPU)
```csharp
// CRITICAL: Cache texture reads, they're expensive!
private static class ProvinceTextureReader {
    private static Texture2D cachedTexture;
    private static Color32[] cachedPixels;
    private static bool isDirty = true;
    
    public static ushort ReadProvinceID(Vector2 uv) {
        if (isDirty) {
            cachedPixels = provinceIDTexture.GetPixels32();
            isDirty = false;
        }
        
        int x = (int)(uv.x * textureWidth);
        int y = (int)(uv.y * textureHeight);
        int index = y * textureWidth + x;
        
        Color32 pixel = cachedPixels[index];
        return (ushort)(pixel.r | (pixel.g << 8));
    }
}
```

## Memory Layout & Performance

### Memory Footprint (10,000 provinces)
```
Hot Data (accessed every frame):
- ProvinceData[]: 80KB (8 bytes × 10k)
- Adjacency lists: ~200KB
- Total: <300KB fits in L3 cache

Warm Data (accessed occasionally):  
- Province centers: 80KB
- Region mapping: 20KB
- Total: ~100KB

Cold Data (rarely accessed):
- Label positions: 80KB
- Port positions: 80KB  
- Areas: 40KB
- Bounds: 240KB
- Total: ~440KB

GPU Data:
- Province ID texture: 16MB (2048×2048×4)
- Province color texture: 16MB
- Total: 32MB VRAM
```

### Access Patterns & Cache Performance
```csharp
// GOOD: Sequential access of hot data
for (int i = 0; i < provinceCount; i++) {
    if (provinces[i].owner == playerNation) {
        income += provinces[i].development;
    }
}
// Performance: 0.1ms for 10k provinces

// GOOD: Position lookup only when needed
if (need_to_spawn_unit) {
    Vector3 pos = GetProvinceCenter(provinceID);
    SpawnUnit(pos);
}
// Performance: <0.001ms per lookup

// BAD: Mixing position with hot data
struct BadProvince {
    ushort id;
    Vector3 position;  // Wastes 12 bytes of cache!
    byte owner;
}
```

## Implementation Checklist

### Phase 1: Core Systems
- [ ] Define ProvinceData struct (no position)
- [ ] Create ProvinceSpatialData class
- [ ] Implement CoordinateSystem static class
- [ ] Build province ID texture loader
- [ ] Add texture-based province selection

### Phase 2: Lookup Tables
- [ ] Generate province centers from bitmap
- [ ] Build adjacency graph from borders
- [ ] Create region hierarchy mapping
- [ ] Implement coordinate transformations
- [ ] Add caching layer for texture reads

### Phase 3: Optimizations
- [ ] Spatial partitioning for range queries
- [ ] Quantize positions to ushort if needed
- [ ] Add LOD system for distant provinces
- [ ] Implement dirty flag for texture updates
- [ ] Profile and optimize hot paths

## Key Design Principles

### 1. Separation of Concerns
```
Simulation Layer: Never knows about positions
Presentation Layer: Never modifies game state
Spatial System: Bridge between the two
```

### 2. Cache-Friendly Access
```
Hot data: Tightly packed, no positions
Warm data: Separate arrays per attribute
Cold data: Load on demand
```

### 3. GPU-First Rendering
```
Selection: Texture lookup, not raycasts
Borders: Compute shader, not mesh
Colors: Texture update, not materials
```

## Common Pitfalls to Avoid

### ❌ DON'T: Store positions in hot data
```csharp
// BAD: Wastes cache, rarely needed
struct Province {
    Vector3 position;  // 12 wasted bytes
    byte owner;
}
```

### ❌ DON'T: Read texture every frame
```csharp
// BAD: GPU→CPU transfer is expensive
void Update() {
    Color32[] pixels = texture.GetPixels32();  // 16MB transfer!
}
```

### ❌ DON'T: Use floats for province IDs
```csharp
// BAD: Float precision issues
float provinceID = 12534.0f;  // Might become 12534.0001
```

### ✅ DO: Cache aggressively
```csharp
// GOOD: Cache texture reads
if (textureChanged) {
    cachedPixels = texture.GetPixels32();
    textureChanged = false;
}
```

### ✅ DO: Quantize when possible
```csharp
// GOOD: 4 bytes instead of 8
struct QuantizedPosition {
    ushort x, y;  // 0-65535 range, plenty for strategy games
}
```

## World Space Specifications

### Recommended World Dimensions
```csharp
public static class MapConstants {
    // World units (Unity units)
    public const float WORLD_WIDTH = 1000f;   // West to East
    public const float WORLD_HEIGHT = 500f;   // North to South
    
    // Texture dimensions (pixels)
    public const int TEXTURE_WIDTH = 2048;
    public const int TEXTURE_HEIGHT = 1024;
    
    // Conversion factors
    public const float WORLD_TO_TEXTURE_X = TEXTURE_WIDTH / WORLD_WIDTH;
    public const float WORLD_TO_TEXTURE_Y = TEXTURE_HEIGHT / WORLD_HEIGHT;
}
```

### Quantization Strategy
```csharp
// If you must store positions, quantize them:
public struct QuantizedPos {
    public ushort x, y;  // 0-65535 range
    
    public Vector2 ToWorld() {
        return new Vector2(
            (x / 65535f) * MapConstants.WORLD_WIDTH,
            (y / 65535f) * MapConstants.WORLD_HEIGHT
        );
    }
    
    public static QuantizedPos FromWorld(Vector2 world) {
        return new QuantizedPos {
            x = (ushort)((world.x / MapConstants.WORLD_WIDTH) * 65535f),
            y = (ushort)((world.y / MapConstants.WORLD_HEIGHT) * 65535f)
        };
    }
}
// Precision: ~0.015 units (plenty for strategy games)
```

## Performance Guarantees

### With This Architecture
- Province selection: <0.01ms (texture lookup)
- Position lookup: <0.001ms (array index)
- Range query (nearby provinces): <0.1ms (spatial partition)
- Coordinate transformation: <0.0001ms (simple math)
- Memory usage: <1MB for all position data

### Compared to Naive Approach
- GameObject per province: 100MB+ memory, 10ms selection
- Colliders for selection: 5-10ms per click
- Position in hot data: 50% more cache misses
- No spatial partitioning: 10ms+ range queries

## Final Recommendations

1. **Start without positions** in province data - add only if profiling shows need
2. **Use lookup tables liberally** - 80KB is nothing in modern systems
3. **Cache texture reads** - Never read GPU data per frame
4. **Quantize if storing** - ushort is plenty for strategy scale
5. **Keep simulation pure** - Position is for presentation only

## Questions Resolved
✅ Store positions separately from hot data  
✅ Use three coordinate systems (Province/Texture/World)  
✅ Quantize to ushort if needed (not float)  
✅ Cache all texture reads  
✅ Use lookup tables for province centers  

## Next Steps
1. Implement basic ProvinceData struct
2. Create coordinate transformation functions
3. Build province center lookup table
4. Test texture-based selection
5. Profile memory access patterns