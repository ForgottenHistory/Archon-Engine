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

            ArchonLogger.Log("GameRegistries initialized with all entity registries", "core_data_linking");
        }

        /// <summary>
        /// Get diagnostic information for all registries
        /// </summary>
        public string GetDiagnostics()
        {
            return $"GameRegistries Status:\n" +
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
            if (Terrains.Count == 0)
            {
                ArchonLogger.LogError("GameRegistries validation failed: No terrains loaded", "core_data_loading");
                isValid = false;
            }

            // Countries and provinces can be empty initially
            ArchonLogger.Log($"GameRegistries validation: {(isValid ? "PASSED" : "FAILED")}", "core_data_linking");
            return isValid;
        }
    }

    // Placeholder data classes - these will be properly implemented in Phase 3
    public class TerrainData
    {
        public string Name { get; set; }
        public byte TerrainId { get; set; }

        // Terrain properties (loaded from terrain.json5)
        public bool IsWater { get; set; }
        public float MovementCost { get; set; } = 1.0f;
        public int DefenceBonus { get; set; }
        public int SupplyLimit { get; set; } = 5;

        // Color (from terrain.json5, for Map layer reference)
        public byte ColorR { get; set; }
        public byte ColorG { get; set; }
        public byte ColorB { get; set; }
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