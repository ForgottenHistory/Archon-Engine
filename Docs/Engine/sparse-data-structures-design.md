# Sparse Data Structures Design
**Purpose:** Architecture for optional/rare data that scales with mods
**Status:** 🔄 Design Phase
**Priority:** High (implement before buildings/modifiers)

---

## The Problem: HOI4's 16x Slowdown

**Hearts of Iron 4 mistake:**
- Base game: 30 equipment types
- With mods: 500+ equipment types
- Dense arrays: Every province checks all 500 slots
- Result: **16x slower** with popular mods

**Why it happened:**
```
Province struct {
    bool[500] hasEquipment;  // Most false, still iterate all
}

Tick processing:
    For each province (10k):
        For each equipment type (500):
            if (hasEquipment[i]) { process }

Result: 5 million checks, 99% wasted
```

**The core issue:** Dense storage scales with POSSIBLE items, not ACTUAL items.

---

## Design Principle

**Use sparse structures when:**
- Base game: N items
- Mods can add: 5-10x more items
- Most entities don't have most items

**Use dense structures when:**
- Data is ALWAYS present
- Mod count doesn't multiply items
- Fixed at engine level

---

## Sparse vs Dense Decision Matrix

| Data Type | Base Count | Mod Multiplier | Most Entities Have | Pattern |
|-----------|------------|----------------|-------------------|---------|
| **Owner/Terrain** | N/A | N/A | ALL | Dense (ProvinceState) |
| **Buildings** | 30 | 5-10x | 0-5 | Sparse |
| **Modifiers** | 50 | 10-20x | 0-3 | Sparse |
| **Trade Goods** | 20 | 2-3x | 1-2 | Sparse |
| **Army Units** | 50 | 5-10x | 0-10 | Sparse |
| **Development** | N/A | N/A | ALL | Dense (Game layer slot) |

**Rule:** If mods multiply count AND most entities don't have most items → Sparse

---

## Architecture: Three-Layer Pattern

### Layer 1: Definitions (Static Registry)
**What:** Type definitions loaded from JSON5
**Where:** `Game/Data/Definitions/`
**Lifetime:** Loaded once at startup, never modified

### Layer 2: Instance Storage (Sparse Collections)
**What:** Which entities have which items
**Where:** `Game/Systems/SparseData/`
**Lifetime:** Pre-allocated at initialization, cleared/reused

### Layer 3: Access API (Query Layer)
**What:** Fast lookups and iteration
**Where:** `Game/Queries/`
**Lifetime:** Stateless, queries Layer 2

---

## Layer 1: Definition Registry

**Purpose:** Static type definitions (immutable after loading)

**Pattern:**
- Load all definitions from JSON5 at startup
- Register in definition registries
- Mods add definitions, never remove
- Each definition gets unique ID (ushort)

**Example Definitions:**
```
BuildingDefinition:
    ID: ushort
    Name: string
    Cost: FixedPoint64
    DevelopmentBonus: ushort
    ProductionType: ProductionTypeId

ModifierDefinition:
    ID: ushort
    Name: string
    Effects: Dictionary<StatType, FixedPoint64>
    Duration: int (ticks)

TradeGoodDefinition:
    ID: ushort
    Name: string
    BasePrice: FixedPoint64
    Category: TradeGoodCategory
```

**Storage:** Dictionary-based registries (cold data, not hot path)

**Mod Loading:**
```
Base game registers: Buildings 1-30
Mod 1 registers: Buildings 31-80
Mod 2 registers: Buildings 81-150
Result: 150 total building types
```

---

## Layer 2: Sparse Instance Storage

**Problem:** Need fast iteration over ACTUAL items (not all possible)

**Solution:** NativeMultiHashMap (entity ID → item IDs)

### Core Pattern: NativeMultiHashMap

**Why NativeMultiHashMap:**
- One key (provinceID) → multiple values (buildingIDs)
- Native container (Burst compatible, pre-allocated)
- Fast iteration over actual items only
- Memory scales with usage, not possibility

**Storage Structure:**
```
ProvinceBuildings: NativeMultiHashMap<ushort, ushort>
    Key: Province ID
    Value: Building ID (multiple per key)

Iteration:
    provinceBuildings.TryGetFirstValue(provinceId, out buildingId, out iterator)
    while (provinceBuildings.TryGetNextValue(out buildingId, ref iterator))
        // Process each building in province
```

**Memory Pre-Allocation (Principle 4):**
```
Initialization:
    Estimate: 10k provinces × 5 buildings average = 50k entries
    Pre-allocate: NativeMultiHashMap(50k capacity, Allocator.Persistent)

Gameplay:
    Add/remove entries (no allocation)
    Capacity fixed, grows if exceeded (warning)
```

