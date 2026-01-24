using Core;
using Core.Commands;
using Core.Data.Ids;
using StarterKit.Validation;
using Utils;

namespace StarterKit.Commands
{
    /// <summary>
    /// StarterKit command: Construct a building in a province.
    /// Used by both player (via console) and AI (programmatically).
    /// Uses type-safe ProvinceId wrapper for compile-time safety.
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
        public ProvinceId ProvinceId { get; set; }

        /// <summary>
        /// Country executing the command. If 0, uses player's country.
        /// Set by AI when executing for AI countries.
        /// Optional for console use (defaults to 0 = player).
        /// </summary>
        [Arg(2, "countryId", Optional = true)]
        public ushort CountryId { get; set; }

        private string validationError;

        public override bool Validate(GameState gameState)
        {
            // Resolve country ID (0 = player's country)
            ushort effectiveCountryId = CountryId;
            if (effectiveCountryId == 0)
            {
                var playerState = Initializer.Instance?.PlayerState;
                effectiveCountryId = playerState?.PlayerCountryId ?? 0;
            }

            // Fluent validation: province valid, building type exists, can construct for country
            return Core.Validation.Validate.For(gameState)
                .Province(ProvinceId)
                .BuildingTypeExists(BuildingTypeId)
                .CanConstructBuildingForCountry(ProvinceId, BuildingTypeId, effectiveCountryId)
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

            // Resolve country ID (0 = player's country)
            ushort effectiveCountryId = CountryId;
            if (effectiveCountryId == 0)
            {
                var playerState = Initializer.Instance?.PlayerState;
                effectiveCountryId = playerState?.PlayerCountryId ?? 0;
            }

            bool success = buildings.ConstructForCountry(ProvinceId, BuildingTypeId, effectiveCountryId);

            if (success)
                LogExecution($"Country {effectiveCountryId} constructed {BuildingTypeId} in province {ProvinceId}");
            else
            {
                buildings.CanConstructForCountry(ProvinceId, BuildingTypeId, effectiveCountryId, out string reason);
                ArchonLogger.LogWarning($"ConstructBuildingCommand: Failed - {reason}", "starter_kit");
            }
        }

        // Serialization handled automatically by SimpleCommand via [Arg] attributes
    }
}
