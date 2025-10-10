# Grand Strategy Game - Time System & Update Architecture

**📊 Implementation Status:** ✅ Implemented (TimeManager exists, layered updates functional)

> **📚 Architecture Context:** See [performance-architecture-guide.md](performance-architecture-guide.md) for dirty flag patterns and [master-architecture-document.md](master-architecture-document.md) for overall architecture.

## Executive Summary
**Problem**: Traditional games update everything every tick, causing massive unnecessary calculations
**Solution**: Layered update frequencies with dirty flags - only update what changed
**Performance**: Dramatic reduction in calculations, maintains excellent performance in late game
**Key Insight**: Most game state doesn't change most of the time

**Core Principles**:
- Different systems update at different frequencies based on need
- Dirty flags track what actually changed
- Bucketing spreads expensive operations across time
- Cascade depth control prevents infinite loops
- Deterministic time for multiplayer compatibility

## Core Architecture: Update Layers

### Update Frequency Hierarchy
```csharp
public enum UpdateFrequency {
    Realtime    = 0,  // Every frame (~60 fps) - presentation only
    Hourly      = 1,  // 24 per day
    Daily       = 2,  // Game's base tick
    Weekly      = 3,  // 7 days
    Monthly     = 4,  // 30 days
    Quarterly   = 5,  // 3 months
    Yearly      = 6,  // 365 days
    OnDemand    = 7   // Only when triggered
}
```

### System Update Mapping
```
REALTIME (every frame - PRESENTATION ONLY):
├── Unit movement interpolation
├── Combat animations
├── Camera and input
└── UI updates only
⚠️  NO SIMULATION STATE CHANGES (breaks determinism)

HOURLY:
├── Active combat resolution
├── Siege progress
├── Weather transitions
└── Supply consumption (military only)

DAILY:
├── Manpower regeneration
├── Army maintenance
├── Diplomatic relation ticks
├── War exhaustion
├── Movement points recovery
└── AI Operational layer updates (see AI Architecture)

WEEKLY:
├── Trade route recalculation
├── Market prices
├── Merchant competition
├── Piracy spread
└── AI Tactical layer updates (see AI Architecture)

MONTHLY:
├── Tax collection
├── Production income
├── Tech/idea progress
├── Construction progress
├── Colonization growth
├── Inflation
└── AI Strategic layer planning (see AI Architecture)

YEARLY:
├── Population growth
├── Development spread
├── Culture conversion
├── Religion spread
├── Age progression
└── Historical events

ON-DEMAND ONLY:
├── Trade goods (when buildings/dev change)
├── Supply limit (when modifiers change)
├── Fort ZoC (when forts change)
├── Diplomatic range (when tech changes)
└── Army quality (when composition changes)
```

## The Time Manager

### Core Time System

**TimeManager Pattern**:
- Maintains game time state (hour, day, month, year)
- Uses fixed-point accumulator for deterministic time advancement
- Tracks current tick for command synchronization
- Delegates trigger layered updates at appropriate frequencies
- Speed control with exact fractional multipliers

**Key Components**:
- **Time State**: Current hour/day/month/year
- **Accumulator**: Fixed-point value to prevent drift
- **Tick Counter**: For multiplayer synchronization
- **Update Delegates**: Registered callbacks for each frequency
- **Speed Control**: Pause and variable game speed

**Time Advancement Pattern**:
1. Convert real deltaTime to game time using fixed-point math
2. Accumulate game time
3. Process full hours when accumulator exceeds threshold
4. Trigger appropriate layered updates (hourly → daily → weekly → monthly → yearly)
5. Increment tick counter

**Determinism Requirements**:
- Fixed-point accumulator (no float drift)
- Simplified calendar (30-day months, no leap years)
- Tick counter for command synchronization
- Speed multipliers as exact fractions
- Synchronization method for multiplayer catchup

## Dirty Flag System

### Province Update State Pattern

**Flag-Based Change Tracking**:
- Bit flags for different system types (Economy, Military, Trade, Population, etc.)
- Per-province dirty state tracking
- Counter fields for time-since-last-update tracking
- Bitwise operations for efficient flag management

