# Decision: Coordinator Pattern for Complex Initializers
**Date:** 2025-10-25
**Status:** ✅ Implemented
**Impact:** Breaking change (file structure refactor)
**Maintainability:** 786→319 lines, 688→289 lines (58-60% reduction)

---

## Decision Summary

**Changed:** Monolithic initializers split into coordinator + stateless phase handlers
**Reason:** Maintain <500 line guideline, enable scalability, enforce phase separation
**Trade-off:** More files (7-12 vs 1) for better maintainability and extensibility

---

## Context

**Problem:** HegemonInitializer (786 lines) and GameSystemInitializer (688 lines) exceeded 500-line guideline
**Constraint:** Initialization sequences grow as game features expand
**Goal:** All files <500 lines, easy to add new systems without modifying coordinator

**Previous Architecture (Monolithic):**
```
GameSystemInitializer.cs (688 lines)
  - Phase 1: HegemonProvinceSystem (40 lines)
  - Phase 2: ResourceSystem (65 lines)
  - Phase 3: EconomySystem (65 lines)
  - Phase 4: BuildingSystem (85 lines)
  - Phase 5: DiplomacySystem (75 lines)
  - Phase 6: UnitVisualization (240 lines)
  - All mixed in one file
```

**Issue:** Single file too large, adding new system requires modifying large file

---

## Options Considered

### Option 1: Keep Monolithic, Ignore Guideline
**Approach:** Accept 700+ line initializers, no changes
**Pros:** No refactor needed, simpler file structure
**Cons:** Grows unbounded as systems added, violates guideline
**Rejected:** User concern: "I'm very sure this init will grow larger as we start adding more systems"

### Option 2: Extract Only Largest Method
**Approach:** Move UnitVisualization (240 lines) to separate file
**Pros:** Minimal changes, quick fix
**Cons:** Still 450 lines, doesn't solve growth problem
**Rejected:** Doesn't address scalability concern

### Option 3: Coordinator Pattern - Orchestrator + Phase Handlers (CHOSEN)
**Approach:** Coordinator orchestrates phases, delegates ALL logic to stateless handlers
**Pros:** Scales infinitely, clear phase separation, trivial to add systems
**Cons:** More files
**Chosen:** Consistent with HegemonInitializer, GameSystemInitializer, EngineInitializer

---

## Final Decision

**Architecture:** Coordinator orchestrates sequence, stateless phase handlers contain ALL logic

### GameSystemInitializer Coordinator Structure:
```
GameSystemInitializer.cs (289 lines) - COORDINATOR
  - Validates Engine dependencies
  - Orchestrates initialization phases (sequential)
  - Registers systems with GameState
  - Provides public accessors

GameSystemHegemonProvincePhaseHandler.cs (40 lines) - STATELESS
  - static InitializeHegemonProvinceSystem(...)
  - Receives dependencies as parameters
  - Returns out HegemonProvinceSystem

GameSystemResourcePhaseHandler.cs (65 lines) - STATELESS
  - static InitializeResourceSystem(...)
  - Loads JSON5, validates, registers resources
  - Returns out ResourceRegistry

GameSystemEconomyPhaseHandler.cs (65 lines) - STATELESS
  - static InitializeEconomySystem(...)
  - Sets dependencies on EconomySystem
  - Returns out EconomySystem

GameSystemBuildingPhaseHandler.cs (85 lines) - STATELESS
  - static InitializeBuildingSystem(...)
  - Loads buildings, initializes BuildingConstructionSystem
  - Returns out BuildingRegistry and BuildingConstructionSystem

GameSystemDiplomacyPhaseHandler.cs (75 lines) - STATELESS
  - static InitializeDiplomacySystem(...)
  - Initializes DiplomacySystem + tick handlers
  - Returns out DiplomacySystem and handlers

GameSystemUnitPhaseHandler.cs (240 lines) - STATELESS
  - static InitializeUnitRegistry(...)
  - static InitializeUnitVisualization(...)
  - Includes GPU setup (material creation, reflection-based setup)
  - Returns out UnitRegistry and UnitVisualizationSystem
```

### HegemonInitializer Coordinator Structure:
```
HegemonInitializer.cs (319 lines) - COORDINATOR
  - Stateful orchestrator (progress tracking, error handling)
  - Delegates ALL logic to phase handlers
  - Four sequential phases

HegemonEnginePhaseHandler.cs (75 lines) - STATELESS
  - static InitializeEngine(...)
  - Initializes EngineInitializer

HegemonMapPhaseHandler.cs (180 lines) - STATELESS
  - static InitializeMap(...)
  - static ScanProvinceAdjacencies(...)
  - Map loading and adjacency scanning

HegemonGameSystemsPhaseHandler.cs (178 lines) - STATELESS
  - static InitializeGameSystems(...)
  - Initializes GameSystemInitializer

HegemonUIPhaseHandler.cs (275 lines) - STATELESS
  - static InitializeUI(...)
  - UI setup and first-frame readiness
```

