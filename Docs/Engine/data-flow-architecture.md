# Data Flow Architecture

**Status:** Production Standard

---

## Core Principle

**Each system owns its data. GameState provides unified access. Communication flows through events and commands.**

---

## System Architecture

### Hub-and-Spoke Pattern
- **GameState** is the central hub (coordinator, not owner)
- **Systems** own their domain data (ProvinceSystem, CountrySystem, etc.)
- **EventBus** handles cross-system notifications
- **Commands** handle all state changes

### Data Ownership Rule
ONE authoritative place for each piece of data. No duplicates.

**Why:** Multiple copies lead to desync bugs. Update one, forget the other = bug.

---

## Communication Patterns

### Events vs Direct Calls

| Use Events For | Use Direct Calls For |
|----------------|---------------------|
| Cross-system notifications | Same-system operations |
| Multiple listeners | Required dependencies |
| Loose coupling | Performance-critical paths |
| Optional reactions | Return values needed |

**Events are fire-and-forget.** Don't use events when you need a response.

### Event Flow
1. System emits event (e.g., ProvinceOwnershipChanged)
2. EventBus queues event
3. End of frame: EventBus processes all queued events
4. Subscribers react (UI updates, AI re-evaluates, caches invalidate)

**Frame-coherent processing:** Events queued during frame, processed once per frame.

---

## State Change Pattern: Commands

ALL state modifications flow through commands.

### Why Commands?
- **Validation:** Check before execute
- **Networking:** Send commands, not state
- **Replay:** Store command log for debugging/replays
- **Determinism:** Same command + same state = same result
- **Undo:** Commands can implement reverse

### Command Requirements
- Validate before execute
- Serialize for network/save
- Calculate checksum for determinism verification
- Execute deterministically (no floats, seeded random only)

---

## Query Patterns

### Simple Queries
Direct array access. Instant. No computation.

### Computed Queries
Calculate from multiple sources on-demand. Use when data isn't frequently accessed.

### Cached Queries
For expensive calculations called multiple times per frame:
- Cache result with frame counter
- Clear cache when frame changes
- Recompute only on first access per frame

---

## Bidirectional Mappings

Forward and reverse lookups are often needed for performance.

**Example:** Province→Owner (forward) AND Owner→Provinces (reverse)

**Rule:** ONE system owns BOTH directions and keeps them synchronized.

**Why:** Without reverse lookup, "What provinces does France own?" requires O(n) scan. With cached reverse lookup, it's O(1).

---

## Zero-Allocation Event System

### The Problem
Storing struct events in interface-typed collections causes boxing (heap allocation per event).

### The Solution
Type-specific `EventQueue<T>` wrapper pattern:
- Internal interface for polymorphism
- Concrete generic queue storage
- Virtual method calls don't box value types
- Direct delegate invocation

**Result:** Zero allocations during gameplay.

---

## Game Loop Flow

1. **Process Input:** Create commands from player/AI actions
2. **Time Tick:** Advance simulation time
3. **Process Commands:** Execute validated commands
4. **Update Dirty Systems:** Only update what changed
5. **Process Events:** Handle all queued events
6. **Render:** Present to screen

---

## Determinism Requirements

For multiplayer and replays, all state changes must be deterministic:
- **Fixed-point math:** Never float/double in simulation
- **Command order:** Identical processing order on all clients
- **Seeded random:** Same seed = same results
- **Checksum verification:** Catch desync immediately

---

## Memory Management

### Pre-Allocation Policy
Allocate everything at initialization. Zero allocations during gameplay.

**Why:** Malloc lock destroys parallelism. Pre-allocation maintains full parallel speedup.

### Object Pooling
Entities are recycled, not destroyed:
- Fixed-size pools initialized at startup
- "Create" pops from free list
- "Destroy" pushes to free list
- Never actually allocate/deallocate during gameplay

---

## Anti-Patterns

| Anti-Pattern | Problem | Solution |
|--------------|---------|----------|
| Multiple owners of same data | Desync bugs | Single source of truth |
| Events for request-response | Events don't return values | Direct calls for queries |
| Float math in simulation | Non-deterministic | Fixed-point math |
| Allocations in hot paths | Performance collapse | Pre-allocate everything |
| Interface-typed event collections | Boxing allocations | EventQueue<T> wrapper |
| GameState as god object | Unmaintainable | Systems own logic, GameState coordinates |

---

## Key Trade-offs

| Decision | Benefit | Cost |
|----------|---------|------|
| Commands for all changes | Networking, replay, validation | Slight overhead vs direct modification |
| Event-driven | Loose coupling, extensibility | Event processing overhead |
| Bidirectional mappings | O(1) lookups in both directions | Memory for reverse index |
| Frame-coherent events | Consistent state during processing | One-frame delay for reactions |
| Pre-allocation | Zero runtime allocations | Higher initial memory |

---

## Related Patterns

- **Pattern 2 (Command Pattern):** All state changes
- **Pattern 3 (Event-Driven):** Cross-system communication
- **Pattern 10 (Frame-Coherent Caching):** Expensive query optimization
- **Pattern 16 (Bidirectional Mapping):** Forward and reverse lookups
- **Pattern 17 (Single Source of Truth):** Data ownership

---

*Systems own data. Commands change state. Events notify. GameState coordinates.*
