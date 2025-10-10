# Grand Strategy Game - Data Flow & System Architecture

**📊 Implementation Status:** ✅ Implemented (Command pattern ✅, EventBus zero-allocation ✅)

**🔄 Recent Update (2025-10-09):** ProvinceState refactored for engine-game separation. Game-specific fields (`development`, `fortLevel`, `flags`) moved to `HegemonProvinceData` in the game layer. Examples updated to show dual-layer architecture. See [phase-3-complete-scenario-loader-bug-fixed.md](../Log/2025-10/2025-10-09/phase-3-complete-scenario-loader-bug-fixed.md) for complete refactoring details.

> **📚 Architecture Context:** This document describes system communication patterns. See [master-architecture-document.md](master-architecture-document.md) for overall architecture.

## Executive Summary
**Question**: How do all these systems actually connect and communicate?
**Answer**: Hub-and-spoke architecture with specialized systems and a central game state
**Key Principle**: Each system owns its data, GameState provides unified access
**Performance**: Zero allocations during gameplay, fast query response

## The Big Picture - System Architecture

```
┌─────────────────────────────────────────────────────────┐
│                     GAME MANAGER                         │
│  (Orchestrates everything, owns the game loop)           │
└────────┬──────────────────────────────────┬──────────────┘
         │                                  │
         ▼                                  ▼
┌──────────────────┐              ┌──────────────────────┐
│   GAME STATE     │◄────────────►│   TIME MANAGER       │
│ (Central truth)  │              │ (Controls ticks)     │
└────────┬─────────┘              └──────────────────────┘
         │
    ┌────┴────┬──────────┬─────────┬──────────┬──────────┐
    ▼         ▼          ▼         ▼          ▼          ▼
┌────────┐ ┌────────┐ ┌────────┐ ┌────────┐ ┌────────┐ ┌────────┐
│Province│ │ Nation │ │Military│ │Economic│ │  AI    │ │Diplo   │
│ System │ │ System │ │ System │ │ System │ │ System │ │System  │
└────────┘ └────────┘ └────────┘ └────────┘ └────────┘ └────────┘
```

## Core Design Principles

### 1. Single Source of Truth
There is ONE authoritative place for each piece of data. No duplicates, no confusion.

```csharp
// GOOD: Province ownership lives in one place
public class ProvinceSystem {
    private NativeArray<ProvinceState> provinces;  // THE authority on ownership

    public ushort GetOwner(ushort provinceId) => provinces[provinceId].ownerID;

    public void SetOwner(ushort provinceId, ushort nation) {
        var state = provinces[provinceId];
        state.ownerID = nation;
        provinces[provinceId] = state;

        EventBus.Emit(new ProvinceOwnershipChanged(provinceId, nation));
    }
}

// BAD: Multiple systems with separate copies of ownership
public class BadDesign {
    // Province system has ownership
    byte[] provinceOwners;

    // Nation system ALSO tracks ownership separately
    List<ushort>[] provincesByNation;

    // Problem: These can get out of sync!
    // Update one, forget to update the other = bug
}
```

### 2. Systems Own Their Domain
Each system is responsible for its own data and logic. Other systems ask, don't touch.

```csharp
// Economic system owns all economic data
public class EconomicSystem {
    private FixedPoint64[] provinceTax;
    private FixedPoint64[] nationTreasury;

    // Only Economic system can modify treasury
    public void AddGold(ushort nation, FixedPoint64 amount) {
        nationTreasury[nation] += amount;

        // Economic system handles all side effects
        CheckBankruptcy(nation);
        UpdateInflation(nation);
    }

    // Others can only read
    public FixedPoint64 GetTreasury(ushort nation) => nationTreasury[nation];
}
```

### 3. Bidirectional Mappings Are Good (When Encapsulated)
Forward and reverse lookups are necessary for performance. The key is WHO owns them.

