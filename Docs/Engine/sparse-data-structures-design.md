# Sparse Data Structures

**Status:** Production Standard

---

## Core Problem

**Dense arrays scale with POSSIBLE items, not ACTUAL items.**

When mods multiply item types (30 buildings → 500 buildings), dense arrays waste memory and iteration time checking empty slots.

**Industry example:** HOI4's equipment system became 16x slower with popular mods because every province checked all 500 equipment slots, even when most were empty.

---

## Core Principle

**Store only what exists. Iterate only over actual items.**

Use sparse structures when:
- Base game has N items, mods can add 5-10x more
- Most entities don't have most items
- Memory should scale with usage, not possibility

Use dense structures when:
- Data is ALWAYS present (owner, terrain)
- Fixed at engine level, mods don't multiply
- Every entity has every field

---

## Sparse vs Dense Decision

| Data Type | Most Entities Have | Mod Multiplier | Pattern |
|-----------|-------------------|----------------|---------|
| Owner/Terrain | ALL | N/A | Dense (always present) |
| Buildings | 0-5 of 500 types | 5-10x | Sparse |
| Modifiers | 0-3 of 1000 types | 10-20x | Sparse |
| Trade Goods | 1-2 of 100 types | 2-3x | Sparse |
| Development | ALL | N/A | Dense (always present) |

**Rule:** If mods multiply count AND most entities don't have most items → Sparse

---

## Architecture: Three Layers

### Layer 1: Definitions (Static)
Type definitions loaded at startup, never modified during gameplay.
- Loaded from data files
- Each definition gets unique numeric ID
- Mods add definitions, never remove

### Layer 2: Instance Storage (Sparse)
Which entities have which items. Pre-allocated at initialization.
- MultiHashMap pattern: entity ID → item IDs
- Memory scales with actual items, not possible items
- Zero allocations during gameplay

### Layer 3: Query API
Fast lookups and iteration over sparse data.
- Existence check: "Does province X have building Y?"
- Get all: "What buildings does province X have?"
- Filter: "Which provinces have building Y?"

---

## Storage Pattern: MultiHashMap

**Why MultiHashMap:**
- One key (entity ID) → multiple values (item IDs)
- Burst-compatible native container
- Fast iteration over actual items only
- Memory scales with usage

**Access patterns:**
- O(m) existence check where m = items per entity (typically <10)
- O(m) iteration over entity's items
- O(n) filter queries (cacheable per frame)

---

## Memory Comparison

**Dense approach (naive):**
- 10k provinces × 500 building types × 1 byte = 5 MB
- Iteration: 5 million checks, 99% wasted

**Sparse approach:**
- 10k provinces × 5 avg buildings × 4 bytes = 200 KB
- Iteration: Only actual buildings

**Savings:** 96% memory reduction, iteration proportional to content

---

## Pre-Allocation Strategy

At initialization:
1. Estimate capacity: entities × expected items × safety margin
2. Pre-allocate MultiHashMap with fixed capacity
3. Warn if approaching 80% capacity
4. Zero allocations during gameplay

**Gameplay adds/removes entries without allocation** (reuses pre-allocated capacity).

---

## Mod Compatibility

### Dynamic ID Assignment
- Base game loads definitions first (IDs 1-30)
- Mods load after (IDs 31-80, 81-150, etc.)
- Sequential assignment during loading

### Save Compatibility
- Save files store string ID + data
- Load looks up definition by string ID
- Graceful degradation if mod missing

---

## Key Trade-offs

| Aspect | Dense | Sparse |
|--------|-------|--------|
| Memory | Scales with capacity | Scales with usage |
| Lookup | O(1) array index | O(m) hash lookup |
| Iteration | Checks all slots | Only actual items |
| Mod scaling | Gets worse | Unchanged |
| Complexity | Simple | Hash table overhead |

---

## When NOT to Use Sparse

- Data every entity has (owner, terrain, development)
- Fixed types that mods don't multiply
- Performance-critical lookups needing O(1) access
- Simple presence/absence (use bitfield instead)

---

## Anti-Patterns

**Dense arrays for sparse data:**
Wastes memory, iterates empty slots, scales poorly with mods.

**Dictionary per entity:**
Allocations everywhere, not Burst compatible.

**Linear search through flat list:**
O(n) for existence check.

---

## Success Criteria

1. **Mods don't degrade performance** - 30→500 types: no slowdown
2. **Memory scales with content** - Not with possible combinations
3. **Zero gameplay allocations** - All storage pre-allocated
4. **Simple query API** - Has/Get/Filter cover all cases

---

## Related Patterns

- **Pattern 8 (Sparse Collections):** Core pattern
- **Pattern 12 (Pre-Allocation):** Zero-allocation requirement
- **Pattern 10 (Frame-Coherent Caching):** Cache expensive filter queries

---

*Store what exists. Iterate what's present. Scale with content, not possibility.*
