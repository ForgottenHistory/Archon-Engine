# Architecture Patterns

**Purpose:** Catalog of proven architectural patterns used throughout the Archon Engine
**Status:** Production Standard
**Last Updated:** 2026-01-12

---

## Overview

This document catalogs the 21 core architectural patterns that make Archon Engine scalable, maintainable, and reusable. Each pattern solves specific problems encountered in grand strategy game development.

**Why Patterns Matter:**
- Consistent solutions to recurring problems
- Faster development (don't reinvent the wheel)
- Easier onboarding (recognizable patterns)
- Better code quality (battle-tested approaches)

---

## Pattern 1: Engine-Game Separation (Mechanism vs Policy)

**Principle:** Engine provides mechanisms (HOW), Game defines policy (WHAT)

**Example:**
- **Engine:** `ProvinceSystem.GetProvinceState()` / `SetProvinceState()` - generic primitives
- **Game:** `EconomySystem.CalculateTax()` - uses engine primitives, defines tax formula

**Benefits:**
- Engine reusable across different games
- Clear separation of concerns
- Can build space strategy, fantasy, or modern games on same engine

**Decision Doc:** [engine-game-separation.md](engine-game-separation.md)

---

## Pattern 2: Command Pattern for State Changes

**Principle:** ALL state modifications flow through commands for validation, networking, and replay

**Interface:** `ICommand` with Validate(), Execute(), GetChecksum(), Serialize()

**Benefits:**
- Multiplayer sync (send commands not state)
- Replay support (store command log)
- Save/load (command history)
- Validation before execution
- Undo support

**Implementation:**
- `CommandProcessor` - Deterministic execution order
- `CommandBuffer` - Ring buffer for rollback
- `CommandLogger` - History tracking

**Example:** `ChangeOwnerCommand`, `BuildBuildingCommand`, `DeclareWarCommand`

**Decision Doc:** [data-flow-architecture.md](data-flow-architecture.md)

---

## Pattern 3: Event-Driven Architecture (Zero-Allocation)

**Principle:** Systems communicate through EventBus with zero allocations during gameplay

**Implementation:**
- `EventBus` - Decoupled system communication
- `EventQueue<T>` - Wrapper avoids boxing (no interface-typed collections)
- Frame-coherent processing in `GameState.Update()`

**Use Events For:**
- Cross-system notifications (province ownership changed)
- Multiple listeners (UI + AI + game systems)
- Loose coupling

**Use Direct Calls For:**
- Required dependencies
- Performance-critical paths
- Same-system operations

**Example:** UI subscribes via `gameState.EventBus.Subscribe<ProvinceOwnershipChangedEvent>(handler)`

**Decision Doc:** [data-flow-architecture.md](data-flow-architecture.md)

---

## Pattern 4: Hot/Cold Data Separation

**Principle:** Separate data by access frequency, not importance

**Hot Data (every frame):**
- Fixed-size structs in NativeArray
- Cache-line aligned
- Minimal fields
- Example: ProvinceState (8 bytes)

**Warm Data (occasional):**
- Can stay in simulation struct if space permits

**Cold Data (rare):**
- Separate dictionaries
- Loaded on-demand
- Can page to disk
- Example: ProvinceColdData (name, color, bounds, history)

**Benefits:**
- Cache-friendly access patterns
- Reduced memory footprint
- Scalable to large datasets

**Decision Doc:** [data-flow-architecture.md](data-flow-architecture.md)

---

## Pattern 5: Fixed-Point Determinism

**Principle:** ALL simulation math uses FixedPoint64 for cross-platform determinism

**Rules:**
- NEVER: float, double, decimal in simulation (Core namespace)
- ALWAYS: FixedPoint64 with exact fractions
- Use `FromFraction(1, 2)` NOT `FromFloat(0.5)`
- 360-day calendar (no leap years)

**Why:**
- Float operations non-deterministic across CPUs/platforms
- Breaks multiplayer and replays
- FixedPoint64 guarantees identical results everywhere

**Decision Doc:** [../Log/decisions/fixed-point-determinism.md](../Log/decisions/fixed-point-determinism.md)

---

## Pattern 6: Facade Pattern for System Organization

**Principle:** High-level coordinator delegates to specialized components

**Examples:**
- `ProvinceSystem` → DataManager + StateLoader + HistoryDatabase
- `EconomySystem` → TaxManager + IncomeCache + ManpowerManager + TreasuryBridge
- `BuildingConstructionSystem` → ValidationManager + CostManager + ConstructionQueue + CompletionHandler

**Benefits:**
- Single-responsibility components
- Clear separation of concerns
- Easier testing (test components independently)
- Unified API (facade provides clean interface)

**Pattern:** Facade owns runtime state, delegates operations to stateless managers

---

## Pattern 7: Registry Pattern for Data Management

**Principle:** Central registries for definitions with bidirectional lookup

**Pattern:** String ID ↔ Numeric ID mapping with fast lookups

**Examples:**
- `CountryRegistry` - "ENG" ↔ CountryId(1)
- `BuildingRegistry` - "farm" ↔ BuildingId(5)
- `ResourceRegistry` - "gold" ↔ ResourceType enum

**Benefits:**
- Auto-discovery (reflection-based factory registration)
- Type safety (strongly-typed ID wrappers prevent confusion)
- Fast lookups (both directions)

---

## Pattern 8: Sparse Collection for Scale-Safe Storage

**Principle:** Only store what exists, not what could exist

**Implementation:** `NativeParallelMultiHashMap` for one-to-many relationships

**Example:**
- Buildings per province: Store only actual buildings (200KB) not possible buildings (5MB dense array)
- Equipment per division: Prevents HOI4's equipment disaster

**Benefits:**
- Memory scales with actual items, not possible items
- Pre-allocated fixed capacity
- Warn at 80%/95% usage

**Decision Doc:** [sparse-data-structures-design.md](sparse-data-structures-design.md)

---

## Pattern 9: Double-Buffer for Zero-Blocking Reads

**Principle:** Simulation writes one buffer while UI reads the other

**Pattern:** Two state buffers, O(1) pointer swap after tick

**Memory Cost:** 2x hot data (acceptable for zero blocking)

**Performance:**
- Zero blocking
- No memcpy overhead
- Victoria 3 learned this the hard way

**Example:** `GameStateSnapshot` - UI reads stable snapshot while simulation updates next frame

---

## Pattern 10: Frame-Coherent Caching

**Principle:** Cache expensive calculations per frame, clear when frame changes

**Pattern:** Dictionary cache + frame counter, clear on frame mismatch

**Use Case:**
- UI queries (tooltip content called multiple times per frame)
- Complex calculations (income with all modifiers)
- Avoid redundant work

**Example:** `EconomyIncomeCache` - Compute province income once, reuse within frame

**Benefits:**
- Compute once per frame
- Reuse across multiple queries
- Automatic invalidation

---

## Pattern 11: Dirty Flag System

**Principle:** Only update what changed, clear flags each frame

**Pattern:**
- Track modified entities in bitmask or list
- Batch GPU updates
- Single upload per frame
- Clear flags after processing

**Benefits:**
- Minimize redundant work
- Efficient GPU updates
- Avoid update-everything-every-frame anti-pattern

**Example:** `MapTextureManager` - Only update changed provinces in owner texture

---

## Pattern 12: Pre-Allocation Policy (Zero Allocations)

**Principle:** Allocate at initialization, clear and reuse during gameplay, zero allocations in hot paths

**HOI4 Lesson:** Malloc lock destroys parallelism when threads allocate

**Rules:**
- **Initialization:** Allocate persistent buffers sized for worst-case
- **Gameplay:** Clear buffers (cheap), reuse existing allocations, zero new memory
- **Enforcement:** Profiler verification, any allocation in hot path = critical bug

**Example:** Command buffers, event queues, ring buffers all pre-allocated

**Decision Doc:** [performance-architecture-guide.md](performance-architecture-guide.md)

---

## Pattern 13: Load-Balanced Scheduling

**Principle:** Split expensive/affordable workloads for optimal parallelism

**Victoria 3 Pattern:** Threshold-based work distribution

**Benefits:**
- 24.9% improvement at 10k provinces in our tests
- Prevents thread starvation
- Better CPU utilization

**Use Case:** Parallel job distribution with variable cost per item

---

## Pattern 14: Hybrid Save/Load Architecture

**Principle:** State snapshot for speed, command log for verification

**Components:**
- **Snapshot:** Full state for instant loading
- **Command Log:** Replay for determinism verification (dev/testing only)
- **Post-Load:** Rebuild derived data (indices, caches, GPU textures)

**Layer Separation:** ENGINE saves core state, GAME layer callbacks for finalization

**Decision Doc:** [save-load-architecture.md](save-load-architecture.md)

---

## Pattern 15: Phase-Based Initialization

**Principle:** Sequential pipeline with clear dependencies and progress reporting

**Pattern:** `IInitializationPhase` interface, `InitializationContext` for shared state

**Phases:**
1. Core Systems (0-5%)
2. Static Data (5-15%)
3. Province Data (15-40%)
4. Country Data (40-60%)
5. Reference Linking (60-65%)
6. Scenario Loading (65-75%)
7. Systems Warmup (75-100%)

**Benefits:**
- Parallel loading where possible
- Clear progress tracking
- Error recovery
- Dependency management

**Decision Doc:** [data-loading-architecture.md](data-loading-architecture.md)

---

## Pattern 16: Bidirectional Mapping Encapsulation

**Principle:** One system owns BOTH forward and reverse lookups

**Example:**
- `ProvinceSystem` owns province→owner (ProvinceState) AND owner→provinces (NativeParallelMultiHashMap)
- Both updated together in same system
- Single source of truth

**Why:**
- Forward + reverse needed for performance
- Single owner prevents desync
- Clear responsibility

**Never:** Multiple systems with separate copies of same relationship

---

## Pattern 17: Single Source of Truth

**Principle:** ONE authoritative place for each piece of data, no duplicates

**Rules:**
- System owns its domain data
- Others query via API
- Derived data rebuilt from authoritative source

**Benefits:**
- No sync bugs
- Clear ownership
- Single update point

**Example:** `ProvinceSystem` owns province state, everyone queries through it, never direct array access

---

## Pattern 18: Coordinator Pattern for Orchestration

**Principle:** High-level coordinator manages lifecycle of subsystems

**Examples:**
- `HegemonInitializer` - Master game initialization
- `MapInitializer` - Map subsystem initialization
- `SaveManager` - Save/load orchestration

**Responsibilities:**
- Order enforcement
- Error handling
- Progress reporting
- Phase coordination

**Benefits:**
- Clear initialization order
- Centralized error recovery
- Single place for sequencing logic

---

## Pattern 19: UI Presenter Pattern for Complex Panels

**Principle:** Separate UI coordination from presentation logic, user actions, and event management

**4-Component Pattern (simple panels):**
1. **View (MonoBehaviour)** - UI creation, show/hide, route clicks, manage subscriber
2. **Presenter (Static)** - Stateless data formatting, query game state, update UI elements
3. **ActionHandler (Static)** - Stateless user actions, validate and execute commands
4. **EventSubscriber (Instance)** - Subscribe/unsubscribe lifecycle, route events to callbacks

**5-Component Pattern (complex panels with >150 lines UI creation):**
1-4. Same as above
5. **UIBuilder (Static)** - Create UI elements, apply styling, wire callbacks

**Use When:**
- Panel >500 lines OR
- 3+ user actions OR
- Complex display logic

**Benefits:**
- View stays <500 lines
- Testable components (stateless helpers)
- Scales for grand strategy UI complexity

**Examples:**
- ProvinceInfoPanel (4 components)
- CountryInfoPanel (5 components with UIBuilder)

**Decision Doc:** [../Log/decisions/ui-presenter-pattern-for-panels.md](../Log/decisions/ui-presenter-pattern-for-panels.md)
**Architecture Doc:** [ui-architecture.md](ui-architecture.md)

---

## Pattern 20: Pluggable Implementation Pattern (Interface + Registry)

**Principle:** ENGINE provides interfaces + default implementations; GAME registers custom implementations via registry

**Components:**
1. **Interface** (e.g., `IBorderRenderer`, `IHighlightRenderer`) - Contract for implementations
2. **Base Class** (e.g., `BorderRendererBase`) - Common utilities, template methods
3. **Registry** (`MapRendererRegistry`) - Central registration and lookup by string ID
4. **Default Implementations** - ENGINE provides working defaults
5. **Configuration** (e.g., `VisualStyleConfiguration`) - References renderers by string ID

**Pattern Flow:**
```
ENGINE defines interface → ENGINE registers defaults → GAME registers customs → Config references by ID
```

**Example - Border Rendering:**
```csharp
// Interface (ENGINE)
public interface IBorderRenderer {
    string RendererId { get; }
    void GenerateBorders(BorderGenerationParams parameters);
}

// Default implementation (ENGINE)
public class DistanceFieldBorderRenderer : BorderRendererBase { ... }

// Custom implementation (GAME)
public class MyStylizedBorderRenderer : BorderRendererBase { ... }

// Registration (GAME initialization)
MapRendererRegistry.Instance.RegisterBorderRenderer(new MyStylizedBorderRenderer());

// Configuration (VisualStyleConfiguration asset)
borders.customRendererId = "MyStylized";  // References custom by ID
```

**Backwards Compatibility:**
- Empty `customRendererId` falls back to enum-based selection
- `GetEffectiveRendererId()` handles mapping

**When to Use:**
- ENGINE provides capability with reasonable defaults
- GAME may want completely different implementation
- Multiple valid approaches exist (not just parameter tweaks)
- Examples: Border rendering, highlight effects, fog visualization

**When NOT to Use:**
- Simple parameter customization (use VisualStyleConfiguration directly)
- No foreseeable need for custom implementations
- Performance-critical paths where interface indirection matters

**Benefits:**
- GAME extends without modifying ENGINE
- Clean separation of mechanism (ENGINE) and policy (GAME)
- Runtime switching between implementations
- Discoverable (registry lists available renderers)

**Current Implementations:**
- `IBorderRenderer` - Border generation (DistanceField, PixelPerfect, MeshGeometry, None)
- `IHighlightRenderer` - Selection/hover highlighting (Default)
- `IFogOfWarRenderer` - Fog of war visibility rendering (Default)
- `ITerrainRenderer` - Terrain blend map generation (Default 4-channel)
- `IMapModeColorizer` - Map mode colorization (Gradient 3-color)
- `IShaderCompositor` - Layer compositing (Default, Minimal, Stylized, Cinematic)

**Related Patterns:**
- Pattern 1 (Engine-Game Separation) - Philosophy this implements
- Pattern 7 (Registry) - Data lookup; this is implementation lookup
- Pattern 6 (Facade) - Often coordinates pluggable renderers

---

## Pattern 21: Auto-Discovery Factory Pattern

**Principle:** Interface + Attribute + Registry enables automatic discovery and ordered execution of factories

**Components:**
1. **Interface** (e.g., `ILoaderFactory`, `ICommandFactory`) - Contract for factories
2. **Metadata Attribute** (e.g., `[LoaderMetadata]`, `[CommandMetadata]`) - Name, priority, metadata
3. **Registry** (e.g., `LoaderRegistry`, `CommandRegistry`) - Auto-discovery via reflection, lookup, execution

**Pattern Flow:**
```
Define interface → Add attribute to implementations → Registry scans assemblies → Execute in priority order
```

**Example - Data Loaders:**
```csharp
// Interface (ENGINE)
public interface ILoaderFactory {
    void Load(LoaderContext context);
}

// Implementation with attribute
[LoaderMetadata("terrain", Priority = 10, Required = true)]
public class TerrainLoader : ILoaderFactory {
    public void Load(LoaderContext context) { ... }
}

// Auto-discovery and execution
var registry = new LoaderRegistry();
registry.DiscoverLoaders(Assembly.GetExecutingAssembly());
registry.DiscoverLoaders(gameAssembly);  // GAME layer loaders
registry.ExecuteAll(context);  // Priority order
```

**When to Use:**
- Multiple implementations of same interface
- GAME layer extends ENGINE capabilities
- Order matters (priority/dependencies)
- No manual wiring desired

**Benefits:**
- Zero manual registration (just add attribute)
- GAME layer adds implementations seamlessly
- Mod support (scan mod assemblies)
- Centralized error handling
- Priority-based ordering

**Current Implementations:**
- `LoaderRegistry` + `ILoaderFactory` - Data file loading
- `CommandRegistry` + `ICommandFactory` - Debug/console commands
- `AIGoalRegistry` + `[Goal]` attribute - AI goal discovery

**Related Patterns:**
- Pattern 7 (Registry) - Data lookup; this is factory lookup + execution
- Pattern 20 (Pluggable Implementation) - Similar but without auto-discovery

---

## Pattern Selection Guide

**Need to change game state?** → Pattern 2 (Command Pattern)

**Need cross-system notification?** → Pattern 3 (EventBus)

**Frequent + rare data together?** → Pattern 4 (Hot/Cold Separation)

**Need deterministic math?** → Pattern 5 (FixedPoint64)

**Complex system with many responsibilities?** → Pattern 6 (Facade)

**String IDs ↔ Numeric IDs?** → Pattern 7 (Registry)

**One-to-many with sparse data?** → Pattern 8 (Sparse Collection)

**UI reads while simulation updates?** → Pattern 9 (Double-Buffer)

**Expensive calculation called multiple times per frame?** → Pattern 10 (Frame-Coherent Cache)

**Only update changed data?** → Pattern 11 (Dirty Flags)

**Avoid allocations during gameplay?** → Pattern 12 (Pre-Allocation)

**Complex UI panel?** → Pattern 19 (UI Presenter)

**Need reusable engine?** → Pattern 1 (Engine-Game Separation)

**Forward + reverse lookup needed?** → Pattern 16 (Bidirectional Mapping)

**One authoritative data source?** → Pattern 17 (Single Source of Truth)

**Complex initialization sequence?** → Pattern 18 (Coordinator)

**GAME needs custom implementation of ENGINE capability?** → Pattern 20 (Pluggable Implementation)

**Auto-discover factories across assemblies?** → Pattern 21 (Auto-Discovery Factory)

---

## Anti-Patterns to Avoid

**❌ GameObject per province** - Use texture-based rendering instead

**❌ CPU pixel processing** - Use GPU compute shaders

**❌ Float in simulation** - Use FixedPoint64 for determinism

**❌ Unbounded data growth** - Use ring buffers with compression

**❌ Mixed hot/cold data** - Separate by access frequency

**❌ Update everything every frame** - Use dirty flags

**❌ Multiple owners of same data** - Single source of truth

**❌ Allocations in hot paths** - Pre-allocate everything

**❌ Direct array access** - Use system APIs

**❌ Circular dependencies** - Clear layer hierarchy (CORE → MAP → GAME)

---

## Related Documentation

**Architecture Documents:**
- [master-architecture-document.md](master-architecture-document.md) - Complete technical architecture
- [engine-game-separation.md](engine-game-separation.md) - Mechanism vs Policy philosophy
- [performance-architecture-guide.md](performance-architecture-guide.md) - Performance patterns in depth
- [ui-architecture.md](ui-architecture.md) - UI Toolkit principles and patterns

**Decision Records:**
- [../Log/decisions/fixed-point-determinism.md](../Log/decisions/fixed-point-determinism.md) - Why FixedPoint64
- [../Log/decisions/ui-presenter-pattern-for-panels.md](../Log/decisions/ui-presenter-pattern-for-panels.md) - UI pattern rationale

**System Guides:**
- [data-flow-architecture.md](data-flow-architecture.md) - Command and Event patterns
- [save-load-architecture.md](save-load-architecture.md) - Hybrid save/load pattern
- [sparse-data-structures-design.md](sparse-data-structures-design.md) - Sparse collections
- [data-loading-architecture.md](data-loading-architecture.md) - Phase-based initialization

**Learning Docs:**
- [../Log/learnings/](../Log/learnings/) - Lessons learned from implementation

---

*Last Updated: 2026-01-14*
*These patterns are battle-tested and production-ready. Use them.*
