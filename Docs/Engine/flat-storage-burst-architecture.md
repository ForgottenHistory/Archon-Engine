# Flat Storage for Burst Compilation
**Purpose:** Enable high-performance parallel processing using Unity Burst compiler

---

## Philosophy: Flatten for Speed

> **Nested containers block parallelization.
> Flat storage unlocks Burst compilation.**

**Traditional approach:** Nested collections (Dictionary→List, Array→Array)
**Problem:** Unity Burst forbids nested NativeContainers (safety restriction)
**Solution:** Flatten all relationships into single-level NativeCollections with tagging/keys

---

## The Core Constraint

**Unity Burst Restriction:**
```
Nested native containers are illegal in jobs.
```

**What This Means:**
- Cannot pass `NativeList<T>` where T contains another `NativeList`
- Cannot store `NativeArray` inside `NativeArray`
- Cannot use `Dictionary<K, NativeList<V>>` in Burst jobs
- Any nested structure = compile error

**Why This Restriction Exists:**
- Memory safety in parallel execution
- Prevents race conditions on nested allocations
- Ensures deterministic parallel behavior

---

## Flattening Strategies

### Strategy 1: Tagged Elements (One-to-Many)

**Use Case:** Store multiple items per entity (modifiers per relationship, waypoints per unit)

**Before (Nested - Illegal in Burst):**
```
Dictionary<ulong, List<OpinionModifier>> modifiersByRelationship
- Key: relationshipKey (country1, country2)
- Value: List of 0-10 modifiers
```

**After (Flat - Burst Compatible):**
```
NativeList<ModifierWithKey> allModifiers
- Each modifier tagged with relationshipKey
- All 610k modifiers in single flat array
```

**Query Pattern:**
```csharp
// Linear scan with early exit (cache first index)
NativeParallelHashMap<ulong, int> firstModifierIndex;

for (int i = firstModifierIndex[key]; i < allModifiers.Length; i++)
{
    if (allModifiers[i].key != relationshipKey) break; // Contiguous storage
    // Process modifier
}
```

**Trade-off:**
- **Cost:** +8 bytes per element (key storage)
- **Gain:** Burst parallelization (26ms → 3ms in DiplomacySystem)
- **Memory:** Predictable, append-only growth

### Strategy 2: MultiHashMap (One-to-Many Sparse)

**Use Case:** Sparse relationships with variable counts (provinces per country, items per container)

**Before (Nested - Illegal in Burst):**
```
Dictionary<ushort, NativeList<ushort>> provincesByCountry
- Key: countryId
- Value: List of province IDs
```

**After (Flat - Burst Compatible):**
```
NativeParallelMultiHashMap<ushort, ushort> provincesByCountry
- Key: countryId
- Values: multiple province IDs
- Automatic handling of one-to-many
```

**Query Pattern:**
```csharp
// Iterator-based retrieval
NativeParallelMultiHashMapIterator<ushort> iterator;
ushort provinceId;

if (map.TryGetFirstValue(countryId, out provinceId, out iterator))
{
    result.Add(provinceId);
    while (map.TryGetNextValue(out provinceId, ref iterator))
    {
        result.Add(provinceId);
    }
}
```

**Trade-off:**
- **Cost:** Hash lookup overhead
- **Gain:** Sparse storage (only active relationships), Burst compatible
- **Memory:** Scales with actual items, not capacity

### Strategy 3: Flatten Queue/Stack to MultiHashMap

**Use Case:** Ordered sequences (movement paths, task queues)

**Before (Nested - Illegal):**
```
Dictionary<ushort, Queue<ushort>> unitPaths
- Key: unitID
- Value: Queue of waypoint province IDs (ordered)
```

**After (Flat - Burst Compatible):**
```
NativeParallelMultiHashMap<ushort, ushort> unitPathsFlat
- Key: unitID
- Values: waypoints in insertion order
- Iterator preserves FIFO order
```

**Query Pattern:**
```csharp
// Rebuild queue from flat storage
var path = new NativeList<ushort>(Allocator.Temp);
NativeParallelMultiHashMapIterator<ushort> iterator;
ushort waypoint;

if (paths.TryGetFirstValue(unitID, out waypoint, out iterator))
{
    path.Add(waypoint); // First = next waypoint
    while (paths.TryGetNextValue(out waypoint, ref iterator))
    {
        path.Add(waypoint); // Subsequent waypoints in order
    }
}
```

**Trade-off:**
- **Cost:** Must rebuild queue for modification (pop/peek)
- **Gain:** Burst compatible, sparse storage
- **Note:** Iterator order matches insertion order (FIFO preserved)

---

## When to Use Flat Storage

### ✅ Use Flat Storage When:

1. **Need Burst Compilation**
   - Processing thousands/millions of items in parallel
   - Performance-critical hot paths (daily/monthly ticks)
   - Example: 610k opinion modifiers decaying monthly

2. **Batch Processing Required**
   - IJobParallelFor across entire dataset
   - Must process ALL items simultaneously
   - Example: Decay all modifiers, update all moving units

3. **Sparse Relationships**
   - Not all entities have data (only 979/4096 countries active)
   - Variable counts per entity (0-10 modifiers per relationship)
   - NativeParallelMultiHashMap ideal

4. **Future-Proofing**
   - System may need Burst later
   - Architectural purity (zero GC allocations)
   - Multiplayer determinism requirements

### ❌ Don't Use Flat Storage When:

1. **Query-Only Methods**
   - Returning UI data (GetAllies() → List<ushort>)
   - One-time queries for display
   - Managed collections acceptable (not simulation state)

2. **One-Time Operations**
   - Loaders (scenario loading, definition parsing)
   - Initialization code (runs once at startup)
   - Serialization/validation (not hot path)

3. **Registries**
   - Static definition lookups (building types, unit types)
   - Never modified during gameplay
   - Dictionary acceptable (not simulation state)

4. **UI/Presentation Layer**
   - UI state (console history, selection lists)
   - Rendering helpers (event-driven, not per-frame)
   - Managed collections acceptable

---

## Performance Characteristics

### Flat Tagged Array (Strategy 1)

**Memory:**
- Base element size + key size (8 bytes)
- Example: 24-byte OpinionModifier + 8-byte key = 32 bytes
- 610k modifiers = 19.5 MB (acceptable)

**Query Performance:**
- O(1) cache lookup (first index)
- O(m) scan where m = items per key (typically <10)
- Contiguous memory = cache-friendly

**Modification:**
- Append: O(1)
- Remove: Mark dirty, compact later
- Compact: O(n) sequential (monthly is fine)

### MultiHashMap (Strategy 2)

**Memory:**
- Hash table overhead + key-value pairs
- Sparse storage (only active items)
- Example: 10k relationships vs 4096² capacity = 99.94% savings

**Query Performance:**
- O(1) hash lookup
- O(m) iterator for multiple values
- Not cache-friendly (hash table indirection)

**Modification:**
- Add: O(1) average
- Remove: O(1) per item (find + delete)
- No compaction needed

---

## Migration Path: Nested to Flat

### Step 1: Identify Nested Structure
```csharp
// BEFORE
Dictionary<ulong, List<OpinionModifier>> modifiersByRelationship;
```

### Step 2: Create Tagged Struct
```csharp
// NEW
struct ModifierWithKey
{
    public ulong relationshipKey; // Tag for grouping
    public OpinionModifier modifier;
}
```

### Step 3: Flatten Storage
```csharp
// NEW
NativeList<ModifierWithKey> allModifiers;
NativeParallelHashMap<ulong, int> modifierCache; // key → first index
```

### Step 4: Update Query Logic
```csharp
// BEFORE
var modifiers = modifiersByRelationship[key];

// AFTER
int firstIndex = modifierCache[key];
for (int i = firstIndex; i < allModifiers.Length; i++)
{
    if (allModifiers[i].relationshipKey != key) break;
    // Process modifier
}
```

### Step 5: Enable Burst Job
```csharp
[BurstCompile]
struct DecayModifiersJob : IJobParallelFor
{
    public NativeList<ModifierWithKey> modifiers; // ✅ Legal!

    public void Execute(int index)
    {
        var mod = modifiers[index];
        mod.modifier.value -= mod.modifier.decayRate;
        modifiers[index] = mod;
    }
}
```

---

## Common Pitfalls

### Pitfall 1: Non-Blittable Values
**Problem:** MultiHashMap<K, V> requires V to be blittable (no managed references)

**Illegal:**
```csharp
NativeParallelMultiHashMap<ushort, Queue<ushort>> // Queue not blittable!
```

**Solution:** Flatten Queue to individual entries
```csharp
NativeParallelMultiHashMap<ushort, ushort> // Each waypoint separate
```

### Pitfall 2: Assuming Order in Tagged Array
**Problem:** Compaction may reorder elements

**Solution:** Use MultiHashMap if order matters, or store sequence number in tag

### Pitfall 3: Excessive Copying
**Problem:** Flattening by copying entire structure every operation

**Solution:**
- Mark dirty, compact periodically (monthly, not per-operation)
- Use append-only for adds
- Batch modifications

### Pitfall 4: Cache Invalidation Complexity
**Problem:** Range-based caching requires complex invalidation on insert/delete

**Solution:** Use tagged elements (simpler cache) or MultiHashMap (no cache needed)

---

## Real-World Example: DiplomacySystem

