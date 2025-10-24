# Determinism and Multiplayer Desyncs: Root Causes and Solutions

**📊 Status:** 🎯 Critical Knowledge | Multiplayer Foundation

## Executive Summary

Analysis of Paradox Games' persistent desync issues reveals that **command-based architecture alone does not prevent desyncs**. Command systems provide the structure for determinism but don't guarantee it. This document identifies the two fundamental causes of desyncs and how Archon's architecture addresses both.

**Key Sources:**
- Stellaris Dev Diary: "The Technical Challenges of Deterministic Lockstep" (Mat Ropert)
- The French Paradox Blog: PhysFS loading system analysis
- EU4/Victoria 3/CK3 multiplayer post-mortems

**Core Insight:** Paradox games desync despite having command systems because of:
1. **Floating-point non-determinism** across platforms/CPUs
2. **Order dependency** in multithreaded execution
3. **Hidden non-determinism** in libraries and platform features

**Archon's Advantage:** Designed for determinism from day one, avoiding the retrofitting nightmare.

---

## Part 1: The Two Fundamental Causes

### 1.1 Floating-Point Non-Determinism

**Problem:** Float operations give different results on different CPUs/platforms.
- x86 vs x64 math differs
- Intel vs AMD have slight variations
- Compiler optimizations reorder operations
- After 10,000 calculations: Complete state divergence

**Real Quote (Victoria 3):**
> "The main source of desyncs was floating-point determinism issues, particularly in the economy calculations. We've moved to fixed-point for critical paths, but there's still legacy float code causing issues."

**Solution:** Fixed-point arithmetic for all gameplay calculations.
- Represents fractions as integers (exact, deterministic)
- Same result on all platforms, always
- Archon uses FixedPoint64 everywhere in simulation

**Status:** ✅ Solved in Archon - No floats in Core namespace

### 1.2 Order Dependency (The Hidden Killer)

**Problem:** Operations happen in different orders on different machines.

**Stellaris Combat Example (From Dev Diary):**
- Host: Unit A attacks first → Unit B dies → Unit A survives
- Client: Unit B attacks first → Unit A dies → Unit B survives
- Result: Different survivors = DESYNC

**Root Cause:** Multithreading with shared state modification.
- Thread scheduling is non-deterministic
- Who updates first depends on CPU timing
- Different machines = different execution order

**Real Quote (Stellaris):**
> "It is much easier to design something with threading in mind rather than retrofitting an existing system for it."

**Solution:** Sequential command execution or two-phase pattern.
- Commands execute one at a time (deterministic order)
- Or: Parallel read-only → Sequential write consolidation
- Archon: Single-threaded command execution

**Status:** ✅ Solved in Archon - Sequential processing

---

## Part 2: Hidden Non-Determinism Sources

### 2.1 Collection Iteration Order

**Problem:** Dictionary/HashSet iteration order not guaranteed in C#.
- Order differs between .NET versions
- Order differs between platforms
- Critical issue in CK3 post-mortems

**Solution:** Fixed-order iteration only.
- Arrays with explicit indices
- Sorted collections with stable ordering
- Never iterate Dictionary.Values in simulation

**Archon Protection:** NativeArray with deterministic indexing throughout.

### 2.2 Third-Party Library Locks

**PhysFS Discovery (From Blog):**
- Library had hidden global mutex for all file operations
- Multithreaded loading blocked on single lock
- Lock acquisition timing non-deterministic
- Timing differences → subtle desyncs

**Lesson:** Hidden state in libraries breaks determinism even if YOUR code is perfect.

**Archon Protection:** Careful library selection, avoid hidden global state.

### 2.3 Platform-Specific Differences

**String Comparison (Real EU4 Bug):**
- Turkish locale: İstanbul < istanbul
- English locale: istanbul < İstanbul
- Sorting by name caused platform-dependent order
- Fixed: Sort by stable numeric IDs

