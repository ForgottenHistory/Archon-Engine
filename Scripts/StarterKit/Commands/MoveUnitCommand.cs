using Core;
using Core.Commands;
using StarterKit.Validation;
using Utils;

namespace StarterKit.Commands
{
    /// <summary>
    /// StarterKit command: Move a unit to a new province.
    /// Demonstrates fluent validation pattern with GAME-layer extensions.
    /// </summary>
    [Command("move_unit",
        Aliases = new[] { "move" },
        Description = "Move a unit to a province",
        Examples = new[] { "move_unit 1 10", "move 2 15" })]
    public class MoveUnitCommand : SimpleCommand
    {
        [Arg(0, "unitId")]
        public ushort UnitId { get; set; }

        [Arg(1, "provinceId")]
        public ushort TargetProvinceId { get; set; }

        // For undo
        private ushort previousProvinceId;
        private string validationError;

        public override bool Validate(GameState gameState)
        {
            // Fluent validation: chain checks, short-circuit on first failure
            return Core.Validation.Validate.For(gameState)
                .Province(TargetProvinceId)     // ENGINE: target province ID valid
                .UnitExists(UnitId)             // GAME: unit exists and is alive
                .Result(out validationError);
        }

        public override void Execute(GameState gameState)
        {
            var units = Initializer.Instance?.UnitSystem;
            if (units == null)
            {
                ArchonLogger.LogError("MoveUnitCommand: UnitSystem not found", "starter_kit");
                return;
            }

            var unit = units.GetUnit(UnitId);
            previousProvinceId = unit.provinceID;

            units.MoveUnit(UnitId, TargetProvinceId);
            LogExecution($"Moved unit {UnitId} from {previousProvinceId} to {TargetProvinceId}");
        }

        public override void Undo(GameState gameState)
        {
            Initializer.Instance?.UnitSystem?.MoveUnit(UnitId, previousProvinceId);
            LogExecution($"Undid move (unit {UnitId} back to {previousProvinceId})");
        }
    }
}
