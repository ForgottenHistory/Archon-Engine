# Universal Modifier System

**Status:** Production Standard

---

## Core Problem

Grand strategy games have many sources of bonuses (buildings, tech, events, government, terrain) that must stack predictably. Without a unified system, bonus application becomes inconsistent, tooltips are impossible, and removal creates orphaned modifiers.

---

## Core Principle: Universal Accumulation

All modifiers follow the industry-standard formula:

```
Final = (Base + Additive) × (1 + Multiplicative)
```

**Why this formula:**
- Additive bonuses stack linearly (+5, +10 = +15)
- Multiplicative bonuses stack additively THEN multiply (50% + 50% = 100%, not 125%)
- Prevents exponential scaling that breaks game balance
- Designer-friendly (percentages add intuitively)

**Industry pattern:** EU4, CK3, Stellaris, Victoria 3 all use this formula.

---

## Scope Inheritance

Modifiers exist at three scopes that inherit downward:

**Global Scope:** Applies to all entities (tech breakthroughs, world events, golden ages)

**Country Scope:** Applies to all provinces owned by that country (government type, national policies, country events)

**Province Scope:** Local only (buildings, terrain, governors)

**Key insight:** When you add a modifier to country scope, ALL provinces in that country inherit it automatically. No iteration required.

**Example:**
- Research tech → add +10% to country scope → all 50 provinces get +10%
- Build farm → add +50% to province scope → only that province gets +50%
- Province total: inherits country (+10%) + local (+50%) = +60%

---

## Source Tracking

Every modifier knows its origin:
- **Source type:** Building, Technology, Event, Government, Character, etc.
- **Source ID:** Which specific building/tech/event
- **Temporary flag:** Permanent vs expires after N ticks

**This enables:**

**Tooltips:** Display exactly where each bonus comes from
```
Production: +80%
  Farm building:  +50% (local)
  Government:     +20% (country)
  Tech:           +10% (country)
```

**Clean removal:** Destroy building → remove all modifiers from that source (no orphans)

**Expiration:** Temporary modifiers auto-expire without manual tracking

---

## Design Constraints

**Fixed-size storage:** Maximum modifier types defined at compile time (not runtime)

**Why fixed-size:**
- Zero allocations during gameplay (multiplayer requirement)
- Deterministic memory layout (same on all platforms)
- O(1) lookup (array index by ID)
- Cache-friendly (contiguous memory)

**Dirty flag caching:** Modifiers change rarely but are queried frequently. Cache the accumulated total and only rebuild when a modifier is added/removed.

---

## Engine-Game Separation

**ENGINE provides mechanism:**
- Scope containers and inheritance
- Accumulation formula application
- Source tracking and removal
- Dirty flag optimization
- Temporary modifier expiration

**GAME defines policy:**
- Which modifier types exist (production, tax, manpower, etc.)
- Which systems apply modifiers (buildings, tech, events)
- String ID ↔ numeric ID mapping for data files

---

## Key Trade-offs

| Aspect | Decision | Rationale |
|--------|----------|-----------|
| Storage | Fixed-size arrays | Zero allocations, deterministic, O(1) lookup |
| Accumulation | Add-then-multiply | Prevents exponential scaling |
| Inheritance | Scope chain | Country changes affect all provinces without iteration |
| Tracking | Source-based | Clean removal, accurate tooltips |

---

## Anti-Patterns

**Don't multiply modifiers separately:**
Each modifier multiplied individually causes exponential scaling (50% × 50% = 125% instead of 100%)

**Don't store modifiers in multiple places:**
Province object AND country object AND modifier system = desync risk. Single source of truth only.

**Don't forget removal:**
Destroying a building without removing its modifiers leaves orphaned bonuses. Always remove by source.

**Don't allocate during modifier application:**
Hot path called thousands of times per frame. Zero allocations required.

---

## Related Patterns

- **Pattern 2 (Command Pattern):** Modifier changes flow through commands for networking
- **Pattern 5 (Fixed-Point Determinism):** Modifier values stored as deterministic types
- **Pattern 10 (Frame-Coherent Caching):** Dirty flag optimization
- **Pattern 17 (Single Source of Truth):** One modifier system owns all modifiers

---

*Modifiers stack predictably. Sources are tracked. Removal is clean.*
