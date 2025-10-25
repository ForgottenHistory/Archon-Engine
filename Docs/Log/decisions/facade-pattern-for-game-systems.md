# Decision: Facade Pattern for Complex Game Systems
**Date:** 2025-10-25
**Status:** ✅ Implemented
**Impact:** Breaking change (file structure refactor)
**Maintainability:** 786→319 lines, 732→258 lines (60-65% reduction)

---

## Decision Summary

**Changed:** Large monolithic systems split into facade + specialized components
**Reason:** Maintain <500 line guideline, improve testability, enforce single responsibility
**Trade-off:** More files (6-7 vs 1) for better maintainability and clarity

---

## Context

**Problem:** EconomySystem (732 lines) and HegemonInitializer (786 lines) exceeded 500-line guideline
**Constraint:** Must maintain clear architecture, single responsibility per file
**Goal:** All files <500 lines, improve testability, enable easier maintenance

**Previous Architecture (Monolithic):**
```
EconomySystem.cs (732 lines)
  - Tax collection (60 lines)
  - Income caching (40 lines)
  - Province caching (50 lines)
  - Manpower regeneration (90 lines)
  - Treasury operations (120 lines)
  - Save/load (50 lines)
  - All mixed in one file
```

**Issue:** Single file too large, unclear separation of concerns

---

## Options Considered

### Option 1: Keep Monolithic, Ignore Guideline
**Approach:** Accept 700+ line files, no changes
**Pros:** No refactor needed, simpler file structure
**Cons:** Hard to navigate, unclear responsibilities, violates guideline
**Rejected:** Guideline exists for good reason (maintainability)

### Option 2: Split by Feature (Traditional OOP)
**Approach:** Separate classes (TaxCollector, ManpowerManager, etc.) with state
**Pros:** Object-oriented, each class owns its data
**Cons:** Data scattered across classes, unclear ownership, complex dependencies
**Rejected:** Violates "Single Source of Truth" (Pattern 17)

### Option 3: Facade Pattern - Stateful Coordinator + Stateless Managers (CHOSEN)
**Approach:** Facade owns ALL state, delegates operations to stateless managers
**Pros:** Clear data ownership, testable components, single source of truth
**Cons:** More files, managers need dependencies passed as parameters
**Chosen:** Consistent with existing DiplomacySystem architecture

### Option 4: Coordinator Pattern (Phase Handlers)
**Approach:** Similar to Facade but for initialization sequences (HegemonInitializer)
**Pros:** Clear phase separation, stateless handlers, orchestrated sequence
**Cons:** More files
**Chosen:** For initialization code (Pattern 18), Facade for runtime systems

---

## Final Decision

**Architecture:** Facade owns state, stateless managers process operations

### EconomySystem Facade Structure:
```
EconomySystem.cs (258 lines) - FACADE
  - Owns: incomeCache, provinceCache, treasuryBridge
  - Delegates: Tax collection, manpower regen to stateless managers

EconomyTaxManager.cs (60 lines) - STATELESS
  - Static methods receiving dependencies as parameters
  - CollectMonthlyTaxes(CountrySystem, incomeCache, treasuryBridge, ...)

EconomyIncomeCache.cs (90 lines) - STATEFUL COMPONENT
  - Owns: cachedCountryIncome[], incomeNeedsRecalculation[]
  - Methods: GetCachedIncome(), InvalidateCountry()

EconomyProvinceCache.cs (100 lines) - STATEFUL COMPONENT
  - Owns: countryProvincesCache (NativeParallelMultiHashMap)
  - Methods: GetCachedProvinces(), InvalidateCountry()

EconomyManpowerManager.cs (110 lines) - STATELESS
  - Static methods receiving dependencies
  - RegenerateManpower(provinceCache, hegemonSystem, resourceSystem, ...)

EconomyTreasuryBridge.cs (145 lines) - STATEFUL COMPONENT
  - Delegates to ResourceSystem (Engine layer)
  - Forwards events for UI backward compatibility

EconomySaveLoadHandler.cs (60 lines) - STATELESS
  - Static Save() and Load() methods
  - No state, pure serialization logic
```

