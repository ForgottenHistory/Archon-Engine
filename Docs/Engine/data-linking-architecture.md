# Grand Strategy Game - Data Linking & Reference Resolution Architecture

**Implementation Status:** ✅ Implemented (CrossReferenceBuilder, ReferenceResolver, DataValidator exist)

**Recent Update (2025-10-09):** ProvinceState refactored for engine-game separation. Game-specific fields moved to HegemonProvinceData. See phase-3-complete-scenario-loader-bug-fixed.md.

## Executive Summary
**Challenge**: Loaded data has string references that need linking to actual game objects
**Solution**: Multi-phase loading with efficient ID mapping and reference resolution
**Key Principle**: Convert strings to IDs once at load time, never use strings at runtime
**Result**: Fast lookups, type-safe references, clear data relationships

## The Core Problem

When you load Paradox-style data files, you get string references everywhere (owner="ENG", religion="catholic", trade_good="grain"). These strings need to be resolved to actual runtime IDs and validated for consistency.

## Three-Phase Loading Architecture

### Phase 1: Discovery & Registration
First pass - discover all entities and assign IDs

### Phase 2: Loading & Parsing
Load actual data with string references intact

### Phase 3: Linking & Resolution
Convert all string references to runtime IDs

### Phase 4: Validation
Validate data integrity after linking

## The Registry Pattern

### Central Registry System
Registries provide bidirectional mapping between string tags and numeric IDs:
- Register(key, item) → assigns ID
- Get(id) → retrieves item
- GetId(key) → retrieves ID from string
- TryGet(key, out item) → safe lookup

### Game-Specific Registries
Common registries include Countries, Religions, Cultures, TradeGoods, Buildings, Technologies, Governments, Terrains, Units. Provinces use special handling due to value type storage.

## ID Mapping Strategy

### String Tags to Runtime IDs
Runtime uses numeric IDs (ushort) instead of strings for performance:
- O(1) array indexing vs O(n) string comparison
- Type-safe when using ID wrapper structs (optional)
- Zero string comparisons during gameplay

### Type-Safe ID Trade-offs
**Pros**: Can't accidentally mix different ID types, self-documenting
**Cons**: Extra type complexity, implicit conversions can confuse debugging
**Recommendation**: Use plain ushort unless you have many entity types

### Province ID Handling (Value Types vs Reference Types)

**Critical difference**: Provinces are value types in NativeArray, not reference types in List.

For reference types (Countries, Religions, Buildings):
- Registry stores items in managed List
- Dictionary maps string keys to IDs

For provinces (value types in NativeArray):
- ProvinceSystem stores states in unmanaged NativeArray
- Burst-compatible for performance
- Dictionary maps definition IDs to runtime IDs (if needed)

### Sparse vs Dense Province IDs

**When sparse→dense mapping is worth it:**
- Province IDs have large gaps
- Wasted memory from gaps exceeds ~10% of total
- Example: Max ID 10000, but only 3000 actual provinces = 70% waste

**When direct indexing is simpler:**
- Province IDs are mostly contiguous
- Wasted memory negligible
- Simpler code with direct array indexing

Most Paradox-style games have nearly contiguous IDs. Unless profiling shows memory issues, direct indexing is simpler.

## Reference Resolution System

### Raw Data with String References
Data loaded from files contains strings ("ENG", "catholic", "grain"). Runtime needs numeric IDs for performance.

### Engine-Game Separation
**ENGINE LAYER (8-byte ProvinceState)**: Generic primitives only - ownerID, controllerID, terrainType, gameDataSlot

**GAME LAYER (4-byte HegemonProvinceData)**: Game-specific hot data - development, fortLevel, unrest, population

**Cold Data**: Stored separately - names, positions, neighbors, religion, culture, trade goods, buildings

### Reference Resolver Pattern
ReferenceResolver converts string references to numeric IDs:
- ResolveCountryRef(tag) → countryID
- ResolveTerrainRef(terrain) → terrainID
- ResolveBuildingList(names) → buildingIDs[]
- Collects errors for reporting
- Supports deferred resolution for forward references

## Cross-Reference System

### Bidirectional References
After loading, build reverse lookups for performance:
- Country → Provinces mapping (O(1) vs O(n))
- CultureGroup → Cultures mapping
- TradeNode → Provinces mapping

