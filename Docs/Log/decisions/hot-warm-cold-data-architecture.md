# Decision: Hot/Warm/Cold Data Architecture for Province Expansion

**Date:** 2025-10-09
**Status:** ✅ Implemented (Hot layer), 📋 Planned (Warm/Cold layers)
**Context:** Phase 3 ProvinceState refactoring to engine-game separation
**Related:** [phase-3-complete-scenario-loader-bug-fixed.md](../2025-10/09/phase-3-complete-scenario-loader-bug-fixed.md)

---

## The Problem

After separating ProvinceState (8 bytes, engine) from HegemonProvinceData (4 bytes, game), a question arose:

**"What happens when we need to add more game data later?"**

We can't fit everything in 4-8 bytes:
- Religion system
- Culture system
- Trade goods
- Buildings (8+ slots)
- Modifiers
- Historical events
- Province flags (40+ boolean properties)
- Great projects
- Institutions

**How do we expand game data without breaking the engine layer or bloating hot data?**

---

## The Solution: Hot/Warm/Cold Data Pattern

### Core Principle
**Separate data by access frequency, not by logical grouping.**

```
HOT (every frame)    → 4-8 bytes,  contiguous NativeArray
WARM (every tick)    → 20-50 bytes, contiguous NativeArray
COLD (rarely)        → Variable,    sparse Dictionary
```

### Architecture

```csharp
// ═══════════════════════════════════════════════════════════
// ENGINE LAYER (8 bytes) - Generic primitives only
// ═══════════════════════════════════════════════════════════
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ProvinceState {
    public ushort ownerID;        // 2 bytes
    public ushort controllerID;   // 2 bytes
    public ushort terrainType;    // 2 bytes
    public ushort gameDataSlot;   // 2 bytes - Future hook for complex indexing
}
NativeArray<ProvinceState> provinces;  // 10,000 × 8 = 80KB


// ═══════════════════════════════════════════════════════════
// GAME LAYER - HOT DATA (4-8 bytes) - Accessed EVERY FRAME/TICK
// ═══════════════════════════════════════════════════════════
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct HegemonProvinceData {
    // Current (4 bytes)
    public byte development;    // 1 byte - Economic level
    public byte fortLevel;      // 1 byte - Fortification
    public byte unrest;         // 1 byte - Stability
    public byte population;     // 1 byte - Population abstraction

    // Future expansion (8 bytes total)
    public byte autonomy;       // 1 byte - Local autonomy
    public byte devastation;    // 1 byte - War damage
    public byte nationalism;    // 1 byte - Separatism
    public byte religion;       // 1 byte - Religion ID
}
NativeArray<HegemonProvinceData> hegemonHotData;  // 10,000 × 8 = 80KB


// ═══════════════════════════════════════════════════════════
// GAME LAYER - WARM DATA (24 bytes) - Accessed OCCASIONALLY
// ═══════════════════════════════════════════════════════════
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct HegemonProvinceWarmData {
    // Economic (6 bytes)
    public byte culture;              // 1 byte - Culture ID
    public byte tradeGood;            // 1 byte - Trade good ID
    public byte baseProduction;       // 1 byte - Production value
    public byte baseTax;              // 1 byte - Tax value
    public byte baseManpower;         // 1 byte - Manpower value
    public byte tradeCompanyRegion;   // 1 byte - Trade company

    // Trade & Natives (8 bytes)
    public ushort centerOfTrade;      // 2 bytes - 0-3 levels
    public ushort nativeSize;         // 2 bytes
    public ushort nativeHostility;    // 2 bytes
    public ushort nativeFerocity;     // 2 bytes

    // Buildings (8 bytes) - Fixed-size slots
    public fixed byte buildingSlots[8];  // Which building in each slot

    // Flags (4 bytes = 32 boolean properties)
    public uint provinceFlags;        // Bit-packed flags
    // bit 0:  has_port
    // bit 1:  is_capital
    // bit 2:  is_city
    // bit 3:  has_revolution
    // bit 4:  is_coastal
    // bit 5:  is_sea
    // bit 6:  is_lake
    // bit 7:  is_strait
    // bit 8:  has_great_project
    // bit 9:  has_institution_1
    // bit 10: has_institution_2
    // ... up to 32 flags
}
NativeArray<HegemonProvinceWarmData> hegemonWarmData;  // 10,000 × 24 = 240KB


// ═══════════════════════════════════════════════════════════
// GAME LAYER - COLD DATA (Variable) - Accessed RARELY
// ═══════════════════════════════════════════════════════════
public class HegemonProvinceColdData {
    // Dynamic collections (grow as needed)
    public List<Building> buildings;           // Building details
    public List<Modifier> modifiers;           // Temporary modifiers
    public List<HistoricalEvent> history;      // Event log

    // Modding support
    public Dictionary<string, float> customModifiers;

    // Player customization
    public string customName;                  // Renamed province
    public Color32 customColor;                // Custom map color

    // Late-game features (only exist after researched)
    public InstitutionProgress institutions;   // Tech spread
    public List<GreatProject> greatProjects;   // Wonders

    // Save game optimization
    public CompressedHistory compressedHistory;  // Old events compressed
}
// Sparse storage - only allocated when needed
Dictionary<ushort, HegemonProvinceColdData> hegemonColdData;  // ~1000 × 1KB = 1MB
```

