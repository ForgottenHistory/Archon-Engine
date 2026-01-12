# Flat Storage for Burst Compilation

**Status:** Production Standard

---

## Core Principle

**Nested containers block parallelization. Flat storage unlocks Burst compilation.**

---

## The Constraint

Unity Burst forbids nested NativeContainers in jobs. This is a safety restriction preventing race conditions on nested allocations.

**Illegal in Burst:**
- NativeList inside NativeList
- NativeArray inside NativeArray
- Dictionary of NativeLists

**Why this restriction:**
- Memory safety in parallel execution
- Prevents race conditions
- Ensures deterministic parallel behavior

---

## The Solution: Flatten Everything

Convert nested relationships into single-level collections with tagging or keys.

### Strategy 1: Tagged Elements
Store all items in flat array with entity key attached to each.

**Before (nested, illegal):**
Entity → List of items

**After (flat, Burst compatible):**
All items in one array, each tagged with entity ID

**Use when:**
- Processing all items in parallel
- Items per entity are relatively fixed
- Need contiguous memory for cache efficiency

### Strategy 2: MultiHashMap
Native container designed for one-to-many relationships.

**Before (nested, illegal):**
Dictionary of entity → List of items

**After (flat, Burst compatible):**
MultiHashMap of entity → items (multiple values per key)

**Use when:**
- Variable items per entity
- Need sparse storage
- Frequent add/remove operations

### Strategy 3: Flatten Queues/Stacks
MultiHashMap preserves insertion order (FIFO).

**Before (nested, illegal):**
Dictionary of entity → Queue of items

**After (flat, Burst compatible):**
MultiHashMap iterator returns items in insertion order

---

## When to Use Flat Storage

### Use When:
- Need Burst compilation for parallel processing
- Processing thousands/millions of items
- Performance-critical hot paths (tick updates)
- Future-proofing for potential parallelization

### Don't Use When:
- Query-only methods (returning data for UI)
- One-time operations (loaders, initialization)
- Static registries (definitions, never modified)
- UI/presentation layer (not simulation state)

---

## Trade-offs

### Tagged Array
| Aspect | Pro | Con |
|--------|-----|-----|
| Memory | Contiguous, cache-friendly | +8 bytes per element for key |
| Access | Cache first index, scan | O(m) scan per entity |
| Modification | Append O(1), remove marks dirty | Needs periodic compaction |

### MultiHashMap
| Aspect | Pro | Con |
|--------|-----|-----|
| Memory | Sparse, scales with actual items | Hash table overhead |
| Access | O(1) hash lookup | Not cache-friendly |
| Modification | O(1) add/remove | No compaction needed |

---

## Performance Impact

**Without flattening:** Burst compilation impossible, single-threaded execution.

**With flattening:** Full Burst parallelization, 5-10x typical speedup.

**Real example:** Opinion modifier decay: 26ms → 3ms (87% improvement) after flattening for Burst.

---

## Query Patterns

### Tagged Array Queries
- **Cache first index** per entity for O(1) start point
- **Scan contiguous block** until key changes
- **Rebuild cache** after compaction

### MultiHashMap Queries
- **Iterator-based** retrieval (TryGetFirst, TryGetNext)
- **No cache needed** - hash provides direct access
- **Insertion order preserved** for ordered data

---

## Compaction Strategy

Tagged arrays accumulate "dead" entries when items are removed.

**Don't compact every removal** - too expensive.

**Do compact periodically** - monthly tick, at acceptable cost.

**Pattern:**
1. Mark removed items as invalid
2. Periodically sweep and compact
3. Rebuild index cache after compaction

---

## Anti-Patterns

**Non-blittable values in MultiHashMap:**
V must be blittable (no managed references). Flatten complex types.

**Assuming order in tagged arrays:**
Compaction may reorder. Use sequence numbers if order matters.

**Excessive copying:**
Don't copy whole structure per operation. Mark dirty, batch modifications.

**Cache invalidation complexity:**
Range-based caching breaks on insert/delete. Use simple first-index cache.

---

## Decision Framework

1. **Is this simulation state storage?**
   - Yes → Must use NativeCollections
   - No → Managed collections acceptable

2. **Is this a hot path?**
   - Yes → Strongly consider flattening for Burst
   - No → Evaluate other factors

3. **Do I need batch processing?**
   - Yes → Must flatten (Burst requires it)
   - No → Flattening still beneficial

4. **Is the relationship one-to-many?**
   - Yes → MultiHashMap or tagged array
   - No → Simple NativeParallelHashMap

5. **Does order matter?**
   - Yes → MultiHashMap (preserves insertion order)
   - No → Either approach works

---

## Integration with Other Patterns

**Pattern 8 (Sparse Collections):**
Flat MultiHashMap IS sparse - only stores active relationships.

**Pattern 10 (Frame-Coherent Caching):**
Cache first indices for tagged arrays.

**Pattern 12 (Pre-Allocation):**
Pre-allocate flat storage capacity at initialization.

**Pattern 5 (Fixed-Point Determinism):**
Burst jobs preserve determinism - same results across platforms.

---

## Success Metrics

- **5-10x speedup** typical with Burst on flat storage
- **Zero GC allocations** - all pre-allocated
- **Deterministic results** - multiplayer-safe
- **Scales to millions** of elements

---

*Nested blocks Burst. Flatten enables parallelism. Tag or hash for relationships.*
