# Grand Strategy Game Performance Architecture Guide
## Avoiding Late-Game Performance Collapse

**📊 Implementation Status:** ⚠️ Partially Implemented (Hot/cold separation ✅, some patterns ✅, advanced features pending)

**🔄 Recent Update (2025-10-09):** ProvinceState refactored to 8-byte engine primitives. Game-specific fields moved to HegemonProvinceData (4 bytes). See [phase-3-complete-scenario-loader-bug-fixed.md](../Log/2025-10/2025-10-09/phase-3-complete-scenario-loader-bug-fixed.md).

> **📚 Architecture Context:** This document focuses on performance patterns. See [master-architecture-document.md](master-architecture-document.md) for the dual-layer architecture foundation.

## Executive Summary
Grand strategy games face unique performance challenges that compound over time. A game can maintain excellent performance early but degrade significantly in late game, even when paused. This document explains why this happens and how to architect systems to maintain performance throughout the entire game lifecycle.

## The Late-Game Performance Problem

### Why Performance Degrades Over Time

**Early Game:**
- Fewer active provinces
- Minimal history
- Simple diplomatic web
- Fast performance

**Late Game:**
- Many active provinces
- Extensive history per province
- Complex diplomatic webs
- Performance degradation (even when PAUSED)

### Common Causes of Slowdown

1. **Data accumulation without cleanup**
2. **O(n²) algorithms that scale poorly**
3. **Memory fragmentation from long-term allocations**
4. **Cache misses from scattered data access**
5. **UI systems that touch entire game state every frame**

## Core Architecture Principles

### Principle 1: Design for the End State
**Wrong approach**: "We'll optimize when it becomes a problem"
**Right approach**: "Architecture assumes worst-case from day one"

**Bad Pattern:**
- Nested loops over all provinces
- O(n²) complexity
- Works with small datasets, fails at scale

**Good Pattern:**
- Pre-computed adjacency lists
- Parallel processing where possible
- Linear complexity algorithms

### Principle 2: Separate Hot, Warm, and Cold Data
Data "temperature" is determined by **access frequency**, not importance.

**Hot Data**: Read/written every frame or every tick
- Example: Owner ID (read every frame for rendering)
- Storage: Tightly-packed structs in contiguous arrays

**Warm Data**: Accessed occasionally (events, tooltips, calculations)
- Example: Development, terrain, fortification (used in calculations)
- Storage: Can remain in main simulation struct if space permits

**Cold Data**: Rarely accessed (history, detailed statistics, flavor text)
- Example: Historical records, building details, modifier descriptions
- Storage: Separate dictionaries, loaded on-demand, can page to disk

> **See:** [master-architecture-document.md](master-architecture-document.md) and [core-data-access-guide.md](core-data-access-guide.md) for complete hot/cold data architecture.

**Key Benefits:**
- Engine state and game state are separate but parallel
- Engine operations access minimal data for rendering/networking
- Game operations access both layers as needed
- Cold data separation prevents cache pollution

### Principle 3: Fixed-Size Data Structures
Dynamic growth is the enemy of performance.

**Bad Pattern:**
- Unbounded lists that grow forever
- Memory accumulation over time
- Performance degrades with age

**Good Pattern:**
- Fixed-size ring buffers
- Automatic compression of old data
- Bounded memory regardless of game length

## System-Specific Optimizations

### Map Rendering System

**Traditional Approach Problem:**
- Update every province mesh every frame
- Multiple draw calls per province
- Massive performance overhead at scale

**GPU-Driven Solution:**
- All province data in textures
- Single draw call for entire map
- GPU shader handles all visual processing
- Dramatic performance improvement

### Province Selection System

**Raycast Problem:**
- Physics system checks thousands of colliders
- Significant overhead per click
- Scales poorly

**Texture-Based Solution:**
- Convert screen position to UV coordinates
- Single texture read for province ID
- Near-instant response time
- Scales perfectly

### UI and Tooltip System

**Recalculation Problem:**
- Expensive calculations every frame
- Touches many provinces for single tooltip
- Performance impact while hovering

