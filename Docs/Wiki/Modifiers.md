# Modifier System

The modifier system provides hierarchical stat modifications with scope inheritance. Modifiers can come from buildings, technologies, events, policies, and other sources.

## Architecture

```
ModifierSystem
├── Global Scope       (inherited by all)
├── Country Scopes     (inherited by country provinces)
└── Province Scopes    (local only)

Scope Inheritance:
Province Final = Province Local + Country + Global
```

**Key Principles:**
- Hierarchical scope inheritance (like EU4, CK3, Stellaris)
- Source tracking for tooltips
- Additive and multiplicative modifiers
- Temporary modifiers with expiration
- FixedPoint64 for determinism

## Modifier Formula

```
Final Value = (Base + Additive) × (1 + Multiplicative)

Example:
- Base production: 10
- Additive bonuses: +5 (from building)
- Multiplicative bonuses: +50% (from tech)
- Result: (10 + 5) × (1 + 0.5) = 22.5
```

## Adding Modifiers

### Province Modifiers

```csharp
// Add building modifier to province
var modifier = ModifierSource.CreatePermanent(
    type: ModifierSource.SourceType.Building,
    sourceId: buildingId,
    modifierTypeId: PRODUCTION_MODIFIER,
    value: FixedPoint64.FromInt(5),
    isMultiplicative: false
);

modifierSystem.AddProvinceModifier(provinceId, modifier);
```

### Country Modifiers

```csharp
// Add tech modifier to country (inherited by all provinces)
var techModifier = ModifierSource.CreatePermanent(
    type: ModifierSource.SourceType.Technology,
    sourceId: techId,
    modifierTypeId: PRODUCTION_MODIFIER,
    value: FixedPoint64.FromFloat(0.2f),  // +20%
    isMultiplicative: true
);

modifierSystem.AddCountryModifier(countryId, techModifier);
```

### Global Modifiers

```csharp
// Add event modifier to everyone
var eventModifier = ModifierSource.CreateTemporary(
    type: ModifierSource.SourceType.Event,
    sourceId: eventId,
    modifierTypeId: PRODUCTION_MODIFIER,
    value: FixedPoint64.FromFloat(0.1f),  // +10%
    isMultiplicative: true,
    expirationTick: currentTick + 8640    // 1 year
);

modifierSystem.AddGlobalModifier(eventModifier);
```

## Removing Modifiers

```csharp
// Remove all modifiers from a source
modifierSystem.RemoveProvinceModifiersBySource(
    provinceId,
    ModifierSource.SourceType.Building,
    buildingId
);

// Remove specific modifier type from source
modifierSystem.RemoveProvinceModifiersBySourceAndType(
    provinceId,
    ModifierSource.SourceType.Building,
    buildingId,
    PRODUCTION_MODIFIER
);

// Clear all modifiers
modifierSystem.ClearProvinceModifiers(provinceId);
modifierSystem.ClearCountryModifiers(countryId);
modifierSystem.ClearGlobalModifiers();
```

## Querying Modifiers

### Get Final Value

```csharp
// Get province modifier with full inheritance
FixedPoint64 production = modifierSystem.GetProvinceModifier(
    provinceId,
    countryId,
    PRODUCTION_MODIFIER,
    baseValue: FixedPoint64.FromInt(10)
);

// Get country modifier (inherits from global)
FixedPoint64 taxRate = modifierSystem.GetCountryModifier(
    countryId,
    TAX_MODIFIER,
    baseValue: FixedPoint64.FromFloat(0.1f)
);

// Get global modifier only
FixedPoint64 globalBonus = modifierSystem.GetGlobalModifier(
    PRODUCTION_MODIFIER,
    baseValue: FixedPoint64.One
);
```

### For Tooltips

```csharp
// Iterate all modifiers affecting a province (for tooltip)
modifierSystem.ForEachProvinceModifierWithInheritance(
    provinceId, countryId,
    (modifier, scopeLevel) =>
    {
        string scope = scopeLevel switch
        {
            ModifierScopeLevel.Province => "Local",
            ModifierScopeLevel.Country => "National",
            ModifierScopeLevel.Global => "Global"
        };
        Debug.Log($"{scope}: {modifier}");
    }
);

// Get modifiers of specific type (for stat breakdown)
modifierSystem.ForEachProvinceModifierByType(
    provinceId, countryId, PRODUCTION_MODIFIER,
    (modifier, scopeLevel) =>
    {
        tooltip.AddLine($"{GetSourceName(modifier)}: {modifier.Value}");
    }
);
```