---

## Rationale

**Why Coordinator Pattern:**
- Initialization is sequential (phase order matters)
- Each phase is one-shot (runs once, discarded)
- Phases have dependencies (Phase N needs output of Phase N-1)
- Scalability: Adding new system = create new phase handler file

**Why Stateless Phase Handlers:**
- No hidden state (all dependencies explicit)
- Pure functions (given inputs → deterministic outputs)
- Easy to test (no setup/teardown)
- No lifecycle complexity (called once, thrown away)

**Why Coordinator Owns References:**
- Progress tracking (isInitialized flag)
- Error recovery (return false, halt initialization)
- Public accessors (systems available after init)
- Reference management (coordinator holds system instances)

**Why This Scales:**
- Adding new system: Create GameSystemXPhaseHandler.cs, call from coordinator
- No need to modify existing phase handlers
- Each handler focused on single system
- Coordinator remains simple orchestrator

---

## Trade-offs Accepted

**File Count:**
- OLD: 1 file (688 lines)
- NEW: 7 files (289 + 6×40-240 lines)
- **Acceptable:** Each file focused on single system

**Parameter Passing:**
- Phase handlers require many dependencies passed as parameters
- Example: `InitializeEconomySystem(gameState, timeMgr, countrySystem, hegemonSystem, resourceRegistry, logProgress, out economySystem)`
- **Acceptable:** Explicit dependencies show phase requirements clearly

**Sequential Execution:**
- Coordinator must call phases in correct order
- Phase handlers assume prior phases completed
- **Acceptable:** Initialization is inherently sequential

**No Performance Cost:**
- Initialization runs once at startup
- Performance irrelevant for one-time operations
- Clarity and maintainability prioritized

---

## Implementation Impact

**HegemonInitializer:**
- **Files Modified:** 1 (HegemonInitializer.cs - 786→319 lines)
- **Files Created:** 4 (Initialization/Hegemon*PhaseHandler.cs)
- **Breaking Changes:** None (initialization still works)
- **Migration:** None needed (internal refactor only)

**GameSystemInitializer:**
- **Files Modified:** 1 (GameSystemInitializer.cs - 688→289 lines)
- **Files Created:** 6 (Initialization/GameSystem*PhaseHandler.cs)
- **Breaking Changes:** None (public API unchanged)
- **Migration:** None needed (internal refactor only)

---

## Validation

**File Size Compliance:**
- HegemonInitializer: 786→319 lines (59% reduction) ✅
- GameSystemInitializer: 688→289 lines (58% reduction) ✅
- All phase handlers: <280 lines each ✅
- **Target:** <500 lines per file ✅

**Functionality:**
- All systems initialize correctly ✅
- Game starts and runs ✅
- No behavior changes ✅
- **Status:** ✅ Feature-complete

