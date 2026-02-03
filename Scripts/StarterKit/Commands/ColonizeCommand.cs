using Core;
using Core.Commands;
using Core.Data.Ids;
using Core.Systems;

namespace StarterKit.Commands
{
    /// <summary>
    /// StarterKit command: Colonize an unowned province.
    /// Deducts gold and sets province ownership.
    /// Used by player UI and AI.
    /// </summary>
    [Command("colonize",
        Description = "Colonize an unowned province",
        Examples = new[] { "colonize 123" })]
    public class ColonizeCommand : SimpleCommand
    {
        public const int COLONIZE_COST = 20;

        [Arg(0, "provinceId")]
        public ProvinceId ProvinceId { get; set; }

        [Arg(1, "countryId")]
        public ushort CountryId { get; set; }

        public override bool Validate(GameState gameState)
        {
            // Check province exists (use direct field reference, not GetComponent)
            var provinces = gameState.Provinces;
            if (provinces == null || ProvinceId.Value == 0 || ProvinceId.Value > provinces.ProvinceCount)
                return false;

            // Check province is unowned
            ushort currentOwner = provinces.GetProvinceOwner(ProvinceId.Value);
            if (currentOwner != 0)
                return false;

            // Check country has enough gold
            var economySystem = Initializer.Instance?.EconomySystem;
            if (economySystem == null)
                return false;

            int gold = economySystem.GetCountryGoldInt(CountryId);
            if (gold < COLONIZE_COST)
                return false;

            return true;
        }

        public override void Execute(GameState gameState)
        {
            var economySystem = Initializer.Instance?.EconomySystem;
            var provinces = gameState.Provinces;

            if (economySystem == null || provinces == null)
            {
                ArchonLogger.LogError("ColonizeCommand: Missing systems", "starter_kit");
                return;
            }

            // Deduct gold
            economySystem.RemoveGoldFromCountry(CountryId, COLONIZE_COST);

            // Set province owner
            provinces.SetProvinceOwner(ProvinceId.Value, CountryId);
        }
    }
}
