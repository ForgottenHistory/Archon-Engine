# ENGINE Infrastructure Priorities - Paradox Dev Diary Analysis
**Date**: 2025-10-15
**Status**: 🔄 Planning
**Priority**: High

---

## Context & Background

**Previous Work:**
- See: [paradox-dev-diary-lessons.md](../../learnings/paradox-dev-diary-lessons.md)
- Analyzed: CK3, Victoria 3, HOI4 performance dev diaries

**Current State:**
- Engine core is functional (ProvinceSystem, command pattern, dual-layer architecture)
- Game layer is minimal (testing infrastructure only)
- Critical infrastructure gaps identified from Paradox's 15 years of production experience

**Why Now:**
Paradox learned these patterns the hard way. Implementing them NOW (before game complexity) is 10x cheaper than retrofitting later.

---

## ENGINE Critical Priorities

### 1. Load Balancing (HIGHEST PRIORITY) 🔥

**Problem:** Unity's job scheduler assumes uniform cost. When provinces have heterogeneous complexity:
- Player capital: 50 buildings, complex economy → 10ms
- AI backwater: 1 building → 0.1ms
- Naive parallel-for: All expensive provinces on thread 0 → threads 1-7 idle

**Victoria 3's Solution:** Cost estimation + separate "expensive" and "affordable" job batches

**Implementation Options:**
1. Sort provinces by cost heuristic (building count) before dispatching job
2. Split into two jobs - expensive (player) and affordable (AI)
3. Custom work-stealing scheduler (overkill for now)

**Decision:** Option 2 (split jobs) - simple, explicit, battle-tested by Paradox

**When:** Before economy system adds heterogeneous province complexity

**Files to Create:**
- `Core/Systems/JobScheduling/LoadBalancedJobScheduler.cs`
- Add cost estimation to ProvinceSystem

---

### 2. UI Data Access Pattern (HIGH PRIORITY) 🔥

**Problem:** UI needs to READ simulation data without blocking. Three options:

**Option A: Locks (Victoria 3's approach)**
- Pro: Simple, no memory duplication
- Con: "Waiting" bars in profiler (Victoria 3 has this), blocking overhead

**Option B: Snapshots (Recommended)**
- Pro: Zero blocking, UI and simulation fully parallel
- Con: 2x memory for hot data (160KB vs 80KB - acceptable within 100MB budget)

**Option C: Frame-Delay Reads**
- Pro: Zero blocking, minimal memory
- Con: UI shows stale data (feels laggy)

**Decision:** Option B (snapshots) - memory is cheap (160KB negligible), blocking is expensive

**Implementation:**
```
After each tick:
1. Copy NativeArray<ProvinceState> to snapshot buffer
2. UI reads from snapshot (never touches live data)
3. Zero locks, zero blocking
```

**When:** Before implementing province/country info panels that read live data

**Files to Create:**
- `Core/GameStateSnapshot.cs`
- `UI/GameStateReader.cs` (wrapper for UI to access snapshots)

---

### 3. Pre-Allocation Policy (MEDIUM PRIORITY)

**Problem:** HOI4 discovered parallel code became sequential due to malloc lock contention

**Our Advantage:** NativeArray = pre-allocated
**Our Risk:** Adding NativeList/NativeHashMap in hot paths without discipline

**Policy:**
```
RULE: All temporary buffers in hot paths MUST be:
1. Pre-allocated at system initialization (Allocator.Persistent)
2. Cleared and reused each frame (no Dispose/Allocate in loop)
3. Never use Allocator.Temp or Allocator.TempJob in gameplay code

✅ OK: var buffer = new NativeList<int>(1000, Allocator.Persistent); // Init once
✅ OK: buffer.Clear(); // Each frame
❌ NO: var buffer = new NativeList<int>(100, Allocator.Temp); // In hot path
```

**When:** Document NOW, enforce during code review

**Documentation Impact:**
- Add section to `performance-architecture-guide.md`
- Add to code review checklist

---

### 4. Sparse Data Structures (MEDIUM PRIORITY)

**Problem:** HOI4's 30 equipment variants → 500 with mods = every system 16x slower

**Pattern:**
- **Dense (fixed array):** Data that's ALWAYS present (ProvinceState, CountryState)
- **Sparse (variable list):** Data that's OPTIONAL/RARE (buildings, modifiers, trade goods)

**Example:**
```csharp
// ❌ Dense - wastes memory, must iterate 100 types
struct ProvinceBuildings {
    bool[100] hasBuilding;  // Most are false
}

// ✅ Sparse - only stores what exists
struct ProvinceBuildings {
    NativeList<ushort> buildingIDs;  // Only 5 entries for 5 buildings
}
```

**When:** Before implementing building/modifier systems

**Design Principle:** If base game has N items and mods can add 10N, use sparse structures

---

## Implementation Plan

### Immediate (This Week)
1. ✅ Document pre-allocation policy in performance-architecture-guide.md
2. 🔄 Decide UI data access pattern (snapshots)
3. 🔄 Design sparse data structure pattern

### Near-Term (Before Economy System)
4. ❌ Implement load balancing (cost-based job scheduling)
5. ❌ Implement GameStateSnapshot system for UI reads

### Can Wait (Until Needed)
- Dependency management system (only needed when systems get complex)
- Game layer performance budgets (need systems to track first)
- Pre-filtering infrastructure (need game queries to filter first)

---

## Success Criteria

**Load Balancing:**
- [ ] Heterogeneous workloads distribute evenly across threads
- [ ] No thread sits idle while others work
- [ ] Profiler shows balanced work distribution

**UI Data Access:**
- [ ] UI reads never block simulation
- [ ] Zero locks between UI and simulation
- [ ] Memory overhead < 1MB (snapshot buffer)

**Pre-Allocation:**
- [ ] Zero allocations during gameplay (confirmed by profiler)
- [ ] All hot path buffers pre-allocated
- [ ] Policy documented and enforced

**Sparse Data:**
- [ ] Building/modifier systems use sparse structures
- [ ] Iteration only over active items (not all possible items)
- [ ] Handles 10x data volume (mod scenario) efficiently

---

## Decisions Made

### Decision: Snapshots for UI Data Access
**Context:** UI needs to read simulation data without blocking
**Options Considered:**
1. Locks (Victoria 3) - blocking overhead
2. Snapshots - 2x memory
3. Frame-delay - stale data

**Decision:** Snapshots
**Rationale:**
- Memory is cheap (160KB negligible within 100MB budget)
- Zero blocking is priceless (Victoria 3 shows "waiting" bars with locks)
- Complete parallelism between UI and simulation

**Trade-offs:** Extra 80KB memory per snapshot
**Documentation Impact:** Update data-flow-architecture.md with snapshot pattern

---

## Architecture Impact

### Documentation Updates Required
- [ ] Add pre-allocation policy section to performance-architecture-guide.md
- [ ] Update data-flow-architecture.md with snapshot pattern
- [ ] Document sparse data structure guidelines
- [ ] Add load balancing pattern to performance-architecture-guide.md

### Architectural Decisions
**Changed:** UI data access strategy
**From:** Undefined
**To:** Snapshot-based reads (zero locks)
**Scope:** All UI systems that read game state
**Reason:** Paradox's experience shows locks create blocking overhead

---

## Next Steps

### Priority Order
1. Document pre-allocation policy (1 hour)
2. Implement GameStateSnapshot system (4 hours)
3. Design sparse data structure API (2 hours)
4. Implement load balancing scheduler (8 hours)

### Blocked Items
None - all items can proceed independently

### Questions to Resolve
1. Should snapshots be double-buffered (UI reads buffer A while we write buffer B)?
2. What's the cost estimation heuristic for load balancing? (building count? pop count?)

---

## Links & References

### Related Documentation
- [paradox-dev-diary-lessons.md](../../learnings/paradox-dev-diary-lessons.md) - Source analysis
- [performance-architecture-guide.md](../../Engine/performance-architecture-guide.md) - Performance patterns
- [data-flow-architecture.md](../../Engine/data-flow-architecture.md) - System communication

### Key Insights from Paradox
- CK3: 50% parallelism, 1.75x speedup on 8 cores (realistic target)
- Victoria 3: Locks between UI and simulation = "waiting" bars in profiler
- HOI4: 30 items → 500 with mods = 16x slower (sparse structures mandatory)

---

## Notes & Observations

**The Big Picture:**
Paradox's 15 years of production experience validates our architecture while tempering expectations. We have technical advantages (GPU separation, zero allocations, Burst), but we'll hit the same walls (heterogeneous workloads, UI blocking, data volume scaling) if we don't implement infrastructure NOW.

**Key Lesson:**
"We'll add that later" is how HOI4 ended up checking 15,000 focus trees every tick. Infrastructure first, features second.

**Our Advantage:**
We read the lessons BEFORE implementing game systems. Paradox learned by shipping broken performance and fixing it over years. We can design it right from day 1.

---

*Created: 2025-10-15*
*Source: Analysis of Paradox dev diaries + current engine state*
*Context: Planning ENGINE infrastructure before game layer complexity*
