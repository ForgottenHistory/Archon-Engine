# Diplomacy System

The diplomacy system manages relationships between countries: opinions, wars, and treaties. It uses a facade pattern with specialized managers for different concerns.

## Architecture Overview

```
DiplomacySystem (Facade)
├── DiplomacyRelationManager    - Opinions and modifiers
├── DiplomacyWarManager         - War state
├── DiplomacyTreatyManager      - Alliances, NAPs, guarantees
├── DiplomacyModifierProcessor  - Burst-optimized decay
└── DiplomacySaveLoadHandler    - Serialization
```

**Key Principles:**
- Facade owns all data (NativeCollections)
- Managers are stateless processors
- FixedPoint64 for deterministic opinions
- Sparse storage (only active relationships)

## Opinion System

Opinions are calculated from a base value plus modifiers:

```csharp
// Get opinion (base + modifiers)
var diplomacy = gameState.GetComponent<DiplomacySystem>();
FixedPoint64 opinion = diplomacy.GetOpinion(countryA, countryB, currentTick);

// Get base opinion only
FixedPoint64 baseOpinion = diplomacy.GetBaseOpinion(countryA, countryB);

// Set base opinion
diplomacy.SetBaseOpinion(countryA, countryB, FixedPoint64.FromInt(50));
```

### Opinion Modifiers

Modifiers are temporary effects that decay over time:

```csharp
// Add a modifier
var modifier = new OpinionModifier
{
    modifierTypeID = 1,                          // Your modifier type
    value = FixedPoint64.FromInt(-50),           // Opinion change
    appliedTick = currentTick,                   // When applied
    decayRate = 3600                             // Ticks until fully decayed
};

diplomacy.AddOpinionModifier(countryA, countryB, modifier, currentTick);

// Remove a specific modifier type
diplomacy.RemoveOpinionModifier(countryA, countryB, modifierTypeID: 1);

// Decay all modifiers (call monthly)
diplomacy.DecayOpinionModifiers(currentTick);
```

### Querying by Opinion

```csharp
// Find countries with positive opinion
var friends = diplomacy.GetCountriesWithOpinionAbove(
    countryID,
    FixedPoint64.FromInt(50),
    currentTick
);

// Find countries with negative opinion
var enemies = diplomacy.GetCountriesWithOpinionBelow(
    countryID,
    FixedPoint64.FromInt(-25),
    currentTick
);
```

## War System

### Checking War State

```csharp
// Check if two countries are at war
bool atWar = diplomacy.IsAtWar(countryA, countryB);

// Check if country is at war with anyone
bool hasWar = diplomacy.IsAtWar(countryID);

// Get all enemies
List<ushort> enemies = diplomacy.GetEnemies(countryID);

// Get all active wars
var allWars = diplomacy.GetAllWars();  // List of (attacker, defender)
```

### War Commands

```csharp
// Declare war (via command)
gameState.ExecuteCommand(new DeclareWarCommand
{
    AttackerID = myCountry,
    DefenderID = targetCountry,
    DeclaredWarModifierType = 1,
    DeclaredWarModifierValue = FixedPoint64.FromInt(-50),
    DeclaredWarDecayTicks = 3600  // 10 years
});

// Make peace
gameState.ExecuteCommand(new MakePeaceCommand
{
    Country1 = myCountry,
    Country2 = enemyCountry
});
```

## Treaty System

### Alliances

Mutual defense pacts between two countries:

```csharp
// Check alliance
bool allied = diplomacy.AreAllied(countryA, countryB);

// Get all allies
List<ushort> allies = diplomacy.GetAllies(countryID);

// Get alliance network (recursive)
HashSet<ushort> network = diplomacy.GetAlliesRecursive(countryID);

// Form/break alliance (usually via command)
diplomacy.FormAlliance(countryA, countryB, currentTick);
diplomacy.BreakAlliance(countryA, countryB, currentTick);
```

### Non-Aggression Pacts

Cannot declare war while NAP is active:

```csharp
bool hasNAP = diplomacy.HasNonAggressionPact(countryA, countryB);

diplomacy.FormNonAggressionPact(countryA, countryB, currentTick);
diplomacy.BreakNonAggressionPact(countryA, countryB, currentTick);
```

### Guarantees

One country guarantees another's independence (directional):

