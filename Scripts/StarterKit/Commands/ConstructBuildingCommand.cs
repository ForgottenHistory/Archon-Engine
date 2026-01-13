using Core;
using Core.Commands;
using Utils;

namespace StarterKit.Commands
{
    /// <summary>
    /// StarterKit command: Construct a building in a province.
    /// </summary>
    [Command("build",
        Aliases = new[] { "construct" },
        Description = "Construct a building in a province",
        Examples = new[] { "build market 5", "construct farm 10" })]
    public class ConstructBuildingCommand : SimpleCommand
    {
        [Arg(0, "buildingType")]
        public string BuildingTypeId { get; set; }

        [Arg(1, "provinceId")]
        public ushort ProvinceId { get; set; }

        public override bool Validate(GameState gameState)
        {
            var buildings = Initializer.Instance?.BuildingSystem;
            if (buildings == null)
                return false;

            return buildings.CanConstruct(ProvinceId, BuildingTypeId, out _);
        }

        public override void Execute(GameState gameState)
        {
            var buildings = Initializer.Instance?.BuildingSystem;
            if (buildings == null)
            {
                ArchonLogger.LogError("ConstructBuildingCommand: BuildingSystem not found", "starter_kit");
                return;
            }

            bool success = buildings.Construct(ProvinceId, BuildingTypeId);

            if (success)
                LogExecution($"Constructed {BuildingTypeId} in province {ProvinceId}");
            else
            {
                buildings.CanConstruct(ProvinceId, BuildingTypeId, out string reason);
                ArchonLogger.LogWarning($"ConstructBuildingCommand: Failed - {reason}", "starter_kit");
            }
        }

        // Note: Undo not supported - BuildingSystem doesn't have demolish
    }
}