**Unity Non-Determinism:**
- GetComponentsInChildren: Order not guaranteed
- Dictionary iteration: Platform-dependent
- DateTime.Now: Timezone differences
- Physics: Completely non-deterministic

**Archon Protection:** No Unity non-deterministic features in simulation layer.

---

## Part 3: The Cascading Desync Problem

**Progression:**
```
Tick 1:     1 gold different (tiny rounding error)
Tick 100:   5% less income (compounds over time)
Tick 1000:  Can't afford building (gameplay divergence)
Tick 5000:  Different war outcome (massive divergence)
Tick 10000: "Out of sync" detected (unplayable)
```

**Why It's Deadly:**
- Root cause was 10,000 ticks ago
- By detection time, states completely diverged
- Logs are gigabytes, impossible to debug
- No way to trace back to original cause

**Defense:** Detect early with checksums every tick.
- Catch at tick 1, not tick 10,000
- Immediate notification of divergence
- Smaller state diff to analyze
- Can identify root cause quickly

---

## Part 4: Why Paradox Can't Fix It

### Technical Debt Compounding

**EU4 Timeline:**
- 2013: Released with some float operations
- 2015: Added more features with floats for "smoothness"
- 2017: Multithreading for performance (introduced order issues)
- 2019: Dictionary-based systems for convenience
- 2023: Millions of lines of legacy code, can't audit everything

**Real Quote (EU4 Blog):**
> "Multithreading made desyncs significantly worse. We had to move most simulation back to single-threaded to maintain determinism, sacrificing performance."

**The Trap:** Each "small compromise" compounds over 10+ years.

### Performance vs Determinism

**Paradox Dilemma:**
- Users demand better performance (more provinces, more characters)
- Solution: Multithreading
- Result: Order dependency → desyncs
- Fix attempt: Move back to single-threaded → poor performance

**Trade-off:** Can't have both without careful architectural design from the start.

### Lockstep Networking Requirement

**Real Quote (Stellaris):**
> "Lockstep networking (only sending inputs) requires perfect determinism across different hardware."

**Why Commands Alone Aren't Enough:**
- Commands provide structure (inputs to sync)
- But don't enforce deterministic execution
- Must also have: Fixed-point math, sequential execution, deterministic iteration

**Alternative (Victoria 3):** Full state sync in some modes.
- Send entire game state, not just commands
- Massive bandwidth requirements
- Still has desync issues even with this!

---

## Part 5: Archon's Architectural Advantages

### Design Decisions That Prevent Desyncs

**1. FixedPoint64 Everywhere**
- No floats in simulation (Core namespace)
- All calculations exact and deterministic
- No gradual drift from rounding errors

**2. Sequential Command Execution**
- Commands execute one at a time
- Deterministic order (Priority → Checksum for ties)
- No race conditions possible

**3. NativeArray with Fixed Indexing**
- No Dictionary/HashSet in simulation
- Deterministic iteration (for loop with index)
- No platform-dependent ordering

**4. Event System Sequential**
- Events processed one at a time
- Clear ordering guarantee
- No parallel event handlers

**5. Command Pattern Two-Phase**
- Phase 1: Validation (read-only, can parallelize if needed)
- Phase 2: Execution (sequential, deterministic)
- Phase 3: Events (sequential)
- Natural separation prevents order dependency

**6. No Hidden State**
- Everything in GameState
- DeterministicRandom is synchronized state
- No thread-local or global state

**7. Early Desync Detection**
- Checksum validation built into GameState
- Compare checksums every tick in multiplayer
- Catch divergence immediately, not 10,000 ticks later

---

## Part 6: Critical Rules for Maintaining Determinism

### The Five Sacred Principles

**1. No Floats in Simulation**
- Use FixedPoint64 for all gameplay math
- Float allowed only in presentation layer (rendering, UI)
- Exception: None

**2. Sequential Command Execution**
- Commands execute one at a time, never parallel
- If optimization needed: Two-phase (parallel read → sequential write)
- Exception: None

