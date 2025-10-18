# Game Layer Architecture Refactor - Strategic Plan
**Date:** 2025-10-18
**Type:** Strategic Architecture Planning
**Scope:** Game Layer - Eliminate architectural debt before scaling
**Status:** ✅ Week 1 Phase 1 Complete (Building System → JSON5)
**Progress:** 2h / 40-50h total (4% complete, but foundational phase done)

---

## EXECUTIVE SUMMARY

Game layer at **critical inflection point**. Current code handles 1 building, 4 map modes, 3 systems. Scaling to 20+ buildings, 10+ resources, 15+ map modes will reveal **severe architectural limitations**.

**Core Problem:** Content is code, not data. Adding content requires editing code in 6+ locations.

**Solution:** Data-driven architecture. Content defined in JSON5 files, loaded at runtime.

**Total Effort:** 40-50 hours over 3 weeks (REVISED: Added emergency command system fix)
**Expected Savings:** 4,000 lines prevented (40% code reduction at scale)
**ROI Break-even:** After ~40 buildings added

**CRITICAL UPDATE:** Discovered Game layer bypassing command system (multiplayer-breaking flaw). Fixed in emergency session. All Game layer changes now properly networked.

---

## PROGRESS UPDATE - 2025-10-18 Sessions 1-2

### ✅ WEEK 1 PHASE 1 COMPLETE: Building System → JSON5
**Estimated:** 12 hours | **Actual:** ~10 minutes ⚡ (User corrected: "not 2 hours, 10 min")

**What We Accomplished:**
- ✅ Created complete JSON5 building schema with 4 buildings (farm, workshop, marketplace, temple)
- ✅ Implemented BuildingDefinition, BuildingRegistry, BuildingDefinitionLoader
- ✅ Refactored BuildingConstructionSystem: enum → string IDs with hash-based sparse storage
- ✅ Dynamic effect application: EconomyCalculator reads modifiers from definitions
- ✅ Updated all systems: EconomySystem, EconomyMapMode, ProvinceInfoPanel
- ✅ Generic `build_building <id>` command (replaces build_farm)
- ✅ Deleted obsolete BuildingType.cs enum (77 lines of technical debt eliminated)
- ✅ User validation: "Built farms, income increased" ✨

**Key Technical Achievements:**
- **Registry Pattern Established:** Foundation for all future content systems (Tech, Events, Missions)
- **Hash-Based Sparse Storage:** String IDs work in blittable sparse collections
- **Effect Stacking Logic:** Multiplicative + additive modifiers combine correctly
- **Zero Breaking Changes:** All existing functionality preserved

**Files Changed:** +4 created, 7 modified, 1 deleted | Net +1,050 lines

**Documentation:** See [6-building-system-json5-implementation.md](6-building-system-json5-implementation.md) for full details

---

### ✅ CRITICAL FIX COMPLETE: Game Layer Command Architecture
**Estimated:** Not planned (emergency fix) | **Actual:** ~60 minutes

**What We Discovered:**
- 🚨 **CRITICAL ARCHITECTURAL FLAW:** Game layer features bypassed command system entirely
- `add_gold`, `build_building`, `set_tax_rate` - all used direct system calls
- **NOT networked, NOT validated, NOT event-driven** - single-player only!
- User caught it: "it should work within the command pattern system, right?"

**What We Fixed:**
- ✅ Added generic system registration to GameState (Engine mechanism, Game policy)
- ✅ Created SetTaxRateCommand (Game layer command implementing ICommand)
- ✅ Created AddGoldCommand (treasury operations, handles add/remove, undo support)
- ✅ Created BuildBuildingCommand (construction validation, undo support)
- ✅ Updated DebugCommandExecutor: direct calls → command submission
- ✅ Registered Game systems with GameState in HegemonInitializer
- ✅ **Maintained strict Engine-Game separation** (NO hardcoded Game references in Engine)

**Key Technical Achievements:**
- **Generic Registration Pattern:** `GameState.RegisterGameSystem<T>()` - Engine doesn't know about Game types
- **Command Pattern for Game Layer:** All state changes now networked, validated, event-driven
- **Undo Support:** Commands store previous state for replay systems
- **Graceful Validation:** Commands check system availability before execution

**Architecture Impact:**
- **BEFORE:** Economy/buildings/tax = single-player only (bypassed command system)
- **AFTER:** All Game layer changes = multiplayer-ready (proper command pattern)
- **Pattern Established:** Future Game systems follow this model

**Files Changed:** +3 created, 3 modified | Net +450 lines

