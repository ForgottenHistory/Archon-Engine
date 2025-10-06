# Week 41 Plan: First Playable Game Loop

**Date:** 2025-10-06 to 2025-10-12
**Milestone:** First Playable - "Select Province → Build Farm → Income Increases"
**Estimated Effort:** 7 days
**Status:** 📋 Planning

---

## Overview

### Goal
Transform Hegemon from a technical demo into a playable game with a complete feedback loop:
1. Player can click provinces and see detailed information
2. Player can view country statistics (treasury, provinces, development)
3. Monthly tax collection generates visible income
4. Player can build a farm to increase development/income

### Why This Sequence?
- **Day 1-2 (Interaction):** Foundation - makes existing data accessible
- **Day 3-5 (Economy):** Core loop - money flows, player sees progress
- **Day 6-7 (Buildings):** Player agency - can improve provinces

### Success Criteria
By end of week, player can:
- ✅ Click a province and see: owner, development, terrain, income
- ✅ View country panel: treasury, total provinces, total development
- ✅ Watch treasury increase each month from tax collection
- ✅ Build a farm in a province (costs money, provides bonus)
- ✅ See income increase after building farm

---

## Architecture Decisions

### Layer Separation
- **Engine Layer:** ProvinceSystem, CountrySystem, TimeManager, CommandProcessor (already exist)
- **Game Layer:** Economic formulas, building definitions, UI panels

### Data Flow
```
User clicks province → ProvinceSelector → UI Panel updates
User clicks "Build Farm" → BuildBuildingCommand → CommandProcessor
TimeManager MonthlyTick → EconomySystem.CollectTaxes() → Treasury updated
```

### New Systems Location
```
Assets/Game/
├── Systems/
│   └── EconomySystem.cs           # Tax collection, treasury management
├── Commands/
│   ├── BuildBuildingCommand.cs    # Build building in province
│   └── AdjustDevelopmentCommand.cs # Dev command for testing
├── Definitions/
│   └── Buildings/
│       ├── BuildingDefinition.cs   # ScriptableObject
│       └── Farm.asset              # First building
├── UI/
│   ├── ProvinceInfoPanel.cs       # Province detail panel
│   ├── CountryInfoPanel.cs        # Country stats panel
│   └── BuildingButton.cs          # UI for building construction
└── Formulas/
    └── EconomyCalculator.cs       # Tax/income formulas
```

---

## Day-by-Day Breakdown

### **Days 1-2: Player Interaction Layer**

#### Day 1: Province Info Panel
**Goal:** Click province → see detailed information

**Tasks:**
1. Create ProvinceInfoPanel UI (Unity Canvas)
   - Province name/ID
   - Owner name
   - Development level
   - Terrain type
   - Base income (calculated)
   - Buildings list (empty for now)

2. Create ProvinceInfoPanel.cs
   - Subscribe to ProvinceSelector.OnProvinceSelected event
   - Query ProvinceQueries, CountryQueries for data
   - Display in UI fields
   - Handle "no province selected" state

3. Create ProvinceInfoPanel prefab
   - Right panel layout (300px wide)
   - Clean UI matching loading screen style
   - Fade in/out when province selected

**Deliverables:**
- [ ] ProvinceInfoPanel.cs (~150 lines)
- [ ] ProvinceInfoPanel prefab (Unity)
- [ ] Integrated with existing ProvinceSelector
- [ ] Shows all current province data

**Test:** Click 10 different provinces, verify data updates correctly

---

#### Day 2: Country Info Panel & Enhanced Tooltips
**Goal:** View country statistics and better tooltips

**Tasks:**
1. Create CountryInfoPanel UI
   - Country name/tag
   - Treasury (gold icon + amount)
   - Total provinces owned
   - Total development
   - Monthly income (calculated)
   - Monthly expenses (0 for now)

2. Create CountryInfoPanel.cs
   - Query ProvinceQueries.GetCountryProvinces()
   - Sum development across all provinces
   - Calculate total monthly income
   - Update every frame (cache per-frame)

3. Enhanced tooltips
   - Multi-line tooltip support
   - Format numbers with commas (1,234)
   - Add "gold" icon for money
   - Show more detailed province info on hover

4. Dev command: ChangeProvinceOwnerCommand
   - For testing ownership changes
   - Validates war/adjacency requirements (skip validation for dev build)
   - Emits ProvinceOwnershipChanged event

**Deliverables:**
- [ ] CountryInfoPanel.cs (~200 lines)
- [ ] CountryInfoPanel prefab (Unity)
- [ ] Enhanced tooltip system
- [ ] ChangeProvinceOwnerCommand.cs (~100 lines)