**3. Deterministic Iteration Only**
- Arrays with explicit indices (for loop)
- Never Dictionary.Values, HashSet, or LINQ with unordered collections
- Exception: None

**4. No Parallel State Modification**
- Single-threaded simulation tick
- Parallel allowed only for read-only queries
- Exception: Two-phase pattern with consolidation

**5. Checksum Validation Always**
- Every tick in multiplayer
- Compare with other clients immediately
- Dump state on mismatch for debugging

### Forbidden Patterns

**NEVER:**
- Float/double in simulation layer
- Parallel.ForEach on state-modifying operations
- Dictionary/HashSet iteration in simulation
- Thread-local state outside GameState
- Platform-specific APIs (string sorting by name, DateTime.Now)
- Unity non-deterministic features (GetComponentsInChildren, Physics)

**ALWAYS:**
- FixedPoint64 for calculations
- Sequential command execution
- Array indexing for iteration
- All state in GameState
- Numeric ID sorting
- Game time (TimeManager), not real time

---

## Part 7: Testing Strategy

### Determinism Unit Test Pattern

**Principle:** Same command sequence → Same state, always.

Test structure:
1. Create two identical GameStates
2. Execute same command sequence on both
3. Compare checksums
4. Assert identical

Run variations:
- Different random seeds (should still match with same seed)
- 1,000+ tick sequences
- Complex command chains (wars, alliances, economy)

### Integration Test Pattern

**Principle:** Two game instances run side-by-side, checksums must match every tick.

Test structure:
1. Launch two game instances
2. Same initial state, same seed
3. Simulate N ticks
4. Compare checksum every tick
5. Fail immediately on first mismatch

Run variations:
- 10,000+ tick playthroughs
- Heavy command load (100+ commands per tick)
- Different Unity versions
- Different platforms (if possible)

### Profile After Every Unity Upgrade

**Reason:** Engine changes might affect determinism.
- New C# compiler optimizations
- Changes to collection implementations
- Platform-specific bug fixes

**Action:** Re-run full determinism test suite after upgrade.

---

## Part 8: Future Performance Strategy

### When Optimization Pressure Comes

**DON'T Compromise Determinism For Performance:**
- ❌ Add floats for "smoothness"
- ❌ Parallelize command execution
- ❌ Use Dictionary for "convenience"
- ❌ Skip validation for "speed"

**DO Profile First, Then Optimize Safely:**
- ✅ Burst compilation (single-threaded speed boost)
- ✅ GPU compute shaders (visual processing only)
- ✅ Data layout optimization (cache-friendly structs)
- ✅ Two-phase pattern if parallelization absolutely required

### Two-Phase Pattern (If Needed)

**Principle:** Separate read-only parallel phase from sequential write phase.

Structure:
1. **Parallel Phase:** Calculate results independently (read-only, no shared state)
2. **Consolidation Phase:** Merge results in deterministic order (sequential)
3. **Write Phase:** Apply consolidated results to state (sequential)

Example use case: Validation of 1000+ commands per tick.
- Validate all in parallel (read-only)
- Sort results deterministically (by Priority, then Checksum)
- Execute sequentially

---

## Part 9: Critical Threats to Watch

### Threat Level: 🔴 IMMEDIATE ACTION REQUIRED

**1. Dictionary/HashSet Iteration in Simulation**
- Causes: Platform-dependent ordering
- Detection: Code review, grep for "foreach.*Dictionary"
- Fix: Replace with array indexing

**2. Parallel State Modification**
- Causes: Race conditions, order dependency
- Detection: Look for Parallel.ForEach with mutations
- Fix: Sequential execution or two-phase pattern

**3. Float Operations in Simulation**
- Causes: Platform-dependent rounding
- Detection: Grep for "float" or "double" in Core namespace
- Fix: Replace with FixedPoint64

### Threat Level: 🟡 MONITOR CLOSELY