**ProvinceUpdateFlags Design**:
- Each system gets a bit flag (up to 32 systems with uint)
- Flags combine via bitwise OR for multi-system updates
- Check flags via bitwise AND for needed updates
- Clear flags after processing

**Update State Tracking**:
- Dirty flags indicate which systems need updates
- Time counters track days/weeks/months since last update
- Methods to mark dirty, check if needs update, clear flags

### Global Dirty Tracking Pattern

**DirtyTracker Components**:
- **Per-Province State**: Array of update states
- **System-Specific Lists**: HashSets for efficient iteration (dirtyEconomy, dirtyMilitary, etc.)
- **Cascade Depth Tracking**: Prevents infinite update loops
- **Bulk Operations**: Mark entire nations or regions dirty at once

**Efficient Iteration**:
- Dedicated HashSets for frequently-checked systems
- Fast lookup avoids scanning all provinces
- Fallback to full scan for less common flag combinations
- Clear lists after processing updates

**Bulk Operations Pattern**:
- Mark all provinces in a nation dirty (e.g., tech advancement)
- Mark all provinces in a region dirty (e.g., trade node changes)
- Add to system-specific lists for fast retrieval

## Update Systems Implementation

### Economic Update System Pattern

**Update Flow**:
1. Query DirtyTracker for provinces flagged with Economy changes
2. Iterate only dirty provinces (not all provinces)
3. Recalculate economic values using fixed-point math
4. Cache results for tooltip/UI queries
5. Clear dirty flags after processing

**Data Storage**:
- Fixed-point arrays for base values (tax, production, trade)
- Cached result arrays for fast lookups
- All calculations use deterministic fixed-point math

**Event Triggers**:
- Building completion marks province dirty
- Tech advancement marks entire nation dirty
- Trade route changes mark trade node provinces dirty
- Updates deferred until scheduled monthly update

### Military Update System Pattern

**Update Flow**:
1. Query DirtyTracker for provinces flagged with Military changes
2. Iterate only dirty provinces
3. Update manpower regeneration using fixed-point math
4. Update fortification status if needed
5. Clear dirty flags after processing

**Fixed-Point Calculations**:
- Regeneration rates as exact fractions
- Daily rate derived from monthly rate
- Clamped to maximum values
- Deterministic across all platforms

## Bucketed Updates for Load Distribution

### The Bucketing Pattern

**Problem**: Updating all provinces for yearly operations creates performance spikes

**Solution**: Spread yearly operations across daily buckets

**Bucketing Pattern**:
- Divide total provinces by number of days in period
- Calculate which bucket (day of year) to process
- Each province processed exactly once per year
- Same province always processes on same day (deterministic)

**Example: Population Growth**:
- Yearly operation split across daily buckets
- Each day processes a fraction of provinces
- Daily growth rate = yearly rate / days in year
- Fixed-point math for determinism

**Benefits**:
- No performance spikes from bulk updates
- Consistent frame time throughout year
- Load evenly distributed
- Deterministic execution order
- Each province updated at correct frequency

## Cascade Depth Control

### The Cascade Problem

**Update chains can trigger infinite loops**:
- Building completes → economy update → nation income update → bankruptcy check → stability change → rebellion check → military update → province capture → economy update → **LOOP**

**Without control**: System can lock up in infinite update cascades

### Cascade Depth Tracking Pattern

**CascadeController Pattern**:
- Track current cascade depth with counter
- Define maximum cascade depth limit
- Defer updates beyond limit to prevent loops
- Queue for next frame processing
- Reset depth counter after frame

**Execution Flow**:
1. Check if cascade depth under limit
2. If under limit: increment depth, execute update, decrement depth
3. If over limit: queue update for deferred processing, log warning
4. Process deferred queue at end of frame with fresh depth counter

**Integration with Update Systems**:
- Wrap cascading updates in cascade control
- Only cascade on significant changes (threshold checks)
- Significant change triggers higher-level update
- Example: Province income changes enough → trigger nation-level economy update