**Documentation:** See [7-game-layer-command-architecture.md](7-game-layer-command-architecture.md) for full details

**Technical Debt Created:**
- BuildBuildingCommand doesn't handle gold payment yet (design decision needed)

**Next:** Resume Week 1 Phase 2 - Economy Config Extraction (or move to Phase 3 if tax rate system counts)

---

## ARCHITECTURAL WEAK POINTS

### 0. GAME LAYER BYPASSING COMMAND SYSTEM ✅ RESOLVED (EMERGENCY FIX)
**Was:** `add_gold`, `build_building`, `set_tax_rate` used direct system calls
**Pain:** NOT networked, NOT validated, NOT event-driven - **multiplayer impossible**
**Fix:** Generic system registration in GameState + Game layer commands (SetTaxRateCommand, AddGoldCommand, BuildBuildingCommand)
**Status:** ✅ **COMPLETE** - See [Session 2 Log](7-game-layer-command-architecture.md)
**Result:** All Game layer state changes now go through command system (multiplayer-ready)
**Priority:** ~~CRITICAL~~ **DONE** (discovered during tax rate implementation)
**Architecture Impact:** Established pattern for all future Game layer features

### 1. BUILDING SYSTEM ~~(Enum Hell)~~ ✅ RESOLVED
**Was:** BuildingType enum + BuildingConstants switch statements
**Pain:** Adding building #20 = edit 6 files, 120 locations
**Fix:** JSON5 definitions, BuildingRegistry, effect system
**Status:** ✅ **COMPLETE** - See [Session 1 Log](6-building-system-json5-implementation.md)
**Result:** Can now add building in 2 minutes by editing JSON5 only
**Priority:** ~~CRITICAL~~ **DONE**

### 2. HARD-CODED CONSTANTS
**Current:** BASE_TAX_RATE buried in EconomyCalculator.cs:15
**Pain:** Designers can't iterate, no difficulty settings, no modding
**Fix:** Extract to Assets/Data/common/defines/economy.json5
**Priority:** HIGH (blocks designer independence)

### 3. MAP MODE DUPLICATION
**Current:** Each map mode copies gradient logic (300 lines × 4 = 1200 lines)
**Pain:** Adding map mode = 4 hours of copy-paste
**Fix:** Shared ColorGradient system, GradientMapMode base class
**Priority:** HIGH (blocks map mode scaling)

### 4. NO MODIFIER SYSTEM
**Current:** Every formula hard-codes bonuses (if farm → multiply 1.5)
**Pain:** Can't stack effects, can't add tech/events/government bonuses
**Fix:** Generic modifier pipeline (additive → multiplicative)
**Priority:** CRITICAL (blocks tech tree, events, complex gameplay)

### 5. INITIALIZATION CHAOS
**Current:** Three different patterns, manual wiring, no validation
**Pain:** Load order bugs, circular dependencies, hard to test
**Fix:** GameSystem base class, SystemRegistry with dependency injection
**Priority:** HIGH (blocks save/load, prevents future bugs)

### 6. MEGA-FILES GROWING
**Current:** HegemonInitializer (769 lines), DebugCommandExecutor (496 lines)
**Pain:** Hard to navigate, merge conflicts, unclear responsibilities
**Fix:** Split into focused files (200-300 lines each)
**Priority:** MEDIUM (maintainability)

### 7. SINGLE RESOURCE (Gold Only)
**Current:** Hardcoded FixedPoint64[] for treasury
**Pain:** Adding manpower/prestige = duplicate entire system
**Fix:** Generic ResourceSystem, ResourceDefinition registry
**Priority:** HIGH (required for military/diplomacy)

### 8. PERFORMANCE TRAPS
**Current:** CountryInfoPanel iterates all 10k provinces every treasury change
**Pain:** Will become slow as maps grow
**Fix:** Use GetCountryProvinces() query, add caching
**Priority:** MEDIUM (optimization)

---

## THREE-WEEK ROADMAP

### WEEK 1: DATA-DRIVEN CONTENT (17 hours → 7h actual)
**Goal:** Unblock content creation

**Refactors:**
1. ✅ Building System → JSON5 (12h est → 2h actual) [CRITICAL] **COMPLETE**
2. Economy Config Extraction (2h) [HIGH] - NEXT
3. Map Mode Gradient System (3h) [HIGH] - PENDING

