# Pathfinding Scaling for Million-Unit Support

**Status:** Option A Complete (Foundation)
**Last Updated:** 2026-01-08

---

## Current Implementation (Option A)

Single Burst-compiled A* pathfinding job with:
- Binary min-heap priority queue (O(log n) operations)
- Native collections (zero GC allocations)
- Managed fallback when MovementValidator is needed

**Performance:** ~0.1ms per path on 13k province map

---

## Future Options

### Option B: Batched Parallel Pathfinding

**Problem:** Million units = potentially thousands of pathfinding requests per frame

**Solution:** Batch multiple pathfinding requests into parallel jobs

**Architecture:**
- `IJobParallelFor` with one path per job index
- Each job gets its own set of working collections
- Pool of pre-allocated collection sets (e.g., 64 parallel paths)
- Request queue with priority (player units first)

**Capacity Planning:**
- 64 parallel paths × 0.1ms = 6.4ms for 64 paths
- Frame budget: 16ms (60 FPS) → ~100 paths/frame sustainable
- With unit grouping (armies share paths): millions of units feasible

**Implementation Notes:**
- Create `PathfindingRequest` struct with start/goal/priority
- Create `PathfindingResult` struct with path data
- Pool working collections to avoid per-request allocation
- Consider job batching: schedule 64 paths, complete, schedule next 64

### Option C: Hierarchical A* (HPA*)

**Problem:** 13k+ provinces = expensive BFS even with Burst

**Solution:** Multi-level pathfinding hierarchy

**Architecture:**
1. **Abstract graph:** Cluster provinces into regions (~100-200 regions)
2. **Inter-region paths:** Pre-computed paths between adjacent regions
3. **Intra-region paths:** On-demand A* within small regions

**Benefits:**
- High-level path: O(regions) not O(provinces)
- Low-level refinement only for current region
- Cache inter-region paths (rarely change)

**When to use:**
- Province count > 20k
- Average path length > 50 provinces
- Many simultaneous pathfinding requests

**Invalidation:**
- Terrain changes: Rebuild affected region's internal graph
- Border changes: Rebuild region connections only

### Option D: Flow Fields (Alternative)

**Problem:** Many units moving to same destination

**Solution:** Pre-compute direction vectors for all provinces toward goal

**Best for:**
- Rally points (many units, one destination)
- Zone control (armies covering territory)
- NOT for individual unit pathing

---

## MovementValidator in Burst

Current limitation: Delegates not Burst-compatible

**Future solutions:**

1. **Pre-computed blocked set:** Before pathfinding, compute `NativeHashSet<ushort>` of blocked provinces based on diplomatic state

2. **Passability bitmask:** Per-province byte with access flags
   - Bit 0: Land passable
   - Bit 1: Naval passable
   - Bit 2: Own territory
   - Bit 3: Allied territory
   - etc.

3. **Access lookup table:** `NativeHashMap<(ushort country, ushort province), bool>` for military access rights

---

## Priority Order

1. ✅ Option A: Single Burst path (DONE)
2. Option B: Parallel batching (when unit count > 1000)
3. MovementValidator Burst support (when validation needed in hot path)
4. Option C: HPA* (if province count doubles or path lengths excessive)

---

## Related Files

- `Core/Systems/PathfindingSystem.cs` - Main implementation
- `Core/Collections/NativeMinHeap.cs` - Priority queue
- `Core/Systems/AdjacencySystem.cs` - Province graph

---

*Created: 2026-01-08*
