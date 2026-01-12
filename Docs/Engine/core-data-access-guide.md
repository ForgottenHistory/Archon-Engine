# Core Data Access Guide

**Status:** Production Standard

---

## Core Principle

**Core owns data. Query layer provides access. Map layer reads, never writes.**

---

## Architecture Overview

### The Right Way
```
Your Code → GameState → Query Classes → Core Systems → Data
```

### The Wrong Way
```
Your Code → Direct System Access → Data (bypasses validation, breaks encapsulation)
```

---

## Access Hierarchy

### GameState (Entry Point)
Central coordinator providing access to all systems and queries.

**Always access data through GameState**, never directly through systems.

### Query Classes (Read Access)
Optimized read-only access to simulation data:
- Basic queries: Direct array lookups (instant)
- Computed queries: Calculated on-demand
- Cached queries: Expensive calculations cached per frame

### Systems (Data Owners)
Own authoritative data. Modify only through commands.

---

## Query Types

### Basic Queries (Ultra-Fast)
Direct access to hot data. No computation.

Examples:
- Get province owner
- Get province terrain
- Get country color
- Check entity existence

### Computed Queries (On-Demand)
Calculate from multiple sources when requested.

Examples:
- Get all provinces owned by country
- Filter provinces by criteria
- Cross-system lookups

**Remember:** Dispose NativeArrays when done.

### Cached Queries (Expensive)
Results cached automatically for performance.

Examples:
- Total development for country
- Province counts
- Aggregate statistics

Cache cleared when underlying data changes.

---

## Data Layers

### Hot Data (Frequent Access)
- Compact structs in contiguous arrays
- Accessed every frame or tick
- Available through query layer

### Cold Data (Rare Access)
- Detailed information, history, flavor text
- Loaded on-demand
- Separate storage from hot path

---

## Memory Management

### Allocator Choice
- **Temp:** Short-lived, same frame
- **TempJob:** Passed between frames/jobs
- **Persistent:** Permanent data

### Disposal Rule
**Always dispose NativeArrays.** Memory leaks crash builds.

Use `using` statements or explicit disposal.

---

## State Changes

### Read: Query Layer
Query classes for read-only access.

### Write: Command Pattern
All modifications flow through commands:
- Validate before execute
- Serialize for networking
- Deterministic execution

**Never modify Core data directly.**

---

## Event-Driven Updates

Subscribe to events through EventBus to react to state changes:
- ProvinceOwnershipChanged
- DevelopmentChanged
- Tick events (hourly, daily, monthly)

---

## Anti-Patterns

| Don't | Do Instead |
|-------|------------|
| Access systems directly | Use GameState + Queries |
| Store Core data in Map layer | Read fresh from Core |
| Forget to dispose NativeArrays | Always dispose |
| Modify Core data directly | Use Commands |
| CPU process millions of pixels | Use GPU compute shaders |

---

## Key Trade-offs

| Approach | Benefit | Cost |
|----------|---------|------|
| Query layer abstraction | Encapsulation, optimization | Slight indirection |
| Cached queries | Fast repeated access | Memory for cache |
| Command pattern | Validation, networking | Boilerplate |
| Hot/cold separation | Cache efficiency | Complexity |

---

## Summary

1. **Always access through GameState and Query classes**
2. **Never access systems directly**
3. **Dispose NativeArrays**
4. **Use Commands for state changes**
5. **Subscribe to Events for notifications**

---

## Related Patterns

- **Pattern 2 (Command Pattern):** State modifications
- **Pattern 3 (Event-Driven):** Change notifications
- **Pattern 4 (Hot/Cold Separation):** Data organization
- **Pattern 17 (Single Source of Truth):** Core owns data

---

*Core is the single source of truth. Query to read. Command to write.*