```csharp
// Check if guarantor is protecting guaranteed
bool isGuaranteeing = diplomacy.IsGuaranteeing(guarantor, guaranteed);

// Get countries that guarantorID is protecting
List<ushort> protecting = diplomacy.GetGuaranteeing(guarantorID);

// Get countries protecting guaranteedID
List<ushort> protectors = diplomacy.GetGuaranteedBy(guaranteedID);

diplomacy.GuaranteeIndependence(guarantor, guaranteed, currentTick);
diplomacy.RevokeGuarantee(guarantor, guaranteed, currentTick);
```

### Military Access

Permission to move units through another country's territory (directional):

```csharp
// Check if granter allows recipient to pass through
bool hasAccess = diplomacy.HasMilitaryAccess(granter, recipient);

diplomacy.GrantMilitaryAccess(granter, recipient, currentTick);
diplomacy.RevokeMilitaryAccess(granter, recipient, currentTick);
```

## Diplomacy Events

Subscribe to diplomatic events for UI updates and AI reactions:

```csharp
// War declared
gameState.EventBus.Subscribe<DiplomacyWarDeclaredEvent>(evt => {
    Debug.Log($"War! {evt.attackerID} vs {evt.defenderID}");
});

// Peace made
gameState.EventBus.Subscribe<DiplomacyPeaceMadeEvent>(evt => {
    Debug.Log($"Peace between {evt.country1} and {evt.country2}");
});

// Opinion changed
gameState.EventBus.Subscribe<DiplomacyOpinionChangedEvent>(evt => {
    Debug.Log($"Opinion: {evt.oldOpinion} → {evt.newOpinion}");
});

// Treaty events
gameState.EventBus.Subscribe<AllianceFormedEvent>(evt => { ... });
gameState.EventBus.Subscribe<AllianceBrokenEvent>(evt => { ... });
gameState.EventBus.Subscribe<NonAggressionPactFormedEvent>(evt => { ... });
gameState.EventBus.Subscribe<GuaranteeGrantedEvent>(evt => { ... });
gameState.EventBus.Subscribe<MilitaryAccessGrantedEvent>(evt => { ... });
```

## Improve Relations Command

Spend resources to improve opinion:

```csharp
gameState.ExecuteCommand(new ImproveRelationsCommand
{
    SourceCountry = myCountry,
    TargetCountry = targetCountry,
    ResourceId = 0,                                    // Gold resource ID
    ResourceCost = FixedPoint64.FromInt(25),          // Cost in gold
    ImproveRelationsModifierType = 3,
    ImproveRelationsModifierValue = FixedPoint64.FromInt(5),
    ImproveRelationsDecayTicks = 360                  // 1 year
});
```

## Integration Example

```csharp
public class DiplomacyPanelUI : MonoBehaviour
{
    private DiplomacySystem diplomacy;
    private CompositeDisposable subscriptions = new CompositeDisposable();

    void Initialize(GameState gameState)
    {
        diplomacy = gameState.GetComponent<DiplomacySystem>();

        // Subscribe to war events
        subscriptions.Add(gameState.EventBus.Subscribe<DiplomacyWarDeclaredEvent>(OnWarDeclared));
        subscriptions.Add(gameState.EventBus.Subscribe<DiplomacyPeaceMadeEvent>(OnPeaceMade));
    }

    void OnWarDeclared(DiplomacyWarDeclaredEvent evt)
    {
        ShowNotification($"War declared!");
        RefreshRelationsPanel();
    }

    void RefreshRelationsPanel()
    {
        var enemies = diplomacy.GetEnemies(playerCountryID);
        var allies = diplomacy.GetAllies(playerCountryID);
        // Update UI...
    }

    void OnDestroy()
    {
        subscriptions.Dispose();
    }
}
```

## Performance

The system is optimized for Paradox-scale games:
- 1000 countries, 30k active relationships
- `GetOpinion()` < 0.1ms (O(1) cache + O(m) modifiers)
- `IsAtWar()` < 0.01ms (HashSet O(1))
- `DecayOpinionModifiers()` < 5ms for 610k modifiers (Burst parallel)

## API Reference

- [DiplomacySystem](~/api/Core.Diplomacy.DiplomacySystem.html) - Main facade
- [DeclareWarCommand](~/api/Core.Diplomacy.DeclareWarCommand.html) - War declaration
- [MakePeaceCommand](~/api/Core.Diplomacy.MakePeaceCommand.html) - Peace treaty
- [ImproveRelationsCommand](~/api/Core.Diplomacy.ImproveRelationsCommand.html) - Improve opinion
- [OpinionModifier](~/api/Core.Diplomacy.OpinionModifier.html) - Modifier struct
