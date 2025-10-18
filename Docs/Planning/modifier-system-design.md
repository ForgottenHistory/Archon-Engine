# Engine Modifier System - Design & Implementation Plan
**Date**: 2025-10-18
**Type**: Engine Infrastructure Design
**Scope**: Core modifier system for grand strategy games
**Priority**: CRITICAL (blocks tech trees, events, government bonuses, complex gameplay)

---

## Executive Summary

**Problem**: Currently, bonuses are hardcoded (buildings give fixed multipliers via switch statements). Can't stack effects from multiple sources (building + tech + government + events). Every formula duplicates modifier logic.

**Solution**: Universal modifier system in ENGINE layer. Fixed-size arrays for performance, generic accumulation pipeline, scope-based application.

**Goal**: Prove ENGINE can handle modifiers at scale (10k provinces × 512 modifier types) with <0.1ms overhead.

**Effort**: 6-8 hours (4-5h ENGINE infrastructure, 1-2h GAME integration, 1h testing)

---

## Core Principles

### 1. Engine Provides Mechanism, Game Provides Policy

**ENGINE (Archon-Engine):**
- ModifierContainer - Storage and accumulation
- ModifierScope - Province/Country/Global/Unit contexts
- ModifierSource - Track who applied what
- Performance guarantees - Fixed-size, cache-friendly

**GAME (Hegemon):**
- Which modifiers exist (enum values)
- Which sources apply modifiers (buildings, tech, events)
- Game-specific formulas (how modifiers change values)

### 2. Universal Pattern Across All Paradox Games

```
Base Value → Add Flat Bonuses → Apply Multiplicative Bonuses → Final Value
```

**Examples:**
- EU4: `production = base * (1 + production_efficiency) * goods_produced_modifier`
- CK3: `levy = base * (1 + levy_size) * buildings_levy_mult`
- Stellaris: `output = base * (1 + output_add) * output_mult`

**Our System:**
```csharp
float ApplyModifiers(float baseValue, ModifierType type, ModifierScope scope) {
    var mods = GetModifiers(scope, type);
    float result = baseValue + mods.Additive;      // Flat bonuses
    result *= (1.0f + mods.Multiplicative);        // Percentage bonuses
    return result;
}
```

### 3. Performance Requirements (Engine Contract)

- **Storage**: O(1) lookup by modifier type
- **Application**: <0.1ms to apply all modifiers to a value
- **Memory**: Fixed-size per scope (no allocations during gameplay)
- **Scale**: 10,000 provinces × 512 modifier types = 20MB max
- **Determinism**: Identical results across platforms (multiplayer-safe)

---

## Architecture Design

### Layer 1: Core Data Structures (ENGINE)

```csharp
/// <summary>
/// ENGINE: Fixed-size modifier storage (cache-friendly, deterministic)
/// Pattern used by: EU4, CK3, Stellaris, Victoria 3
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct ModifierSet
{
    private const int MAX_MODIFIER_TYPES = 512;

    // Separate arrays for additive and multiplicative modifiers
    // Using fixed-size arrays for cache locality and zero allocations
    private fixed float additive[MAX_MODIFIER_TYPES];
    private fixed float multiplicative[MAX_MODIFIER_TYPES];

    /// <summary>
    /// Get accumulated modifier value
    /// </summary>
    public ModifierValue Get(ushort modifierTypeId)
    {
        return new ModifierValue {
            Additive = additive[modifierTypeId],
            Multiplicative = multiplicative[modifierTypeId]
        };
    }

    /// <summary>
    /// Add a modifier (stacks with existing)
    /// </summary>
    public void Add(ushort modifierTypeId, float value, bool isMultiplicative)
    {
        if (isMultiplicative)
            multiplicative[modifierTypeId] += value;
        else
            additive[modifierTypeId] += value;
    }

    /// <summary>
    /// Remove a modifier (for temporary effects)
    /// </summary>
    public void Remove(ushort modifierTypeId, float value, bool isMultiplicative)
    {
        if (isMultiplicative)
            multiplicative[modifierTypeId] -= value;
        else
            additive[modifierTypeId] -= value;
    }

    /// <summary>
    /// Clear all modifiers (reset to zero)
    /// </summary>
    public void Clear()
    {
        unsafe
        {
            fixed (float* add = additive, mult = multiplicative)
            {
                // Vectorized clear for performance
                for (int i = 0; i < MAX_MODIFIER_TYPES; i++)
                {
                    add[i] = 0f;
                    mult[i] = 0f;
                }
            }
        }
    }
}

/// <summary>
/// ENGINE: Modifier value with additive and multiplicative components
/// </summary>
public struct ModifierValue
{
    public float Additive;           // Flat bonus (e.g., +5 production)
    public float Multiplicative;     // Percentage bonus (e.g., +50% = 0.5)

    /// <summary>
    /// Apply this modifier to a base value
    /// Formula: (base + additive) * (1 + multiplicative)
    /// </summary>
    public float Apply(float baseValue)
    {
        return (baseValue + Additive) * (1.0f + Multiplicative);
    }
}
```

