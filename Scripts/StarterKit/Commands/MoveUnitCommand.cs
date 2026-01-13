using Core;
using Core.Commands;
using UnityEngine;
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
            var initializer = Object.FindFirstObjectByType<Initializer>();
            if (initializer?.UnitSystem == null)
                return false;

            // Check unit exists (strength > 0 means unit is alive)
            var unit = initializer.UnitSystem.GetUnit(UnitId);
            if (unit.strength == 0)
                return false;

            // Check target province is valid
            if (TargetProvinceId == 0 || TargetProvinceId >= gameState.Provinces.ProvinceCount)
                return false;

            return true;
        }

        public override void Execute(GameState gameState)
        {
            var initializer = Object.FindFirstObjectByType<Initializer>();
            if (initializer?.UnitSystem == null)
            {
                ArchonLogger.LogError("MoveUnitCommand: UnitSystem not found", "starter_kit");
                return;
            }

            var unit = initializer.UnitSystem.GetUnit(UnitId);
            previousProvinceId = unit.provinceID;

            initializer.UnitSystem.MoveUnit(UnitId, TargetProvinceId);
            LogExecution($"Moved unit {UnitId} from {previousProvinceId} to {TargetProvinceId}");
        }

        public override void Undo(GameState gameState)
        {
            var initializer = Object.FindFirstObjectByType<Initializer>();
            initializer?.UnitSystem?.MoveUnit(UnitId, previousProvinceId);
            LogExecution($"Undid move (unit {UnitId} back to {previousProvinceId})");
        }
    }
}