**Key Principles**:
- **Track depth**: Count cascade levels
- **Defer when deep**: Queue instead of loop
- **Log warnings**: Alert developers to cascade limits
- **Threshold checks**: Only cascade for meaningful changes
- **Reset per frame**: Fresh start each frame

## Event-Driven Update Triggers

### Event Trigger Patterns

**Construction Events**:
- Building queued → mark Buildings flag dirty
- Building complete → mark Economy and Military flags dirty
- Trade building complete → cascade to trade node
- Infrastructure complete → mark Development dirty

**Warfare Events**:
- Siege start → mark Military dirty, increase update frequency to hourly
- Siege end → return to normal daily updates
- Province occupation → mark all systems dirty, cascade to neighbors
- Army movement → mark Military dirty for affected provinces

**Technology Events**:
- Tech advancement → mark entire nation dirty for affected systems
- Different tech categories affect different flag combinations
- Taxation tech → Economy flags
- Military tech → Military flags
- Diplomatic tech → Diplomacy flags

**Diplomatic Events**:
- War declaration → mark Military for all involved nations
- Alliance formed → mark Diplomacy for member nations
- Trade agreement → mark Trade for affected trade nodes
- Vassal integration → mark all systems for integrated provinces

**Dynamic Frequency Adjustment**:
- Active combat/sieges switch to hourly updates
- Normal provinces use daily/monthly updates
- Return to lower frequency when events complete
- Reduces unnecessary calculations while maintaining responsiveness

## Command Pattern Integration

### Command Execution Pattern

**Tick-Based Execution**:
- Commands store target execution tick
- Validated before execution
- Execute state changes
- Mark affected entities dirty
- Tick verification for multiplayer safety

**Command Queue Processing**:
- Priority queue ordered by execution tick
- Process all commands for current tick
- Validate before executing
- Dequeue after processing

**TimeManager Integration Order** (Critical):
1. **Process commands first** - deterministic player/AI input
2. **Run scheduled updates** - layered system updates (hourly/daily/etc.)
3. **Process dirty flags** - update changed entities
4. **Advance time** - increment tick, advance to next hour/day

**Why Order Matters**:
- Commands execute first to provide deterministic input
- Commands mark entities dirty
- Updates process those dirty flags
- Clean separation ensures multiplayer synchronization
- Next tick begins with clean state

## Pause State Handling

### Pause Behavior Pattern

**When Paused**:
- Time stops advancing (tick counter frozen)
- Dirty flags continue accumulating normally
- Player actions still mark entities dirty
- No updates execute until unpaused

**When Unpaused**:
- Time resumes from pause point
- Accumulated dirty flags remain
- Updates process on normal schedule (not all at once)
- No performance spike from deferred updates

**Key Principles**:
- Pause doesn't clear dirty flags
- Unpause doesn't trigger immediate bulk updates
- Updates execute when their scheduled time arrives
- Example: Building completes while paused → Economy flag dirty → Next monthly update processes it

**Benefits**:
- No frame time spike when unpausing
- State changes accumulate properly
- Updates batched naturally at scheduled intervals
- Player can make multiple changes while paused without penalty

## Multiplayer Time Synchronization

### Lockstep Determinism Pattern

**Synchronization Requirements**:
- All clients on same tick before advancing
- Client ready signals coordinate advancement
- Commands execute in deterministic order
- Scheduled updates identical across clients
- Dirty flag processing deterministic

**Tick Advancement Flow**:
1. Clients signal ready for next tick
2. Wait for all clients to signal ready
3. Advance tick on all clients simultaneously
4. Process commands in sorted order (by player ID)
5. Execute scheduled updates
6. Process dirty flags
7. Repeat

**Critical Multiplayer Rules**:
1. **Tick synchronization** - no client advances alone
2. **Deterministic command order** - sorted by player ID
3. **Fixed-point math only** - no float divergence
4. **No real-time dependencies** - time budget doesn't affect simulation
5. **Identical iteration order** - dirty flags processed same way
6. **Same RNG seed** - deterministic random number generation