```csharp
// ✅ GOOD: Province system owns both mappings
public class ProvinceSystem {
    // Forward: Province → Owner
    private NativeArray<ProvinceState> provinces;  // Source of truth

    // Reverse: Owner → Provinces (cached index)
    private List<ushort>[] provincesByOwner;  // Derived from provinces

    public void SetOwner(ushort province, ushort newOwner) {
        ushort oldOwner = provinces[province].ownerID;

        // Update source of truth
        var state = provinces[province];
        state.ownerID = newOwner;
        provinces[province] = state;

        // Update cached index
        provincesByOwner[oldOwner].Remove(province);
        provincesByOwner[newOwner].Add(province);

        EventBus.Emit(new ProvinceOwnershipChanged(province, oldOwner, newOwner));
    }

    // Fast queries in both directions
    public ushort GetOwner(ushort province) => provinces[province].ownerID;
    public List<ushort> GetNationProvinces(ushort nation) => provincesByOwner[nation];
}
```

**Why this is necessary:**
- Without reverse mapping, queries require full iteration (O(n))
- With reverse mapping, it's O(1) array lookup
- The key: ONE system owns BOTH mappings and keeps them synchronized

### 4. Communication Patterns: Events vs Direct Calls

**Use events for:**
- Cross-system notifications (province ownership changed)
- Multiple independent listeners (UI, AI, stats tracking)
- Loose coupling between systems
- Optional listeners (modding, debugging)

**Use direct calls for:**
- Same-system operations
- Performance-critical paths
- Required dependencies
- Clear data flow

```csharp
// Events: Multiple listeners, loose coupling
public void SetProvinceOwner(ushort province, ushort newOwner) {
    // Update state
    provinces[province].ownerID = newOwner;

    // Event allows many systems to react
    EventBus.Emit(new ProvinceOwnershipChanged(province, newOwner));
    // → Economic system updates taxes
    // → Military system updates manpower
    // → AI system re-evaluates goals
    // → UI shows notification
}

// Direct calls: Required dependency, performance-critical
public FixedPoint64 CalculateProvinceIncome(ushort province) {
    var engineState = provinces[province];        // Engine layer data
    var gameState = hegemonData[province];        // Game layer data

    // Direct calls - we NEED this data immediately
    FixedPoint64 baseTax = economicSystem.GetBaseTax(gameState.development);
    FixedPoint64 terrainMod = terrainSystem.GetModifier(engineState.terrainType);

    return baseTax * terrainMod;
}
```

## The Central GameState

### What It Is
GameState is the central hub that provides unified access to all game data. It doesn't OWN the data, it COORDINATES access to it.

```csharp
public class GameState {
    // References to all systems
    public readonly ProvinceSystem Provinces;
    public readonly NationSystem Nations;
    public readonly MilitarySystem Military;
    public readonly EconomicSystem Economy;
    public readonly DiplomaticSystem Diplomacy;
    public readonly AISystem AI;
    public readonly TimeManager Time;

    // Convenient unified access
    public ushort GetProvinceOwner(ushort province) {
        return Provinces.GetOwner(province);
    }

    public FixedPoint64 GetNationTreasury(ushort nation) {
        return Economy.GetTreasury(nation);
    }

    // Complex queries that span systems
    public FixedPoint64 GetNationTotalTax(ushort nation) {
        FixedPoint64 total = FixedPoint64.Zero;
        foreach (var province in Provinces.GetNationProvinces(nation)) {
            total += Economy.GetProvinceTax(province);
        }
        return total;
    }
}
```

### What It Doesn't Do
GameState is NOT a god object. It doesn't contain business logic. Complex operations belong in systems or commands.

```csharp
// ❌ BAD: GameState doing too much
public class BadGameState {
    public void ConquerProvince(ushort province, ushort newOwner) {
        // Don't put complex logic here!
        provinces[province].owner = newOwner;

        // All this logic should be in systems or commands
        UpdateTaxes();
        RecalculateManpower();
        CheckForFormableNations();
        UpdateTradeRoutes();
        ValidateAlliances();
        RecalculateSupply();
        // This becomes unmaintainable!
    }
}

// ✅ GOOD: Use command pattern for complex operations
public class ConquerProvinceCommand : ICommand {
    public ushort provinceId;
    public ushort newOwner;

    public void Execute(GameState state) {
        // Simple: just change owner
        state.Provinces.SetOwner(provinceId, newOwner);

        // Event system triggers updates in each relevant system
        // Each system handles its own logic
    }
}
```

## Startup Flow - The Loading Screen

Game initialization follows a sequential pipeline optimized for minimal load time:

