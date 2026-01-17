using Core;
using Core.Commands;
using Core.Data.Ids;
using StarterKit.Validation;
using Utils;

namespace StarterKit.Commands
{
    /// <summary>
    /// StarterKit command: Create a unit at a province.
    /// Demonstrates fluent validation pattern with GAME-layer extensions.
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

        // For undo
        private ushort createdUnitId;
        private string validationError;

        public override bool Validate(GameState gameState)
        {
            // Fluent validation: chain checks, short-circuit on first failure
            // Uses ENGINE's Validate.For() + StarterKit's extension methods
            return Core.Validation.Validate.For(gameState)
                .Province(ProvinceId)                       // ENGINE: province ID valid
                .UnitTypeExists(UnitTypeId)                 // GAME: unit type defined
                .ProvinceOwnedByPlayer(ProvinceId)          // GAME: player owns province
                .Result(out validationError);
        }

        public override void Execute(GameState gameState)
        {
            var units = Initializer.Instance?.UnitSystem;
            if (units == null)
            {
                ArchonLogger.LogError("CreateUnitCommand: UnitSystem not found", "starter_kit");
                return;
            }

            createdUnitId = units.CreateUnit(ProvinceId, UnitTypeId);

            if (createdUnitId != 0)
                LogExecution($"Created {UnitTypeId} (ID={createdUnitId}) in province {ProvinceId}");
            else
                ArchonLogger.LogWarning($"CreateUnitCommand: Failed - {validationError ?? "unknown error"}", "starter_kit");
        }

        public override void Undo(GameState gameState)
        {
            if (createdUnitId == 0) return;

            Initializer.Instance?.UnitSystem?.DisbandUnit(createdUnitId);
            LogExecution($"Undid unit creation (disbanded {createdUnitId})");
        }
    }
}