---

## Data Access Patterns

### Hot Data: Direct Array Access (Every Frame)
```csharp
public byte GetDevelopment(ushort provinceId) {
    return hegemonHotData[provinceId].development;  // <0.001ms
}

public void SetDevelopment(ushort provinceId, byte value) {
    var data = hegemonHotData[provinceId];
    data.development = value;
    hegemonHotData[provinceId] = data;
}
```

### Warm Data: Direct Array Access (Occasional)
```csharp
public byte GetCulture(ushort provinceId) {
    return hegemonWarmData[provinceId].culture;  // <0.001ms
}

public bool HasPort(ushort provinceId) {
    const uint HAS_PORT_FLAG = 1 << 0;
    return (hegemonWarmData[provinceId].provinceFlags & HAS_PORT_FLAG) != 0;
}

public void SetPort(ushort provinceId, bool hasPort) {
    var data = hegemonWarmData[provinceId];
    const uint HAS_PORT_FLAG = 1 << 0;
    if (hasPort) {
        data.provinceFlags |= HAS_PORT_FLAG;
    } else {
        data.provinceFlags &= ~HAS_PORT_FLAG;
    }
    hegemonWarmData[provinceId] = data;
}
```

### Cold Data: Sparse Dictionary Lookup (Rare)
```csharp
public List<Building> GetBuildings(ushort provinceId) {
    if (hegemonColdData.TryGetValue(provinceId, out var coldData)) {
        return coldData.buildings;  // ~0.01ms (dictionary lookup)
    }
    return emptyBuildingList;  // Most provinces don't have cold data
}

public void AddBuilding(ushort provinceId, Building building) {
    // Lazy allocation - only create cold data when needed
    if (!hegemonColdData.ContainsKey(provinceId)) {
        hegemonColdData[provinceId] = new HegemonProvinceColdData();
    }
    hegemonColdData[provinceId].buildings.Add(building);
}
```

### Typical Usage: Access Multiple Layers
```csharp
public FixedPoint64 CalculateProvinceIncome(ushort provinceId) {
    // Layer 1: Engine (ownership)
    var engineState = provinces[provinceId];

    // Layer 2: Game Hot (development, unrest)
    var hotData = hegemonHotData[provinceId];

    // Layer 3: Game Warm (culture, base tax, buildings bitmap)
    var warmData = hegemonWarmData[provinceId];

    // Base calculation
    FixedPoint64 baseTax = FixedPoint64.FromInt(warmData.baseTax);
    FixedPoint64 devModifier = FixedPoint64.FromFraction(hotData.development, 10);
    FixedPoint64 terrainMod = terrainModifiers[engineState.terrainType];
    FixedPoint64 unrestPenalty = FixedPoint64.One -
        FixedPoint64.FromFraction(hotData.unrest, 100);

    FixedPoint64 baseIncome = baseTax * devModifier * terrainMod * unrestPenalty;

    // Layer 4: Cold (building bonuses, modifiers) - only if exists
    FixedPoint64 buildingBonus = FixedPoint64.Zero;
    if (hegemonColdData.TryGetValue(provinceId, out var coldData)) {
        buildingBonus = CalculateBuildingBonus(coldData.buildings);
        baseIncome += ApplyModifiers(baseIncome, coldData.modifiers);
    }

    return baseIncome + buildingBonus;
}
```

---

## Memory Layout & Performance

### Memory Budget (10,000 Provinces)

```
ENGINE HOT:     80KB   (8 bytes × 10,000)  ← Rendering, selection
GAME HOT:       80KB   (8 bytes × 10,000)  ← Income, unrest calculations
GAME WARM:     240KB  (24 bytes × 10,000)  ← Culture, buildings, flags
GAME COLD:       1MB  (~1KB × ~1,000)      ← History, modifiers (sparse)
─────────────────────────────────────────
TOTAL:       ~1.4MB  for complete game state
```