**Loading Pipeline:**
1. Core Systems - Instantiate managers, event bus
2. Static Data - Parallel load all definitions
3. Map Data - GPU texture generation
4. Scenario/Save - Initialize game state
5. Cross-References - Build derived data structures
6. AI Initialization - Per-nation AI setup
7. Final Prep - Cache warmup, UI setup

**Key Performance**: Fast loading with parallel processing.

## Data Access Patterns

### Reading Data - The Query Pattern
Three query types based on performance needs:
- **Simple queries**: Direct array access (instant)
- **Computed queries**: Calculate on-demand from multiple sources
- **Cached queries**: Frame-coherent caching for expensive calculations

```csharp
// Simple query: Direct array access
public ushort GetProvinceOwner(ushort province) {
    return provinces[province].ownerID;
}

// Computed query: Calculate from multiple sources
public FixedPoint64 GetProvinceIncome(ushort province) {
    var engineState = provinces[province];
    var gameState = hegemonData[province];
    return economicSystem.CalculateIncome(gameState.development, engineState.terrainType);
}

// Cached query: Expensive calculation cached per-frame
private Dictionary<ushort, FixedPoint64> armyStrengthCache = new();
private int cacheFrame = -1;

public FixedPoint64 GetNationArmyStrength(ushort nation) {
    // Clear cache if new frame
    if (Time.frameCount != cacheFrame) {
        armyStrengthCache.Clear();
        cacheFrame = Time.frameCount;
    }

    // Return cached or calculate
    if (!armyStrengthCache.TryGetValue(nation, out var strength)) {
        strength = CalculateArmyStrength(nation);  // Expensive!
        armyStrengthCache[nation] = strength;
    }

    return strength;
}
```

### Writing Data - The Command Pattern
All state changes use commands for validation, event emission, multiplayer sync, and replay support.

```csharp
public interface ICommand {
    bool Validate(GameState state);
    void Execute(GameState state);
    void Serialize(BinaryWriter writer);
    uint CalculateChecksum();  // For determinism verification
}

// Example: Province ownership change
public struct ChangeProvinceOwnerCommand : ICommand {
    public ushort provinceId;
    public ushort newOwner;

    public bool Validate(GameState state) {
        // Check province exists
        if (provinceId >= state.Provinces.Count) return false;

        // Check nation exists
        if (newOwner >= state.Nations.Count) return false;

        // Game-specific validation
        var province = state.Provinces.Get(provinceId);
        if (!state.Diplomacy.IsAtWar(province.ownerID, newOwner)) {
            return false;  // Can't conquer without war
        }

        return true;
    }

    public void Execute(GameState state) {
        state.Provinces.SetOwner(provinceId, newOwner);
        // SetOwner emits ProvinceOwnershipChanged event
    }

    public void Serialize(BinaryWriter writer) {
        writer.Write(provinceId);
        writer.Write(newOwner);
    }

    public uint CalculateChecksum() {
        return (uint)provinceId ^ ((uint)newOwner << 16);
    }
}
```

**Command Benefits:**
- **Multiplayer**: Send commands over network instead of full state
- **Save/Load**: Replay commands for time-travel or replay system
- **Validation**: Centralized validation before state changes
- **Determinism**: Commands are deterministic, float calculations are not
- **Debugging**: Log all commands to reproduce bugs

See [../Planning/save-load-design.md](../Planning/save-load-design.md) *(not implemented)* for serialization details and [../Planning/multiplayer-design.md](../Planning/multiplayer-design.md) *(not implemented)* for network synchronization.

### Deleting Data - Pooling Pattern
Pre-allocated object pools prevent runtime allocations. Objects are recycled, not destroyed.

```csharp
public class ArmySystem {
    private Army[] armyPool = new Army[MAX_ARMIES];
    private Stack<int> freeArmies = new Stack<int>();

    public int CreateArmy(ushort nation) {
        if (freeArmies.Count == 0) {
            Debug.LogError("Army pool exhausted!");
            return -1;
        }

        int armyId = freeArmies.Pop();
        armyPool[armyId].Reset(nation);
        return armyId;
    }

    public void DestroyArmy(int armyId) {
        armyPool[armyId].Clear();
        freeArmies.Push(armyId);
    }
}
```

## Event System Design

### Zero-Allocation Event System with Frame-Coherent Batching