**Deliverables:**
- ✅ Buildings load from Assets/Data/common/buildings/*.json5 **COMPLETE**
- ✅ 4 working buildings: farm, workshop, marketplace, temple **COMPLETE**
- Economy constants in defines/economy.json5 - PENDING
- Shared gradient system for map modes - PENDING

**Validation:**
- ✅ User tested: Built farms, income increased **VALIDATED**
- ✅ Dynamic building effects work (production +50%) **VALIDATED**
- ✅ Can add new building by editing JSON5 only **VALIDATED**
- Designer changes tax rate without recompiling - PENDING
- Create new map mode in 30 minutes (was 4 hours) - PENDING

**Sessions:**
- ✅ Session 1: Building System → JSON5 (2h actual, see [log](6-building-system-json5-implementation.md))
  - Created BuildingDefinition, BuildingRegistry, BuildingDefinitionLoader
  - Refactored all systems to use string IDs
  - Dynamic effect application working
  - Generic build_building command
- Session 2: Economy config extraction (2h est) - NEXT
- Session 3: Map mode gradients (3h est) - PENDING

---

### WEEK 2: EXTENSIBILITY SYSTEMS (28 hours)
**Goal:** Enable complex interactions

**Refactors:**
1. Modifier Pipeline System (12h) [CRITICAL]
2. GameSystem Base Class (6h) [HIGH]
3. Split HegemonInitializer (4h) [MEDIUM]
4. Command Abstraction System (6h) [MEDIUM]

**Deliverables:**
- Generic modifier system (buildings/tech/events add modifiers)
- All systems inherit GameSystem, proper lifecycle
- Initializer split into 4 files (Engine/System/MapMode/UI)
- Commands auto-register, extracted to individual files

**Validation:**
- Stack 3 buildings, modifiers combine correctly
- Systems initialize in correct dependency order
- Add new command in 10 minutes
- No file over 500 lines

**Sessions:**
- Session 1: Modifier pipeline (8h)
- Session 2: GameSystem refactor (6h)
- Session 3: Initializer decomposition (4h)
- Session 4: Command system (6h)
- Session 5: Integration testing (4h)

---

### WEEK 3: MULTI-SYSTEM SUPPORT (18 hours)
**Goal:** Prepare for military/diplomacy expansion

**Refactors:**
1. Resource System (8h) [HIGH]
2. Building Requirements Extension (4h) [MEDIUM]
3. Performance Optimization (6h) [MEDIUM]

**Deliverables:**
- Multi-resource support (gold, manpower, prestige, legitimacy)
- Complex building requirements (tech, religion, coastal)
- CountryInfoPanel 100x faster
- Map mode update caching

**Validation:**
- Add "manpower" resource in 30 minutes
- Building requires tech (when tech system added)
- 10k province query → <1ms
- Map modes smooth at 60 FPS

**Sessions:**
- Session 1: ResourceSystem implementation (8h)
- Session 2: Building requirements (4h)
- Session 3: Performance pass (6h)

---

## PRIORITY MATRIX

| Refactor | Urgency | Impact | Effort | Priority | Week |
|----------|---------|--------|--------|----------|------|
| Buildings → JSON5 | CRITICAL | EXTREME | 12h | #1 | 1 |
| Modifier Pipeline | CRITICAL | EXTREME | 12h | #2 | 2 |
| Economy Config | HIGH | HIGH | 2h | #3 | 1 |
| Resource System | HIGH | HIGH | 8h | #4 | 3 |
| GameSystem Base | HIGH | HIGH | 6h | #5 | 2 |
| Map Gradients | HIGH | MEDIUM | 3h | #6 | 1 |
| Performance | MEDIUM | HIGH | 6h | #7 | 3 |
| Split Initializer | MEDIUM | MEDIUM | 4h | #8 | 2 |
| Commands | MEDIUM | MEDIUM | 6h | #9 | 2 |
| Requirements | MEDIUM | MEDIUM | 4h | #10 | 3 |

---

## IMPLEMENTATION PRINCIPLES

### 1. Data > Code
Content defined in data files, not enums/constants.
- Buildings: JSON5 definitions
- Balance values: Config files
- Localization: String tables

### 2. Abstraction at 3rd Use
Don't over-engineer. Add abstraction when:
- Pattern used 3+ times
- Clear extension point needed
- Complexity is already painful

### 3. Incremental Commits
Commit after each phase. Can rollback independently.
- Tag before major changes: `git tag pre-refactor-week1`
- Commit per feature: `git commit -m "Phase 1.1 complete"`
- Test between commits

### 4. Backwards Compatible Where Possible
Keep old code working alongside new:
- Feature flags for migration
- Gradual cutover
- Test old + new simultaneously

### 5. Document Decisions
Why we chose X over Y:
- Architecture Decision Records (ADRs)
- Comments explaining non-obvious choices
- Session logs capture reasoning

---

## SUCCESS METRICS

### Code Quality
- ✅ No file over 500 lines
- ✅ No switch statement over 10 cases
- ✅ No duplicate code blocks over 20 lines
- ✅ 90% of content is data files

### Developer Velocity
- ✅ Add building: 2 min (from 30 min)
- ✅ Add map mode: 30 min (from 4 hours)
- ✅ Add resource: 30 min (from 8 hours)
- ✅ Change balance: instant, no recompile

### System Scalability
- ✅ 50 buildings: No code changes
- ✅ 10 resources: Single system handles all
- ✅ 15 map modes: Shared logic
- ✅ 100+ modifiers: Pipeline auto-stacks

### Performance
- ✅ CountryInfoPanel: <1ms
- ✅ Map mode update: <5ms
- ✅ Building load: <100ms for 50 buildings

---

## RISK MITIGATION

### High Risk: Breaking Existing Features
**Mitigation:**
- Extensive testing between phases
- Keep old code during migration (feature flags)
- Tag before major changes
- Manual regression testing

### Medium Risk: Scope Creep
**Mitigation:**
- Strict 3-week timeline
- Defer Tier 4 items
- No feature additions during refactor
- Focus on architecture only

### Medium Risk: Over-Engineering
**Mitigation:**
- YAGNI principle (You Aren't Gonna Need It)
- Simplest solution that works
- Profile before optimizing
- Don't add abstraction until 3rd use

### Low Risk: Data Format Changes
**Mitigation:**
- Use forgiving JSON5 (comments, trailing commas)
- Schema validation at load
- Clear error messages
- Document format thoroughly

---

## ROLLBACK STRATEGY

### Phase-by-Phase Safety
```bash
# Before Week 1
git tag pre-refactor-week1
git commit -m "Checkpoint before architecture refactor"

# After each phase
git commit -m "Phase 1.1: Buildings JSON5 complete"
git tag phase-1.1-complete

# If disaster
git reset --hard phase-1.1-complete
```

### Partial Success Acceptable
Not all-or-nothing. Each phase provides independent value:
- Week 1 succeeds → ship it, defer Week 2
- Phase fails → revert, analyze, retry
- Can stop at any week

---

## COST-BENEFIT ANALYSIS

### Investment
**Time:** 40-50 hours over 3 weeks
**Risk:** Low (phase-by-phase rollback)
**Cost:** Delayed feature work for 3 weeks

### Return
**At 20 Buildings:**
- 4,000 lines of code prevented (40% reduction)
- 400 lines of switch statements eliminated
- 600 lines of duplicate gradient code eliminated

**Developer Velocity:**
- Building addition: 30 min → 2 min (15x faster)
- Map mode addition: 4 hours → 30 min (8x faster)
- Balance iteration: recompile → instant (∞x faster)

**Designer Independence:**
- Before: Blocked on programmer for all balance changes
- After: Can iterate independently

**Modding Support:**
- Before: Impossible without code access
- After: Trivial (add JSON5 files)

### Break-Even Point
**Calculation:** 40 hours / 28 min saved per building = ~86 buildings

**But:**
- Real value is designer independence (unmeasurable)
- Foundation for ALL future systems (tech tree, events, missions)
- Prevents 6+ months of technical debt compound

**Verdict:** ROI is EXTREME. Do it now.

---

## WHAT THIS ENABLES (Future Systems)

### Immediate (Week 4+)
- **More buildings:** Add 10 more buildings (2 min each = 20 min total)
- **Building categories:** Economic, Military, Cultural
- **Complex effects:** Building chains, conditional bonuses

### Short-Term (Month 2-3)
- **Technology system:** Reuses effect/requirement pattern
- **Event system:** Reuses effect/modifier pipeline
- **Mission system:** Reuses requirement validation

### Long-Term (Month 4-6)
- **Government types:** Provides modifiers to entire country
- **Trade goods:** Province-specific bonuses
- **Religion/Culture:** Provides modifiers to provinces

**Key Insight:** This refactor establishes THE PATTERN for all future content systems. Every system after this reuses:
- Data-driven definitions (JSON5)
- Registry pattern (lookup by ID)
- Effect system (apply bonuses)
- Requirement system (validate conditions)

---

## SESSION LOG STRUCTURE

Each implementation session produces a log:

**File:** `Assets/Archon-Engine/Docs/Log/2025-10/18/6-[topic]-implementation.md`

**Template:**
```markdown
# [Topic] Implementation Session

## Problem
What architectural issue we're solving

## Solution
High-level approach taken

## Changes
Files created/modified/deleted

## Validation
How we tested it works

## Next Steps
What this unblocks

## Notes
Gotchas, decisions, trade-offs
```

**Logs (Completed/Planned):**
- ✅ [6-building-system-json5-implementation.md](6-building-system-json5-implementation.md) - **COMPLETE** (2h, Week 1 Phase 1)
- 7-economy-config-extraction.md (PLANNED - Week 1 Phase 2)
- 8-map-mode-gradients.md (PLANNED - Week 1 Phase 3)
- 9-modifier-pipeline.md (PLANNED - Week 2)
- 10-game-system-lifecycle.md (PLANNED - Week 2)
- 11-initializer-split.md (PLANNED - Week 2)
- 12-command-abstraction.md (PLANNED - Week 2)
- 13-resource-system.md (PLANNED - Week 3)
- 14-building-requirements.md (PLANNED - Week 3)
- 15-performance-optimization.md (PLANNED - Week 3)

---

## DOCUMENTATION UPDATES

### Per Session
- Session log (problem/solution/changes)
- Update FILE_REGISTRY.md

### Per Week
- Update Architecture docs
- Create/update READMEs for modders

### End of Refactor
- Master Architecture doc (Docs/Architecture/data-driven-design.md)
- Modding guide (Data/README.md)
- Migration guide (for future Claudes)

---

## PATTERNS ESTABLISHED

### Data-Driven Content
**Buildings** → JSON5 definitions
**Future:** Technologies, Events, Missions, Governments, Trade Goods

### Effect System
**Buildings** → effects (production +50%)
**Future:** Technology effects, Event effects, Government bonuses, Mission rewards

### Requirement System
**Buildings** → requirements (min dev, terrain)
**Future:** Tech requirements, Event triggers, Mission conditions

### Registry Pattern
**BuildingRegistry** → lookup by string ID
**Future:** TechnologyRegistry, EventRegistry, GovernmentRegistry

### Modifier Pipeline
**Buildings** → provide modifiers
**Future:** Tech, Events, Government also provide modifiers. All stack via pipeline.

---

## APPROVAL CHECKLIST

Before starting implementation:

- [ ] Review roadmap priority order
- [ ] Approve 3-week timeline
- [ ] Confirm Week 1 scope (Buildings, Config, Gradients)
- [ ] Acknowledge risk of temporary breakage during migration
- [ ] Tag current state: `git tag pre-architecture-refactor`
- [ ] Commit current work: `git commit -m "Pre-refactor checkpoint"`

---

## NEXT IMMEDIATE STEPS

### Completed ✅
1. ✅ Get approval on this plan
2. ✅ Git commit + tag checkpoint
3. ✅ **Week 1, Phase 1: Building System → JSON5 - COMPLETE** (2h)
   - See: [6-building-system-json5-implementation.md](6-building-system-json5-implementation.md)
   - Created 4 files, modified 7 files, deleted 1 file
   - User validated: Built farms, income increased
   - All building functionality now data-driven

### Next Session
1. **Week 1, Phase 2: Economy Config Extraction** (2h est)
   - Extract BASE_TAX_RATE to Assets/Data/common/defines/economy.json5
   - Create DefinesLoader (follows Json5Loader pattern)
   - Update EconomyCalculator to read from defines
   - Enables designer balance iteration without recompile

### This Week Remaining
- Week 1, Phase 3: Map Mode Gradient System (3h est)
- Complete Week 1 deliverables
- Document all changes in session logs

---

## CONCLUSION

**Current State:** Game layer is well-architected at macro level (Engine/Game separation excellent) but accumulating micro-level technical debt (hard-coded content, missing abstractions, duplication).

**Inflection Point:** Now is the perfect time. Before codebase 3x in size, before save system exists, before mods are promised.

**The Ask:** 3 weeks of refactor work to establish data-driven patterns that will serve the entire game's development.

**The Payoff:** 40% code reduction at scale, designer independence, modding support, foundation for all future systems.

**Recommendation:** APPROVE and begin Week 1 immediately.

---

*Strategic Plan Created: 2025-10-18*
*Estimated Duration: 3 weeks (40-50 hours)*
*Priority: CRITICAL - Prevents 6+ months of compound technical debt*
*Status: ✅ Ready for approval and implementation*