**Scalability:**
- Adding new system requires only new phase handler file ✅
- Coordinator remains stable size (won't grow) ✅
- Pattern established for future systems ✅
- **Status:** ✅ Scales as game grows

---

## Alternatives Rejected & Why

**Partial Split (Extract Only Large Methods):**
- Approach: Extract UnitVisualization (240 lines), keep rest inline
- Issue: Coordinator still 450 lines, inconsistent pattern
- Rejected: Doesn't solve growth problem

**Nested Classes (Inner Classes):**
- Approach: Move phase handlers into nested classes
- Issue: Still one large file, harder to navigate
- Rejected: Doesn't solve file size problem

**Dynamic Phase Registration:**
- Approach: Systems register their own initialization delegates
- Issue: Order unclear, dependencies implicit, over-engineered
- Rejected: Sequential phases are simple and explicit

---

## Pattern: When to Use Coordinator

**Use Coordinator When:**

1. **Initialization Sequence (Not Runtime)**
   - One-time setup, not ongoing operations
   - Runs at startup, then discarded
   - Use Facade Pattern for runtime systems instead

2. **Sequential Phases with Dependencies**
   - Phase 2 needs output of Phase 1
   - Clear ordering required
   - Each phase is distinct operation

3. **File Exceeds 500 Lines**
   - Initializer has grown too large
   - Multiple systems initialized
   - Clear candidates for phase extraction

4. **Likely to Grow Over Time**
   - User: "I'm very sure this init will grow larger"
   - Proactive architecture (not reactive)
   - Easy to add phases without coordinator changes

**Don't Use Coordinator When:**

1. **File Under 300 Lines**
   - Simple initialization, few systems
   - No need to over-engineer
   - Keep together if manageable

2. **No Clear Phases**
   - Logic is tightly coupled
   - No natural phase boundaries
   - Extraction would create complex dependencies

3. **Runtime System (Not Initialization)**
   - Active during gameplay
   - State persists across frames
   - Use Facade Pattern instead

---

## Pattern: Coordinator vs Facade

**Coordinator (GameSystemInitializer):**
- Initialization sequence (runs once)
- Orchestrates phases (sequential steps)
- Delegates to stateless phase handlers
- Example: Engine init → Map init → Systems init → UI init
- Lifecycle: Startup only

**Facade (EconomySystem):**
- Runtime system (active during gameplay)
- Owns state (caches, bridges, managers)
- Delegates to stateless managers
- Example: Tax collection, income queries
- Lifecycle: Entire game session

**Key Difference:**
- Coordinator: Orchestrator, one-time sequence
- Facade: State owner, runtime operations

---

## Consistency Across Codebase

**Systems Using Coordinator Pattern:**
- ✅ EngineInitializer (Engine) - Phase handlers for Engine init
- ✅ HegemonInitializer (Game) - Session 2 refactor
- ✅ GameSystemInitializer (Game) - Session 3 refactor

**Benefits of Consistency:**
- Predictable architecture (same pattern everywhere)
- Easy to add systems (follow template)
- Onboarding simplified (recognize pattern immediately)

**Goal:** All initializers >500 lines use Coordinator pattern

---

## Future Applications

**Scalability Precedent:**
- Adding new game system: Create GameSystemXPhaseHandler.cs
- Adding new initialization step: Create HegemonXPhaseHandler.cs
- No need to modify coordinator (stable API)

**Benefits:**
- Coordinator never grows beyond ~300 lines
- Phase handlers remain focused (<250 lines each)
- Clear template for future systems

---

## Lessons Learned

**Proactive Architecture Wins:**
- User anticipated growth: "I'm very sure this init will grow larger"
- Refactoring now easier than later (no legacy to migrate)
- Coordinator pattern prevents future bloat

**Stateless Handlers Ideal for Init:**
- Initialization is one-shot (no state needed after)
- All dependencies passed explicitly (clear requirements)
- Pure functions (testable, predictable)

**Sequential Dependencies Are Clear:**
- Phase order explicit in coordinator
- Phase handlers assume prior phases complete
- No hidden dependencies (all in parameters)

**Consistency Reduces Cognitive Load:**
- HegemonInitializer and GameSystemInitializer use same pattern
- Developers know where to find initialization logic
- Template established for future initializers

---

## Documentation Impact

**Updated:**
- `Assets/Game/FILE_REGISTRY.md` - New phase handlers documented
- Session log: `2025-10/25/3-facade-coordinator-refactoring-session.md`

**Created:**
- `decisions/coordinator-pattern-for-initializers.md` - This doc

**Architecture Docs:**
- Pattern 18 (Coordinator) documented in CLAUDE.md
- Establishes pattern for all future initializers

---

## Success Criteria Met

- [x] All files <500 lines (319 and 289 for coordinators)
- [x] Clear phase separation (6-7 focused handlers)
- [x] Easy to add systems (create new phase handler)
- [x] Stateless handlers (no hidden dependencies)
- [x] Public API unchanged (backward compatible)
- [x] Consistent with existing coordinators (HegemonInitializer)
- [x] Scales as game grows (coordinator never grows)
- [x] Documentation complete

---

## Quick Reference

**Coordinator Pattern Template:**

```csharp
// 1. Coordinator orchestrates sequence
public class SystemCoordinator
{
    private bool isInitialized = false;
    private SystemA systemA;
    private SystemB systemB;

    public bool Initialize(Dependencies deps)
    {
        // DELEGATE: Phase 1
        if (!PhaseAHandler.InitializePhaseA(deps, out systemA))
            return false;

        // DELEGATE: Phase 2 (depends on Phase 1)
        if (!PhaseBHandler.InitializePhaseB(systemA, deps, out systemB))
            return false;

        isInitialized = true;
        return true;
    }

    // Public accessors
    public SystemA SystemA => systemA;
    public SystemB SystemB => systemB;
}

// 2. Stateless phase handler (pure function)
public static class PhaseAHandler
{
    public static bool InitializePhaseA(
        Dependencies deps,
        out SystemA systemA)
    {
        // All logic here (no coordinator logic)
        systemA = new SystemA();
        systemA.Initialize(deps);
        return true;
    }
}
```

**Adding New System:**
```csharp
// 1. Create new phase handler file
public static class GameSystemNewSystemPhaseHandler
{
    public static bool InitializeNewSystem(
        GameState gameState,
        Dependencies deps,
        bool logProgress,
        out NewSystem newSystem)
    {
        // All initialization logic
        newSystem = new NewSystem();
        return true;
    }
}

// 2. Add one call to coordinator
if (!GameSystemNewSystemPhaseHandler.InitializeNewSystem(
    gameState, deps, logProgress, out newSystem))
    return false;

// Done! Coordinator remains small, new system isolated
```

---

*Decision made: 2025-10-25*
*Implemented: 2025-10-25*
*Status: Production-ready*
*Impact: Template for all future initializers, guaranteed scalability*
