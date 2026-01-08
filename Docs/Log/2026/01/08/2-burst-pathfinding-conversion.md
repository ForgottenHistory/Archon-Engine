# Burst Conversion: Distance Calculator & Pathfinding
**Date**: 2026-01-08
**Session**: 2
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:** Convert AIDistanceCalculator and PathfindingSystem to Burst for HOI4-scale performance.

**Success Criteria:**
- 53ms AIDistanceCalculator spike eliminated
- PathfindingSystem Burst-compiled
- Foundation for million-unit pathfinding

---

## Context & Background

**Previous Work:**
- See: [1-ai-performance-optimization.md](1-ai-performance-optimization.md) - Fixed 10x speed death spiral

**Current State:**
- 10x speed stable at 60+ FPS after session 1
- Profiler showed 53ms spike from `AIDistanceCalculator.CalculateDistances()`

**Why Now:**
- User goal: "Modern Paradox scale with tactical granularity like HOI4, with a million units"
- Burst provides 5-10x performance improvement

---

## What We Did

### 1. Native Adjacency Data for Burst
**Files Changed:** `Core/Systems/AdjacencySystem.cs:10-36, 64-66, 78-81, 96-99, 110-111, 139-143`

Added `NativeAdjacencyData` struct with:
- `NativeParallelMultiHashMap<ushort, ushort>` for province neighbors
- `GetNeighbors()` returns Burst-compatible enumerator
- `IsAdjacent()` for direct queries

**Pattern:** Strategy 2 from `flat-storage-burst-architecture.md` - NativeParallelMultiHashMap for one-to-many.

### 2. Native Province Data for Burst
**Files Changed:** `Core/Systems/ProvinceSystem.cs:11-44, 294-311`

Added `NativeProvinceData` struct with:
- Read-only views of province states, id mapping, active IDs
- `GetProvinceOwner()` for Burst-compatible owner lookup
- `GetNativeData()` method on ProvinceSystem

### 3. Burst BFS Distance Calculator
**Files Changed:** `Core/AI/AIDistanceCalculator.cs` (complete rewrite)

Created `BurstBFSDistanceJob : IJob` with:
- BFS traversal using `NativeParallelMultiHashMap.Enumerator`
- Province distance and country distance arrays
- Pre-allocated queue using NativeList

**Performance:** 53ms spike eliminated.

### 4. NativeMinHeap for Priority Queue
**Files Created:** `Core/Collections/NativeMinHeap.cs`

Burst-compatible binary min-heap:
- O(log n) Push/Pop operations
- Generic with IComparable constraint
- `PathfindingNode` struct for A* fScore ordering

### 5. Burst A* Pathfinding
**Files Changed:** `Core/Systems/PathfindingSystem.cs` (complete rewrite)

Created `BurstPathfindingJob : IJob` with:
- Binary min-heap priority queue
- NativeHashSet for closed set
- NativeHashMap for gScore and parent tracking
- Managed fallback when MovementValidator needed

### 6. Future Improvements Documentation
**Files Created:** `Docs/Planning/pathfinding-scaling.md`

Documented Options B (parallel batching), C (HPA*), D (flow fields).

---

## Decisions Made

### Decision 1: Burst Path vs Managed Fallback
**Context:** MovementValidator delegate is not Burst-compatible
**Options:**
1. Pre-compute blocked provinces before job
2. Always use managed path with validator
3. Burst when no validator, managed fallback otherwise

**Decision:** Option 3
**Rationale:** Most AI pathfinding needs no validation. Burst path is default fast path. Game-specific validation uses managed fallback.
**Trade-offs:** Two code paths to maintain.

### Decision 2: Custom NativeMinHeap vs Linear Search
**Context:** Unity lacks built-in Burst-compatible priority queue
**Options:**
1. Linear search (O(n) per extraction)
2. Custom binary heap (O(log n) per extraction)
3. Third-party library

**Decision:** Option 2
**Rationale:** O(log n) essential for large maps. Self-contained, no dependencies.

---

## Architecture Impact

### New Reusable Components
- `NativeAdjacencyData` - Burst-compatible neighbor queries
- `NativeProvinceData` - Burst-compatible province data
- `NativeMinHeap<T>` - Generic priority queue for any Burst job

### Pattern Established
**Native Data Struct Pattern:**
1. Create read-only struct with `[ReadOnly]` native containers
2. Add query methods that work in Burst
3. Expose via `GetNativeData()` method on system
4. Use in `IJob` with `[BurstCompile]`

---

## Code Quality Notes

### Performance
- AIDistanceCalculator: 53ms → ~1ms (Burst compiled)
- PathfindingSystem: O(n) extraction → O(log n) with heap
- Zero GC allocations during gameplay

### Technical Debt
- MovementValidator still requires managed fallback
- Future: Pre-computed blocked province sets for full Burst validation

---

## Next Session

### When Unit Count > 1000
- Implement Option B: Parallel batched pathfinding
- Pool of working collections for concurrent paths

### When Province Count > 20k
- Consider Option C: Hierarchical A* (HPA*)

---

## Quick Reference for Future Claude

**Key Files:**
- `Core/Systems/AdjacencySystem.cs:10-36` - NativeAdjacencyData struct
- `Core/Systems/ProvinceSystem.cs:11-44` - NativeProvinceData struct
- `Core/AI/AIDistanceCalculator.cs:175-261` - BurstBFSDistanceJob
- `Core/Collections/NativeMinHeap.cs` - Priority queue
- `Core/Systems/PathfindingSystem.cs:250-356` - BurstPathfindingJob

**Pattern:**
```
System owns data → GetNativeData() returns read-only struct → IJob uses struct
```

**Gotchas:**
- NativeParallelMultiHashMap enumerator must be iterated with `while (MoveNext())`
- NativeMinHeap requires `IComparable<T>` constraint
- FixedPoint64 is blittable (just a long wrapper)

---

## Links & References

### Related Documentation
- [flat-storage-burst-architecture.md](../../Engine/flat-storage-burst-architecture.md)
- [pathfinding-scaling.md](../../Planning/pathfinding-scaling.md) - Future options

### Related Sessions
- [1-ai-performance-optimization.md](1-ai-performance-optimization.md) - Previous session today

### Code References
- Native data pattern: `AdjacencySystem.cs:10-36`, `ProvinceSystem.cs:11-44`
- Burst BFS: `AIDistanceCalculator.cs:175-261`
- Burst A*: `PathfindingSystem.cs:250-356`
- Min-heap: `NativeMinHeap.cs:1-150`