**Frame-Coherent Caching Solution:**
- Cache calculation results per frame
- Clear cache when frame changes
- Reuse cached data within frame
- Minimal overhead for cached lookups

### History System

**Unbounded Growth Problem:**
- Events accumulate indefinitely
- Memory and iteration overhead
- Performance degrades over time

**Tiered Compression Solution:**
- Recent history: Full detail (ring buffer)
- Medium-term: Compressed representation
- Long-term: Statistical summary only
- Automatic aging and compression

### Game State Updates

**Update Everything Problem:**
- Process all provinces every tick
- Wasteful even when nothing changed
- Scales linearly with province count

**Dirty Flag Solution:**
- Track only changed provinces
- Update only dirty entries
- Clear flags after processing
- Scales with changes, not total count

See [time-system-architecture.md](time-system-architecture.md) for layered update frequencies that work with dirty flags.

## Memory Architecture

### Memory Layout Strategy

**Simulation State:**
- Compact province data in contiguous arrays
- Fixed-size structs for cache efficiency

**Cold Data:**
- Loaded on-demand
- Can page to disk
- Separate from hot path

**GPU Textures (VRAM):**
- Province ID map
- Owner/controller textures
- Color palettes
- Border cache

**History Storage:**
- Ring buffers with compression
- Bounded size regardless of game length

**Presentation Data:**
- Not synchronized with simulation
- Visual-only information

### Memory Layout Philosophy: Structure Data by Access Pattern

**The Key Question:** "How is this data typically accessed?"

#### Array of Structures (AoS) vs Structure of Arrays (SoA)

**Use AoS when:**
- Operations access multiple fields together
- Fields are tightly related
- Network synchronization is needed
- Cache line fits entire struct

**Use SoA when:**
- Frequently iterate single field across all elements
- Field access is isolated
- Profiling shows bottleneck

**For Grand Strategy:**
- Most operations need multiple fields together
- AoS is typically optimal
- Don't split prematurely - profile first

#### Cache Efficiency: The Real Enemy is Pointers

**Performance killer:** Pointers that scatter data across memory

**Bad Pattern:**
- References/pointers in hot structures
- Heap allocations mixed with stack data
- Cache misses from pointer chasing

**Good Pattern:**
- Value types only (primitives, no references)
- Contiguous memory layout
- Separate cold data storage

**Key Principle:** Keep simulation state as value types only to ensure cache-friendly performance.

## Profiling and Metrics

### Key Performance Indicators

Monitor critical systems:
- Frame time targets
- Selection response time
- Tooltip calculation time
- Draw call counts
- Memory usage

Profile each major system separately to identify bottlenecks.

### Performance Budget Allocation

Allocate frame time carefully across systems:
- Map rendering
- Game logic
- UI updates
- Province selection
- Tooltips
- Reserve for spikes

## Anti-Patterns to Avoid

| Anti-Pattern | Problem | Solution |
|--------------|---------|----------|
| **"It Works For Now"** | O(n) operations scale poorly | Design for scale from start |
| **"Invisible O(n²)"** | Hidden quadratic complexity | Pre-compute adjacency lists |
| **"Death by Thousand Cuts"** | Many small allocations per frame | Pre-allocate pools |
| **"Update Everything"** | Processing unchanged data every tick | Dirty flag systems (see [time-system-architecture.md](time-system-architecture.md)) |
| **"Premature SoA Optimization"** | Splitting data that's used together | Profile first, optimize only if needed |
| **"Float in Simulation"** | Non-deterministic across platforms | Use fixed-point math |
| **"Interface-Typed Collections"** | Storing structs boxes every item | Use generic wrapper pattern |
| **"Reflection in Hot Path"** | Reflection boxes all parameters | Use virtual methods |

### Case Study: EventBus Zero-Allocation Pattern

**Problem:** EventBus allocated heavily per frame due to boxing struct events.

**Failed Approach:** Reflection-based processing still caused boxing.