### Layer 2: Modifier Sources (ENGINE)

```csharp
/// <summary>
/// ENGINE: Track source of modifiers for tooltips and removal
/// Supports temporary effects (duration-based removal)
/// </summary>
public struct ModifierSource
{
    public enum SourceType : byte
    {
        Permanent,      // Base values, buildings (removed when building destroyed)
        Temporary,      // Events, decisions (removed after duration)
        Conditional     // Active while condition met (terrain, season, etc.)
    }

    public ushort sourceId;         // Which building/tech/event applied this
    public SourceType sourceType;
    public float endTime;           // For temporary effects (float.MaxValue = permanent)
    public byte flags;              // Custom flags for game-specific logic

    public bool IsExpired(float currentTime) =>
        sourceType == SourceType.Temporary && currentTime >= endTime;
}

/// <summary>
/// ENGINE: Active modifier tracking for source management
/// Enables tooltips ("Production: +5 from Farm, +10% from Tech")
/// </summary>
public class ActiveModifierList
{
    private struct ActiveModifier
    {
        public ushort modifierTypeId;
        public float value;
        public bool isMultiplicative;
        public ModifierSource source;
    }

    // Fixed-size pool to avoid allocations
    private ActiveModifier[] modifiers;
    private int count;
    private bool isDirty;

    public ActiveModifierList(int capacity = 64)
    {
        modifiers = new ActiveModifier[capacity];
        count = 0;
        isDirty = false;
    }

    public void AddModifier(ushort modifierTypeId, float value, bool isMultiplicative, ModifierSource source)
    {
        if (count >= modifiers.Length)
        {
            // Double capacity if needed (rare during gameplay)
            Array.Resize(ref modifiers, modifiers.Length * 2);
        }

        modifiers[count++] = new ActiveModifier {
            modifierTypeId = modifierTypeId,
            value = value,
            isMultiplicative = isMultiplicative,
            source = source
        };

        isDirty = true;
    }

    public void RemoveExpired(float currentTime)
    {
        int writeIndex = 0;
        for (int readIndex = 0; readIndex < count; readIndex++)
        {
            if (!modifiers[readIndex].source.IsExpired(currentTime))
            {
                modifiers[writeIndex++] = modifiers[readIndex];
            }
            else
            {
                isDirty = true;
            }
        }
        count = writeIndex;
    }

    public void RemoveBySource(ushort sourceId)
    {
        int writeIndex = 0;
        for (int readIndex = 0; readIndex < count; readIndex++)
        {
            if (modifiers[readIndex].source.sourceId != sourceId)
            {
                modifiers[writeIndex++] = modifiers[readIndex];
            }
            else
            {
                isDirty = true;
            }
        }
        count = writeIndex;
    }

    public ModifierSet AccumulateModifiers()
    {
        var result = new ModifierSet();
        result.Clear();

        for (int i = 0; i < count; i++)
        {
            var mod = modifiers[i];
            result.Add(mod.modifierTypeId, mod.value, mod.isMultiplicative);
        }

        isDirty = false;
        return result;
    }

    public bool IsDirty => isDirty;
}
```

