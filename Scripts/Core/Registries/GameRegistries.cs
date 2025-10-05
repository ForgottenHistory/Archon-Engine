using Core.Data;
using Utils;

namespace Core.Registries
{
    /// <summary>
    /// Central container for all game entity registries
    /// Provides access to all string-to-ID mapping systems
    /// Following data-linking-architecture.md specifications
    /// </summary>
    public class GameRegistries
    {
        // Static data registries (no dependencies)
        public readonly Registry<ReligionData> Religions = new("Religion");
        public readonly Registry<CultureData> Cultures = new("Culture");
        public readonly Registry<TradeGoodData> TradeGoods = new("TradeGood");
        public readonly Registry<TerrainData> Terrains = new("Terrain");
        public readonly Registry<BuildingData> Buildings = new("Building");
        public readonly Registry<TechnologyData> Technologies = new("Technology");
        public readonly Registry<GovernmentData> Governments = new("Government");

        // Entity registries (have dependencies on static data)
        public readonly CountryRegistry Countries;
        public readonly ProvinceRegistry Provinces;

        public GameRegistries()
        {
            // Initialize special registries with references to static data
            Countries = new CountryRegistry(this);
            Provinces = new ProvinceRegistry();

            ArchonLogger.LogDataLinking("GameRegistries initialized with all entity registries");
        }

        /// <summary>
        /// Get diagnostic information for all registries
        /// </summary>
        public string GetDiagnostics()
        {
            return $"GameRegistries Status:\n" +
                   $"  {Religions.GetDiagnostics()}\n" +
                   $"  {Cultures.GetDiagnostics()}\n" +
                   $"  {TradeGoods.GetDiagnostics()}\n" +
                   $"  {Terrains.GetDiagnostics()}\n" +
                   $"  {Buildings.GetDiagnostics()}\n" +
                   $"  {Technologies.GetDiagnostics()}\n" +
                   $"  {Governments.GetDiagnostics()}\n" +
                   $"  {Countries.GetDiagnostics()}\n" +
                   $"  {Provinces.GetDiagnostics()}";
        }

        /// <summary>
        /// Validate all registries have required data
        /// </summary>
        public bool ValidateRegistries()
        {
            bool isValid = true;

            // Check that static data is loaded
            if (Religions.Count == 0)
            {
                ArchonLogger.LogError("GameRegistries validation failed: No religions loaded");
                isValid = false;
            }

            if (Cultures.Count == 0)
            {
                ArchonLogger.LogError("GameRegistries validation failed: No cultures loaded");
                isValid = false;
            }

            if (TradeGoods.Count == 0)
            {
                ArchonLogger.LogError("GameRegistries validation failed: No trade goods loaded");
                isValid = false;
            }

            // Countries and provinces can be empty initially
            ArchonLogger.LogDataLinking($"GameRegistries validation: {(isValid ? "PASSED" : "FAILED")}");
            return isValid;
        }
    }

    // Placeholder data classes - these will be properly implemented in Phase 3
    public class ReligionData
    {
        public string Name { get; set; }
        public string IconPath { get; set; }
    }

    public class CultureData
    {
        public string Name { get; set; }
        public string CultureGroup { get; set; }
    }

    public class TradeGoodData
    {
        public string Name { get; set; }
        public float BasePrice { get; set; }
    }

    public class TerrainData
    {
        public string Name { get; set; }
        public byte TerrainId { get; set; }
    }

    public class BuildingData
    {
        public string Name { get; set; }
        public int Cost { get; set; }
    }

    public class TechnologyData
    {
        public string Name { get; set; }
        public string TechGroup { get; set; }
    }

    public class GovernmentData
    {
        public string Name { get; set; }
        public string Type { get; set; }
    }
}