### HegemonInitializer Coordinator Structure:
```
HegemonInitializer.cs (319 lines) - COORDINATOR
  - Stateful orchestrator (owns references, progress tracking)
  - Delegates ALL logic to phase handlers

HegemonEnginePhaseHandler.cs (75 lines) - STATELESS
  - Static InitializeEngine(...) method
  - Receives all dependencies as parameters

HegemonMapPhaseHandler.cs (180 lines) - STATELESS
  - Static InitializeMap(...) method
  - Static ScanProvinceAdjacencies(...) method

HegemonGameSystemsPhaseHandler.cs (178 lines) - STATELESS
  - Static InitializeGameSystems(...) method
  - Receives dependencies, no state

HegemonUIPhaseHandler.cs (275 lines) - STATELESS
  - Static InitializeUI(...) method
  - All initialization logic, no state
```

---

## Rationale

**Why Facade Pattern:**
- Consistent with DiplomacySystem (Engine already uses this)
- Clear data ownership (facade owns ALL state)
- Single Source of Truth (Pattern 17)
- Testable (stateless managers easy to unit test)

**Why Stateless Managers:**
- No hidden state (all dependencies explicit)
- Pure functions (given inputs → deterministic outputs)
- Easy to test (no setup/teardown)
- Clear contracts (parameters show dependencies)

**Why Facade Owns State:**
- Single source of truth (one owner per data)
- Clear lifecycle (facade initialization/shutdown)
- No scattered ownership (data in one place)
- Easy serialization (facade delegates to handler)

**Why Coordinator for Initialization:**
- Initialization is sequential (not runtime operations)
- Needs progress tracking (stateful coordinator)
- Phase handlers are one-shot (run once, throw away)
- Pattern 18 established in codebase

---

## Trade-offs Accepted

**File Count:**
- OLD: 1 file (732 lines)
- NEW: 7 files (258 + 6×60-145 lines)
- **Acceptable:** Each file focused, under guideline

**Parameter Passing:**
- Stateless managers require many parameters
- Example: `CollectMonthlyTaxes(CountrySystem, incomeCache, treasuryBridge, globalTaxRate, logTaxCollection)`
- **Acceptable:** Explicit dependencies better than hidden state

**Indirection:**
- Call facade → facade calls manager → manager does work
- One extra hop vs direct implementation
- **Acceptable:** Negligible performance cost, huge maintainability gain

**No Optimization Given Up:**
- Can still inline hot paths if needed
- Can still use Burst (stateless managers ideal)
- Can still optimize individual components

---

## Implementation Impact