### Layer 3: Modifier Scopes (ENGINE)

```csharp
/// <summary>
/// ENGINE: Different contexts where modifiers apply
/// Mirrors Paradox games (province modifiers, country modifiers, global modifiers)
/// </summary>
public enum ModifierScope : byte
{
    Province,      // Applies to single province (building bonuses)
    Country,       // Applies to entire country (tech, government)
    Global,        // Applies to all countries (game rules, difficulty)
    Unit           // Applies to specific unit (leader bonuses, terrain)
}

/// <summary>
/// ENGINE: Container for scoped modifiers
/// Province has its own modifiers + inherits country modifiers
/// </summary>
public class ScopedModifierContainer
{
    // Province-specific modifiers (buildings, terrain, etc.)
    private ModifierSet provinceModifiers;
    private ActiveModifierList provinceActiveModifiers;

    // Cached combined modifiers (province + country + global)
    private ModifierSet cachedCombinedModifiers;
    private bool cacheValid;

    // Reference to parent country container (for inheritance)
    private ScopedModifierContainer countryContainer;

    public void Initialize(ScopedModifierContainer parentCountry = null)
    {
        provinceModifiers.Clear();
        provinceActiveModifiers = new ActiveModifierList();
        countryContainer = parentCountry;
        cacheValid = false;
    }

    public void AddModifier(ushort modifierTypeId, float value, bool isMultiplicative, ModifierSource source)
    {
        provinceActiveModifiers.AddModifier(modifierTypeId, value, isMultiplicative, source);
        cacheValid = false;
    }

    public ModifierValue GetModifier(ushort modifierTypeId)
    {
        if (!cacheValid)
        {
            RecalculateCache();
        }

        return cachedCombinedModifiers.Get(modifierTypeId);
    }

    private void RecalculateCache()
    {
        // Start with province modifiers
        cachedCombinedModifiers = provinceActiveModifiers.AccumulateModifiers();

        // Add country modifiers (if exists)
        if (countryContainer != null)
        {
            var countryMods = countryContainer.GetAllModifiers();
            for (ushort i = 0; i < 512; i++)
            {
                var countryMod = countryMods.Get(i);
                cachedCombinedModifiers.Add(i, countryMod.Additive, false);
                cachedCombinedModifiers.Add(i, countryMod.Multiplicative, true);
            }
        }

        cacheValid = true;
    }

    public ModifierSet GetAllModifiers()
    {
        if (!cacheValid)
        {
            RecalculateCache();
        }
        return cachedCombinedModifiers;
    }

    public void InvalidateCache()
    {
        cacheValid = false;
    }
}
```

### Layer 4: Modifier System Manager (ENGINE)