### Multiplayer Architecture Comparison

**Lockstep** (recommended for smaller games):
- All clients simulate identically
- Slowest client sets pace
- Minimal bandwidth (commands only)
- Perfect synchronization
- Best for 2-8 players

**Client-Server** (recommended for larger games):
- Server authoritative
- Clients predict and rollback
- Higher bandwidth (state deltas)
- Scales to many players
- Best for 8+ players

## Performance Optimizations

### Caching Strategy Pattern

**Version-Based Cache Invalidation**:
- Cache stores value + version number
- Global version counter for each system
- Increment version to invalidate all caches
- Recompute only when version mismatch

**Cached Value Pattern**:
- Store computed value with version tag
- Validity flag tracks if cached
- Get method checks version before returning
- Recalculate if version outdated
- Example: Trade values cached, invalidated when trade routes change

**Benefits**:
- Avoid redundant expensive calculations
- Version-based invalidation is simple and fast
- Works well with dirty flag systems
- Deterministic for multiplayer

### Single-Player Time Budgets

**⚠️ WARNING: Single-Player Only**

**Time Budget Pattern**:
- Set maximum milliseconds per frame
- Process batches until time budget exhausted
- Defer remaining work to next frame
- Continue where left off

**Why This Breaks Multiplayer**:
- Fast client processes more work than slow client
- Game states diverge between clients
- Desynchronization occurs
- Non-deterministic execution

**Multiplayer Solution**:
- All clients must process ALL work
- No time budgets for simulation
- Performance determined by slowest client
- Determinism over performance

## Profiling & Metrics

### Performance Monitoring Pattern

**Metrics to Track**:
- Update time per frequency layer (hourly, daily, monthly, etc.)
- Entity count processed per update
- Call count per update type
- Average time per update
- Peak time per update

**Update Metrics Pattern**:
- Dictionary keyed by update frequency
- Track total time, call count, entities updated
- Calculate averages for reporting
- Log daily or on-demand
- Identify performance bottlenecks

**Key Metrics**:
- Average milliseconds per update type
- Entity count processed
- Frequency of updates
- Performance trends over time

## Configuration & Tuning

### Time Constants Pattern

**Base Time Units** (deterministic):
- Hours per day: Standard
- Days per week: Standard
- Days per month: Simplified (30 days always)
- Months per year: Standard
- Days per year: Simplified (360 days, not 365)

**Why Simplified Calendar**:
- Evenly divisible by months and days
- No leap year complexity
- Simpler bucketing math
- Fully deterministic across platforms
- Easier modulo operations

**Speed Settings**:
- Exact fractions for determinism
- Pause, slow, normal, fast, very fast options
- Fixed-point representation
- Identical across all clients

**Cascade Limits**:
- Maximum cascade depth to prevent loops
- Maximum deferred updates queue size

### System Configuration Pattern

**Configurable Settings**:
- **Update Frequencies**: Enable/disable specific frequency layers
- **Performance**: Bucket size limits for load distribution
- **Bucketing**: Enable bucketing, configure bucket counts
- **Cascade Control**: Depth limits, warning flags
- **Multiplayer**: Time budget flags (single-player only)

**ScriptableObject Pattern**:
- Configuration stored in asset
- Designer-friendly tuning
- Runtime accessible
- Version controlled

## Common Patterns & Anti-Patterns

| Anti-Pattern | Why Wrong | Correct Pattern |
|--------------|-----------|----------------|
| **Update Everything** | 99% unchanged calculations | Update only dirty provinces |
| **Immediate Updates** | Scattered recalculations, cache thrashing | Mark dirty, batch update later |
| **Polling for Changes** | Check all entities every frame | Event-driven triggers |
| **No Bucketing** | Spike when all entities update same tick | Spread across multiple ticks |
| **Float Accumulator** | Drifts in multiplayer, desyncs | Fixed-point accumulator |
| **Unbounded Cascades** | Infinite update loops | Cascade depth limit + deferred queue |
| **Time Budgets in MP** | Different work on different clients = desync | All clients process all work |
| **Real Calendar (365 days)** | Complex leap year logic, edge cases | Simplified 360-day year |