**EconomySystem:**
- **Files Modified:** 1 (EconomySystem.cs - 732→258 lines)
- **Files Created:** 6 (Economy/* components)
- **Breaking Changes:** None (public API unchanged)
- **Migration:** None needed (internal refactor only)

**HegemonInitializer:**
- **Files Modified:** 1 (HegemonInitializer.cs - 786→319 lines)
- **Files Created:** 4 (Initialization/* phase handlers)
- **Breaking Changes:** None (initialization still works)
- **Migration:** None needed (internal refactor only)

---

## Validation

**File Size Compliance:**
- HegemonInitializer: 786→319 lines (59% reduction) ✅
- EconomySystem: 732→258 lines (65% reduction) ✅
- All components: <300 lines each ✅
- **Target:** <500 lines per file ✅

**Functionality:**
- All tests pass (manual verification)
- First-month stutter fixed (147ms→1.5ms, separate fix)
- Initialization still works (game starts)
- **Status:** ✅ Feature-complete

**Maintainability:**
- Clear separation of concerns ✅
- Single responsibility per file ✅
- Easy to find logic (focused files) ✅
- **Status:** ✅ Improved

---

## Alternatives Rejected & Why

**Partial Split (Keep Some Logic in Facade):**
- Tested: 400-line facade + some components
- Issue: Still violates guideline, unclear boundary
- Rejected: Half-measures worse than full refactor

**Nested Classes (Inner Classes):**
- Approach: Move components into nested classes
- Issue: Still one large file, harder to test
- Rejected: Doesn't solve file size problem

**Procedural (Static Functions):**
- Approach: All static, no facade, just functions
- Issue: No clear data ownership, scattered state
- Rejected: Violates Pattern 17 (Single Source of Truth)

---

## Pattern: When to Use Facade

**Use Facade When:**

1. **File Exceeds 500 Lines**
   - System has grown too large
   - Multiple responsibilities mixed
   - Hard to navigate/understand

2. **Multiple Concerns Present**
   - Tax collection + income calculation + manpower + treasury + save/load
   - Each concern is 50-150 lines
   - Clear candidates for extraction

3. **Complex State Management**
   - System owns multiple caches/data structures
   - State needs clear ownership (not scattered)
   - Single source of truth required

4. **Runtime System (Not Initialization)**
   - Active during gameplay (monthly ticks, queries)
   - State persists across frames
   - Use Coordinator Pattern for initialization instead

**Don't Use Facade When:**

1. **File Under 300 Lines**
   - System is simple, focused
   - Single responsibility already
   - No need to over-engineer

2. **No Clear Concerns**
   - Logic is tightly coupled
   - Extraction would create complex dependencies
   - Keep together if separation unclear

3. **Stateless Utility**
   - Pure functions, no state
   - Already testable as-is
   - Static class acceptable

---

## Pattern: Facade vs Coordinator

**Facade (EconomySystem):**
- Runtime system (active during gameplay)
- Owns state (caches, bridges, managers)
- Delegates to stateless managers
- Example: Tax collection, income queries

**Coordinator (HegemonInitializer):**
- Initialization sequence (runs once)
- Orchestrates phases (sequential steps)
- Delegates to stateless phase handlers
- Example: Engine init → Map init → Systems init → UI init

**Key Difference:**
- Facade: State owner, runtime operations
- Coordinator: Orchestrator, one-time sequence

---

## Consistency Across Codebase

**Systems Using Facade Pattern:**
- ✅ DiplomacySystem (Engine) - Original implementation
- ✅ EconomySystem (Game) - This refactor
- 🔄 BuildingConstructionSystem (Next candidate - 500+ lines)

**Systems Using Coordinator Pattern:**
- ✅ EngineInitializer (Engine)
- ✅ HegemonInitializer (Game) - This refactor

**Goal:** All systems >500 lines use Facade or Coordinator by end of project

---

## Future Applications

**Immediate Candidates:**
- BuildingConstructionSystem (~500 lines) - Facade pattern
- Any future system that exceeds 500 lines

**Benefits of Consistency:**
- Predictable architecture (know where to find logic)
- Easier onboarding (same pattern everywhere)
- Copy-paste refactoring (follow template)

---

## Lessons Learned

**Architecture > Line Count:**
- Guideline exists to force good architecture
- Splitting forces you to think about responsibilities
- Smaller files naturally have clearer purpose

**Stateless > Stateful:**
- Stateless managers trivial to test
- Explicit dependencies reveal coupling
- Pure functions easier to reason about

**Consistency Matters:**
- Following existing pattern (DiplomacySystem) was easy
- New pattern would require documentation/justification
- Architectural consistency reduces cognitive load

**Refactoring Safe When:**
- Public API unchanged (internal only)
- Tests verify behavior preserved
- Incremental (one system at a time)

---

## Documentation Impact

**Updated:**
- `Assets/Game/FILE_REGISTRY.md` - New structure documented
- Session log: `2025-10/25/2-performance-refactoring-session.md`

**Created:**
- `decisions/facade-pattern-for-game-systems.md` - This doc

**Architecture Docs:**
- No changes needed (Pattern 6 already documented in CLAUDE.md)
- Facade pattern already established (DiplomacySystem example)

---

## Success Criteria Met

- [x] All files <500 lines (319 and 258 for main files)
- [x] Clear separation of concerns (6-7 focused components)
- [x] Single Source of Truth maintained (facade owns state)
- [x] Testability improved (stateless managers)
- [x] Public API unchanged (backward compatible)
- [x] Consistent with existing architecture (DiplomacySystem)
- [x] Documentation complete

---

## Quick Reference

**Facade Pattern Template:**

```csharp
// 1. Facade owns state
public class SystemFacade
{
    // State ownership
    private ComponentA componentA;
    private ComponentB componentB;

    // Lifecycle
    public void Initialize()
    {
        componentA = new ComponentA(...);
        componentB = new ComponentB(...);
    }

    // Delegation to stateless managers
    public void OnMonthlyTick()
    {
        StatelessManager.ProcessOperation(componentA, componentB, ...);
    }
}

// 2. Stateless manager (pure functions)
public static class StatelessManager
{
    public static void ProcessOperation(ComponentA a, ComponentB b, ...)
    {
        // All dependencies passed as parameters
        // No hidden state
    }
}

// 3. Stateful components (owned by facade)
public class ComponentA
{
    private DataStructure data;
    public DataType Query() { return data.Get(); }
    public void Modify() { data.Set(); }
}
```

---

*Decision made: 2025-10-25*
*Implemented: 2025-10-25*
*Status: Production-ready*
*Impact: Foundation for all future system refactoring*