**Scale:** 610,750 opinion modifiers across 61,075 relationships

**Before (Nested):**
- `NativeList<DiplomacyColdDataNative>` each containing `NativeList<OpinionModifier>`
- Cannot use Burst (nested containers)
- Monthly decay: 21ms (unacceptable)

**After (Flat Tagged Array):**
- `NativeList<ModifierWithKey>` - single flat array
- `NativeParallelHashMap<ulong, int>` - cache first index per relationship
- Burst IJobParallelFor for decay
- Monthly decay: 3ms (87% faster, within budget)

**Implementation:**
```csharp
[BurstCompile]
struct DecayModifiersJob : IJobParallelFor
{
    public NativeList<ModifierWithKey> modifiers;
    public NativeArray<bool> decayedFlags;

    public void Execute(int index)
    {
        var modWithKey = modifiers[index];
        if (modWithKey.modifier.decayRate == 0) return;

        modWithKey.modifier.value -= modWithKey.modifier.decayRate;

        if (modWithKey.modifier.value <= 0)
        {
            decayedFlags[index] = true; // Mark for removal
        }
        else
        {
            modifiers[index] = modWithKey; // Update
        }
    }
}
```

**Results:**
- 26ms → 3ms (87% improvement)
- Zero GC allocations
- Deterministic (multiplayer-safe)
- Scales to millions of modifiers

---

## Decision Framework

**Question 1:** Is this simulation state storage?
- **Yes:** Must use NativeCollections (flat or not)
- **No:** Managed collections acceptable (UI, loaders, registries)

**Question 2:** Is this a hot path (daily/monthly tick)?
- **Yes:** Strongly consider flat storage for future Burst
- **No:** Evaluate based on other factors

**Question 3:** Do I need batch processing?
- **Yes:** Must use flat storage (Burst requires it)
- **No:** Flat storage still beneficial (zero GC, future-proof)

**Question 4:** Is the relationship one-to-many?
- **Yes:** Use MultiHashMap (Strategy 2) or Tagged Array (Strategy 1)
- **No:** Use simple NativeParallelHashMap

**Question 5:** Does order matter?
- **Yes:** Use MultiHashMap (preserves insertion order) or Tagged Array with sequence
- **No:** Either approach works

---

## Integration with Existing Patterns

### Pattern 8: Sparse Collections
Flat storage IS sparse collection when using MultiHashMap
- Only stores active relationships
- Scales with actual data, not capacity

### Pattern 11: Frame-Coherent Caching
Cache first index for tagged arrays
- O(1) lookup to start of contiguous block
- Rebuild cache after compaction

### Pattern 12: Pre-Allocation
Pre-allocate flat storage capacity
- NativeList.Capacity = worst-case estimate
- Avoid mid-gameplay allocations

### Pattern 5: Fixed-Point Determinism
Burst jobs preserve determinism
- No float operations
- Identical results across platforms
- Multiplayer-safe

---

## Success Metrics

**Performance:**
- 5-10x speedup typical with Burst on flat storage
- DiplomacySystem: 26ms → 3ms (87% improvement)
- Scales to millions of elements

**Memory:**
- Predictable overhead (+8 bytes per element for tagging)
- Sparse storage prevents waste (only active items)
- No fragmentation (append-only with periodic compact)

**Determinism:**
- Burst compilation ensures identical results
- No float operations in jobs
- Multiplayer-safe

**Maintainability:**
- Simpler than range-based caching
- Clear query patterns (cache + scan or iterator)
- Batch modifications (compact monthly, not per-op)

---

## Summary

**Core Principle:** Nested containers block Burst. Flatten with tagging or MultiHashMap.

**Three Strategies:**
1. Tagged Array (dense, cache-friendly, batch processing)
2. MultiHashMap (sparse, flexible, iterator-based)
3. Flatten Queue/Stack (preserve order in MultiHashMap)

**When to Use:** Simulation state + (hot path OR future Burst OR architectural purity)

**When Not to Use:** UI, loaders, registries, query-only methods

**Typical Results:** 5-10x speedup, zero GC, deterministic, scales to millions

---

**Related Documentation:**
- [data-flow-architecture.md](data-flow-architecture.md) - System communication patterns
- [decisions/diplomacy-flat-storage-architecture.md](../decisions/diplomacy-flat-storage-architecture.md) - Detailed decision log
- CLAUDE.md - Pattern 12 (Pre-allocation), Pattern 5 (Determinism)

**Related Sessions:**
- [Log/2025-10/25/1-diplomacy-burst-optimization.md](../Log/2025-10/25/1-diplomacy-burst-optimization.md)
- [Log/2025-10/25/2-performance-refactoring-session.md](../Log/2025-10/25/2-performance-refactoring-session.md)