**Test:**
- Country panel shows correct totals
- Changing province owner updates country panel
- Tooltips show formatted data

---

### **Days 3-5: Economy System MVP**

#### Day 3: Economy Formulas & Tax Calculation
**Goal:** Define how income is calculated

**Tasks:**
1. Create EconomyCalculator.cs (Game/Formulas/)
   ```csharp
   public static FixedPoint64 CalculateBaseTax(byte development)
   public static FixedPoint64 CalculateProvinceIncome(ProvinceState state)
   public static FixedPoint64 CalculateCountryMonthlyIncome(ushort countryId)
   ```

2. Formula design decisions:
   - Base tax = development × 0.1 (dev 10 = 1.0 gold/month)
   - Terrain modifiers (optional): plains 1.0x, mountains 0.8x, etc.
   - Building bonuses (for day 6-7)

3. Update ProvinceInfoPanel to show calculated income
4. Update CountryInfoPanel to show monthly income

**Deliverables:**
- [ ] EconomyCalculator.cs (~150 lines)
- [ ] Income formulas documented
- [ ] UI panels show calculated income

**Test:**
- Province with dev 10 shows 1.0 gold/month income
- Country monthly income = sum of all provinces
- Formulas use FixedPoint64 (deterministic)

---

#### Day 4: Treasury & Tax Collection System
**Goal:** Money flows into treasury each month

**Tasks:**
1. Create EconomySystem.cs (Game/Systems/)
   - NativeArray<FixedPoint64> countryTreasuries (or managed array)
   - Subscribe to TimeManager.OnMonthlyTick
   - CollectTaxes(): iterate provinces, add income to owner's treasury
   - GetTreasury(countryId), AddGold(countryId, amount), RemoveGold(...)

2. Initialize treasury for all countries
   - Starting treasury: 100 gold (configurable)
   - Save/restore treasury in EngineInitializer (future)

3. Integrate with CountryInfoPanel
   - Show current treasury
   - Show +X gold this month (preview)

4. Event emission
   - TreasuryChangedEvent(countryId, oldAmount, newAmount)
   - UI subscribes to update in real-time

**Deliverables:**
- [ ] EconomySystem.cs (~300 lines)
- [ ] Monthly tax collection working
- [ ] TreasuryChangedEvent
- [ ] Treasury visible in UI

**Test:**
- Play for 3 months, verify treasury increases
- Check gold amount matches sum of province incomes
- Verify deterministic (run twice with same seed = same treasury)

---

#### Day 5: Economy UI Polish & Commands
**Goal:** Clean economy visualization and testing tools

**Tasks:**
1. Treasury display polish
   - Animate treasury changes (smooth count-up)
   - Show "+X gold" floating text when tax collected
   - Gold icon/sprite

2. Dev commands for economy testing
   - AddGoldCommand(countryId, amount) - testing
   - RemoveGoldCommand(countryId, amount) - testing
   - SetDevelopmentCommand(provinceId, development) - testing

3. Economy map mode (optional stretch goal)
   - Color provinces by monthly income
   - Green gradient (high income) to red (low income)
   - Tooltip shows "Income: X gold/month"

4. Documentation
   - Update Game/FILE_REGISTRY.md with EconomySystem
   - Document formulas in GAME_CURRENT_FEATURES.md

**Deliverables:**
- [ ] Polished treasury UI
- [ ] 3 dev commands for testing
- [ ] Economy map mode (optional)
- [ ] Updated documentation

**Test:**
- Treasury UI looks professional
- Dev commands work correctly
- Economy map mode (if implemented) updates monthly

---

### **Days 6-7: First Building (Farm)**

#### Day 6: Building System Architecture
**Goal:** Building definition and command system

**Tasks:**
1. Create BuildingDefinition.cs (ScriptableObject)
   ```csharp
   public class BuildingDefinition : ScriptableObject
   {
       public string buildingName;
       public string description;
       public FixedPoint64 buildCost;
       public byte developmentRequirement;
       public FixedPoint64 incomeBonus;      // +X gold/month
       public byte developmentBonus;          // +X development
       public Sprite icon;
   }
   ```

2. Create Farm.asset (first building)
   - Name: "Farm"
   - Description: "Increases food production and development"
   - Cost: 50 gold
   - Dev requirement: 5
   - Income bonus: +0.5 gold/month
   - Development bonus: +2 development

3. Province building storage
   - Add to ProvinceColdData: `List<ushort> buildingIds`
   - BuildingRegistry: Register all building definitions
   - ProvinceQueries.GetProvinceBuildings(provinceId)

