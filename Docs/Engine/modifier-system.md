# Universal Modifier System

**📊 Implementation Status:** ✅ Implemented (ModifierSystem in GameState, scope inheritance working)

> **📚 Architecture Context:** This document describes the universal bonus system. See [master-architecture-document.md](master-architecture-document.md) for overall architecture.

## Executive Summary
**Question**: How do buildings, tech, events, and government bonuses stack and apply?
**Answer**: Universal modifier pipeline with scope inheritance (Province ← Country ← Global)
**Key Principle**: Engine provides mechanism (ModifierSystem), Game defines policy (which modifiers exist)
**Performance**: O(1) lookup, <0.1ms application, zero allocations, multiplayer-safe

## The Big Picture - Modifier Flow

```
Sources (Buildings/Tech/Events) → ModifierSystem (Scope Inheritance) → Applied Values

Example:
  Global:  +10% production (tech)
  Country: +20% production (government)
  Province: +50% production (farm building)
  ──────────────────────────────────
  Final:   (base + 0) × (1 + 0.1 + 0.2 + 0.5) = base × 1.8
```

## Core Design Principles

### 1. Universal Accumulation Formula (Paradox Standard)

All grand strategy games use the same formula:

```
Final Value = (Base Value + Additive Bonuses) × (1 + Multiplicative Bonuses)
```

**Industry Pattern:**
- EU4: `production = base × (1 + production_efficiency) × goods_produced_modifier`
- CK3: `levy = base × (1 + levy_size) × buildings_levy_mult`
- Stellaris: `output = base × (1 + output_add) × output_mult`
- Vic3: `throughput = base × (1 + throughput_add) × throughput_mult`

**Why this formula:**
- Additive bonuses stack linearly (+5, +10 = +15 total)
- Multiplicative bonuses stack additively THEN multiply (50% + 50% = 100% total, not 125%)
- Prevents exponential scaling (3 buildings × 1.5 each = 3.375x, should be 2.5x)
- Designer-friendly (percentages add intuitively)

```csharp
// ✅ CORRECT: Modifiers stack additively in same type
Farm:     +50% production (multiplicative[ProductionEfficiency] += 0.5)
Workshop: +50% production (multiplicative[ProductionEfficiency] += 0.5)
Result:   (base + 0) × (1 + 0.5 + 0.5) = base × 2.0  // 100% bonus

// ❌ WRONG: Multiplying modifiers separately
Farm:     base × 1.5
Workshop: (base × 1.5) × 1.5 = base × 2.25  // 125% bonus (exponential!)
```

### 2. Engine-Game Separation (Mechanism vs Policy)

**ENGINE provides mechanism:**
- ModifierSet: Fixed-size storage (512 types, 4KB per scope)
- ModifierSystem: Scope inheritance (Province ← Country ← Global)
- ModifierSource: Track origin (building/tech/event), temporary support
- Performance guarantee: <0.1ms lookup, zero allocations

**GAME defines policy:**
- ModifierType enum: Which modifiers exist (LocalProductionEfficiency, NationalTaxModifier, etc.)
- ModifierTypeRegistry: String ↔ ID conversion (JSON5 uses strings, Engine uses ushort)
- Sources: Which systems apply modifiers (BuildingConstructionSystem, TechSystem, EventSystem)

```csharp
// ENGINE: Generic mechanism (no game knowledge)
public class ModifierSystem {
    public float GetProvinceModifier(ushort provinceId, ushort countryId,
                                     ushort modifierTypeId, float baseValue) {
        // Scope chain: Province Local + Country + Global
        var provinceScope = provinceScopes[provinceId];
        var countryScope = countryScopes[countryId];
        return provinceScope.ApplyModifier(modifierTypeId, baseValue, countryScope, globalScope);
    }
}

// GAME: Specific policy (defines what exists)
public enum ModifierType : ushort {
    LocalProductionEfficiency = 0,
    LocalTaxModifier = 1,
    NationalTaxModifier = 10,
    GlobalProductionModifier = 20,
    // ... game-specific types
}
```

