using Core;
using Core.Commands;
using Utils;

namespace StarterKit.Commands
{
    /// <summary>
    /// StarterKit command: Disband a unit.
    /// </summary>
    [Command("disband_unit",
        Aliases = new[] { "disband", "kill" },
        Description = "Disband a unit",
        Examples = new[] { "disband_unit 1", "disband 2" })]
    public class DisbandUnitCommand : SimpleCommand
    {
        [Arg(0, "unitId")]
        public ushort UnitId { get; set; }

        // For undo - store unit state before disbanding
        private ushort previousProvinceId;
        private ushort previousUnitTypeId;

        public override bool Validate(GameState gameState)
        {
            var units = Initializer.Instance?.UnitSystem;
            if (units == null)
                return false;

            // Check unit exists (unitCount > 0 means unit is alive)
            var unit = units.GetUnit(UnitId);
            return unit.unitCount > 0;
        }

        public override void Execute(GameState gameState)
        {
            var units = Initializer.Instance?.UnitSystem;
            if (units == null)
            {
                ArchonLogger.LogError("DisbandUnitCommand: UnitSystem not found", "starter_kit");
                return;
            }

            // Store state for potential undo
            var unit = units.GetUnit(UnitId);
            previousProvinceId = unit.provinceID;
            previousUnitTypeId = unit.unitTypeID;

            units.DisbandUnit(UnitId);
            LogExecution($"Disbanded unit {UnitId}");
        }

        public override void Undo(GameState gameState)
        {
            var units = Initializer.Instance?.UnitSystem;
            if (units == null) return;

            // Recreate the unit (note: may get different ID)
            ushort newUnitId = units.CreateUnit(previousProvinceId, previousUnitTypeId);
            LogExecution($"Undid disband (recreated as unit {newUnitId})");
        }
    }
}