### Pattern: Sparse Collection Manager

**Purpose:** Centralized management of all sparse collections

**Responsibilities:**
- Pre-allocate all sparse collections at initialization
- Provide add/remove/query API
- Track memory usage
- Warn if capacity exceeded

**Location:** `Game/Systems/SparseData/SparseCollectionManager.cs`

---

## Layer 3: Access Patterns

### Pattern A: Existence Check (Fast)
**Question:** "Does province 42 have Farm building?"

**Implementation:**
```
API: bool HasBuilding(ushort provinceId, ushort buildingId)

Method:
    1. Get first value from MultiHashMap
    2. Iterate until match or exhausted
    3. Return true/false

Performance: O(m) where m = buildings in province (typically 3-5)
```

### Pattern B: Iteration (Fast)
**Question:** "What buildings does province 42 have?"

**Implementation:**
```
API: NativeArray<ushort> GetBuildings(ushort provinceId, Allocator allocator)

Method:
    1. Get first value from MultiHashMap
    2. Collect all values into NativeList
    3. Convert to NativeArray
    4. Return (caller disposes)

Performance: O(m) where m = buildings in province
Memory: Temp allocation for result (caller disposes)
```

### Pattern C: Process All Provinces (Parallel)
**Question:** "Update all farms across all provinces"

**Implementation:**
```
API: ProcessBuildingType(ushort buildingTypeId, Action<ushort> processor)

Method:
    For each province:
        If province has buildingTypeId:
            processor(provinceId)

Optimization: Can parallelize with IJobParallelFor
Performance: O(n*m) where n = provinces, m = buildings per province
```

### Pattern D: Filter Query (Moderate)
**Question:** "Which provinces have farms?"

**Implementation:**
```
API: NativeArray<ushort> GetProvincesWithBuilding(ushort buildingId, Allocator allocator)

Method:
    Result list = NativeList
    For each province:
        If HasBuilding(province, buildingId):
            Result.Add(province)
    Return NativeArray (caller disposes)

Performance: O(n) where n = provinces
Optimization: Cache result per frame (frame-coherent caching)
```

---

## Memory Budget Analysis

**Scenario: 10k provinces, 100 building types**

**Naive Dense Approach:**
```
bool[100] per province = 100 bytes
10k provinces × 100 bytes = 1 MB
With mods (500 types): 5 MB
```

**Sparse Approach:**
```
Average 5 buildings per province
10k provinces × 5 buildings × 4 bytes (entry) = 200 KB
With mods (500 types possible): Still 200 KB
```

**Savings:** 4.8 MB saved (96% reduction at mod scale)

**Trade-off:** Iteration complexity (linear per province vs constant lookup)

---

## Pre-Allocation Strategy (Principle 4)

### Initialization Phase

**Estimate capacity based on:**
- Province count: 10k (known)
- Average buildings per province: 5 (estimated)
- Headroom multiplier: 2x (safety margin)
- Total: 10k × 5 × 2 = 100k entries

**Pre-allocate:**
```
provinceBuildings = new NativeMultiHashMap<ushort, ushort>(
    100_000,  // Capacity
    Allocator.Persistent
);

provinceModifiers = new NativeMultiHashMap<ushort, ushort>(
    50_000,   // Fewer modifiers
    Allocator.Persistent
);

provinceTradeGoods = new NativeMultiHashMap<ushort, ushort>(
    20_000,   // Even fewer trade goods
    Allocator.Persistent
);
```

### Gameplay Phase

**Add/Remove entries:**
- No allocation (reuse pre-allocated capacity)
- If capacity exceeded: Log warning, allow growth (rare)

**Monitoring:**
- Track usage per frame (debug info)
- Warn if approaching capacity (80% threshold)
- Profile memory pressure

---

## Mod Compatibility

### Dynamic ID Assignment

**Problem:** Mods add definitions at runtime, need unique IDs

**Solution:** Sequential ID assignment during loading

**Pattern:**
```
Base game loads:
    Farm (ID 1)
    Mine (ID 2)
    ...
    Market (ID 30)

Mod 1 loads:
    Advanced_Farm (ID 31)
    Workshop (ID 32)
    ...

Mod 2 loads:
    Mega_Mine (ID 51)
    ...
```

**Save compatibility:**
- Save files store ID + definition name
- Load verifies definition exists
- Warns if mod missing (graceful degradation)

### Definition Versioning