### 3. Scope Inheritance (Automatic Stacking)

Modifiers inherit down the hierarchy:
- **Global scope**: Applies to everyone (tech, disasters, golden ages)
- **Country scope**: Applies to all country provinces (government, policies)
- **Province scope**: Local only (buildings, terrain, governors)

**Accumulation is automatic:**

```csharp
// Province gets ALL modifiers in scope chain
Province 42 owned by Country 5:
  Province Local: +50% production (farm building)
  Country 5:      +20% production (government type)
  Global:         +10% production (tech researched)
  ─────────────────────────────────────────────────
  Final:          (base + 0) × (1 + 0.5 + 0.2 + 0.1) = base × 1.8

// Different province, same country
Province 99 owned by Country 5:
  Province Local: +0% production (no buildings)
  Country 5:      +20% production (government type - inherited)
  Global:         +10% production (tech - inherited)
  ─────────────────────────────────────────────────
  Final:          (base + 0) × (1 + 0.0 + 0.2 + 0.1) = base × 1.3
```

**Why this is powerful:**
- Research tech → ALL provinces in ALL countries get bonus (if global)
- Change government → ALL provinces in that country get bonus (no iteration needed)
- Build farm → ONLY that province gets bonus
- Zero code duplication - inheritance is automatic

### 4. Source Tracking (Tooltips and Removal)

Every modifier knows its origin:

```csharp
public struct ModifierSource {
    public SourceType Type;        // Building, Technology, Event, Government, etc.
    public uint SourceID;          // Building hash, tech ID, event ID
    public ushort ModifierTypeId;  // Which modifier (production, tax, etc.)
    public float Value;            // Bonus amount
    public bool IsMultiplicative;  // Additive vs multiplicative
    public bool IsTemporary;       // Permanent vs timed
    public int ExpirationTick;     // When temporary modifiers expire
}
```

**Source tracking enables:**

**Tooltips:**
```
Production: 15.0 gold/month
  Base:      10.0
  Modifiers: +5.0 (+50%)
    Farm:      +50%  (local)
    Tech:      +10%  (global)
    Govern:    +20%  (country)
```

**Removal:**
```csharp
// Destroy farm building → remove ALL modifiers from that source
modifierSystem.RemoveProvinceModifiersBySource(
    provinceId,
    ModifierSource.SourceType.Building,
    buildingHash
);
// All farm bonuses removed automatically (production, tax, etc.)
```

**Temporary modifiers:**
```csharp
// Event: "Harvest Festival" gives +20% production for 12 months
var source = ModifierSource.CreateTemporary(
    type: SourceType.Event,
    sourceId: eventId,
    modifierTypeId: LocalProductionEfficiency,
    value: 0.2,
    isMultiplicative: true,
    expirationTick: currentTick + 12
);

// ModifierSystem.ExpireModifiers() called every tick
// Automatically removes expired modifiers (no manual tracking)
```

### 5. Fixed-Size Storage (Performance Contract)

**Design constraint:** 512 modifier types maximum

**Why fixed-size:**
- Zero allocations during gameplay (multiplayer requirement)
- Cache-friendly (4KB = one cache line)
- Deterministic (same memory layout on all platforms)
- O(1) lookup (ushort ID → array index)

**Memory cost:**
```
ModifierSet size:
  512 types × 2 arrays (additive/multiplicative) × 4 bytes (float) = 4KB

Total memory (10k provinces):
  Global scope:    1 × 4KB = 4KB
  Country scope:   256 × 4KB = 1MB
  Province scope:  10,000 × 4KB = 40MB
  ────────────────────────────────
  Total:           ~41MB (acceptable)
```

**Comparison to Paradox games:**
- EU4: ~230 modifier types
- CK3: ~200 modifier types
- Stellaris: ~180 modifier types
- Our limit: 512 types (2x largest Paradox game)