### Cache Performance

**Hot path (rendering, every frame):**
```
Load provinces[id].ownerID    → 8 bytes  → L1 cache hit
Skip warm/cold entirely       → 0 bytes loaded
Result: <0.001ms per province
```

**Warm path (monthly income calculation):**
```
Load provinces[id]            → 8 bytes   → L1 cache
Load hegemonHotData[id]       → 8 bytes   → L1 cache
Load hegemonWarmData[id]      → 24 bytes  → L2 cache
Skip cold if not needed       → 0 bytes
Result: <0.01ms per province
```

**Cold path (building construction - rare):**
```
Load provinces[id]            → 8 bytes   → L1 cache
Load hegemonHotData[id]       → 8 bytes   → L1 cache
Load hegemonWarmData[id]      → 24 bytes  → L2 cache
Lookup hegemonColdData[id]    → ~1KB      → RAM (dictionary)
Result: ~0.1ms per province (acceptable for rare operations)
```

### Access Frequency Analysis

```
EVERY FRAME (60 Hz):
- provinces[].ownerID          → Rendering
- provinces[].terrainType      → Map modes

EVERY TICK (daily, ~1 Hz):
- hegemonHotData[].development → Income
- hegemonHotData[].unrest      → Rebellion check
- hegemonHotData[].fortLevel   → Siege calculations

MONTHLY (~0.033 Hz):
- hegemonWarmData[].culture    → Cultural conversion
- hegemonWarmData[].tradeGood  → Trade calculations
- hegemonWarmData[].buildings  → Building effects

RARELY (player action):
- hegemonColdData[].buildings  → Construction UI
- hegemonColdData[].modifiers  → Modifier tooltips
- hegemonColdData[].history    → Province history panel
```

---

## Benefits

### ✅ Cache-Friendly
Only load what you need. Hot path stays in L1/L2 cache.

### ✅ Scalable
Add new fields to warm/cold without touching hot data.

```csharp
// Easy expansion
public struct HegemonProvinceWarmData {
    // ... existing 24 bytes ...
    public byte tradeCompanyInvestment;  // NEW: Trade company system
    public byte reformationProgress;     // NEW: Religion mechanics
    // Still only 26 bytes
}
```

### ✅ Memory-Efficient
Sparse storage for cold data - only allocate when needed.

```csharp
// Only 10% of provinces are capitals
if (isCapital) {
    if (!hegemonColdData.ContainsKey(provinceId)) {
        hegemonColdData[provinceId] = new HegemonProvinceColdData();
    }
    hegemonColdData[provinceId].isCapital = true;
}
// Saves 900KB vs allocating for all 10,000 provinces
```

### ✅ Burst-Compatible
Hot and warm layers use NativeArray - fully compatible with Burst compiler.

```csharp
[BurstCompile]
public struct CalculateIncomeJob : IJobParallelFor {
    [ReadOnly] public NativeArray<ProvinceState> provinces;
    [ReadOnly] public NativeArray<HegemonProvinceData> hotData;
    [ReadOnly] public NativeArray<HegemonProvinceWarmData> warmData;
    [WriteOnly] public NativeArray<FixedPoint64> incomes;

    public void Execute(int index) {
        // Burst-optimized SIMD operations
        incomes[index] = CalculateIncome(
            provinces[index],
            hotData[index],
            warmData[index]
        );
    }
}
```

---

## Trade-offs

### ❌ More Complex Access
Instead of single struct access, need multiple array lookups.

```csharp
// Before (all in one):
var state = provinces[id];
income = state.baseTax * state.development;

// After (multiple lookups):
var hot = hegemonHotData[id];
var warm = hegemonWarmData[id];
income = warm.baseTax * hot.development;
```

### ❌ More Memory Than Minimal
Total is 1.4MB vs theoretical minimum of ~500KB if everything packed tight.

**Why acceptable:**
- Still well under 100MB budget
- Cache performance more valuable than absolute size
- Clean architecture prevents tech debt

### ✅ But Worth It For
- **Scalability**: Easy to add features without refactoring
- **Performance**: Hot paths stay fast even as game grows
- **Maintainability**: Clear separation of concerns

---

## Real-World Comparison: Europa Universalis 4

EU4 uses a similar pattern (from community reverse engineering):

```
HOT (accessed daily):
- Owner, controller, development, unrest, autonomy
- ~8-12 bytes per province

WARM (accessed monthly):
- Religion, culture, trade goods, buildings (bitmap)
- ~20-30 bytes per province

COLD (accessed on UI interaction):
- Province history, custom names, triggered modifiers
- Variable size, sparse storage
```