4. Create BuildBuildingCommand.cs
   ```csharp
   public struct BuildBuildingCommand : ICommand
   {
       public ushort provinceId;
       public ushort buildingId;
       public ushort countryId;  // who's building

       public bool Validate(GameState state)
       {
           // Check: country owns province
           // Check: sufficient gold
           // Check: development requirement met
           // Check: building not already built
       }

       public void Execute(GameState state)
       {
           // Deduct gold from treasury
           // Add building to province
           // Apply development bonus
           // Apply income bonus (via modifier system)
           // Emit BuildingBuiltEvent
       }
   }
   ```

**Deliverables:**
- [ ] BuildingDefinition.cs (~100 lines)
- [ ] Farm.asset (ScriptableObject)
- [ ] BuildBuildingCommand.cs (~150 lines)
- [ ] Building storage in ProvinceColdData

**Test:**
- Farm definition loads correctly
- BuildBuildingCommand validation works
- Building farm deducts gold and adds to province

---

#### Day 7: Building UI & Integration
**Goal:** Player can build farm via UI

**Tasks:**
1. Update ProvinceInfoPanel
   - "Buildings" section showing built buildings
   - "Available Buildings" section (can afford + requirements met)
   - Building button: icon, name, cost

2. Create BuildingButton.cs
   - Show building icon, name, cost
   - Disable if can't afford or requirements not met
   - Click → BuildBuildingCommand → CommandProcessor
   - Show success/failure message

3. Building effects integration
   - EconomyCalculator includes building income bonuses
   - ProvinceSystem includes building development bonuses
   - UI updates after building built

4. Visual feedback
   - "Building constructed!" message
   - Treasury updates (gold deducted)
   - Development updates (+2 dev)
   - Province income updates (+0.5 gold/month)

5. Testing & polish
   - Build 5 farms in different provinces
   - Verify income increases correctly
   - Verify treasury balance is correct
   - Verify deterministic (same actions = same result)

**Deliverables:**
- [ ] Updated ProvinceInfoPanel with buildings UI
- [ ] BuildingButton.cs (~80 lines)
- [ ] Complete building → income feedback loop
- [ ] Session log documenting the week

**Test:**
- Click province with 100 gold, dev 10
- Build farm (cost 50)
- Verify: treasury = 50, dev = 12, income increased by 0.5
- Next month: treasury increases by new income amount

---

## Expected Code Metrics

### New Files (Game Layer)
```
Systems/
  EconomySystem.cs                ~300 lines

Commands/
  BuildBuildingCommand.cs         ~150 lines
  AdjustDevelopmentCommand.cs     ~80 lines
  ChangeProvinceOwnerCommand.cs   ~100 lines

Definitions/Buildings/
  BuildingDefinition.cs           ~100 lines
  Farm.asset                      (ScriptableObject)

UI/
  ProvinceInfoPanel.cs            ~200 lines
  CountryInfoPanel.cs             ~250 lines
  BuildingButton.cs               ~80 lines

Formulas/
  EconomyCalculator.cs            ~150 lines

Total: ~1,410 lines of game code
```

### Unity Assets
- ProvinceInfoPanel.prefab
- CountryInfoPanel.prefab
- Farm.asset (ScriptableObject)
- Building icons (sprites)

---

## Architecture Validation

### Engine Usage (No Engine Changes!)
- ✅ ProvinceSystem.GetProvinceState() - read province data
- ✅ CountrySystem.GetCountryColor() - country info
- ✅ TimeManager.OnMonthlyTick - trigger tax collection
- ✅ CommandProcessor.SubmitCommand() - building construction
- ✅ EventBus.Subscribe<TreasuryChanged>() - UI updates
- ✅ ProvinceQueries, CountryQueries - data access

### Game Layer Additions
- ✅ EconomySystem (IGameSystem interface - future)
- ✅ Economic formulas (game policy)
- ✅ Building definitions (game content)
- ✅ UI panels (game presentation)

### Determinism Check
- ✅ All calculations use FixedPoint64
- ✅ Commands are deterministic
- ✅ No Unity.Random calls
- ✅ Tax collection order is deterministic (iterate provinces in ID order)

---

## Risk Mitigation

### Potential Issues

**UI Layout Complexity**
- **Risk:** UI panels overlap or look bad
- **Mitigation:** Use anchored layouts, test at multiple resolutions
- **Fallback:** Simple text-based UI first, polish later

**Building Modifier System**
- **Risk:** Complex modifier stacking (multiple buildings)
- **Mitigation:** Start with simple +X bonuses, avoid percentages
- **Fallback:** Buildings only affect development, not income directly