Without reverse mapping, queries like "What does France own?" require iterating all provinces (O(n)). With reverse mapping, it's O(1) array lookup. The key: ONE system owns BOTH mappings and keeps them synchronized.

## Validation System

### Data Integrity Checker
Validates loaded data after reference resolution:
- Every owned province must have valid owner
- Controller must be valid if different from owner
- Capital must be owned by country
- Technology groups must exist
- Countries should own at least one province

Error vs Warning strategy:
- Errors: Critical data integrity issues (throw exception)
- Warnings: Suspicious but playable situations (log and continue)

## Loading Pipeline Implementation

Complete loading process follows sequential steps:
1. Load static data (definitions with no dependencies)
2. Register all entities (assign IDs)
3. Load entity data with string refs
4. Resolve all references to IDs
5. Build cross-references (bidirectional mappings)
6. Validate data integrity
7. Optimize for runtime

## Optimization Strategies

### String Interning
During loading, many strings are repeated (government types, religions shared across countries). String interning reduces memory by ensuring identical strings reference same memory location.

### Compile-Time ID Generation (Optional)
For frequently-used constants, generate IDs at build time from data files. Enables compile-time constants for common cases, eliminating runtime lookups. Trade-off: Requires build-time code generation.

### Lazy Loading for Cold Data
Rarely accessed data can be loaded on-demand using lazy loaders. Reduces initial load time and memory usage.

## Error Handling

### Missing Reference Strategies
- ThrowException: Fail fast (use for critical refs like countries)
- LogWarning: Continue with warning (use for optional refs)
- UseDefault: Silent fallback (use for cosmetic refs)
- CreatePlaceholder: Generate missing entity (use for modding)

## Performance Considerations

### Memory Layout
**ENGINE (8-byte ProvinceState)**: Generic primitives only, compact for cache efficiency

**GAME (4-byte HegemonProvinceData)**: Game-specific hot data, separate from engine

**Cold Data**: Separate storage accessed rarely

This separation keeps hot data cache-friendly for high performance.

### Lookup Performance
- O(1) array lookup for runtime IDs (direct array access)
- O(1) dictionary lookup for string tags (only during loading)
- Avoid O(n) searches at runtime

## Burst Compatibility

### Ensuring Burst Can Compile Hot Path
**Burst-compatible**: Value type structs in NativeArray for hot data (ProvinceState)
**NOT Burst-compatible**: Managed references (List, Dictionary) for cold data

Key principle: Hot data must be in NativeArray for Burst. Cold data can use managed collections since it's accessed rarely.

## Usage Examples

### Loading Process
Complete loading process: Load all game data → Link with IDs → Runtime uses IDs only. No string comparisons at runtime.

### Modding Support
Mods can add new entities or overwrite base game data. Reference resolution runs again for mod data. Re-validate after mod loading to catch conflicts.

## Best Practices

1. **Never use strings at runtime** - Convert everything to IDs during loading
2. **Validate early and often** - Catch bad references during loading, not gameplay
3. **Use ushort for IDs** - Simpler than typed wrappers unless you have complex cross-referencing
4. **Dense arrays over dictionaries** - Array indexing is much faster (unless IDs are very sparse)
5. **Separate hot/cold data** - Compact hot structs in NativeArray, cold data in Dictionary
6. **Build reverse lookups once** - Don't search arrays repeatedly (O(n) → O(1))
7. **Reserve 0 for "none"** - Makes checking for unset values easy
8. **Log all resolution failures** - Help modders debug their data
9. **Support partial loading** - Allow game to run with some missing optional data
10. **Keep hot data Burst-compatible** - NativeArray with value types only

## Summary

This linking architecture ensures:
- **Type safety** through ID-based references
- **Performance** through array indexing instead of string lookups
- **Validation** catches all bad references at load time
- **Flexibility** for mods to extend base game data
- **Memory efficiency** through compact structs and separate cold data
- **Burst compatibility** through NativeArray and value types
- **Clear error messages** for debugging data issues

The key is the three-phase approach: discover entities, load raw data with strings, then resolve strings to IDs. This allows handling forward references, validating everything, and converting to efficient runtime representations.

## Related Documents

- [data-flow-architecture.md](data-flow-architecture.md) - System communication and bidirectional mappings
- [performance-architecture-guide.md](performance-architecture-guide.md) - Memory layout and cache optimization
- [../Planning/modding-design.md](../Planning/modding-design.md) - Mod system uses same reference resolution

---

*Last Updated: 2025-10-15*