**Result:** 5000+ provinces at 60 FPS even on late-game saves.

---

## Implementation Status

### ✅ Currently Implemented (Phase 3)
```csharp
// Engine hot (8 bytes)
public struct ProvinceState { ... }
NativeArray<ProvinceState> provinces;

// Game hot (4 bytes)
public struct HegemonProvinceData { ... }
NativeArray<HegemonProvinceData> hegemonData;
```

### 📋 Planned (Future Phases)
```csharp
// Game warm (24 bytes)
public struct HegemonProvinceWarmData { ... }
NativeArray<HegemonProvinceWarmData> hegemonWarmData;

// Game cold (variable)
public class HegemonProvinceColdData { ... }
Dictionary<ushort, HegemonProvinceColdData> hegemonColdData;
```

### 🔧 Migration Path
When implementing warm/cold layers:

1. **Analyze access patterns** - Profile which fields are accessed when
2. **Move to warm** - Fields accessed monthly (culture, trade goods)
3. **Move to cold** - Fields accessed rarely (history, custom names)
4. **Update queries** - Change code to access multiple layers
5. **Validate performance** - Ensure no regressions

---

## Decision Rationale

### Why This Pattern?

**Alternative 1: Keep everything in hot data (8-12 bytes)**
- ❌ Limited to 12 bytes max for cache efficiency
- ❌ Can't add features without breaking engine layer
- ❌ Forces compromises on game design

**Alternative 2: Single large struct (100+ bytes)**
- ❌ Cache performance degrades significantly
- ❌ Hot paths load unnecessary data
- ❌ Wastes memory for rarely-used fields

**Alternative 3: Hot/Cold only (no warm)**
- ❌ Forces binary choice: super-fast or super-slow
- ❌ 24-byte warm data is still cache-friendly
- ❌ Loses granularity for optimization

**Our choice: Hot/Warm/Cold (8 / 24 / Variable bytes)**
- ✅ Hot paths stay in L1 cache (8 bytes)
- ✅ Common operations fit L2 cache (8+24=32 bytes)
- ✅ Rare operations pay small penalty (dictionary lookup)
- ✅ Clear migration path for new features
- ✅ Burst-compatible (hot + warm NativeArrays)

---

## Future Considerations

### Potential Optimizations

**1. SIMD Batch Processing**
```csharp
// Process 4 provinces simultaneously with SIMD
[BurstCompile]
public void CalculateIncomesBatch(int startIndex) {
    float4 developments = new float4(
        hotData[startIndex + 0].development,
        hotData[startIndex + 1].development,
        hotData[startIndex + 2].development,
        hotData[startIndex + 3].development
    );
    // SIMD multiply all 4 at once
}
```

**2. Structure of Arrays (SoA) for Hot Data**
If profiling shows benefit, convert hot data to SoA:

```csharp
// Current (AoS): Province = [dev, fort, unrest, pop]
NativeArray<HegemonProvinceData> hotData;

// Potential (SoA): Separate arrays per field
NativeArray<byte> development;
NativeArray<byte> fortLevel;
NativeArray<byte> unrest;
NativeArray<byte> population;
```

Better for operations accessing only one field across many provinces.

**3. Custom Allocator for Cold Data**
```csharp
// Pool cold data objects to reduce GC pressure
ObjectPool<HegemonProvinceColdData> coldDataPool;
```

---

## References

- [phase-3-complete-scenario-loader-bug-fixed.md](../2025-10/09/phase-3-complete-scenario-loader-bug-fixed.md) - ProvinceState refactoring context
- [performance-architecture-guide.md](../../Engine/performance-architecture-guide.md) - Hot/cold data separation patterns
- [core-data-access-guide.md](../../Engine/core-data-access-guide.md) - Data access patterns
- [data-flow-architecture.md](../../Engine/data-flow-architecture.md) - System communication patterns

---

## Summary

**The hot/warm/cold pattern allows unlimited game feature expansion while maintaining performance.**

- **Hot (8 bytes)**: Every frame/tick access
- **Warm (24 bytes)**: Occasional access (monthly)
- **Cold (variable)**: Rare access (UI, player actions)

This decision prioritizes:
1. **Performance** - Hot paths stay fast
2. **Scalability** - Easy to add features
3. **Maintainability** - Clear separation of concerns

The extra 40KB for warm data (240KB total) is negligible compared to the architectural benefits gained.

---

*Decision documented: 2025-10-09*
*Implementation: Hot layer complete, Warm/Cold planned for future phases*