**Architecture Goal:** Type-safe events with **zero allocations** during gameplay and frame-coherent processing.

**Key Innovation:** `EventQueue<T>` wrapper pattern avoids boxing struct events.

```csharp
// Event types are structs for zero allocation
public struct ProvinceOwnershipChanged : IGameEvent {
    public readonly ushort provinceId;
    public readonly ushort oldOwner;
    public readonly ushort newOwner;

    public ProvinceOwnershipChanged(ushort provinceId, ushort oldOwner, ushort newOwner) {
        this.provinceId = provinceId;
        this.oldOwner = oldOwner;
        this.newOwner = newOwner;
    }
}

// EventBus with zero-allocation architecture
public class EventBus {
    // Type-specific event queues (no boxing!)
    private readonly Dictionary<Type, IEventQueue> eventQueues = new();

    // Internal interface for polymorphism without boxing
    private interface IEventQueue {
        int ProcessEvents();
        void Clear();
        int Count { get; }
    }

    // Type-specific wrapper - keeps Queue<T> and Action<T> as concrete types
    private class EventQueue<T> : IEventQueue where T : struct, IGameEvent {
        private readonly Queue<T> eventQueue = new();
        private Action<T> listeners;

        public void Subscribe(Action<T> listener) {
            listeners += listener;  // Multicast delegate
        }

        public void Enqueue(T gameEvent) {
            eventQueue.Enqueue(gameEvent);  // NO BOXING - T stays T
        }

        public int ProcessEvents() {
            int processed = 0;
            while (eventQueue.Count > 0) {
                var evt = eventQueue.Dequeue();  // NO BOXING
                listeners?.Invoke(evt);          // Direct Action<T> call
                processed++;
            }
            return processed;
        }

        public void Clear() => eventQueue.Clear();
        public int Count => eventQueue.Count;
    }

    public void Subscribe<T>(Action<T> listener) where T : struct, IGameEvent {
        var type = typeof(T);
        if (!eventQueues.TryGetValue(type, out var queue)) {
            var newQueue = new EventQueue<T>();
            eventQueues[type] = newQueue;
            queue = newQueue;
        }
        ((EventQueue<T>)queue).Subscribe(listener);
    }

    public void Emit<T>(T gameEvent) where T : struct, IGameEvent {
        var type = typeof(T);
        if (!eventQueues.TryGetValue(type, out var queue)) {
            var newQueue = new EventQueue<T>();
            eventQueues[type] = newQueue;
            queue = newQueue;
        }
        ((EventQueue<T>)queue).Enqueue(gameEvent);
    }

    // Called once per frame from GameState.Update()
    public void ProcessEvents() {
        // Take snapshot to allow new events during processing
        queuesToProcess.Clear();
        foreach (var queue in eventQueues.Values) {
            queuesToProcess.Add(queue);
        }

        // Process all queued events
        for (int i = 0; i < queuesToProcess.Count; i++) {
            queuesToProcess[i].ProcessEvents();
        }
    }
}

// Usage
EventBus.Subscribe<ProvinceOwnershipChanged>(OnProvinceOwnershipChanged);

void OnProvinceOwnershipChanged(ProvinceOwnershipChanged evt) {
    // Type-safe, ZERO allocation
    UpdateProvinceLists(evt.provinceId, evt.oldOwner, evt.newOwner);
}
```

**Why This Pattern Works:**
- **No Boxing:** `EventQueue<T>` stores concrete `Queue<T>` and `Action<T>` types
- **Virtual Calls Don't Box:** `IEventQueue.ProcessEvents()` is a virtual method call, which doesn't box value types
- **Frame-Coherent:** Events queued during frame, processed once per frame via `GameState.Update()`
- **Type Safety:** Compile-time type checking via generics

**Performance Results:**
- Dramatic reduction in allocations
- Significant performance improvement
- Near-zero allocation pattern achieved

### Event Ordering and Guarantees

**Ordering rules:**
1. Events are **queued** during frame, processed once per frame
2. Processing order: Type registration order (deterministic within type)
3. Events emitted during `ProcessEvents()` are queued for next frame
4. All listeners for a type execute before next type processes

