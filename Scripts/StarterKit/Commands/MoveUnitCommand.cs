using Core;
using Core.Commands;
using UnityEngine;
using Utils;

namespace StarterKit.Commands
{
    /// <summary>
    /// StarterKit command: Move a unit to a new province.
    /// </summary>
    public class MoveUnitCommand : BaseCommand
    {
        private ushort unitId;
        private ushort targetProvinceId;
        private ushort previousProvinceId; // For undo

        public MoveUnitCommand() { }

        public MoveUnitCommand(ushort unitId, ushort targetProvinceId)
        {
            this.unitId = unitId;
            this.targetProvinceId = targetProvinceId;
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

            // Check target province is valid
            if (targetProvinceId == 0 || targetProvinceId >= gameState.Provinces.ProvinceCount)
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

            var unit = initializer.UnitSystem.GetUnit(unitId);
            previousProvinceId = unit.provinceID;

            initializer.UnitSystem.MoveUnit(unitId, targetProvinceId);
            LogExecution($"Moved unit {unitId} from province {previousProvinceId} to {targetProvinceId}");
        }

        public override void Undo(GameState gameState)
        {
            var initializer = Object.FindFirstObjectByType<Initializer>();
            if (initializer?.UnitSystem == null) return;

            initializer.UnitSystem.MoveUnit(unitId, previousProvinceId);
            LogExecution($"Undid move (unit {unitId} back to province {previousProvinceId})");
        }

        public override void Serialize(System.IO.BinaryWriter writer)
        {
            writer.Write(unitId);
            writer.Write(targetProvinceId);
        }

        public override void Deserialize(System.IO.BinaryReader reader)
        {
            unitId = reader.ReadUInt16();
            targetProvinceId = reader.ReadUInt16();
        }
    }

    /// <summary>
    /// Factory for creating MoveUnitCommand from console input.
    /// </summary>
    [CommandMetadata("move_unit",
        Aliases = new[] { "move" },
        Description = "Move a unit to a province",
        Usage = "move_unit <unitId> <provinceId>",
        Examples = new[] { "move_unit 1 10", "move 2 15" })]
    public class MoveUnitCommandFactory : ICommandFactory
    {
        public bool TryCreateCommand(string[] args, GameState gameState, out ICommand command, out string errorMessage)
        {
            command = null;
            errorMessage = null;

            if (args.Length < 2)
            {
                errorMessage = "Usage: move_unit <unitId> <provinceId>";
                return false;
            }

            if (!ushort.TryParse(args[0], out ushort unitId))
            {
                errorMessage = $"Invalid unit ID: '{args[0]}'";
                return false;
            }

            if (!ushort.TryParse(args[1], out ushort provinceId))
            {
                errorMessage = $"Invalid province ID: '{args[1]}'";
                return false;
            }

            command = new MoveUnitCommand(unitId, provinceId);
            return true;
        }
    }
}
