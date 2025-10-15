# Data Loading Architecture - JSON5 + Burst Hybrid System

**Implementation Status:** ✅ Fully Implemented (BurstProvinceHistoryLoader, BurstCountryLoader)

## Executive Summary

**Question**: How do we load thousands of game data files while maintaining high performance?
**Answer**: Hybrid JSON5 + Burst architecture with two-phase loading
**Key Innovation**: Parse readable JSON5 on main thread, process with Burst jobs for speedup
**Performance**: Fast loading with parallel Burst compilation

---

## Design Rationale

### Why JSON5?
The project migrated from Paradox .txt format to JSON5 after the .txt parser became unmaintainable.

**Advantages of JSON5:**
- Readable and debuggable format with IDE support
- Reliable parsing with battle-tested Newtonsoft.Json library
- Type safety through structured data
- Maintainable - easy to modify and extend
- Eliminated complex parser code

**Why Not Pure Burst?**
- Burst doesn't support string parsing or reference types
- Burst doesn't support file I/O operations
- JSON parsing requires managed collections (Dictionary, List)

### The Hybrid Approach

**Solution**: Split loading into two phases:
1. **Phase 1 (Main Thread)**: JSON5 parsing → Burst-compatible structs
2. **Phase 2 (Multi-threaded)**: Burst jobs process structs in parallel

This gives both **maintainability** (JSON5) and **performance** (Burst).

---

## Two-Phase Loading Pattern

### Architecture Overview

**Phase 1: JSON5 Loading (Main Thread Only)**
- File I/O → JSON5 Parse → Burst Struct Conversion
- Input: .json5 files
- Output: NativeArray<RawData>
- Tools: Newtonsoft.Json, System.IO

**Phase 2: Burst Processing (Multi-threaded via Jobs)**
- Validation → Transformation → Final Game State
- Input: NativeArray<RawData>
- Output: NativeArray<FinalState>
- Tools: Unity Jobs, Burst Compiler

### Phase 1: JSON5 Loading

**Responsibilities:**
- Read .json5 files from disk
- Parse JSON5 with Newtonsoft.Json
- Convert to Burst-compatible structs
- Store in NativeArray with Allocator.Persistent

**Constraints:**
- Can use managed types (string, Dictionary, List)
- Can perform file I/O
- Single-threaded only (Unity API limitation)
- No Burst optimization

**Pattern**: Find files → Parse JSON → Extract to structs → Convert to NativeArray

### Phase 2: Burst Processing

**Responsibilities:**
- Validate data (bounds checking, default values)
- Transform raw data to final game state
- Apply game logic (calculate development, pack flags)
- Parallel processing across CPU cores

**Constraints:**
- Multi-threaded via IJobParallelFor
- Burst-compiled for speedup
- SIMD auto-vectorization
- No managed types (structs only)
- No file I/O or Unity API calls

**Pattern**: Process structs in parallel with Burst jobs

---

## Province Loading Implementation

### Complete Flow

**Loading pipeline**:
1. JSON5 Loading (Main Thread) → NativeArray<RawProvinceData>
2. Burst Processing (Multi-threaded) → NativeArray<ProvinceInitialState>
3. Cleanup disposed NativeArrays

**Parallel execution**: Jobs process multiple provinces per batch for efficiency

### Province JSON5 Format

JSON5 format with historical events:
- Initial state fields (owner, culture, religion, base_tax, etc.)
- Dated events with format "YYYY.M.D" for historical changes
- Boolean flags (is_city, hre)
- Numeric values (development, trade center level)

### Temporal Event Processing (Historical Dates)

**Problem:** EU4 province history files use incremental format - initial values + dated events. At game start, need effective state after applying all historical events up to that date.

**Example**: Province with initial owner TIM, event at 1442.1.1 changes to QOM, event at 1451.1.1 changes to QAR. At 1444.11.11 start date, owner should be QOM (not TIM).

**Implementation - ApplyHistoricalEventsToStartDate**:
1. Start with all non-dated properties
2. Find dated events at or before start date
3. Sort events chronologically
4. Apply events in order (later events override earlier ones)
5. Return effective state at start date

**Date Parsing**: Parse EU4 date format "1442.1.1" → (1442, 1, 1)

**Date Comparison**: Chronological order with int arithmetic only (no float)

**Why This Design:**
- Chronological Application ensures correct state at any point
- Deterministic with int arithmetic only
- Efficient parsing once during loading
- Future-proof for dynamic start dates

**Impact**: Adds minimal load time overhead, zero extra runtime memory (events discarded after processing), ensures correct political map state

**Pattern for Future**: This pattern applies to any temporal data with dated events - parse initial state, parse events, filter events ≤ target date, sort chronologically, apply in order

### Data Structures

**Raw data from JSON5 (Phase 1 output)**:
- Province ID and string references (FixedString32Bytes)
- Numeric base values (baseTax, baseProduction, baseManpower)
- Boolean flags (isCity, hre)
- Center of trade level

**Final game state (Phase 2 output)**:
- Province ID and processed string references
- Validated numeric values (clamped bytes)
- Packed boolean flags
- Calculated development
- Validity flag

