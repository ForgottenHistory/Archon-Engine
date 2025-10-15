# Lessons from Paradox Development Diaries

**📊 Status:** 📚 Knowledge Synthesis | Industry Validation

## Executive Summary

Analysis of official Paradox development diaries (CK3, Victoria 3, HOI4) reveals production-scale lessons about grand strategy performance. This document separates **ENGINE concerns** (reusable architecture patterns) from **GAME concerns** (content design patterns) to maintain clear layer boundaries.

**Sources:**
- CK3 Performance Dev Diary (threading model, rendering separation)
- Victoria 3 Performance Dev Diary (tick tasks, load balancing, UI blocking)
- HOI4 Performance Dev Diary (algorithmic assumptions, hidden costs)

**Key Insight:** All three engines face the same core problems: dependency chains limit parallelism, data volume assumptions break at scale, and incremental feature additions cause performance death-by-a-thousand-cuts.

---

## Part 1: ENGINE Architecture Lessons

### 1.1 Threading Models

**CK2 Pattern: Object-Level Parallelism**
- Parallelize across characters (Thread 1: characters 0-999, Thread 2: characters 1000-1999)
- Complex rules: "Can't check other character data", "Can't modify visible fields"
- Result: High bug rate (OOS errors), difficult to maintain

**CK3 Pattern: System-Level Parallelism** ✅
- Parallelize across systems (Thread 1: Scheme system, Thread 2: Opinion system)
- Simple rule: "Don't modify visible state during parallel, store changes instead"
- Result: Lower bug rate, easier to identify parallelization opportunities

**Validation:** CK3 achieves ~50% parallelism with 1.75x speedup on 8 cores (measured).

**Our Architecture:** Phase-based execution (similar to CK3 system-level)
```
Phase 1: Calculate (parallel, ReadOnly)
Phase 2: Aggregate (parallel reduction)
Phase 3: Evaluate (parallel, ReadOnly)
Phase 4: Apply changes (sequential)
```

**Lesson:** System/phase-level parallelism > Object-level parallelism.

**Reference:** [engine-game-separation.md](../../Engine/engine-game-separation.md), [master-architecture-document.md](../../Engine/master-architecture-document.md) §3.3

---

### 1.2 Rendering Thread Separation

**CK2 Pattern:** Rendering blocks main thread (serial execution: update gamestate → render → update → render)

**CK3 Pattern:** Separate render thread with locks
- Render thread runs parallel to gamestate updates
- Lock system: render thread waits when gamestate modifies data
- Key insight: "During parallel updates [to gamestate] aren't allowed to change visible state, so... it is safe to update the render thread"
- Result: Better thread utilization, more stable framerate

**Victoria 3 Reality:** Interface reads gamestate (pink "waiting" bars throughout profiler)
- Gamestate must periodically release lock for UI/graphics to read data
- "Can't happen anywhere though, it's limited to in between tick tasks"
- Trade-off: Release locks often (smooth UI) vs rarely (faster simulation)

