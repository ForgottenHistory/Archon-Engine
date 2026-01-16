using Core;
using Core.Commands;
using Utils;

namespace StarterKit.Commands
{
    /// <summary>
    /// StarterKit command: Move a unit to a new province.
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

        public override bool Validate(GameState gameState)
        {
            var units = Initializer.Instance?.UnitSystem;
            if (units == null)
                return false;

            // Check unit exists (unitCount > 0 means unit is alive)
            var unit = units.GetUnit(UnitId);
            if (unit.unitCount == 0)
                return false;

            // Check target province is valid
            if (TargetProvinceId == 0 || TargetProvinceId >= gameState.Provinces.ProvinceCount)
                return false;

            return true;
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