**Solution:** EventQueue<T> wrapper pattern:
- Internal interface for polymorphism
- Type-specific wrapper keeps Queue<T> concrete
- Virtual method calls don't box value types
- Direct delegate invocation

**Key Insight:** Virtual method calls don't box value types. Use interface with virtual methods, not interface-typed collections.

See [data-flow-architecture.md](data-flow-architecture.md) for complete EventBus implementation.

## Implementation Checklist

### Phase 1: Foundation (Prevent Problems)
- [x] Design data structures for 10,000+ provinces
- [x] Separate hot/cold data (ProvinceState vs ProvinceColdData)
- [x] Implement GPU-based rendering
- [x] Use fixed-size allocations
- [ ] Profile from day one

### Phase 2: Optimization (Maximize Performance)
- [ ] Implement dirty flag systems
- [ ] Add frame-coherent caching
- [ ] Use compute shaders for parallel work
- [ ] Optimize memory layout
- [ ] Add LOD systems

### Phase 3: Scaling (Handle Growth)
- [ ] Implement history compression
- [ ] Add data pagination
- [ ] Create progressive loading
- [ ] Implement spatial partitioning
- [ ] Add performance auto-scaling

### Phase 4: Polish (Maintain Performance)
- [ ] Add performance budgets
- [ ] Implement automatic profiling
- [ ] Create performance regression tests
- [ ] Add debug visualizations
- [ ] Document performance constraints

## Testing for Scale

### Stress Test Scenarios

**Large-Scale Performance Test:**
- Create maximum province count
- Simulate extended gameplay
- Measure average frame time
- Assert performance targets

**Province Selection Test:**
- Create large province set
- Measure selection response time
- Assert sub-millisecond target

**Memory Bounds Test:**
- Create large province set
- Simulate extended gameplay
- Measure total memory usage
- Assert memory budget

**Determinism Test:**
- Create identical game states
- Execute identical commands
- Compare final checksums
- Assert perfect match

## Practical Optimization Decision Tree

**When considering memory layout optimizations:**

1. **Is this core simulation state?**
   - Yes → Keep in compact state struct if possible
   - No → Separate storage (cold data, presentation data)

2. **Do operations need multiple fields together?**
   - Yes → Keep fields together (AoS)
   - No → Consider splitting (SoA)

3. **Have you profiled and confirmed a bottleneck?**
   - No → Don't optimize yet
   - Yes → Proceed with targeted optimization

4. **Will this make the code significantly more complex?**
   - Yes → Reconsider if the gains justify the cost
   - No → Implement if profiling justifies it

**Remember:** Compact simulation structs are already highly optimized. Most performance work should focus on:
- GPU compute shaders for visual processing
- Dirty flag systems to minimize work
- Frame-coherent caching for expensive calculations
- Fixed-size data structures to prevent unbounded growth

Don't prematurely split data structures based on textbook advice. Profile first.

## Conclusion

Late-game performance collapse is not inevitable. By designing for the end state, separating hot and cold data, using GPU-driven rendering, and implementing proper caching strategies, you can maintain excellent performance throughout the entire game lifecycle.

The key is to **architect for scale from day one** rather than trying to optimize after problems appear. Every system should be designed with the question: "What happens at maximum scale?"

Remember: Sustained performance comes from architecture, not post-hoc optimization.

**Most Important Principles:**
1. **Compact simulation state** - cache-friendly, network-friendly
2. **GPU for visuals** - single draw call, compute shaders
3. **Fixed-point math** - deterministic for multiplayer
4. **Dirty flags** - only update what changed
5. **Ring buffers** - prevent unbounded growth
6. **Profile before optimizing** - don't split data structures prematurely

---

## Related Documents

- **[master-architecture-document.md](master-architecture-document.md)** - Overview of dual-layer architecture
- **[core-data-access-guide.md](core-data-access-guide.md)** - How to access hot and cold data
- **[time-system-architecture.md](time-system-architecture.md)** - Layered update frequencies and dirty flag systems

---

*Last Updated: 2025-10-10*