### Check Source Presence

```csharp
// Check if building has any modifiers on province
bool hasModifiers = modifierSystem.HasProvinceModifiersFromSource(
    provinceId,
    ModifierSource.SourceType.Building,
    buildingId
);

// Count modifiers from source
int count = modifierSystem.CountProvinceModifiersBySource(
    provinceId,
    ModifierSource.SourceType.Building,
    buildingId
);
```

## Source Types

```csharp
public enum SourceType : byte
{
    Building = 0,      // From constructed buildings
    Technology = 1,    // From unlocked tech
    Event = 2,         // From triggered events (usually temporary)
    Government = 3,    // From government type
    Trait = 4,         // From character traits
    Policy = 5,        // From active policies
    Custom = 255       // Custom game-specific sources
}
```

## Temporary Modifiers

### Creating Temporary Modifiers

```csharp
var tempModifier = ModifierSource.CreateTemporary(
    type: ModifierSource.SourceType.Event,
    sourceId: eventId,
    modifierTypeId: HAPPINESS_MODIFIER,
    value: FixedPoint64.FromInt(10),
    isMultiplicative: false,
    expirationTick: currentTick + 4320  // 6 months (180 days × 24 hours)
);
```

### Expiring Modifiers

Call `ExpireModifiers()` every tick to remove expired modifiers:

```csharp
// In your game loop
void OnHourlyTick(int tick)
{
    modifierSystem.ExpireModifiers(tick);
}
```

## Integration Example

```csharp
public class BuildingSystem
{
    private ModifierSystem modifiers;

    public void ConstructBuilding(ushort provinceId, BuildingDefinition building)
    {
        // Add production modifier
        if (building.ProductionBonus > 0)
        {
            var modifier = ModifierSource.CreatePermanent(
                ModifierSource.SourceType.Building,
                building.ID,
                PRODUCTION_MODIFIER,
                FixedPoint64.FromInt(building.ProductionBonus),
                isMultiplicative: false
            );
            modifiers.AddProvinceModifier(provinceId, modifier);
        }

        // Add tax modifier
        if (building.TaxMultiplier > 0)
        {
            var modifier = ModifierSource.CreatePermanent(
                ModifierSource.SourceType.Building,
                building.ID,
                TAX_MODIFIER,
                FixedPoint64.FromFloat(building.TaxMultiplier),
                isMultiplicative: true
            );
            modifiers.AddProvinceModifier(provinceId, modifier);
        }
    }

    public void DestroyBuilding(ushort provinceId, uint buildingId)
    {
        // Remove all modifiers from this building
        modifiers.RemoveProvinceModifiersBySource(
            provinceId,
            ModifierSource.SourceType.Building,
            buildingId
        );
    }
}
```

## Defining Modifier Types

Define your game's modifier types as constants:

```csharp
public static class ModifierTypes
{
    public const ushort PRODUCTION = 1;
    public const ushort TAX_RATE = 2;
    public const ushort MANPOWER = 3;
    public const ushort HAPPINESS = 4;
    public const ushort BUILDING_COST = 5;
    public const ushort MOVEMENT_SPEED = 6;
    // etc.
}
```

## Performance

- O(1) scope lookup
- O(n) rebuild where n = active modifiers in scope chain
- Dirty flag optimization (only rebuild when changed)
- FixedPoint64 for deterministic multiplayer

## API Reference

- [ModifierSystem](~/api/Core.Modifiers.ModifierSystem.html) - Main manager
- [ModifierSource](~/api/Core.Modifiers.ModifierSource.html) - Modifier with source tracking
- [ModifierValue](~/api/Core.Modifiers.ModifierValue.html) - Additive/multiplicative value
- [ModifierScopeLevel](~/api/Core.Modifiers.ModifierScopeLevel.html) - Scope enumeration