**Our Architecture:** Complete GPU separation (better than CK3's approach)
- CPU simulation and GPU rendering use separate memory
- No locks needed (async texture upload ~0.1ms)
- GPU reads from textures (uploaded once per frame)
- Zero blocking overhead

**Advantage:** CK3 saves lock overhead but pays 0.1ms texture upload. We gain complete parallelism.

**Caveat:** UI data access still needs design (snapshots vs locks).

**Reference:** [map-system-architecture.md](../../Engine/map-system-architecture.md) §2

---

### 1.3 Memory Allocation and Parallelism

**HOI4 Discovery:** "Every time we do something that requires dynamic memory allocation, the operative system may grab a global lock"
- Result: Parallel code becomes partially sequential due to malloc lock contention
- Solution: "Eliminating the worst of these dynamic memory allocations"

**Victoria 3 Discovery:** Memory allocator overhead significant enough to switch to mimalloc
- Result: ~4% performance improvement from better allocator alone

**Our Advantage:** NativeArray eliminates this problem entirely
- Pre-allocated at startup (Allocator.Persistent)
- Zero runtime allocations during gameplay
- Burst compiler prevents managed allocations at compile time

**Risk:** If we add NativeList/NativeHashMap during hot paths, must pre-allocate and reuse.

**Reference:** [unity-burst-jobs-architecture.md](unity-burst-jobs-architecture.md) §Native Collections

---

### 1.4 Load Balancing Heterogeneous Workloads

**Victoria 3 Pattern:** "Expensive vs Affordable" job scheduling
- Problem: Large countries (China, Russia, USA) take 100x longer than small countries (Luxembourg)
- Naive distribution: All large countries on same thread → one thread works, others idle
- Solution: Estimate cost heuristic, sort by cost, schedule expensive items separately

**From profiler:** "Expensive jobs" vs "Affordable jobs" clearly separated.

**Our Architecture:** Must implement this for player vs AI provinces
- Player capital: 50 buildings, complex economy → 10ms per province
- AI backwater: 1 building, simple → 0.1ms per province
- Naive Unity scheduler: Assumes uniform cost (will fail)

**Implementation Strategy:**
```
Option 1: Sort provinces by estimated cost (building count, pop count)
Option 2: Split into expensive job (player provinces) and affordable job (AI provinces)
Option 3: Custom work-stealing scheduler
```

**When:** Before adding complex economy/AI systems (don't wait for bottleneck).

**Reference:** [performance-architecture-guide.md](../../Engine/performance-architecture-guide.md) §4.3

---

### 1.5 Dependency Chain Management

**Victoria 3 Pattern:** Tick task dependency graph
- Each task declares dependencies (TaskA → TaskB → TaskC)
- Engine parallelizes independent branches
- Reality: Long dependency chains limit parallelism

**CK3 Pattern:** Store changes, apply later
- Parallel updates can't modify visible state
- Changes stored in buffers, applied sequentially after parallel section
- This creates clean dependency boundaries

**Amdahl's Law Validation:**
- CK3: ~50% parallel → 1.75x speedup on 8 cores (measured)
- Formula: `Speedup = 1 / (0.50 + 0.50/8) = 1.78x` (matches!)

**Our Expectation Revision:**
- Original: 95% parallel, 6x speedup
- Realistic: 60-70% parallel, 3-4x speedup
- Success: Match or beat CK3's 50% parallel

**Why lower than theory:** Real systems have dependencies that can't be eliminated (aggregation, sorting, deterministic application).

**Reference:** [data-flow-architecture.md](../../Engine/data-flow-architecture.md) §3

---

### 1.6 Sparse vs Dense Data Structures

**HOI4 Lesson:** Equipment variant explosion
- Original: 30 variants total → iterate all variants = 30 checks
- Reality: 500+ variants → iterate all variants = 500 checks (16x slower)
- Many systems iterate equipment variants → multiplicative cost

**Solution:** Sparse data structures (only store what exists, not all possibilities)

**Engine Design Principle:**
- Dense: Fixed-size array with all types (good if most types active)
- Sparse: Variable-size list of active items (good if most types inactive)

**Example:**
```
Dense: ProvinceBuildings = bool[100 types] → must check all 100
Sparse: ProvinceBuildings = List<BuildingID> → only check 5 active buildings
```

**When to use sparse:** Optional/rare data that scales with mods (building types, trade goods, modifiers).

**When to use dense:** Core data that's always present (ProvinceState, CountryState).

**Reference:** [core-data-access-guide.md](../../Engine/core-data-access-guide.md) §2.2

---

## Part 2: GAME Design Lessons

### 2.1 Algorithmic Assumptions Break at Scale

**HOI4 Case Study:** Focus bypass check
- Original assumption: 60 focuses per tree
- Algorithm: Check every focus in game every time
- Cost: 600 focuses × 0.1ms = 0.06ms (negligible)
- Reality: 15,000+ focuses total (Germany: 400, Belgium: 180, etc.)
- Cost: 15,000 × 0.1ms = 1.5ms (25x slower, now significant)
- Solution: "Only check the focus that has a remote chance of being bypassed"

**Lesson:** O(n) algorithms designed for small n break when n grows 25x.

**Pattern:** Changed from "check all" to "check relevant subset" (O(n) → O(k) where k << n).

**Our Risks:**
- Event trigger checks (100 events → 5,000 with mods)
- Modifier applications (30 modifiers → 300 with mods)
- Building type iterations (20 types → 100 with mods)

**Mitigation:** Pre-filter before iteration. Never iterate entire gamestate.

---

### 2.2 Data Volume Scaling (The Modding Problem)

**HOI4 Lesson:** Equipment variants
- Base game: Manageable number of variants
- With MIOs + International Market + Lend Lease: 500+ variants
- Every system that touches equipment: Now 16x slower
- "Every interaction wasn't adding much but all put together, that's quite a bit"

**Pattern:** Systems designed for 30 items break with 500 items.

**Design Rule:** If base game has N items, system must handle 10N (for mods/DLC).

**Examples:**
- Plan 20 trade goods → design for 200
- Plan 30 modifiers → design for 300
- Plan 50 events → design for 500

**Testing Strategy:** Inflate data volume 10x during stress tests.

---

### 2.3 Object Count vs Object Complexity

**Victoria 3 Lesson:** Pop object explosion
- Same total population (10 million)
- Scenario A: 1,000 large pops → 1,000 iterations
- Scenario B: 100,000 tiny pops → 100,000 iterations (100x slower!)
- Solution: "Design has tweaked how aggressively the game merges small pops"

**Lesson:** Object count matters more than data volume. Iteration overhead dominates.

**Our Equivalent:**
- 10,000 provinces with moderate complexity → good
- 50,000 micro-provinces with simple data → bad (iteration overhead)
- 1,000 mega-provinces with extreme complexity → bad (per-iteration cost)

**Sweet Spot:** 10k provinces, balanced complexity.

---

### 2.4 LOD-Style Optimizations

**CK3 Pattern:** Player vs AI update frequency
- Player council tasks: Updated daily (player notices)
- AI council tasks: Updated monthly (player doesn't notice)
- Cost reduction: 1/30th for AI countries

**Result:** 100 countries (1 player, 99 AI) → 23x speedup with minimal gameplay impact.

**Pattern:** Conditional execution based on visibility/relevance.

**Our Opportunity:**
- Player provinces: Full detail updates daily
- AI provinces: Simplified updates monthly
- 10k provinces (100 player, 9,900 AI) → 23x speedup potential

**Design Consideration:** Where does "player won't notice" end and "bad AI" begin?

---

### 2.5 Hidden Cost Accumulation

**HOI4 Insight:** "The performance goal of this DLC was to not decay performance"
- Pattern: Big optimization (50ms → 10ms) masks new system costs (5ms)
- Year 1: Optimize old systems (+40ms improvement)
- Year 2-5: Add new systems (-5ms each, -20ms total)
- Net: Still +20ms better than launch, looks good!
- Problem: New systems individually inefficient (discovered years later)

**Victoria 3 Example:** International Market automation costs 2ms (0.5% of frame)
- "It's not a lot" per system
- But 10 systems × 2ms each = 20ms = 50% of frame!

**The Boiling Frog:** Each feature looks reasonable, collectively they kill performance.

**Our Mitigation Strategy:** Per-system performance budgets (see Part 3).

---

### 2.6 UI Performance Separate from Simulation

**Victoria 3 Case Study:** Construction queue
- Old UI: Compute layout for ALL 1,000 items (including 990 off-screen)
- Result: UI update takes 100x longer than needed
- Solution: Virtualized list (only layout 10 visible items)

**Before/After:** Dev diary shows dramatic GIF improvement.

**Lesson:** UI performance is distinct bottleneck from simulation performance.

**Our Risk:** Province lists, event lists, country lists all need virtualization at 10k scale.

**Mitigation:** Test UI with full-scale data (10k items), not dev data (100 items).

---

## Part 3: Cross-Cutting Concerns

### 3.1 Performance Budget Discipline

**From HOI4:** "Net-zero performance cost" goal
- For every 1ms new feature costs, optimize 1ms elsewhere
- Prevents incremental degradation over DLCs

**Our Framework:**

```
Total budget: 5ms per frame (200 FPS target)
Current baseline: 0.24ms (stress test)
Available: 4.76ms

Per-System Budgets:
- Province core: 1.0ms
- Economy: 0.3ms
- Diplomacy: 0.2ms
- AI: 0.5ms
- Combat: 0.4ms
- Events: 0.2ms
- Modifiers: 0.2ms
- Other: 0.2ms
- Buffer: 2.0ms (reserve)

Rule: Each system MUST hit budget before moving to next system.
Don't allow "borrowing" from buffer ("we have 2ms remaining so 0.5ms is fine").
```

**Tracking:** Document per-system costs in `performance-budget-tracking.md` (to be created).

**Reference:** [performance-architecture-guide.md](../../Engine/performance-architecture-guide.md) §4.2

---

### 3.2 Parallelism Expectations

**Industry Reality from Dev Diaries:**
- CK3: ~50% parallel, 1.75x speedup on 8 cores (measured)
- Victoria 3: Implies similar (based on tick task graphs)
- HOI4: Focus on sequential optimization (not just parallelism)

**Revised Expectations:**

```
Original (theory): 95% parallel, 6x speedup
After CK3: 70% parallel, 4x speedup
After Victoria 3: 60-70% parallel, 3-4x speedup
Realistic (accounting for complexity): 60% parallel, 3x speedup

Success Criterion: Match or exceed CK3's 50% parallel baseline
Stretch Goal: 70% parallel (leveraging simpler systems than CK3)
```

**Why lower than theory:**
- Aggregation steps require sequential execution
- Deterministic application must be ordered
- System dependencies create sync points
- Visibility constraints limit parallel modifications

**Reference:** This analysis document (conditional reasoning sections).

---

### 3.3 Testing Strategy

**Lessons from All Three:**
1. **Test at target scale early** (don't wait for 10k provinces if that's the goal)
2. **Test with inflated data** (10x trade goods, 10x events for mod scenarios)
3. **Test each system individually** (catch budget violations per-system)
4. **Test combined load** (interaction overhead between systems)
5. **Test long-term** (100+ year simulations to catch memory leaks, degradation)

**Validation Sequence:**

```
Phase 1: Foundation (Done)
✅ Stress test core at 3,923 provinces
✅ Validate zero memory leaks
✅ Confirm fixed-point performance

Phase 2: Per-System (Next)
🚧 Implement economy system
🚧 Stress test at 10k provinces
🚧 Validate budget (0.3ms target)
🚧 Test with 10x data volume

Phase 3: Integration
❌ Economy + Diplomacy together
❌ Measure interaction overhead
❌ Validate combined budget

Phase 4: Long-Term
❌ 400-year simulation
❌ Check for degradation
❌ Validate stability
```

**Reference:** [performance-architecture-guide.md](../../Engine/performance-architecture-guide.md) §7 "Validation Tests"

---

### 3.4 Modifier System Design (If Implemented)

**Victoria 3 Lesson:** Modifier dependency graphs with batched parallelization
- Complexity: "About an order of magnitude more complex than CK3"
- Solution: Dependency batching (Batch 1 → Batch 2 → Batch 3)
- Parallelization: Within each batch (across entities)
- "One of the main contributors" to performance improvements

**If We Implement Modifiers:**
- Use Victoria 3's batched approach
- Calculate dependencies once (don't recalculate every frame)
- Parallelize within batches
- Use dirty flags (only recalculate what changed)

**Reference:** [time-system-architecture.md](../../Engine/time-system-architecture.md) §4 "Dirty Flag System"

---

### 3.5 Pre-Filtering Pattern

**From All Three Dev Diaries:** The most common optimization is "don't check everything, check relevant subset"

**Examples:**
- HOI4: Focus bypass checks relevant focuses (not all 15k)
- Victoria 3: Tick tasks operate on relevant entities (not all entities)
- CK3: System-level parallelism naturally filters by system

**Engine Pattern:**

```
❌ Anti-Pattern: Iterate All
foreach (var item in allItems) {
    if (IsRelevant(item)) {
        Process(item);
    }
}

✅ Pattern: Pre-Filter Then Iterate
var relevantItems = GetRelevantSubset(context);
foreach (var item in relevantItems) {
    Process(item);
}
```

**Game Design Pattern:**
- Use indices/registries to pre-filter
- Tag entities with relevance flags
- Maintain "active" lists separate from "all" lists

---

## Part 4: Concrete Action Items

### For Engine Layer

1. **Implement Load Balancing** 🎯 Priority: High
   - Cost-based job scheduling (expensive vs affordable)
   - Before: Adding complex economy/AI systems
   - Pattern: Victoria 3's heuristic cost estimation

2. **Design UI Data Access** 🎯 Priority: High
   - Decision: Locks vs snapshots for gamestate reads
   - Before: Implementing UI panels that read province/country data
   - Reference: Victoria 3's lock patterns vs our GPU separation

3. **Pre-Allocation Strategy** 🎯 Priority: Medium
   - Pre-allocate all temporary buffers
   - Reuse NativeArrays across frames
   - Prevent any runtime allocations in hot paths

4. **Dependency Management System** 🎯 Priority: Medium
   - Consider Victoria 3's tick task graph for complex systems
   - Use JobHandle dependencies for clear phase boundaries
   - Track which phases can run in parallel

### For Game Layer

5. **Performance Budget Tracking** 🎯 Priority: High
   - Create `performance-budget-tracking.md` document
   - Track each system's cost as implemented
   - Enforce budget before moving to next system

6. **Pre-Filtering Infrastructure** 🎯 Priority: High
   - Design registry system for filtered lookups
   - Avoid "check all" patterns from day 1
   - Every iteration must be O(k) relevant items, not O(n) all items

7. **LOD System for Updates** 🎯 Priority: Medium
   - Player provinces: Daily updates
   - AI provinces: Monthly updates
   - Design this distinction into economy system

8. **Data Volume Testing** 🎯 Priority: Medium
   - Create stress test scenarios with 10x data
   - 200 trade goods, 500 events, 300 modifiers
   - Test before shipping features

### For Both Layers

9. **Sparse Data Structures** 🎯 Priority: High
   - Use NativeList for optional/rare data
   - Use fixed arrays for common/required data
   - Design with mods in mind (data volume scales 10x)

10. **Virtualized UI Components** 🎯 Priority: Medium
    - Any list over 100 items needs virtualization
    - Test with 10k province lists
    - Don't layout off-screen elements

---

## Part 5: Success Metrics

### Engine Performance Targets

```
Frame Time Budget: 5ms (200 FPS)
Parallelism: 60-70% (3-4x speedup on 8 cores)
Memory: <100MB for simulation
Allocations: Zero during gameplay
UI Blocking: Zero (GPU separation)
```

### Game Performance Targets

```
Per-System Budgets (enforced):
- Province core: 1.0ms
- Economy: 0.3ms
- Diplomacy: 0.2ms
- AI: 0.5ms
- Combat: 0.4ms
- Events: 0.2ms
- Modifiers: 0.2ms

Combined: 3.0ms
Buffer: 2.0ms
Total: 5.0ms
```

### Validation Criteria

```
✅ Each system hits budget individually
✅ Combined systems stay within total budget
✅ 10x data volume test passes (mod scenario)
✅ 400-year simulation stable
✅ Zero memory leaks
✅ Performance doesn't degrade over time
```

---

## Part 6: What We Do Differently

### Advantages Over Paradox Engines

**1. Zero Runtime Allocations**
- Paradox: Needed mimalloc (4% gain), still has allocation overhead
- Us: NativeArray = pre-allocated = zero overhead

**2. Complete GPU Separation**
- Paradox: Render thread shares memory with simulation (needs locks)
- Us: GPU has separate memory (zero blocking)

**3. Burst Compilation**
- Paradox: C++ JIT or interpreted scripts
- Us: Burst generates SIMD-optimized native code

**4. Compile-Time Safety**
- Paradox: Runtime OOS bugs from threading violations
- Us: Burst won't compile code that violates safety

**5. Starting Clean**
- Paradox: 15+ years of technical debt, legacy assumptions
- Us: Greenfield project with modern patterns from day 1

### Challenges Paradox Doesn't Face

**1. Unproven at Scale**
- Paradox: Millions of players, years of production data
- Us: Stress tests only, no production validation

**2. DOD Learning Curve**
- Paradox: Team trained in existing codebase
- Us: Must learn DOD patterns, Burst restrictions, etc.

**3. No Safety Net**
- Paradox: Can fall back to OOP if DOD fails
- Us: If DOD doesn't scale, restart from scratch

---

## Conclusion

**Key Takeaways:**

1. **Threading:** System-level parallelism (CK3 pattern) > Object-level parallelism. Expect 60-70% parallel (not 95%).

2. **Memory:** Pre-allocation eliminates lock contention (our NativeArray advantage). Don't waste it with runtime allocations.

3. **Load Balancing:** Heterogeneous workloads need cost-based scheduling (Victoria 3's "Expensive vs Affordable" pattern).

4. **Assumptions:** Algorithms designed for small data break at scale (HOI4's focus tree). Design for 10x from day 1.

5. **Accumulation:** Hidden costs accumulate (HOI4's "net-zero" goal). Per-system budgets prevent death-by-a-thousand-cuts.

6. **Data Volume:** Systems must handle mod-scale data (10x base game). Test with inflated data early.

7. **Pre-Filtering:** Most optimization is "check relevant subset, not everything" (pattern across all three engines).

**The Big Picture:**

Paradox engines face the same core challenges we will. Their solutions (system-level parallelism, load balancing, pre-filtering, budget discipline) are battle-tested patterns we should adopt proactively.

Our advantages (zero allocations, GPU separation, Burst compilation) give us a higher performance ceiling. But we'll still hit the same walls (dependencies, data volume, hidden costs) if we're not disciplined.

**The lessons from 15+ years of Paradox grand strategy development validate our architecture while tempering our expectations. We're on the right path, but success requires constant vigilance.**

---

## Related Documentation

**Engine Architecture:**
- [master-architecture-document.md](../../Engine/master-architecture-document.md) - Dual-layer architecture
- [performance-architecture-guide.md](../../Engine/performance-architecture-guide.md) - Optimization strategies
- [engine-game-separation.md](../../Engine/engine-game-separation.md) - Layer boundaries
- [data-flow-architecture.md](../../Engine/data-flow-architecture.md) - System dependencies

**Learning Documents:**
- [data-oriented-vs-traditional-grand-strategy-performance.md](data-oriented-vs-traditional-grand-strategy-performance.md) - DOD analysis
- [unity-burst-jobs-architecture.md](unity-burst-jobs-architecture.md) - Burst+Jobs patterns

**Planning:**
- [../Planning/performance-budget-tracking.md](../Planning/performance-budget-tracking.md) - (To be created)

---

*Created: 2025-10-14*
*Sources: CK3 Performance Dev Diary, Victoria 3 Performance Dev Diary, HOI4 Performance Dev Diary*
*Context: Industry validation of architectural choices + realistic expectation setting*
