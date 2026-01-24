using Core;
using Core.Commands;
using Core.Data.Ids;
using StarterKit.Validation;
using Utils;

namespace StarterKit.Commands
{
    /// <summary>
    /// StarterKit command: Create a unit at a province.
    /// Handles gold cost and creates the unit.
    /// Uses type-safe ProvinceId wrapper for compile-time safety.
    /// </summary>
    [Command("create_unit",
        Aliases = new[] { "unit", "spawn" },
        Description = "Create a unit at a province",
        Examples = new[] { "create_unit infantry 5", "unit cavalry 10" })]
    public class CreateUnitCommand : SimpleCommand
    {
        [Arg(0, "unitType")]
        public string UnitTypeId { get; set; }

        [Arg(1, "provinceId")]
        public ProvinceId ProvinceId { get; set; }

        [Arg(2, "countryId")]
        public ushort CountryId { get; set; }

        // For undo
        private ushort createdUnitId;
        private int unitCost;
        private string validationError;

        public override bool Validate(GameState gameState)
        {
            var units = Initializer.Instance?.UnitSystem;
            var economy = Initializer.Instance?.EconomySystem;

            if (units == null || economy == null)
                return false;

            var unitType = units.GetUnitType(UnitTypeId);
            if (unitType == null)
                return false;

            // Check gold
            int gold = economy.GetCountryGoldInt(CountryId);
            if (gold < unitType.Cost)
                return false;

            // Fluent validation: chain checks, short-circuit on first failure
            return Core.Validation.Validate.For(gameState)
                .Province(ProvinceId)                       // ENGINE: province ID valid
                .UnitTypeExists(UnitTypeId)                 // GAME: unit type defined
                .ProvinceOwnedByCountry(ProvinceId, CountryId) // GAME: country owns province
                .Result(out validationError);
        }

        public override void Execute(GameState gameState)
        {
            var units = Initializer.Instance?.UnitSystem;
            var economy = Initializer.Instance?.EconomySystem;

            if (units == null || economy == null)
            {
                ArchonLogger.LogError("CreateUnitCommand: UnitSystem or EconomySystem not found", "starter_kit");
                return;
            }

            var unitType = units.GetUnitType(UnitTypeId);
            if (unitType == null)
            {
                ArchonLogger.LogError($"CreateUnitCommand: Unit type '{UnitTypeId}' not found", "starter_kit");
                return;
            }

            // Deduct gold
            unitCost = unitType.Cost;
            economy.RemoveGoldFromCountry(CountryId, unitCost);

            // Create unit with explicit country ID (for multiplayer sync)
            createdUnitId = units.CreateUnit(ProvinceId.Value, unitType.ID, CountryId);

            if (createdUnitId != 0)
                LogExecution($"Country {CountryId} created {UnitTypeId} (ID={createdUnitId}) in province {ProvinceId} for {unitCost} gold");
            else
            {
                // Refund gold on failure
                economy.AddGoldToCountry(CountryId, unitCost);
                ArchonLogger.LogWarning($"CreateUnitCommand: Failed - {validationError ?? "unknown error"}", "starter_kit");
            }
        }

        public override void Undo(GameState gameState)
        {
            if (createdUnitId == 0) return;

            var units = Initializer.Instance?.UnitSystem;
            var economy = Initializer.Instance?.EconomySystem;

            units?.DisbandUnit(createdUnitId);
            economy?.AddGoldToCountry(CountryId, unitCost);

            LogExecution($"Undid unit creation (disbanded {createdUnitId}, refunded {unitCost} gold)");
        }
    }
}