```csharp
/// <summary>
/// ENGINE: Central modifier system managing all scopes
/// Handles province, country, and global modifiers
/// </summary>
public class ModifierSystem
{
    // Modifier containers per province
    private ScopedModifierContainer[] provinceModifiers;

    // Modifier containers per country
    private ScopedModifierContainer[] countryModifiers;

    // Global modifiers (game rules, difficulty)
    private ScopedModifierContainer globalModifiers;

    // Capacity
    private int maxProvinces;
    private int maxCountries;

    public void Initialize(int provinceCapacity, int countryCapacity)
    {
        maxProvinces = provinceCapacity;
        maxCountries = countryCapacity;

        // Allocate containers
        provinceModifiers = new ScopedModifierContainer[maxProvinces];
        countryModifiers = new ScopedModifierContainer[maxCountries];
        globalModifiers = new ScopedModifierContainer();
        globalModifiers.Initialize();

        // Initialize all containers
        for (int i = 0; i < maxProvinces; i++)
        {
            provinceModifiers[i] = new ScopedModifierContainer();
        }

        for (int i = 0; i < maxCountries; i++)
        {
            countryModifiers[i] = new ScopedModifierContainer();
            countryModifiers[i].Initialize(globalModifiers);
        }
    }

    /// <summary>
    /// Link province to its owning country (for modifier inheritance)
    /// Called when province ownership changes
    /// </summary>
    public void SetProvinceOwner(ushort provinceId, ushort countryId)
    {
        if (provinceId >= maxProvinces || countryId >= maxCountries)
            return;

        provinceModifiers[provinceId].Initialize(countryModifiers[countryId]);
    }

    /// <summary>
    /// Add modifier to province
    /// </summary>
    public void AddProvinceModifier(ushort provinceId, ushort modifierTypeId, float value, bool isMultiplicative, ModifierSource source)
    {
        if (provinceId >= maxProvinces)
            return;

        provinceModifiers[provinceId].AddModifier(modifierTypeId, value, isMultiplicative, source);
    }

    /// <summary>
    /// Add modifier to country (affects all owned provinces)
    /// </summary>
    public void AddCountryModifier(ushort countryId, ushort modifierTypeId, float value, bool isMultiplicative, ModifierSource source)
    {
        if (countryId >= maxCountries)
            return;

        countryModifiers[countryId].AddModifier(modifierTypeId, value, isMultiplicative, source);

        // Invalidate cache for all provinces owned by this country
        // (actual implementation would track province ownership)
        InvalidateCountryProvinces(countryId);
    }

    /// <summary>
    /// Get final modifier value for a province (includes country and global modifiers)
    /// </summary>
    public ModifierValue GetProvinceModifier(ushort provinceId, ushort modifierTypeId)
    {
        if (provinceId >= maxProvinces)
            return new ModifierValue();

        return provinceModifiers[provinceId].GetModifier(modifierTypeId);
    }

    /// <summary>
    /// Apply modifiers to a base value
    /// </summary>
    public float ApplyModifiers(ushort provinceId, ushort modifierTypeId, float baseValue)
    {
        var modifier = GetProvinceModifier(provinceId, modifierTypeId);
        return modifier.Apply(baseValue);
    }

    private void InvalidateCountryProvinces(ushort countryId)
    {
        // TODO: Implement province ownership tracking
        // For now, invalidate all (not optimal but correct)
        for (int i = 0; i < maxProvinces; i++)
        {
            provinceModifiers[i].InvalidateCache();
        }
    }
}
```

---

## Game Layer Integration

### Hegemon Modifier Types (GAME Policy)

```csharp
namespace Game.Data
{
    /// <summary>
    /// GAME LAYER: Hegemon-specific modifier types
    /// Each grand strategy game defines its own set
    /// </summary>
    public enum ModifierType : ushort
    {
        // Economic Modifiers (0-99)
        ProductionEfficiency = 0,
        TaxEfficiency = 1,
        TradeIncome = 2,
        BuildingCost = 3,
        DevelopmentCost = 4,

        // Military Modifiers (100-199)
        Manpower = 100,
        ManpowerRecovery = 101,
        Morale = 102,
        Discipline = 103,
        RecruitmentCost = 104,

        // Administrative Modifiers (200-299)
        CoreCreationCost = 200,
        AdministrativeEfficiency = 201,
        StabilityCost = 202,

        // Development Modifiers (300-399)
        PopulationGrowth = 300,
        UrbanizationRate = 301,

        // Reserve space for future expansion
        // 400-511 available for new modifier types
    }

    /// <summary>
    /// GAME LAYER: String name mapping for debugging/UI
    /// </summary>
    public static class ModifierNames
    {
        private static readonly Dictionary<ModifierType, string> names = new()
        {
            [ModifierType.ProductionEfficiency] = "Production Efficiency",
            [ModifierType.TaxEfficiency] = "Tax Efficiency",
            [ModifierType.TradeIncome] = "Trade Income",
            [ModifierType.Manpower] = "Manpower",
            [ModifierType.Morale] = "Morale",
            // ... etc
        };

        public static string GetName(ModifierType type) => names[type];
    }
}
```