**Frame-Coherent Processing:**
```csharp
// GameState.Update() - called every frame
void Update() {
    if (!IsInitialized) return;

    // Process all queued events once per frame
    EventBus?.ProcessEvents();
}
```
```

### When NOT to Use Events

**Avoid events for:**
- Performance-critical paths (direct calls are faster)
- Required dependencies (explicit is better than implicit)
- Same-system communication (methods are clearer)
- Return values needed (events don't return values)

```csharp
// ❌ BAD: Event for required dependency
public FixedPoint64 CalculateIncome(ushort province) {
    FixedPoint64 result = FixedPoint64.Zero;
    EventBus.Emit(new CalculateIncomeRequest(province));
    // How do we get result back? Events don't return values!
    return result;
}

// ✅ GOOD: Direct call for required dependency
public FixedPoint64 CalculateIncome(ushort province) {
    var engineState = provinces[province];
    var gameState = hegemonData[province];
    return economicSystem.CalculateIncome(gameState.development, engineState.terrainType);
}
```

### Anti-Patterns to Avoid

**❌ ANTI-PATTERN: Interface-Typed Collections for Value Types**
Storing structs in interface-typed collections causes boxing:
- Queue of interface type boxes every struct
- Massive allocations for many events
- Performance degradation

**❌ ANTI-PATTERN: Reflection for Hot Path Operations**
Using reflection in event processing causes boxing:
- Reflection always boxes value type parameters
- Significant performance overhead
- Avoid in hot paths

**✅ CORRECT: EventQueue<T> Wrapper Pattern**
Pattern that avoids boxing:
- Interface with virtual methods
- Concrete generic queue type
- Virtual calls don't box value types
- Direct delegate invocation

**Key Principle:** When you need polymorphic storage of generic types without boxing, use **interface with virtual methods**, not **interface-typed collections**.

## The Game Loop

```csharp
public class GameManager {
    private const float FIXED_TIMESTEP = 1f / 60f;

    public void GameLoop() {
        while (isRunning) {
            ProcessInput();                    // Immediate (create commands)
            FixedUpdate(FIXED_TIMESTEP);       // Deterministic simulation
            Render(Time.deltaTime);            // As fast as possible
        }
    }

    private void FixedUpdate(float dt) {
        TimeManager.Tick(dt);                  // Triggers daily/monthly updates
        CommandProcessor.ProcessQueue();       // Execute player/AI commands
        UpdateDirtySystems();                  // Only update what changed
    }

    private void UpdateDirtySystems() {
        // Each system tracks its own dirty flags
        if (provinceSystem.IsDirty()) {
            provinceSystem.Update();
        }

        if (economicSystem.IsDirty()) {
            economicSystem.Update();
        }

        // GPU updates only for dirty provinces
        if (provinceSystem.HasDirtyProvinces()) {
            mapRenderer.UpdateProvinces(provinceSystem.GetDirtyProvinces());
        }
    }
}
```

See [time-system-architecture.md](time-system-architecture.md) for details on tick-based update scheduling and time progression.

## Command Queue and Determinism

### Command Processing

```csharp
public class CommandProcessor {
    private Queue<ICommand> commandQueue = new();
    private List<ICommand> executedCommands = new();  // For replay/debugging

    public void EnqueueCommand(ICommand command) {
        if (command.Validate(gameState)) {
            commandQueue.Enqueue(command);
        } else {
            Debug.LogWarning($"Command validation failed: {command}");
        }
    }

    public void ProcessQueue() {
        while (commandQueue.Count > 0) {
            var command = commandQueue.Dequeue();

            // Execute command
            command.Execute(gameState);

            // Store for debugging/replay
            executedCommands.Add(command);

            // Calculate checksum for determinism verification
            if (isMultiplayer) {
                uint checksum = command.CalculateChecksum();
                VerifyChecksum(checksum);
            }
        }
    }
}
```

### Determinism Requirements

**For multiplayer, every command MUST:**
1. Use fixed-point math (FixedPoint64), never floats
2. Process in identical order on all clients
3. Have identical game state as input
4. Produce identical output (verified by checksum)

```csharp
// ❌ BAD: Non-deterministic (uses float, random)
public void CalculateCombat(ushort army1, ushort army2) {
    float roll = Random.value;  // Different on each client!
    float damage = roll * army1.strength;
    army2.health -= damage;
}