### 6. Dirty Flag Optimization (Cached Rebuilds)

Modifiers change rarely (buildings constructed, tech researched), but are queried frequently (every income calculation).

**Pattern:**
```csharp
public struct ScopedModifierContainer {
    private ModifierSet cachedModifierSet;  // Pre-calculated total
    private bool isDirty;                   // Needs rebuild?

    public void Add(ModifierSource source) {
        localModifiers.Add(source);
        isDirty = true;  // Mark for rebuild
    }

    public float ApplyModifier(ushort typeId, float baseValue, ScopedModifierContainer? parent) {
        if (isDirty) {
            RebuildCache(parent);  // O(n) rebuild where n = active modifiers
            isDirty = false;
        }
        return cachedModifierSet.ApplyModifier(typeId, baseValue);  // O(1) cached lookup
    }
}
```

**Performance:**
- 99% of queries: O(1) cached lookup (modifier didn't change)
- 1% of queries: O(n) rebuild (modifier added/removed)
- Rebuild happens once per change, used many times

**Example:**
```
Frame 1: Build farm → isDirty = true
Frame 2: Calculate income → Rebuild cache (farm +50%), apply cached value
Frame 3: Calculate income → Use cached value (no rebuild)
Frame 4: Calculate income → Use cached value (no rebuild)
...
Frame 1000: Research tech → isDirty = true (at country scope)
Frame 1001: Calculate income → Rebuild cache (tech +10%), apply cached value
```

## Common Patterns

### Pattern: Building Applies Modifiers on Construction

```csharp
// Game layer: BuildingConstructionSystem
private void CompleteBuilding(ushort provinceId, BuildingDefinition building) {
    // 1. Convert JSON5 modifiers (string keys) → Engine modifiers (ushort IDs)
    foreach (var modifier in building.Modifiers) {
        ushort? typeId = ModifierTypeRegistry.GetId(modifier.Key);
        bool isMult = ModifierTypeRegistry.IsMultiplicative(modifier.Key);

        // 2. Create modifier source
        var source = ModifierSource.CreatePermanent(
            type: SourceType.Building,
            sourceId: building.Id.GetHashCode(),
            modifierTypeId: typeId.Value,
            value: modifier.Value,
            isMultiplicative: isMult
        );

        // 3. Add to province scope (Engine)
        modifierSystem.AddProvinceModifier(provinceId, source);
    }
}
```

### Pattern: Formula Uses Modifiers

```csharp
// Game layer: EconomyCalculator
public static FixedPoint64 CalculateProvinceIncome(
    ushort provinceId, ushort countryId, HegemonProvinceSystem hegemon, ModifierSystem modifiers) {

    byte baseTax = hegemon.GetBaseTax(provinceId);
    byte baseProduction = hegemon.GetBaseProduction(provinceId);

    // Apply modifiers (automatic scope inheritance: province + country + global)
    float modifiedTax = modifiers.GetProvinceModifier(
        provinceId, countryId, ModifierType.LocalTaxModifier, baseTax);

    float modifiedProduction = modifiers.GetProvinceModifier(
        provinceId, countryId, ModifierType.LocalProductionEfficiency, baseProduction);

    return FixedPoint64.FromFloat((modifiedTax + modifiedProduction) * TAX_RATE);
}
```

### Pattern: Tech Applies Country-Wide Modifiers

```csharp
// Game layer: TechSystem (future)
private void ResearchComplete(ushort countryId, TechDefinition tech) {
    foreach (var modifier in tech.Modifiers) {
        var source = ModifierSource.CreatePermanent(
            type: SourceType.Technology,
            sourceId: tech.Id,
            modifierTypeId: ModifierTypeRegistry.GetId(modifier.Key).Value,
            value: modifier.Value,
            isMultiplicative: true
        );

        // Add to COUNTRY scope → all provinces inherit automatically
        modifierSystem.AddCountryModifier(countryId, source);
    }
}
```

### Pattern: Event Applies Temporary Modifiers

```csharp
// Game layer: EventSystem (future)
private void TriggerEvent(ushort provinceId, EventDefinition evt) {
    foreach (var modifier in evt.Modifiers) {
        var source = ModifierSource.CreateTemporary(
            type: SourceType.Event,
            sourceId: evt.Id,
            modifierTypeId: ModifierTypeRegistry.GetId(modifier.Key).Value,
            value: modifier.Value,
            isMultiplicative: true,
            expirationTick: currentTick + evt.DurationMonths
        );

        modifierSystem.AddProvinceModifier(provinceId, source);
    }

    // Automatic expiration on monthly tick (no manual cleanup needed)
}
```

## Performance Characteristics

**Lookup Performance:**
- Cached lookup: O(1) - array index by ushort ID
- Rebuild: O(n) where n = active modifiers in scope chain (typically <50)
- Dirty flag ensures rebuild only when needed

**Memory Profile:**
- Fixed allocation: 4KB per ModifierSet
- Zero allocations during gameplay (NativeArray pools, struct-based)
- Deterministic layout (multiplayer-safe)

**Scale Testing:**
- 10k provinces × 512 types = 40MB (province scope)
- 256 countries × 512 types = 1MB (country scope)
- 1 global × 512 types = 4KB (global scope)
- Total: ~41MB (well within budget)

**Query Patterns:**
```
Typical frame:
  1000 income calculations → 1000 cached lookups (O(1) each)
  0 modifier changes → 0 rebuilds
  Total: <0.1ms

Building constructed:
  1 modifier added → 1 scope marked dirty
  Next query: 1 rebuild (O(50) = iterate 50 active modifiers)
  Subsequent queries: Cached (O(1))
  Total: <0.2ms one-time cost
```

## Multiplayer Safety

**Determinism requirements:**
- Fixed-size data structures (no dynamic allocation)
- Consistent iteration order (array-based, not hash-based)
- Floating-point operations produce identical results (same formula order)

**Network optimization:**
- Modifiers are state, not transmitted every frame
- Commands transmit modifier changes (AddModifier, RemoveModifier)
- Clients rebuild local cache from commands (deterministic result)

**Example:**
```
Server: BuildBuildingCommand(provinceId=42, buildingId="farm")
  → Apply modifiers locally
  → Transmit command to clients

Clients: Receive BuildBuildingCommand
  → Execute same command locally
  → Apply same modifiers
  → Result: Identical state (deterministic)
```

## Design Rationale

### Why Fixed-Size Arrays (Not Dictionary)?

**❌ Dictionary/List approach:**
```csharp
private Dictionary<ushort, float> additive;  // Dynamic allocation
private Dictionary<ushort, float> multiplicative;

// Problems:
// - Allocates on Add/Remove (GC pressure)
// - Non-deterministic iteration order (hash collisions differ by platform)
// - Slower lookup (hash calculation, collision resolution)
// - Variable memory usage (breaks multiplayer)
```

**✅ Fixed array approach:**
```csharp
private fixed float additive[512];  // Stack allocation, no GC
private fixed float multiplicative[512];

// Benefits:
// - Zero allocations (multiplayer requirement)
// - O(1) lookup (array index)
// - Deterministic (same memory layout on all platforms)
// - Cache-friendly (contiguous memory)
```

### Why Scope Inheritance (Not Per-Province Calculation)?

**❌ Flat approach:**
```csharp
// Calculate modifiers for each province individually
for (each province) {
    float bonus = 0;
    bonus += GetBuildingBonus(province);
    bonus += GetTechBonus(GetOwner(province));  // Repeated for EVERY province!
    bonus += GetGovernmentBonus(GetOwner(province));
    bonus += GetGlobalBonus();
}
```

**✅ Scope inheritance:**
```csharp
// Calculate country modifiers ONCE, provinces inherit
CountryScope[5].Add(techBonus);      // Added once
CountryScope[5].Add(governmentBonus);

// Each province just references parent
for (each province in Country 5) {
    bonus = ProvinceScope[province].Apply(baseValue, CountryScope[5]);
    // Country bonuses applied automatically (cached, not recalculated)
}
```

### Why 512 Modifier Types?

**Design constraints:**
- EU4: ~230 types (largest Paradox game)
- Power of 2 for alignment (better CPU cache performance)
- Ushort ID (2 bytes, not 4 bytes like uint)

**Headroom calculation:**
```
Current Hegemon usage: 13 types
Future expansion:
  - Military:    30-49  (20 types)
  - Diplomatic:  50-69  (20 types)
  - Religious:   70-89  (20 types)
  - Trade:       90-109 (20 types)
  - Technology:  110-129 (20 types)
  - Character:   130-149 (20 types)
  - Terrain:     150-169 (20 types)
  - ...etc
Total realistic: ~200-300 types

512 = 2x largest Paradox game, 2x realistic expansion
```

## Anti-Patterns to Avoid

### ❌ DON'T: Multiply Modifiers Separately

```csharp
// WRONG: Exponential stacking
float bonus = baseValue;
bonus *= (1 + farm_bonus);      // × 1.5
bonus *= (1 + workshop_bonus);  // × 1.5 again = 2.25x total
bonus *= (1 + tech_bonus);      // × 1.1 again = 2.475x total
```

### ✅ DO: Accumulate Then Multiply Once

```csharp
// CORRECT: Linear stacking
float totalMult = 0;
totalMult += farm_bonus;      // +0.5
totalMult += workshop_bonus;  // +0.5 = 1.0 total
totalMult += tech_bonus;      // +0.1 = 1.1 total
float result = baseValue * (1 + totalMult);  // × 2.1
```

### ❌ DON'T: Store Modifiers in Multiple Places

```csharp
// WRONG: Duplication
public class Province {
    public List<Modifier> provinceModifiers;  // Stored here
}
public class Country {
    public List<Modifier> countryModifiers;   // AND here
}
public class ModifierSystem {
    public List<Modifier> allModifiers;       // AND here??
}
// Problem: Which is source of truth? They can desync!
```

### ✅ DO: Single Source of Truth

```csharp
// CORRECT: One owner
public class ModifierSystem {
    private ScopedModifierContainer globalScope;
    private NativeArray<ScopedModifierContainer> countryScopes;
    private NativeArray<ScopedModifierContainer> provinceScopes;
    // ModifierSystem owns ALL modifiers, others query
}
```

### ❌ DON'T: Forget to Remove Modifiers

```csharp
// WRONG: Orphaned modifiers
public void DestroyBuilding(ushort provinceId, string buildingId) {
    buildings.Remove(buildingId);
    // Forgot to remove modifiers! Farm bonus stays forever!
}
```

### ✅ DO: Source-Based Removal

```csharp
// CORRECT: Remove by source
public void DestroyBuilding(ushort provinceId, string buildingId) {
    buildings.Remove(buildingId);

    // Remove ALL modifiers from this building (automatic)
    modifierSystem.RemoveProvinceModifiersBySource(
        provinceId,
        ModifierSource.SourceType.Building,
        buildingId.GetHashCode()
    );
}
```

---

**Related Documentation:**
- [data-flow-architecture.md](data-flow-architecture.md) - System communication patterns
- [master-architecture-document.md](master-architecture-document.md) - Overall architecture

**Implementation Reference:**
- Engine: `Scripts/Core/Modifiers/` (6 core classes)
- Game: `Assets/Game/Economy/` (ModifierType, ModifierTypeRegistry)
- Integration: BuildingConstructionSystem, EconomyCalculator, EconomySystem
