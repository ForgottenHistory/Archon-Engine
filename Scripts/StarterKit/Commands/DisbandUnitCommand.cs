using Core;
using Core.Commands;
using UnityEngine;
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
            var initializer = Object.FindFirstObjectByType<Initializer>();
            if (initializer?.UnitSystem == null)
                return false;

            // Check unit exists (strength > 0 means unit is alive)
            var unit = initializer.UnitSystem.GetUnit(UnitId);
            return unit.strength > 0;
        }

        public override void Execute(GameState gameState)
        {
            var initializer = Object.FindFirstObjectByType<Initializer>();
            if (initializer?.UnitSystem == null)
            {
                ArchonLogger.LogError("DisbandUnitCommand: UnitSystem not found", "starter_kit");
                return;
            }

            // Store state for potential undo
            var unit = initializer.UnitSystem.GetUnit(UnitId);
            previousProvinceId = unit.provinceID;
            previousUnitTypeId = unit.unitTypeID;

            initializer.UnitSystem.DisbandUnit(UnitId);
            LogExecution($"Disbanded unit {UnitId}");
        }

        public override void Undo(GameState gameState)
        {
            var initializer = Object.FindFirstObjectByType<Initializer>();
            if (initializer?.UnitSystem == null) return;

            // Recreate the unit (note: may get different ID)
            ushort newUnitId = initializer.UnitSystem.CreateUnit(previousProvinceId, previousUnitTypeId);
            LogExecution($"Undid disband (recreated as unit {newUnitId})");
        }
    }
}
