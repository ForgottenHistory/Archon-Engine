using Core;
using Core.Commands;
using StarterKit.Validation;
using Utils;

namespace StarterKit.Commands
{
    /// <summary>
    /// StarterKit command: Construct a building in a province.
    /// Demonstrates fluent validation with multiple GAME-layer validators.
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

        private string validationError;

        public override bool Validate(GameState gameState)
        {
            // Fluent validation: province valid, building type exists, can construct
            return Core.Validation.Validate.For(gameState)
                .Province(ProvinceId)
                .BuildingTypeExists(BuildingTypeId)
                .CanConstructBuilding(ProvinceId, BuildingTypeId)
                .Result(out validationError);
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