## Performance Comparison

### Traditional Update-Everything Approach

**Daily Tick Performance**:
- All provinces × all systems = massive checks
- Most calculations are redundant (nothing changed)
- Time per tick increases with province count
- Late game performance degrades significantly

### Layered Update with Dirty Flags Approach

**Daily Tick Performance**:
- Check dirty flags (fast array scan)
- Update only changed provinces (small fraction)
- Process event queue (minimal overhead)
- Dramatically faster per tick
- Late game performance remains consistent

**Key Performance Differences**:
- Dirty flags eliminate redundant calculations
- Update time scales with changes, not total entities
- Consistent performance from early to late game
- Event-driven ensures accuracy without polling

### Memory Overhead

**Dirty Flag System Memory**:
- Province update state array (minimal per-province overhead)
- Dirty lists for fast iteration (small hash sets)
- Cached calculations (modest overhead)
- Cascade control structures (negligible)
- Total overhead: Minimal relative to benefits

## Best Practices

1. **Default to OnDemand updates** - Only use scheduled updates for true time-based mechanics
2. **Cache aggressively** - Never recalculate unchanged values
3. **Bucket expensive operations** - Spread yearly updates across 360 days (not all at once)
4. **Use events, not polling** - React to changes rather than checking for them
5. **Profile everything** - Monitor update times and counts
6. **Fail fast** - Skip update entirely if nothing changed
7. **Batch similar updates** - Process all economic updates together for cache efficiency
8. **Control cascade depth** - Prevent infinite update loops
9. **Fixed-point time** - No float accumulator drift
10. **Simplified calendar** - 360-day year (30-day months) for determinism

## Implementation Checklist

### Phase 1: Core Time System
- [x] Implement TimeManager with basic tick system
- [x] Create UpdateFrequency enum and scheduling
- [x] Build dirty flag system with ProvinceUpdateFlags
- [x] Add performance metrics collection
- [ ] Add fixed-point accumulator
- [ ] Add tick counter for commands

### Phase 2: Update Systems
- [x] Implement economic update system
- [x] Implement military update system
- [ ] Create bucketed update distributor
- [x] Add event-driven triggers
- [ ] Add cascade depth control

### Phase 3: Optimization
- [ ] Add caching layer for calculations
- [ ] Implement update batching (single-player only!)
- [ ] Add profiling and auto-tuning
- [ ] Add cascade detection and warnings

### Phase 4: Multiplayer
- [ ] Add tick synchronization
- [ ] Add command queue with tick scheduling
- [ ] Add determinism verification (checksums)
- [ ] Test lockstep with 2-4 clients

## Summary

This time system architecture achieves:
- **Dramatic reduction in calculations** compared to traditional update-everything approach
- **Consistent performance** throughout game lifecycle
- **Fast update times** even at large scale
- **Event-driven accuracy** instead of arbitrary polling schedules
- **Zero performance degradation** in late game
- **Multiplayer determinism** through fixed-point math and tick synchronization
- **Cascade control** prevents infinite update loops
- **Bucketing** prevents performance spikes from bulk updates

The key insights:
1. **Most game state is static most of the time** - dirty flags eliminate redundant work
2. **Expensive operations should be spread across time** - bucketing prevents spikes
3. **Updates cascade and need depth control** - cascade tracking prevents loops
4. **Multiplayer requires perfect determinism** - fixed-point math and tick synchronization
5. **Different systems need different frequencies** - layered updates match mechanics to update rates

## Related Architecture Documents

- [data-flow-architecture.md](data-flow-architecture.md) - Command pattern and event system integration
- [performance-architecture-guide.md](performance-architecture-guide.md) - Memory pooling and cache optimization techniques
- [master-architecture-document.md](master-architecture-document.md) - Overall dual-layer architecture this time system supports
- [../Planning/multiplayer-design.md](../Planning/multiplayer-design.md) - Multiplayer synchronization details *(not implemented)*

---

*Last Updated: 2025-10-10*