### Buildings Apply Modifiers (GAME Implementation)

```csharp
// BEFORE (hardcoded in EconomyCalculator):
if (buildingSystem.HasBuilding(provinceId, "farm")) {
    production *= 1.5f;  // HARDCODED
}

// AFTER (data-driven via modifiers):
// BuildingDefinition.json5
{
    id: "farm",
    name: "Farm",
    cost: 50,
    constructionTime: 6,
    modifiers: [
        { type: "ProductionEfficiency", value: 0.5, isMultiplicative: true }  // +50%
    ]
}

// BuildingConstructionSystem applies modifier when construction completes:
public void OnBuildingCompleted(ushort provinceId, BuildingDefinition building)
{
    foreach (var modifier in building.Modifiers)
    {
        var source = new ModifierSource {
            sourceId = building.GetHashCode(),
            sourceType = ModifierSource.SourceType.Permanent
        };

        modifierSystem.AddProvinceModifier(
            provinceId,
            (ushort)modifier.Type,
            modifier.Value,
            modifier.IsMultiplicative,
            source
        );
    }
}

// EconomyCalculator uses modifier system:
public FixedPoint64 CalculateProvinceProduction(ushort provinceId)
{
    float baseProduction = GetBaseProd(provinceId);

    // Apply modifiers from: buildings, tech, government, events
    float finalProduction = modifierSystem.ApplyModifiers(
        provinceId,
        (ushort)ModifierType.ProductionEfficiency,
        baseProduction
    );

    return FixedPoint64.FromFloat(finalProduction);
}
```

---

## Implementation Plan

### Phase 1: Engine Core (3-4 hours)

**Step 1: Core Data Structures (1h)**
- [ ] Create `Assets/Archon-Engine/Scripts/Core/Modifiers/` directory
- [ ] Implement `ModifierSet` struct (fixed-size arrays)
- [ ] Implement `ModifierValue` struct (apply logic)
- [ ] Unit tests for accumulation logic

**Step 2: Source Tracking (1h)**
- [ ] Implement `ModifierSource` struct
- [ ] Implement `ActiveModifierList` class
- [ ] Add/remove/expire logic
- [ ] Unit tests for source management

**Step 3: Scoped Containers (1h)**
- [ ] Implement `ScopedModifierContainer` class
- [ ] Cache invalidation logic
- [ ] Modifier inheritance (province ← country ← global)
- [ ] Unit tests for scope inheritance

**Step 4: System Manager (1h)**
- [ ] Implement `ModifierSystem` class
- [ ] Province/country/global modifier management
- [ ] Integration with Core/GameState
- [ ] Performance validation (10k provinces test)

### Phase 2: Game Integration (2-3 hours)

**Step 5: Hegemon Modifier Types (30m)**
- [ ] Create `Assets/Game/Data/ModifierType.cs` enum
- [ ] Define economic, military, administrative modifiers
- [ ] Create modifier name mapping for UI

**Step 6: Building System Integration (1h)**
- [ ] Add `modifiers[]` field to `BuildingDefinition`
- [ ] Update JSON5 building files with modifier data
- [ ] BuildingConstructionSystem applies modifiers on completion
- [ ] BuildingConstructionSystem removes modifiers on destruction

**Step 7: Formula Integration (1h)**
- [ ] Update `EconomyCalculator` to use modifier system
- [ ] Replace hardcoded building bonuses with modifier lookups
- [ ] Add modifier support to tax, production, trade formulas
- [ ] Validate calculations match previous behavior

**Step 8: UI Integration (30m)**
- [ ] Update tooltips to show modifier sources
- [ ] ProvinceInfoPanel displays active modifiers
- [ ] "Production: 10 base + 5 (Farm) + 50% (Tech)" format