**Performance with Many Buildings**
- **Risk:** Iterating all buildings per province expensive
- **Mitigation:** Cache province income per-frame (frame-coherent)
- **Fallback:** Recalculate only when buildings change (dirty flag)

**Treasury Overflow**
- **Risk:** FixedPoint64 overflow with large treasuries
- **Mitigation:** Clamp to reasonable max (1,000,000 gold)
- **Fallback:** Treasury as FixedPoint64 has huge range (±2 billion)

---

## Testing Strategy

### Daily Tests
**Day 1:** Click provinces, verify panel updates
**Day 2:** Change ownership, verify country panel updates
**Day 3:** Check income calculations match formulas
**Day 4:** Play 12 months, verify treasury grows correctly
**Day 5:** Use dev commands, verify economy updates
**Day 6:** Build farm command, verify gold deducted
**Day 7:** Full loop: start → build farm → income increases

### Integration Test (End of Week)
1. Start new game
2. Player country has 100 gold, 10 provinces
3. Month 1: Collect ~10 gold tax (assume avg dev 10)
4. Month 2: Treasury = 110 gold
5. Build farm in capital (cost 50, dev +2)
6. Treasury = 60 gold
7. Month 3: Collect ~10.5 gold (farm adds +0.5)
8. Treasury = 70.5 gold
9. **Success:** Income increased after building farm

---

## Documentation Updates

### Update These Files
1. **Assets/Docs/GAME_CURRENT_FEATURES.md**
   - Add Economy System section
   - Add Building System section
   - Add UI Systems section

2. **Assets/Game/FILE_REGISTRY.md**
   - Add all new scripts with descriptions
   - Update total file count
   - Document extension points

3. **Session Log (This Document)**
   - Update with actual implementation notes
   - Document decisions made during week
   - Note any deviations from plan

---

## Stretch Goals (If Time Permits)

### Additional Buildings
- Market (+income, no dev bonus)
- Workshop (+income, higher cost)
- Temple (+stability, future mechanic)

### Economy Map Mode
- Visualize province income
- Green (high) to red (low) gradient
- Update monthly

### Building Construction Time
- Buildings take X months to complete
- Show "Under Construction" in province panel
- Complete on future monthly tick

### Province Modifiers System
- Generic modifier system (key-value pairs)
- Buildings add modifiers
- Future: events, terrain, etc. add modifiers
- EconomyCalculator reads modifiers

---

## Success Metrics

### End of Week Goals
- ✅ Player can interact with provinces (click, select)
- ✅ Economy generates income monthly
- ✅ Player can spend money on buildings
- ✅ Buildings provide visible benefit
- ✅ Complete feedback loop working

### Code Quality
- ✅ All files under 500 lines
- ✅ No engine changes required
- ✅ Deterministic (FixedPoint64 for all calculations)
- ✅ Zero allocations in hot paths
- ✅ Full documentation

### Playability
- ✅ Game is "fun" to watch (treasury growing)
- ✅ Player has agency (can build things)
- ✅ Decisions matter (spend gold wisely)
- ✅ Portfolio-ready (can show to others)

---

## Next Week Preview (Week 42)

After completing first playable, logical next steps:

**Option A: Expand Economy**
- More buildings (5-10 total)
- Building categories (economic, military, cultural)
- Province trade goods (bonus resources)

**Option B: Add Military**
- Army units (strength, movement)
- Combat resolution
- Conquer provinces via military

**Option C: Multiple Countries + AI**
- Other countries also collect taxes
- Other countries build buildings
- Simple AI (build farms when gold > 100)

**Option D: Save/Load**
- Serialize economy state
- Save/load commands
- Continue game across sessions

**Recommendation:** Option A (Expand Economy) → builds on this week's foundation, low risk, high value

---

## Notes for Claude

### Context for Future Sessions
This week plan creates the **first playable game loop** - the foundation for all future gameplay systems. Every system after this (military, diplomacy, AI) will interact with the economy.

### Key Architecture Principles
- Game layer defines formulas and content
- Engine provides data structures and APIs
- Commands for all state changes
- Events for UI updates
- FixedPoint64 for all calculations

### If Things Go Wrong
- **Behind schedule:** Cut day 7 (building UI), add next week
- **UI too complex:** Text-only panels first, visual polish later
- **Bugs in economy:** Add debug logging, unit tests for calculations

### When to Ask for Help
- Treasury not updating → check TimeManager.OnMonthlyTick subscription
- UI not updating → check EventBus subscription
- Commands failing → check validation logic
- Determinism broken → check for float usage

---

*Week 41 - First Playable: Transform technical demo into actual game*
*Previous milestone: Week 40 - Engine foundation complete (120+ features)*
*Next milestone: Week 42 - Expand gameplay systems*
