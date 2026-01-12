using Core;
using Core.Commands;
using Core.Units;
using UnityEngine;
using Utils;

namespace StarterKit.Commands
{
    /// <summary>
    /// StarterKit command: Disband a unit.
    /// </summary>
    public class DisbandUnitCommand : BaseCommand
    {
        private ushort unitId;

        // For undo - store unit state before disbanding
        private ushort previousProvinceId;
        private ushort previousUnitTypeId;

        public DisbandUnitCommand() { }

        public DisbandUnitCommand(ushort unitId)
        {
            this.unitId = unitId;
        }

        public override bool Validate(GameState gameState)
        {
            var initializer = Object.FindFirstObjectByType<Initializer>();
            if (initializer == null || initializer.UnitSystem == null)
                return false;

            // Check unit exists (strength > 0 means unit is alive)
            var unit = initializer.UnitSystem.GetUnit(unitId);
            if (unit.strength == 0)
                return false;

            return true;
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
            var unit = initializer.UnitSystem.GetUnit(unitId);
            previousProvinceId = unit.provinceID;
            previousUnitTypeId = unit.unitTypeID;

            initializer.UnitSystem.DisbandUnit(unitId);
            LogExecution($"Disbanded unit {unitId}");
        }

        public override void Undo(GameState gameState)
        {
            var initializer = Object.FindFirstObjectByType<Initializer>();
            if (initializer?.UnitSystem == null) return;

            // Recreate the unit (note: may get different ID)
            ushort newUnitId = initializer.UnitSystem.CreateUnit(previousProvinceId, previousUnitTypeId);
            LogExecution($"Undid disband (recreated as unit {newUnitId})");
        }

        public override void Serialize(System.IO.BinaryWriter writer)
        {
            writer.Write(unitId);
        }

        public override void Deserialize(System.IO.BinaryReader reader)
        {
            unitId = reader.ReadUInt16();
        }
    }

    /// <summary>
    /// Factory for creating DisbandUnitCommand from console input.
    /// </summary>
    [CommandMetadata("disband_unit",
        Aliases = new[] { "disband", "kill" },
        Description = "Disband a unit",
        Usage = "disband_unit <unitId>",
        Examples = new[] { "disband_unit 1", "disband 2" })]
    public class DisbandUnitCommandFactory : ICommandFactory
    {
        public bool TryCreateCommand(string[] args, GameState gameState, out ICommand command, out string errorMessage)
        {
            command = null;
            errorMessage = null;

            if (args.Length < 1)
            {
                errorMessage = "Usage: disband_unit <unitId>";
                return false;
            }

            if (!ushort.TryParse(args[0], out ushort unitId))
            {
                errorMessage = $"Invalid unit ID: '{args[0]}'";
                return false;
            }

            command = new DisbandUnitCommand(unitId);
            return true;
        }
    }
}
