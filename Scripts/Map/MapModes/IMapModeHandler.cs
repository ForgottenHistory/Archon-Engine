using UnityEngine;
using Core.Queries;
using Map.Rendering;

namespace Map.MapModes
{
    /// <summary>
    /// Interface for map mode handlers following the new architecture
    /// Each map mode is responsible for its own data textures and rendering logic
    /// Performance: &lt;0.1ms mode switching, specialized texture updates
    /// </summary>
    public interface IMapModeHandler
    {
        /// <summary>
        /// Map mode type this handler manages
        /// </summary>
        MapMode Mode { get; }

        /// <summary>
        /// Display name for UI
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Shader mode ID for the GPU
        /// </summary>
        int ShaderModeID { get; }

        /// <summary>
        /// Whether this mode requires frequent texture updates
        /// </summary>
        bool RequiresFrequentUpdates { get; }

        /// <summary>
        /// Called when this map mode becomes active
        /// Set up shader properties, enable keywords, etc.
        /// </summary>
        void OnActivate(Material mapMaterial, MapModeDataTextures dataTextures);

        /// <summary>
        /// Called when this map mode becomes inactive
        /// Clean up, disable keywords, etc.
        /// </summary>
        void OnDeactivate(Material mapMaterial);

        /// <summary>
        /// Update the mode-specific data textures from Core simulation data
        /// Only called when data has changed or mode requires frequent updates
        /// </summary>
        /// <param name="gameProvinceSystem">Optional game-specific province system - engine passes through without knowing type</param>
        void UpdateTextures(MapModeDataTextures dataTextures, ProvinceQueries provinceQueries, CountryQueries countryQueries, ProvinceMapping provinceMapping, object gameProvinceSystem = null);

        /// <summary>
        /// Get tooltip text for a specific province in this map mode
        /// </summary>
        string GetProvinceTooltip(ushort provinceId, ProvinceQueries provinceQueries, CountryQueries countryQueries);

        /// <summary>
        /// Get update frequency for this map mode's textures
        /// </summary>
        UpdateFrequency GetUpdateFrequency();
    }

    /// <summary>
    /// Map mode types supported by the system
    /// </summary>
    public enum MapMode
    {
        // Basic modes (single data source)
        Political = 0,      // Owner ID → Country color
        Terrain = 1,        // Terrain.bmp → Geographical terrain colors
        Development = 2,    // Development level → Gradient
        Culture = 3,        // Culture ID → Culture color
        Religion = 4,       // Religion ID → Religion color

        // Composite modes (multiple data sources)
        Diplomatic = 5,     // Relations → Color gradient
        Trade = 6,          // Trade value + flow → Heatmap
        Military = 7,       // Army strength → Threat colors
        Economic = 8,       // Income/expenses → Green/red gradient

        // Special modes
        Selected = 9,       // Highlights for selected country
        StrategicView = 10, // Simplified military view
        PlayerMapMode = 11, // Custom player-defined
        ProvinceColors = 12, // Provinces.bmp → Original province colors (debug/simple)

        // Debug modes (100+)
        BorderDebug = 100,  // Shows border texture in grayscale
        ProvinceIDDebug = 101, // Shows province IDs as colors
        HeightmapDebug = 102,  // Shows heightmap in grayscale
        NormalMapDebug = 103   // Shows normal map as RGB
    }

    /// <summary>
    /// How frequently a map mode's textures need updating
    /// </summary>
    public enum UpdateFrequency
    {
        Never = 0,          // Static data (terrain)
        Yearly = 1,         // Very slow changes (culture)
        Monthly = 2,        // Slow changes (development)
        Weekly = 3,         // Regular changes (trade)
        Daily = 4,          // Fast changes (military during war)
        PerConquest = 5,    // Event-driven (political ownership)
        RealTime = 6        // Continuous updates (selected highlights)
    }

    /// <summary>
    /// Base class for map mode handlers with common functionality
    /// </summary>
    public abstract class BaseMapModeHandler : IMapModeHandler
    {
        public abstract MapMode Mode { get; }
        public abstract string Name { get; }
        public abstract int ShaderModeID { get; }
        public virtual bool RequiresFrequentUpdates => GetUpdateFrequency() >= UpdateFrequency.Daily;

        public abstract void OnActivate(Material mapMaterial, MapModeDataTextures dataTextures);
        public abstract void OnDeactivate(Material mapMaterial);
        public abstract void UpdateTextures(MapModeDataTextures dataTextures, ProvinceQueries provinceQueries, CountryQueries countryQueries, ProvinceMapping provinceMapping, object gameProvinceSystem = null);
        public abstract string GetProvinceTooltip(ushort provinceId, ProvinceQueries provinceQueries, CountryQueries countryQueries);
        public abstract UpdateFrequency GetUpdateFrequency();

        /// <summary>
        /// Utility: Disable all map mode keywords in the shader
        /// </summary>
        protected void DisableAllMapModeKeywords(Material material)
        {
            material.DisableKeyword("MAP_MODE_POLITICAL");
            material.DisableKeyword("MAP_MODE_TERRAIN");
            material.DisableKeyword("MAP_MODE_DEVELOPMENT");
            material.DisableKeyword("MAP_MODE_CULTURE");
            material.DisableKeyword("MAP_MODE_RELIGION");
            material.DisableKeyword("MAP_MODE_DIPLOMATIC");
            material.DisableKeyword("MAP_MODE_TRADE");
            material.DisableKeyword("MAP_MODE_MILITARY");
            material.DisableKeyword("MAP_MODE_ECONOMIC");
            material.DisableKeyword("MAP_MODE_SELECTED");
            material.DisableKeyword("MAP_MODE_STRATEGIC");
        }

        /// <summary>
        /// Utility: Enable a specific map mode keyword
        /// </summary>
        protected void EnableMapModeKeyword(Material material, string keyword)
        {
            material.EnableKeyword(keyword);
        }

        /// <summary>
        /// Utility: Set common shader properties
        /// </summary>
        protected void SetShaderMode(Material material, int modeId)
        {
            material.SetInt("_MapMode", modeId);
        }

        /// <summary>
        /// Utility: Log mode activation
        /// </summary>
        protected void LogActivation(string message = null)
        {
            var msg = message ?? $"{Name} map mode activated";
            ArchonLogger.Log($"MapMode: {msg}", "map_modes");
        }

        /// <summary>
        /// Utility: Log mode deactivation
        /// </summary>
        protected void LogDeactivation(string message = null)
        {
            var msg = message ?? $"{Name} map mode deactivated";
            ArchonLogger.Log($"MapMode: {msg}", "map_modes");
        }
    }
}