### Phase 3: Testing & Validation (1 hour)

**Step 9: Functional Testing**
- [ ] Build multiple buildings, verify modifiers stack
- [ ] Remove building, verify modifiers removed
- [ ] Change province ownership, verify country modifiers inherited
- [ ] Check tooltip displays correct breakdown

**Step 10: Performance Testing**
- [ ] Measure modifier application time (target: <0.1ms)
- [ ] Profile with 10k provinces, verify no allocations
- [ ] Stress test: apply 100 modifiers, measure impact
- [ ] Verify determinism (same inputs = same outputs)

---

## Success Criteria

### Functional Requirements
- ✅ Buildings apply modifiers instead of hardcoded bonuses
- ✅ Multiple modifiers stack correctly (additive → multiplicative)
- ✅ Removing building removes its modifiers
- ✅ Province inherits country modifiers
- ✅ Tooltips show modifier breakdown

### Performance Requirements
- ✅ <0.1ms to apply modifiers to a value
- ✅ <20MB memory for 10k provinces × 512 modifiers
- ✅ Zero allocations during gameplay (after initialization)
- ✅ Deterministic (same result across platforms)

### Architecture Requirements
- ✅ ENGINE layer has NO knowledge of Hegemon-specific modifiers
- ✅ GAME layer defines modifier types via enum
- ✅ Extensible (easy to add tech, events, government modifiers later)

---

## Future Extensions (Not in Scope)

### Tech System Integration (Future)
```csharp
public void OnTechResearched(ushort countryId, TechDefinition tech)
{
    foreach (var modifier in tech.Modifiers)
    {
        modifierSystem.AddCountryModifier(countryId, modifier.Type, modifier.Value, ...);
    }
}
```

### Event System Integration (Future)
```csharp
public void OnEventTriggered(ushort countryId, EventDefinition event)
{
    foreach (var modifier in event.Modifiers)
    {
        var source = new ModifierSource {
            sourceType = ModifierSource.SourceType.Temporary,
            endTime = currentTime + event.Duration
        };
        modifierSystem.AddCountryModifier(countryId, modifier.Type, modifier.Value, ..., source);
    }
}
```

### Tooltip System (Future)
```csharp
public string GetModifierTooltip(ushort provinceId, ModifierType type)
{
    var breakdown = modifierSystem.GetModifierBreakdown(provinceId, type);
    // "Production Efficiency: +50%
    //   +30% from Farm
    //   +20% from Agricultural Techniques (Tech)"
}
```

---

## Questions to Resolve Before Implementation

1. **Should ModifierSystem be part of GameState or standalone?**
   - GameState.ModifierSystem (centralized access)
   - Standalone system (more modular)

2. **How to handle province ownership changes?**
   - ModifierSystem tracks ownership internally?
   - External system notifies ModifierSystem?

3. **Cache invalidation strategy?**
   - Dirty flag per province (current plan)
   - Version number per country (alternative)
   - Immediate recalculation (simpler, potentially slower)

4. **Unit vs Province modifiers?**
   - Include unit scope now?
   - Defer to military system implementation?

---

## Related Documentation

- **[engine-game-separation.md](../Engine/engine-game-separation.md)** - Engine provides HOW, Game provides WHAT
- **[6-building-system-json5-implementation.md](../Log/2025-10/18/6-building-system-json5-implementation.md)** - Building system ready for modifier integration
- **[modding-design.md](modding-design.md)** - Future vision (bytecode VM, scripting) - NOT in current scope

---

## Notes

- **Keep it simple**: Don't build the bytecode VM / scripting system yet
- **Prove the pattern**: Validate fixed-size arrays + accumulation pipeline works
- **Foundation first**: This unblocks tech, events, government - implement those later
- **ENGINE focus**: This is reusable infrastructure, not Hegemon-specific

---

*Next Steps: Review design, resolve questions, begin Phase 1 implementation*