// ✅ GOOD: Deterministic (fixed-point, seeded random)
public void CalculateCombat(ushort army1, ushort army2, uint seed) {
    SeededRandom rng = new SeededRandom(seed);  // Same seed = same result
    FixedPoint64 roll = rng.NextFixed();
    FixedPoint64 damage = roll * armies[army1].strength;
    armies[army2].health -= damage;
}
```

## Memory Management Strategy

### Pre-Allocation

Pre-allocate all objects at startup to avoid runtime allocations:
- Global object pools for common types
- Fixed-size arrays initialized on startup
- Never allocate during gameplay
- Reuse pooled objects instead of creating new ones

### Memory Layout for Core Simulation

**Engine Layer:** Compact structs in contiguous arrays
- Generic primitive fields
- Fixed-size for cache efficiency
- Native array storage

**Game Layer:** Game-specific hot data
- Separate but parallel to engine data
- Compact representation
- Native array storage

**Derived Indices:** Managed collections for reverse lookups
- Owner to provinces mapping
- Other categorical indices

**Cold Data:** Separate storage loaded on-demand

See [performance-architecture-guide.md](performance-architecture-guide.md) for memory layout optimization strategies.

## Error Handling & Validation

### Validation at System Boundaries

```csharp
public class ValidationLayer {
    // Validate at entry points
    public bool TryConquerProvince(ushort province, ushort nation) {
        // Validate inputs
        if (province >= ProvinceCount) {
            LogError($"Invalid province {province}");
            return false;
        }

        if (nation >= NationCount) {
            LogError($"Invalid nation {nation}");
            return false;
        }

        // Check game rules
        var owner = GetProvinceOwner(province);
        if (!IsAtWar(owner, nation)) {
            LogError("Can't conquer province when not at war");
            return false;
        }

        // If valid, enqueue command
        var command = new ConquerProvinceCommand { provinceId = province, newOwner = nation };
        commandProcessor.EnqueueCommand(command);
        return true;
    }
}
```

## Performance Monitoring

### Built-in Profiling

Track system performance metrics:
- Record update times per system
- Calculate running averages
- Periodic logging of metrics
- Warn when systems exceed thresholds

Pattern allows identifying performance bottlenecks early.

## Summary - The Complete Flow

1. **Startup**: Load data → Build cross-references → Initialize systems → Warm caches
2. **Game Loop**: Input → Time tick → Process commands → Update dirty → Render
3. **Data Access**: Always through systems, never direct
4. **Changes**: Through commands for validation and sync
5. **Communication**: Events for cross-system notifications, direct calls for dependencies
6. **Performance**: Pool objects, cache queries, update only what changes
7. **Determinism**: Fixed-point math, command checksums, seeded random

The key is that each system is independent but coordinated through GameState and EventBus. This gives you:
- **Clean separation** - Easy to understand and modify
- **Performance** - Each system optimized independently
- **Multiplayer-ready** - Commands can be networked
- **Deterministic** - Fixed-point math and command checksums
- **Maintainable** - Clear ownership and responsibilities

## Key Architectural Decisions

### Why Bidirectional Mappings?
Without reverse lookups, asking "What provinces does France own?" requires O(n) iteration. With cached indices, it's O(1). The key is encapsulation: ONE system owns BOTH mappings.

### Why Events AND Direct Calls?
Events decouple systems but add overhead. Use events for notifications, direct calls for dependencies. Profile and choose appropriately.

### Why Commands?
Commands enable multiplayer sync, replay systems, validation, and debugging. The overhead is minimal compared to the benefits.

### Why Fixed-Point Math?
Floats produce different results on different CPUs/compilers. Fixed-point math is deterministic across all platforms, essential for multiplayer.

## Related Documentation
- [time-system-architecture.md](time-system-architecture.md) - Tick-based update scheduling and game speed control
- [performance-architecture-guide.md](performance-architecture-guide.md) - Memory layout optimization and cache efficiency
- [data-linking-architecture.md](data-linking-architecture.md) - Reference resolution and ID mapping
- [../Planning/save-load-design.md](../Planning/save-load-design.md) - Serialization, persistence, and replay system *(not implemented)*
- [../Planning/multiplayer-design.md](../Planning/multiplayer-design.md) - Network synchronization and command distribution *(not implemented)*

---

*Last Updated: 2025-10-10*