---

## Country Loading Implementation

### Complete Flow

**Loading pipeline**:
1. JSON5 Loading (Main Thread) → NativeArray<RawCountryData>
2. Burst Processing (Multi-threaded for hot data) → NativeArray<CountryHotData>
3. Create Collection (Main Thread for cold data) → CountryDataCollection
4. Cleanup disposed NativeArrays

### Hot/Cold Data Separation

**Why Split Data?**
- **Hot Data**: Accessed frequently (colors, tags) → Burst-optimized structs
- **Cold Data**: Accessed rarely (display names, descriptions) → Managed types on main thread

**Hot data**: Processed by Burst jobs, performance-critical fields only (tag, colors, packed RGB)

**Cold data**: Processed on main thread, reference types allowed (display names, graphical culture, descriptions)

### Country JSON5 Format

JSON5 format includes:
- Graphical culture
- RGB color arrays
- Revolutionary colors
- Historical idea groups
- Preferred religion
- Monarch names with weights

---

## Performance Characteristics

### Benchmark Results

**Phase 1 (JSON5 Loading)**: File I/O, JSON parsing, struct conversion - acceptable for game startup

**Phase 2 (Burst Processing)**: Job scheduling and parallel execution - benefits from CPU cores

**Combined Total**: Acceptable startup time with parallel processing

### Scaling Analysis

Performance scales with province count:
- Phase 1 scales linearly with file count (I/O bound)
- Phase 2 benefits from parallelism (CPU core count matters)
- Burst compilation provides significant speedup over managed code

### Memory Usage

**Province Loading**: Temporary NativeArrays for raw and processed data - negligible memory footprint

**Country Loading**: Hot data in NativeArray, cold data in managed collections - compact total memory

### Optimization Notes

1. **Allocator.Persistent** - Required because data survives multiple frames during loading
2. **Batch Size** - Optimal balance between job overhead and parallelism
3. **[ReadOnly] Attributes** - Enable Burst's aliasing analysis for better SIMD
4. **FixedString Types** - Burst-compatible strings with fixed size

---

## Error Handling & Validation

### Phase 1 Validation (JSON5 Loading)

Validate required fields and provide fallbacks:
- Check for missing required fields
- Use default values when appropriate
- Log warnings for missing optional data
- Continue loading despite individual file failures

### Phase 2 Validation (Burst Jobs)

Validate data in Burst jobs:
- Bounds checking on numeric values
- Validate enums and ranges
- Ensure valid IDs
- Mark invalid entries

### Error Recovery Strategy

**Principle**: Load as much data as possible, skip corrupt files

Result types track success/failure:
- Total count loaded
- Failed count
- Error messages
- Success boolean

Partial success is acceptable if most data loads correctly.

---

## Integration with Game Systems

### Startup Flow

Loading during game initialization:
- Set loading phase with progress
- Call Burst loader
- Check result for success
- Initialize game state from loaded data
- Log completion

### ProvinceSystem Integration

Initialize ProvinceSystem from loaded results:
- Iterate through loaded province states
- Add valid provinces to system
- Store initial state for reference resolution
- Dispose loaded data after extraction

---

## Best Practices

### DO

1. Use Allocator.Persistent for data that survives multiple frames
2. Dispose NativeArrays when done - prevents memory leaks
3. Optimal batch size for IJobParallelFor scheduling
4. [ReadOnly] attributes on input NativeArrays for safety
5. Validate in Phase 1 - catch malformed JSON early
6. Validate in Phase 2 - enforce game rules with Burst
7. Graceful degradation - skip corrupt files, continue loading

### DON'T

1. Don't use Allocator.Temp - disposed too quickly for async loading
2. Don't mix managed/unmanaged - keep phases strictly separated
3. Don't parse strings in Burst - use FixedString types instead
4. Don't forget .Dispose() - memory leaks crash builds
5. Don't use Unity API in jobs - breaks Burst compilation
6. Don't nest jobs - schedule jobs in sequence with dependencies

---

## Related Documentation

### Architecture Documents
- **master-architecture-document.md** - Overall system architecture and dual-layer design
- **data-flow-architecture.md** - System communication and data access patterns
- **performance-architecture-guide.md** - Memory optimization and cache efficiency

### Technical References
- **unity-burst-jobs-architecture.md** - Complete Burst compiler guide with examples
- **Core/FILE_REGISTRY.md** - Complete listing of loader files and jobs

---

## Conclusion

The hybrid JSON5 + Burst architecture provides the best of both worlds:

1. **Maintainability** - Readable JSON5 format with standard tooling
2. **Performance** - Burst-compiled parallel processing for speedup
3. **Reliability** - Battle-tested JSON parser, no fragile custom parser
4. **Scalability** - Handles many files with acceptable startup time

This architecture replaced a complex .txt parser that was unmaintainable. The new system is simpler, faster, and more reliable.

**Key Innovation**: By splitting parsing (managed code) from processing (Burst jobs), we avoid Burst's limitations while maintaining high performance where it matters.

---

*Last Updated: 2025-10-15*
*Implementation: Complete*
*Status: Production-ready*
