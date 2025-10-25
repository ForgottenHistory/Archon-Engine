# Burst Optimization: Flat Storage Pattern
**Pattern Type:** Performance Architecture
**Applies To:** Unity Burst, NativeCollections, Parallel Processing
**Context:** 610k modifiers, 26ms → 3ms (87% improvement)

---

## Core Principle

**Burst requires flat data structures.** Nested NativeContainers = architectural limitation, not fixable.

---

## The Pattern

**Problem:** Nested collections need parallel processing
```
NativeList<Parent>
  └── Each contains NativeList<Child>  ← ILLEGAL in Burst jobs
```

**Solution:** Flatten + tag each item with parent key
```
NativeList<ItemWithKey>  ← Single flat array, all items from all parents
  struct ItemWithKey { ulong parentKey; Item data; }
```

**Cache:** Map parentKey → first index for O(1) queries

---

## Why This Works

**Burst Constraint:** Jobs cannot access nested NativeContainers (safety system)
**Flat Array:** Zero nesting, direct parallel access across all cores
**Tagging:** Each item knows its parent, no separate index structure
**Cache:** O(1) lookup to parent's items (items contiguous by parent)

**Trade-offs:**
- Memory: +8 bytes per item for parent key
- Complexity: Cache rebuild after modifications
- Benefit: True parallelization (10× speedup)

---

## Failed Approaches (Don't Try These)

### 1. Nested NativeContainers
**Error:** `Nested native containers are illegal in jobs`
**Why:** Unity safety system, architectural limitation
**Lesson:** ALWAYS flatten before Burst

### 2. Job-Per-Parent (61k separate IJob instances)
**Result:** WORSE performance (35ms vs 21ms baseline)
**Why:** Job scheduling overhead > parallel speedup
**Threshold:** Use batches of 64-256, not thousands of tiny jobs

### 3. Flatten + Rebuild Each Frame
**Result:** WORSE performance (57ms vs 21ms baseline)
**Why:** Copying 610k structs twice > computational savings
**Lesson:** Data structure determines performance, not just algorithm

---

## Implementation Checklist

**Data Structure:**
- [ ] Flat NativeList/NativeArray (no nested containers)
- [ ] Tag struct with parent identifier (ulong/int key)
- [ ] Parent → index cache (NativeParallelHashMap)

**Burst Job:**
- [ ] IJobParallelFor with batches of 64
- [ ] READ-ONLY operations (write to separate output array)
- [ ] No branching/divergence in hot path

**Cache Management:**
- [ ] Rebuild after modifications (not incremental)
- [ ] Map parent key → first item index
- [ ] Items for same parent stored contiguously

**Determinism:**
- [ ] Job is READ-ONLY (no race conditions)
- [ ] Modifications sequential on main thread
- [ ] Insertion order preserved (append-only)

---

## Performance Expectations

**Burst Speedup:** 5-10× for compute-bound operations
**Overhead:** Flat storage rebuild ~O(n), acceptable if monthly
**Cache Rebuild:** O(n), do after modifications not per-query
**Memory Cost:** +30% for parent keys (acceptable for 10× speedup)

**When NOT Worth It:**
- Small datasets (<10k items) - overhead > benefit
- Infrequent operations (yearly ticks) - complexity not justified
- Simple O(1) operations - already fast

---

## Pattern Extensions

**Amortization:** Spread processing across frames
- 3ms job ÷ 4 frames = <1ms per frame
- Process 25% of items per frame

**Dirty Flags:** Only process changed items
- Track modifications since last process
- Skip unchanged parent groups

**LOD System:** Variable processing frequency
- Important items: Every frame
- Distant items: Every 10 frames

---

## Real-World Results

**DiplomacySystem Case Study:**
- Items: 610,750 modifiers across 61,075 relationships
- Baseline: 26ms (managed collections)
- NativeCollections: 21ms (19% improvement)
- Flat + Burst: 3ms (87% improvement)

**Key Factors:**
- Compute-bound: IsFullyDecayed() called 610k times
- Parallel-friendly: No dependencies between items
- Monthly frequency: Rebuild overhead acceptable
- Determinism: Read-only job, sequential compaction

---

## Anti-Patterns

**Don't:**
- ❌ Try to "fix" nested containers in Burst (architectural limitation)
- ❌ Schedule job-per-item for large datasets (overhead kills)
- ❌ Update cache incrementally (rebuild simpler + predictable)
- ❌ Use O(n) filtering on flat arrays without cache
- ❌ Copy data unnecessarily (flatten-in-place when possible)

**Do:**
- ✅ Flatten data structure before considering Burst
- ✅ Use batches of 64-256 items per job thread
- ✅ Rebuild cache after modifications (monthly/batch)
- ✅ Keep jobs READ-ONLY for determinism
- ✅ Profile before/after (Burst not always worth complexity)

---

## Generalization

**This Pattern Applies To:**
- Units with equipment lists (unit → equipment)
- Provinces with buildings (province → buildings)
- Countries with modifiers (country → modifiers)
- Characters with traits (character → traits)

**Pattern Recognition:**
- Parent-child one-to-many relationship
- Need parallel processing of children
- Children accessed frequently
- Dataset large enough to justify complexity (>10k items)

**Alternative:** If children rarely accessed, keep nested. Flat storage only worth it for hot paths.

---

*Lesson from: DiplomacySystem Burst optimization (2025-10-25)*
*Pattern discovered through 4 failed attempts, 1 success*
*Key insight: Data structure > algorithm for Burst effectiveness*
