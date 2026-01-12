# Data Loading Architecture

**Status:** Production Standard

---

## Core Principle

**JSON5 for maintainability. Burst for performance. Two-phase loading bridges both.**

---

## The Problem

Burst compiler provides massive parallel speedup but can't:
- Parse strings
- Handle reference types
- Perform file I/O

JSON5 provides maintainable data format but:
- Requires managed collections
- Single-threaded parsing
- No Burst optimization

---

## The Solution: Two-Phase Loading

### Phase 1: JSON5 Loading (Main Thread)
- Read files from disk
- Parse JSON5 to managed objects
- Convert to Burst-compatible structs
- Store in NativeArrays

**Constraints:** Single-threaded, managed types allowed, no Burst

### Phase 2: Burst Processing (Multi-threaded)
- Validate data (bounds, defaults)
- Transform to final game state
- Apply game logic
- Parallel processing across cores

**Constraints:** Multi-threaded, unmanaged types only, Burst-compiled

---

## Why This Design?

### JSON5 Benefits
- Human-readable and debuggable
- IDE support with syntax highlighting
- Battle-tested Newtonsoft.Json parsing
- Easy to modify and extend
- Eliminates fragile custom parsers

### Burst Benefits
- 5-10x speedup via parallelization
- SIMD auto-vectorization
- Zero allocations in hot path
- Cross-platform determinism

### Hybrid Benefits
- Maintainability WHERE IT MATTERS (data files)
- Performance WHERE IT MATTERS (processing)
- Clean separation of concerns

---

## Temporal Event Processing

**Problem:** Historical data files use incremental format (base state + dated events). Need effective state at specific date.

**Solution:**
1. Parse initial (undated) values
2. Find events at or before target date
3. Sort events chronologically
4. Apply in order (later overrides earlier)

**Key insight:** Process once during loading, discard events after. Zero runtime memory cost.

---

## Hot/Cold Data Separation

During loading, split data by access frequency:

**Hot Data → Burst Processing:**
- Frequently accessed fields
- Compact struct representation
- Performance-critical

**Cold Data → Main Thread Only:**
- Rarely accessed fields
- Reference types allowed
- Loaded on-demand acceptable

---

## Error Handling Strategy

**Principle:** Load as much as possible, skip corrupt files.

### Phase 1 (JSON5)
- Validate required fields
- Use defaults for missing optional data
- Log warnings, continue loading

### Phase 2 (Burst)
- Bounds checking
- Mark invalid entries
- Report failures in result

**Partial success is acceptable** if majority loads correctly.

---

## Key Constraints

### Phase 1 (Main Thread)
- File I/O allowed
- Managed types allowed
- Unity API allowed
- Single-threaded only

### Phase 2 (Burst Jobs)
- No file I/O
- No managed types (structs only)
- No Unity API
- Multi-threaded required

---

## Anti-Patterns

**Parsing in Burst:**
Burst can't handle strings. Parse in Phase 1.

**Phase mixing:**
Keep managed/unmanaged strictly separated.

**Forgetting disposal:**
NativeArrays must be disposed. Use Allocator.Persistent.

**Unity API in jobs:**
Breaks Burst compilation. Keep all Unity calls in Phase 1.

---

## Key Trade-offs

| Aspect | JSON5 | Burst | Hybrid |
|--------|-------|-------|--------|
| Readability | Excellent | N/A | Excellent |
| Parse speed | Moderate | N/A | Moderate |
| Process speed | Slow | Fast | Fast |
| Maintainability | High | Low | High |
| Parallelism | None | Full | Phase 2 only |

---

## Related Patterns

- **Pattern 4 (Hot/Cold Separation):** Applied during loading
- **Pattern 12 (Pre-Allocation):** NativeArrays pre-allocated
- **Pattern 15 (Phase-Based Init):** Two-phase structure

---

*Parse for maintainability. Process for performance. Don't mix phases.*
