using Core;
using Core.Commands;
using UnityEngine;
using Utils;

namespace StarterKit.Commands
{
    /// <summary>
    /// StarterKit command: Create a unit at a province.
    /// </summary>
    public class CreateUnitCommand : BaseCommand
    {
        private ushort provinceId;
        private string unitTypeId;
        private ushort createdUnitId; // For undo

        public CreateUnitCommand() { }

        public CreateUnitCommand(ushort provinceId, string unitTypeId)
        {
            this.provinceId = provinceId;
            this.unitTypeId = unitTypeId;
        }

        public override bool Validate(GameState gameState)
        {
            var initializer = Object.FindFirstObjectByType<Initializer>();
            if (initializer == null || initializer.UnitSystem == null)
                return false;

            // Check unit type exists
            var unitType = initializer.UnitSystem.GetUnitType(unitTypeId);
            if (unitType == null)
                return false;

            // Check province is owned by player
            if (!initializer.UnitSystem.IsProvinceOwnedByPlayer(provinceId))
                return false;

            return true;
        }

        public override void Execute(GameState gameState)
        {
            var initializer = Object.FindFirstObjectByType<Initializer>();
            if (initializer?.UnitSystem == null)
            {
                ArchonLogger.LogError("CreateUnitCommand: UnitSystem not found", "starter_kit");
                return;
            }

            createdUnitId = initializer.UnitSystem.CreateUnit(provinceId, unitTypeId);

            if (createdUnitId != 0)
            {
                LogExecution($"Created {unitTypeId} (ID={createdUnitId}) in province {provinceId}");
            }
            else
            {
                ArchonLogger.LogWarning($"CreateUnitCommand: Failed to create {unitTypeId} in province {provinceId}", "starter_kit");
            }
        }

        public override void Undo(GameState gameState)
        {
            if (createdUnitId == 0) return;

            var initializer = Object.FindFirstObjectByType<Initializer>();
            if (initializer?.UnitSystem == null) return;

            initializer.UnitSystem.DisbandUnit(createdUnitId);
            LogExecution($"Undid unit creation (disbanded unit {createdUnitId})");
        }

        public override void Serialize(System.IO.BinaryWriter writer)
        {
            writer.Write(provinceId);
            writer.Write(unitTypeId ?? "");
        }

        public override void Deserialize(System.IO.BinaryReader reader)
        {
            provinceId = reader.ReadUInt16();
            unitTypeId = reader.ReadString();
        }
    }

    /// <summary>
    /// Factory for creating CreateUnitCommand from console input.
    /// </summary>
    [CommandMetadata("create_unit",
        Aliases = new[] { "unit", "spawn" },
        Description = "Create a unit at a province",
        Usage = "create_unit <unitType> <provinceId>",
        Examples = new[] { "create_unit infantry 5", "unit cavalry 10" })]
    public class CreateUnitCommandFactory : ICommandFactory
    {
        public bool TryCreateCommand(string[] args, GameState gameState, out ICommand command, out string errorMessage)
        {
            command = null;
            errorMessage = null;

            if (args.Length < 2)
            {
                errorMessage = "Usage: create_unit <unitType> <provinceId>";
                return false;
            }

            string unitTypeId = args[0];

            if (!ushort.TryParse(args[1], out ushort provinceId))
            {
                errorMessage = $"Invalid province ID: '{args[1]}'";
                return false;
            }

            command = new CreateUnitCommand(provinceId, unitTypeId);
            return true;
        }
    }
}