**Pattern:**
```
BuildingDefinition {
    ID: ushort (assigned at load)
    StringID: string ("farm") (stable across saves)
    Version: ushort (mod compatibility)
}

Save format:
    Building instance: { StringID: "farm", Count: 5 }

Load process:
    1. Look up definition by StringID
    2. Get current ID
    3. Add to sparse collection with current ID
    4. Warn if definition missing
```

---

## Implementation Checklist

### Phase 1: Foundation
- [x] Create SparseCollectionManager class
- [x] Design definition registry interfaces
- [x] Plan capacity estimation formulas
- [x] Document access patterns

### Phase 2: Core Infrastructure
- [x] Implement NativeMultiHashMap allocations
- [x] Add pre-allocation policy enforcement
- [x] Create query APIs (Has/Get/Process/Filter)
- [x] Add memory monitoring

**Status:** ✅ Complete (2025-10-15) - See [session log](../Log/2025-10/15/4-pre-allocation-and-sparse-data-infrastructure.md)

### Phase 3: Definition System
- [ ] Create BuildingDefinition structure
- [ ] Create ModifierDefinition structure
- [ ] Create TradeGoodDefinition structure
- [ ] Implement definition loading from JSON5

### Phase 4: Integration
- [ ] Integrate with building system
- [ ] Integrate with modifier system
- [ ] Add frame-coherent caching for queries
- [ ] Create profiling hooks

### Phase 5: Mod Support
- [ ] Dynamic ID assignment
- [ ] Save/load with string IDs
- [ ] Version compatibility checks
- [ ] Graceful degradation

---

## Performance Guarantees

**Access Patterns:**
- Existence check: O(m) where m = items per entity (typically < 10)
- Get all items: O(m) where m = items per entity
- Process all entities: O(n×m) where n = entities, m = items per entity
- Filter query: O(n) where n = entities (cacheable)

**Memory:**
- Scales with ACTUAL usage, not POSSIBLE items
- Pre-allocated (zero gameplay allocations)
- Bounded growth (capacity warnings)

**Mod Scaling:**
- 30 types → 500 types: No performance degradation
- Memory scales with usage, not type count
- Iteration only over actual items

---

## Anti-Patterns to Avoid

**❌ Dense Arrays:**
```
bool[500] buildings;  // Wastes memory, slow iteration
```

**❌ Dictionary Per Entity:**
```
Dictionary<ushort, List<ushort>> buildings;  // Allocations, not Burst compatible
```

**❌ Linear Search:**
```
List<(ushort province, ushort building)> all;  // O(n) for existence check
```

**✅ Sparse MultiHashMap:**
```
NativeMultiHashMap<ushort, ushort> buildings;  // Optimal for sparse data
```

---

## Related Documents

- **[performance-architecture-guide.md](performance-architecture-guide.md)** - Principle 4: Pre-allocation
- **[engine-game-separation.md](engine-game-separation.md)** - Definition vs instance pattern
- **[master-architecture-document.md](master-architecture-document.md)** - Hot/cold separation
- **[../Planning/paradox-dev-diary-lessons.md](../Planning/paradox-dev-diary-lessons.md)** - HOI4's equipment mistake

---

## Success Criteria

**The design succeeds if:**

1. ✅ **Mods can add 10x items without performance degradation**
   - 30 buildings → 300 buildings: No slowdown
   - Memory scales with usage, not type count

2. ✅ **Zero allocations during gameplay (Principle 4)**
   - All collections pre-allocated at initialization
   - Add/remove operations reuse capacity

3. ✅ **Iteration only over actual items**
   - Never iterate over empty slots
   - Performance proportional to usage, not possibility

4. ✅ **Simple query API**
   - Has/Get/Process/Filter patterns cover all use cases
   - Frame-coherent caching for expensive queries

5. ✅ **Mod compatibility**
   - Dynamic ID assignment
   - Save/load with string IDs
   - Graceful degradation when mods missing

---

## Key Insights

**Paradox Lesson:**
- HOI4 designed dense first → couldn't change without breaking saves/mods
- We design sparse first → scales from day 1

**Architecture Lesson:**
- Engine provides NativeMultiHashMap mechanism
- Game defines what's sparse (buildings, modifiers)
- Clear separation prevents HOI4's mistake

**Performance Lesson:**
- Dense = O(all possible) iteration (bad with mods)
- Sparse = O(actual items) iteration (scales with usage)
- Pre-allocation = zero malloc lock contention

---

*Created: 2025-10-15*
*Status: 🔄 Design phase - ready for implementation*
*Context: Learned from HOI4's 30→500 equipment type disaster*
