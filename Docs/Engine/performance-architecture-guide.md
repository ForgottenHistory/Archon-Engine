# Performance Architecture Guide

**Status:** Production Standard

---

## Core Principle

**Design for the end state from day one. Late-game performance collapse is preventable.**

---

## The Problem

Grand strategy games face compounding performance challenges:

**Early game:** Few active provinces, minimal history, simple relationships. Fast.

**Late game:** Many provinces, extensive history, complex webs. Slow - even when PAUSED.

**Root causes:**
- Data accumulation without cleanup
- O(n²) algorithms that scale poorly
- Memory fragmentation
- Cache misses from scattered data
- UI touching entire game state every frame

---

## Core Principles

### Principle 1: Design for Scale
**Wrong:** "We'll optimize when it becomes a problem"
**Right:** "Architecture assumes worst-case from day one"

Profile at target scale regularly. Don't wait for problems.

### Principle 2: Hot/Cold Data Separation
Data "temperature" = access frequency, not importance.

**Hot:** Every frame/tick → Compact structs, contiguous arrays
**Warm:** Occasional → Can stay in main struct if space permits
**Cold:** Rare → Separate storage, loaded on-demand

**Benefit:** Hot data fits in cache. Cold data doesn't pollute it.

### Principle 3: Fixed-Size Data Structures
Dynamic growth is the enemy.

**Bad:** Unbounded lists that grow forever
**Good:** Ring buffers with automatic compression

**Result:** Bounded memory regardless of game length.

### Principle 4: Pre-Allocation (Zero Allocations During Gameplay)
**Industry lesson:** Malloc lock destroys parallelism.

**The problem:**
- Memory allocator uses global lock
- All threads wait for allocation
- Parallel code becomes sequential

**The solution:**
- Pre-allocate at initialization
- Clear and reuse during gameplay
- Zero allocations = zero lock contention = full parallelism

---

## System-Specific Patterns

### Map Rendering
**Problem:** Update every province mesh every frame.
**Solution:** GPU textures + single draw call.

### Province Selection
**Problem:** Physics raycast against thousands of colliders.
**Solution:** Texture lookup - single read, near-instant.

### UI/Tooltips
**Problem:** Expensive calculations every frame.
**Solution:** Frame-coherent caching - compute once, reuse within frame.

### History System
**Problem:** Unbounded event accumulation.
**Solution:** Tiered compression (recent=full, medium=compressed, old=summary).

### Game State Updates
**Problem:** Process all provinces every tick.
**Solution:** Dirty flags - update only what changed.

---

## Memory Layout

### Structure by Access Pattern

**Array of Structures (AoS):** When operations need multiple fields together.
- Most simulation operations
- Cache line fits entire struct
- Default choice for grand strategy

**Structure of Arrays (SoA):** When iterating single field across all elements.
- Rare in practice
- Profile before splitting
- Don't optimize prematurely

### The Real Enemy: Pointers

Pointers scatter data across memory → cache misses.

**Bad:** References in hot structures
**Good:** Value types only, contiguous layout

---

## Anti-Patterns

| Anti-Pattern | Problem | Solution |
|--------------|---------|----------|
| "It works for now" | O(n) scales poorly | Design for scale |
| Invisible O(n²) | Hidden quadratic complexity | Pre-compute adjacencies |
| Death by thousand cuts | Many small allocations | Pre-allocate pools |
| Allocator.Temp in hot path | Malloc lock | Persistent allocators, reuse |
| Update everything | Processing unchanged data | Dirty flags |
| Premature SoA | Splitting data used together | Profile first |
| Float in simulation | Non-deterministic | Fixed-point math |
| Interface-typed collections | Boxing allocations | Generic wrapper pattern |

---

## Key Trade-offs

| Decision | Benefit | Cost |
|----------|---------|------|
| Pre-allocation | Zero runtime allocation | Higher initial memory |
| Hot/cold split | Cache efficiency | Access complexity |
| Fixed-size buffers | Bounded memory | May need reallocation if undersized |
| Dirty flags | Process only changes | Flag management overhead |
| GPU for visuals | Massive parallelism | GPU programming complexity |

---

## Decision Framework

**Before adding a collection:**
1. Is this accessed every frame? → Pre-allocate
2. Is this temporary? → Clear and reuse
3. Is this in hot path? → Must be persistent
4. Can this grow unbounded? → Use ring buffer

**Before optimizing memory layout:**
1. Is this core simulation state? → Keep compact
2. Do operations need multiple fields? → Keep together (AoS)
3. Have you profiled? → Don't optimize without data
4. Is complexity worth it? → Reconsider if marginal gains

---

## Validation

Regular profiling at target scale:
- Frame time budget allocation
- Memory usage bounds
- Selection response time
- Zero allocations during gameplay (profiler confirmed)

**Any allocation in hot path = critical bug.**

---

## Summary

1. **Compact simulation state** - cache-friendly
2. **GPU for visuals** - single draw call, compute shaders
3. **Fixed-point math** - deterministic
4. **Pre-allocation** - zero gameplay allocations
5. **Dirty flags** - update only changes
6. **Ring buffers** - bounded growth
7. **Profile before optimizing** - data-driven decisions

---

## Related Patterns

- **Pattern 4 (Hot/Cold Separation):** Data temperature
- **Pattern 10 (Frame-Coherent Caching):** UI optimization
- **Pattern 11 (Dirty Flags):** Update minimization
- **Pattern 12 (Pre-Allocation):** Zero allocations

---

*Architecture prevents late-game collapse. Design for scale from day one.*
