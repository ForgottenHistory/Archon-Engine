# Commands

Commands are the foundation for all state changes in Archon. Every modification to game state flows through the command system, enabling multiplayer sync, undo/redo, replay, and save/load.

## Why Commands?

```
❌ WRONG - Direct modification
province.ownerID = newOwner;

✅ CORRECT - Use a command
gameState.CommandProcessor.Execute(new ChangeOwnerCommand { ... });
```

Commands provide:
- **Validation** before execution
- **Deterministic** execution order for multiplayer
- **Serialization** for network and save files
- **Undo support** for player actions
- **Logging** for debugging and replay

## Creating a Command

### Using SimpleCommand (Recommended)

`SimpleCommand` handles serialization automatically via attributes:

```csharp
using Core;
using Core.Commands;

namespace MyGame.Commands
{
    [Command("add_gold",
        Aliases = new[] { "gold" },
        Description = "Add or remove gold from treasury",
        Examples = new[] { "add_gold 100", "add_gold -50" })]
    public class AddGoldCommand : SimpleCommand
    {
        [Arg(0, "amount")]
        public int Amount { get; set; }

        private int previousGold; // For undo

        public override bool Validate(GameState gameState)
        {
            if (Amount < 0)
            {
                // Use fluent validation for checks
                return Validate.For(gameState)
                    .HasGold(-Amount)
                    .Result(out _);
            }
            return true;
        }

        public override void Execute(GameState gameState)
        {
            var economy = MyInitializer.Instance.EconomySystem;
            previousGold = economy.Gold;

            if (Amount >= 0)
                economy.AddGold(Amount);
            else
                economy.RemoveGold(-Amount);

            LogExecution($"Gold changed by {Amount}");
        }

        public override void Undo(GameState gameState)
        {
            var economy = MyInitializer.Instance.EconomySystem;
            int difference = previousGold - economy.Gold;

            if (difference > 0)
                economy.AddGold(difference);
            else if (difference < 0)
                economy.RemoveGold(-difference);
        }
    }
}
```

### Key Elements

1. **`[Command]` attribute** - Registers command for console/debug use
   - `name` - Primary command name
   - `Aliases` - Alternative names
   - `Description` - Help text
   - `Examples` - Usage examples

2. **`[Arg]` attribute** - Marks properties for auto-serialization
   - Position (0, 1, 2...) determines argument order
   - Name is used in help text

3. **`Validate()`** - Return false to reject invalid commands

4. **`Execute()`** - Perform the state change

5. **`Undo()`** - Optional, restore previous state

## Fluent Validation

Archon provides chainable validation that short-circuits on first failure:

```csharp
public override bool Validate(GameState gameState)
{
    return Validate.For(gameState)
        .Province(provinceId)           // Province ID is valid
        .ProvinceOwnedBy(provinceId, countryId)  // Owned by country
        .IsPositive(amount)             // Amount > 0
        .Result(out validationError);   // Get error message
}
```

### ENGINE Validators (Core.Validation)
- `.Province(id)` - Province exists
- `.Country(id)` - Country exists
- `.ProvinceOwnedBy(province, country)` - Ownership check
- `.IsPositive(value)` - Value > 0
- `.IsInRange(value, min, max)` - Range check
- `.NotNull(obj)` - Null check

### Adding GAME Validators

Extend `ValidationBuilder` with your own checks:

```csharp
namespace MyGame.Validation
{
    public static class MyValidationExtensions
    {
        public static ValidationBuilder UnitExists(
            this ValidationBuilder v, ushort unitId)
        {
            var units = MyInitializer.Instance?.UnitSystem;
            if (units == null)
                return v.Fail("UnitSystem not available");

            if (units.GetUnit(unitId).unitCount == 0)
                return v.Fail($"Unit {unitId} does not exist");

            return v;
        }

        public static ValidationBuilder HasGold(
            this ValidationBuilder v, int amount)
        {
            var economy = MyInitializer.Instance?.EconomySystem;
            if (economy == null)
                return v.Fail("EconomySystem not available");

            if (economy.Gold < amount)
                return v.Fail($"Need {amount} gold, have {economy.Gold}");

            return v;
        }
    }
}
```

## Executing Commands

### From Code
```csharp
var cmd = new AddGoldCommand { Amount = 100 };
gameState.CommandProcessor.Execute(cmd);
```

### From Debug Console
Commands with `[Command]` attribute are available in the debug console:
```
> add_gold 100
> build market 5
> move_unit 1 10
```

## Type-Safe IDs

Use type-safe ID wrappers for compile-time safety:

```csharp
[Arg(1, "provinceId")]
public ProvinceId ProvinceId { get; set; }  // Not ushort

[Arg(0, "countryId")]
public CountryId CountryId { get; set; }    // Not ushort
```

This prevents accidentally passing a province ID where a country ID is expected.

## Supported Arg Types

`SimpleCommand` auto-serializes these types:
- Integers: `int`, `uint`, `short`, `ushort`, `byte`, `sbyte`, `long`, `ulong`
- Floating point: `float`, `double` (avoid in simulation!)
- Other: `bool`, `string`, `FixedPoint64`
- Type-safe IDs: `ProvinceId`, `CountryId` (ushort wrappers)

## When to Use BaseCommand

Use `BaseCommand` instead of `SimpleCommand` when you need:
- Custom serialization format
- Complex undo logic
- Maximum performance (no reflection)

## StarterKit Examples

- `AddGoldCommand` - Simple economy command with undo
- `ConstructBuildingCommand` - Multi-validator command
- `MoveUnitCommand` - Unit movement with undo
- `CreateUnitCommand` - Unit creation
- `DisbandUnitCommand` - Unit removal

See `Scripts/StarterKit/Commands/` for complete implementations.

## Best Practices

1. **Always validate** - Never assume input is valid
2. **Store undo state in Execute()** - Capture previous values before changing
3. **Use fluent validation** - Chainable, readable, short-circuits
4. **Use type-safe IDs** - ProvinceId, CountryId prevent mix-ups
5. **Log execution** - Call `LogExecution()` for debugging
6. **Keep commands focused** - One command = one logical action
