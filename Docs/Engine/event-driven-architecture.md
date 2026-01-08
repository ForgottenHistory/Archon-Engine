# Event-Driven Architecture

**Purpose:** Deep-dive on EventBus usage, event-driven caching, and cross-system communication
**Status:** Production Standard
**Last Updated:** 2026-01-08

---

## Overview

EventBus enables decoupled communication between systems. Changes in one system notify others without direct dependencies. Critical for maintaining clean layer separation (CORE → MAP → GAME).

**Key Principles:**
- Zero allocations during gameplay (struct events, no boxing)
- Frame-coherent processing (events queued, processed once per frame)
- Unidirectional data flow (events notify, don't request)

---

## EventBus Basics

### Access Points
- `gameState.EventBus` - Primary access
- `AIGoal.Initialize(EventBus)` - Passed to AI goals
- `GameState.Instance.EventBus` - For MonoBehaviours

### Lifecycle
1. **Subscribe** during initialization (store handler reference)
2. **Handle** events when notified
3. **Unsubscribe** during disposal (prevent memory leaks)

### Event Definition Rules
- Use structs implementing `IGameEvent` (no heap allocation)
- Include entity IDs for targeted invalidation
- Include old/new values when useful for handlers
- Keep payload small

---

## Available Events

### Simulation Events (CORE Layer)
| Event | Purpose |
|-------|---------|
| `ProvinceOwnershipChangedEvent` | Province changes hands |
| `ProvinceDevelopmentChangedEvent` | Development changes |
| `ProvinceSystemInitializedEvent` | System ready |
| `HourlyTickEvent` | Each game hour |
| `DailyTickEvent` | Each game day |
| `MonthlyTickEvent` | Each game month |
| `YearlyTickEvent` | Each game year |
| `TimeStateChangedEvent` | Speed/pause changes |

### Adding New Events
Location: Relevant system's events file (e.g., `ProvinceEvents.cs`)

Required fields:
- Relevant entity IDs
- Old/new values if applicable
- `float TimeStamp { get; set; }` (interface requirement)

---

## Event-Driven Caching Pattern

**Problem:** Expensive calculations called frequently, but underlying data changes rarely.

**Solution:** Cache results, subscribe to change events, invalidate only affected entries.

### Architecture Flow
```
Data Owner (emits) → EventBus → Cache Consumer (invalidates)
                                      ↓
                              Next query recalculates
```

### Cache Components
1. **Cache storage** - Dictionary keyed by entity ID
2. **Invalidation set** - HashSet of entities needing recalculation
3. **Event handler** - Marks affected entities as invalidated
4. **Query method** - Returns cached data or recalculates if invalidated

### Invalidation Strategies

**Conservative (recommended start):**
- Invalidate the changed entity
- Invalidate related entities (neighbors, dependencies)
- Higher recalculation rate, but always correct

**Targeted (optimize later):**
- Only invalidate exactly what changed
- Lower recalculation rate, but more complex logic
- Risk of stale data if relationships missed

### When to Use Event-Driven Caching
- Calculation is O(n) or worse
- Data changes infrequently relative to queries
- Clear event exists for underlying data changes
- Multiple consumers need same cached data

---

## When to Use Events vs Direct Calls

### Use Events
| Scenario | Reason |
|----------|--------|
| Multiple systems react to same change | Decoupling |
| Cross-layer communication (CORE → GAME) | Layer separation |
| Cache invalidation | Lazy recalculation |
| UI updates from simulation | Loose coupling |

### Use Direct Calls
| Scenario | Reason |
|----------|--------|
| Single required dependency | Simpler |
| Performance-critical hot path | No indirection |
| Same-system operations | Already coupled |
| Request-response (need return value) | Events are fire-and-forget |

---

## Performance Considerations

### Zero-Allocation Requirements
- Events are structs (no heap allocation)
- Handlers stored as delegates (one-time allocation at subscribe)
- Use typed `EventQueue<T>`, never `List<IGameEvent>` (boxing)

### Frame Coherence
- Events queued during simulation
- Processed once in `GameState.Update()`
- Handlers see consistent state within frame

### Cache Memory Budget
- ~50 bytes base + entry size per cached entity
- For 300 countries with 3 cache types: ~60KB total
- Acceptable trade-off for O(1) lookup

---

## Anti-Patterns

| Anti-Pattern | Problem | Solution |
|--------------|---------|----------|
| Events for request-response | Events are fire-and-forget | Use direct calls for queries |
| Heavy computation in handlers | Blocks event processing | Only invalidate, recalculate lazily |
| Forgetting to unsubscribe | Memory leaks, stale handlers | Always unsubscribe in Dispose |
| Boxing events | Heap allocations | Use typed EventQueue<T> |
| Circular event chains | Infinite loops | Design acyclic event flow |
| Invalidating everything | Defeats caching purpose | Invalidate only affected entities |

---

## Field-Tested Case: AI Performance

**Problem:** AI evaluating 300 countries at 10x speed. Each evaluation scanned 13k provinces for neighbor detection. Result: 10 FPS death spiral.

**Solution:** Event-driven cache for neighbor countries, owned provinces, and province counts. Subscribe to `ProvinceOwnershipChangedEvent`.

**Result:**
- Border changes: Few per game-hour
- AI evaluations: Hundreds per game-hour
- Cache hit rate: >99%
- Performance: 10 FPS → 60+ FPS

**Key Files:**
- `Game/AI/Goals/ExpandTerritoryGoal.cs` - Cache implementation
- `Core/Systems/Province/ProvinceEvents.cs` - Event definitions

**Session Log:** `Docs/Log/2026/01/08/ai-performance-optimization.md`

---

## Related Documentation

- [architecture-patterns.md](architecture-patterns.md) - Pattern 3: Event-Driven Architecture
- [data-flow-architecture.md](data-flow-architecture.md) - Overall data flow
- [performance-architecture-guide.md](performance-architecture-guide.md) - Zero-allocation requirements

---

*Last Updated: 2026-01-08*