**4. Third-Party Libraries**
- Causes: Hidden global state, platform-specific behavior
- Detection: Profile library, check for threading
- Mitigation: Isolate library calls, validate determinism

**5. Team Knowledge**
- Causes: New developers unaware of determinism requirements
- Detection: Code review process
- Mitigation: Mandatory reading of this document, strict review

**6. Unity Engine Updates**
- Causes: Collection implementations might change
- Detection: Full test suite after upgrade
- Mitigation: Pin Unity version, test thoroughly before upgrading

### Threat Level: 🟢 ACCEPTABLE RISK

**7. Presentation Layer Non-Determinism**
- Acceptable: Floats in rendering, UI animations
- Not simulation-affecting: Visual-only operations
- Safe: As long as strictly separated from simulation layer

---

## Part 10: Key Takeaways

### What Paradox Teaches Us

**1. Command Pattern ≠ Determinism**
- Paradox has commands, still desyncs
- Commands are necessary but not sufficient
- Need: Commands + Fixed-point + Sequential execution + Careful design

**2. Order Dependency Is Real**
- Separate issue from floating-point
- Threading breaks determinism unless very carefully designed
- Retrofitting is nearly impossible

**3. Hidden Non-Determinism Everywhere**
- Dictionary iteration, string sorting, library locks, Unity features
- Easy to introduce accidentally
- Must maintain constant vigilance

**4. Cascading Effect Makes Debugging Impossible**
- Small desync → massive divergence over time
- Root cause long gone by detection time
- Early detection is critical

**5. Technical Debt Compounds**
- Each small compromise adds up over years
- "Just this once" becomes "everywhere"
- Clean slate is precious, don't waste it

### Archon's Differentiators

**Starting Right:**
- FixedPoint64 from day one (no legacy float code)
- Sequential execution from day one (no race conditions)
- NativeArray from day one (no iteration issues)
- Designed for determinism, not retrofitted

**Structural Advantages:**
- Command pattern with phase separation
- Event system with sequential processing
- No hidden state, everything in GameState
- Clear layer separation (simulation vs presentation)

**Verification Built-In:**
- Checksum validation every tick
- Early desync detection
- Comprehensive test suite
- Clear rules that can be audited

### The Sacred Contract

**These principles are non-negotiable:**
1. No floats in simulation
2. Sequential command execution
3. Deterministic iteration only
4. No parallel state modification
5. Checksum validation always

**Breaking any principle risks the entire multiplayer architecture.**

**Cost of violation:** Unrecoverable technical debt, years of desync issues, user frustration, failed multiplayer.

**Benefit of discipline:** Perfect synchronization, smooth multiplayer, player trust, competitive viability.

---

## Conclusion

Paradox's 20-year struggle with desyncs isn't from lack of talent or resources. They're trapped by architectural decisions made in 2003 before multiplayer determinism was well understood. Command systems help but don't solve the problem alone.

**We have what they don't: A clean slate.**

Every decision we make now determines whether we spend the next 20 years fighting desyncs or building features. The patterns are proven, the mistakes are documented, the solutions are clear.

**The choice is simple: Design it right from the start, or inherit Paradox's nightmare.**

This isn't optional. This isn't negotiable. This is the foundation of multiplayer grand strategy.

**Protect it.**

---

**Document Version:** 1.0
**Last Updated:** 2025-10-24
**Related Docs:**
- `paradox-dev-diary-lessons.md` - Performance and architecture lessons
- `data-oriented-vs-traditional-grand-strategy-performance.md` - Data layout optimization
- `Assets/Archon-Engine/Docs/Engine/master-architecture-document.md` - Overall architecture

**Required Reading For:**
- All engineers working on simulation layer
- Anyone touching multiplayer code
- Team leads making architectural decisions
- Code reviewers evaluating PRs

**Key Contributors:**
- Mat Ropert (Stellaris) - Order dependency insights
- Paradox Development Diaries - Real-world desync causes
- The French Paradox Blog - PhysFS hidden lock discovery
