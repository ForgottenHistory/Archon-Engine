using Core;
using Core.Commands;
using UnityEngine;
using Utils;

namespace StarterKit.Commands
{
    /// <summary>
    /// StarterKit command: Construct a building in a province.
    /// </summary>
    public class ConstructBuildingCommand : BaseCommand
    {
        private ushort provinceId;
        private string buildingTypeId;

        public ConstructBuildingCommand() { }

        public ConstructBuildingCommand(ushort provinceId, string buildingTypeId)
        {
            this.provinceId = provinceId;
            this.buildingTypeId = buildingTypeId;
        }

        public override bool Validate(GameState gameState)
        {
            var initializer = Object.FindFirstObjectByType<Initializer>();
            if (initializer == null || initializer.BuildingSystem == null)
                return false;

            // Use BuildingSystem's validation
            return initializer.BuildingSystem.CanConstruct(provinceId, buildingTypeId, out _);
        }

        public override void Execute(GameState gameState)
        {
            var initializer = Object.FindFirstObjectByType<Initializer>();
            if (initializer?.BuildingSystem == null)
            {
                ArchonLogger.LogError("ConstructBuildingCommand: BuildingSystem not found", "starter_kit");
                return;
            }

            bool success = initializer.BuildingSystem.Construct(provinceId, buildingTypeId);

            if (success)
            {
                LogExecution($"Constructed {buildingTypeId} in province {provinceId}");
            }
            else
            {
                // Get reason for failure
                initializer.BuildingSystem.CanConstruct(provinceId, buildingTypeId, out string reason);
                ArchonLogger.LogWarning($"ConstructBuildingCommand: Failed - {reason}", "starter_kit");
            }
        }

        public override void Undo(GameState gameState)
        {
            // Note: BuildingSystem doesn't have a Demolish method
            // Undo is not fully supported for buildings in StarterKit
            ArchonLogger.LogWarning("ConstructBuildingCommand: Undo not supported (no demolish)", "starter_kit");
        }

        public override void Serialize(System.IO.BinaryWriter writer)
        {
            writer.Write(provinceId);
            writer.Write(buildingTypeId ?? "");
        }

        public override void Deserialize(System.IO.BinaryReader reader)
        {
            provinceId = reader.ReadUInt16();
            buildingTypeId = reader.ReadString();
        }
    }

    /// <summary>
    /// Factory for creating ConstructBuildingCommand from console input.
    /// </summary>
    [CommandMetadata("build",
        Aliases = new[] { "construct" },
        Description = "Construct a building in a province",
        Usage = "build <buildingType> <provinceId>",
        Examples = new[] { "build market 5", "construct farm 10" })]
    public class ConstructBuildingCommandFactory : ICommandFactory
    {
        public bool TryCreateCommand(string[] args, GameState gameState, out ICommand command, out string errorMessage)
        {
            command = null;
            errorMessage = null;

            if (args.Length < 2)
            {
                errorMessage = "Usage: build <buildingType> <provinceId>";
                return false;
            }

            string buildingTypeId = args[0];

            if (!ushort.TryParse(args[1], out ushort provinceId))
            {
                errorMessage = $"Invalid province ID: '{args[1]}'";
                return false;
            }

            command = new ConstructBuildingCommand(provinceId, buildingTypeId);
            return true;
        }
    }
}
