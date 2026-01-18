# Troubleshooting: Performance Issues

This guide documents common performance problems and their solutions when building games with Archon Engine.

## Cache Bypass (Expensive Recalculations)

### Symptom
- First tick/frame is slow, subsequent ones fast
- Profiler shows expensive calculations repeated
- Cache exists but doesn't help performance

### Root Cause
Code calling calculator directly instead of cached values:

```csharp
// ❌ WRONG - Bypasses cache, recalculates every time
public FixedPoint64 GetIncome(ushort countryId)
{
    return incomeCalculator.CalculateMonthlyIncome(countryId);
}

// ✅ CORRECT - Uses cached value
public FixedPoint64 GetIncome(ushort countryId)
{
    return cachedIncome[countryId]; // Pre-calculated
}
```

### Solution
1. Verify cache is actually being USED, not just populated
2. Add profiling logs to identify the slow path
3. Use sparse collection iteration (iterate active items, not capacity)

```csharp
// ❌ SLOW - Iterates full capacity
for (int i = 0; i < countries.Capacity; i++)

// ✅ FAST - Iterates only active entries
foreach (var countryId in activeCountryIds)
```

---

## Managed Collections in Hot Paths

### Symptom
- GC allocations during gameplay
- Burst compilation failures
- Performance degradation in tick processing

### Root Cause
Using `Dictionary`, `List`, `Queue` in simulation-critical paths:

```csharp
// ❌ WRONG - Managed collections cause GC
Dictionary<ushort, List<UnitState>> unitsByProvince;
Queue<MovementOrder> pendingMovements;
```

### Solution
Convert simulation state to NativeCollections:

| Managed | Native Replacement |
|---------|-------------------|
| `Dictionary<K,V>` | `NativeParallelHashMap<K,V>` |
| `List<T>` | `NativeList<T>` |
| `Queue<T>` | `NativeQueue<T>` |
| `Dictionary<K, List<V>>` | `NativeParallelMultiHashMap<K,V>` |

```csharp
// ✅ CORRECT - Native collections, zero GC
NativeParallelMultiHashMap<ushort, UnitState> unitsByProvince;
NativeQueue<MovementOrder> pendingMovements;
```

---

## Frame-Coherent Cache Pattern

### Symptom
- Cached values stale after state changes
- UI shows old data
- Expensive recalculations happening multiple times per frame

### Solution
Use frame counter for cache invalidation:

```csharp
private int cachedFrame = -1;
private FixedPoint64 cachedValue;

public FixedPoint64 GetCachedValue()
{
    int currentFrame = Time.frameCount;
    if (currentFrame != cachedFrame)
    {
        cachedValue = ExpensiveCalculation();
        cachedFrame = currentFrame;
    }
    return cachedValue;
}
```

### Key Insight
Compute once per frame, reuse within frame. Avoids redundant calculations when multiple UI elements query same data.

---

## Boxing in Event Systems

### Symptom
- Profiler shows allocations when emitting events
- GC spikes correlating with event emission
- Frame stutters during heavy event periods

### Root Cause
Storing struct events in interface-typed collections causes boxing:

```csharp
// ❌ WRONG - Boxing occurs here
private Queue<IGameEvent> eventQueue;
eventQueue.Enqueue(myStructEvent); // Boxes struct to heap
```

### Solution
Use generic queues per event type:

```csharp
// ✅ CORRECT - No boxing
private Queue<MyEvent> myEventQueue; // T stays as value type
myEventQueue.Enqueue(myEvent);       // No boxing
```

### Key Lesson
Interface-typed collections ALWAYS box value types in C#. Use generic collections with concrete types.

---

## Dirty Flagging for Persistent State

### Symptom
- Performance degrades as data grows
- Full buffer copies every frame
- System doesn't scale

### Root Cause
Copying entire buffers when only a few entries changed:

```csharp
// ❌ WRONG - O(n) every frame
void OnBufferSwap()
{
    NativeArray<T>.Copy(readBuffer, writeBuffer); // Copies everything
}
```

### Solution
Track modified entries with dirty flags:

```csharp
private NativeList<int> dirtyIndices;

public void SetValue(int index, T value)
{
    buffer[index] = value;
    dirtyIndices.Add(index);  // Mark dirty
}

void OnBufferSwap()
{
    // Copy only dirty entries - O(k) where k = changes
    for (int i = 0; i < dirtyIndices.Length; i++)
    {
        writeBuffer[dirtyIndices[i]] = readBuffer[dirtyIndices[i]];
    }
    dirtyIndices.Clear();
}
```

### Performance Impact
10k provinces, 100 changed per frame:
- Full copy: ~80KB, ~0.1ms
- Dirty tracking: ~800B, ~0.001ms (100x faster)

### Key Lesson
Dirty flags are essential for persistent state. Mark dirty on every modification, copy only what changed.

---

## Burst: Flatten Before Parallelizing

### Symptom
- Error: "Nested native containers are illegal in jobs"
- Can't parallelize operations on hierarchical data
- Burst compilation fails

### Root Cause
Burst forbids nested NativeContainers for safety:

```csharp
// ❌ WRONG - Nested containers forbidden
NativeList<NativeList<Modifier>> modifiersByProvince;

[BurstCompile]
struct ProcessJob : IJobParallelFor
{
    public NativeList<NativeList<Modifier>> data; // ERROR!
}
```

### Solution
Flatten + Tag + Cache pattern:

```csharp
// ✅ CORRECT - Flat array with parent key
struct ModifierWithKey
{
    public ushort provinceId;  // Parent key
    public Modifier data;
}

NativeList<ModifierWithKey> flatModifiers;  // All modifiers flat
NativeParallelHashMap<ushort, int> provinceToFirstIndex;  // O(1) lookup

[BurstCompile]
struct ProcessJob : IJobParallelFor
{
    [ReadOnly] public NativeList<ModifierWithKey> modifiers;  // Works!
}
```

### Performance Impact
610k modifiers: 26ms → 3ms (87% improvement)

### Key Lesson
Data structure determines performance. Always flatten hierarchical data before Burst parallelization.

---

## Performance Debugging Checklist

1. **Profile first** - Don't guess, measure with Unity Profiler
2. **Check cache usage** - Is cache populated but bypassed?
3. **Verify sparse iteration** - Iterating active items, not capacity?
4. **Check collection types** - Any managed collections in hot paths?
5. **Monitor allocations** - Deep Profiler → GC Alloc column
6. **Frame-coherent caching** - Expensive calculations cached per frame?
7. **Dirty flagging** - Only copying/updating what changed?
8. **Burst compatibility** - Data flattened for parallelization?

## Critical Rules

| DO | DON'T |
|----|-------|
| Use NativeCollections in hot paths | Use Dictionary/List in simulation |
| Cache expensive calculations | Recalculate every query |
| Iterate sparse (active items) | Iterate full capacity |
| Use generic queues for events | Store structs in interface collections |
| Profile before optimizing | Guess at bottlenecks |
| Use dirty flags for persistent state | Copy entire buffers every frame |
| Flatten data for Burst | Use nested NativeContainers |

## API Reference

- [GameState](~/api/Core.GameState.html) - Central state hub
- [EventBus](~/api/Core.EventBus.html) - Zero-allocation event system
