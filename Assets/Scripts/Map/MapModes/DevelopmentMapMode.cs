using UnityEngine;
using Unity.Collections;
using Core.Queries;
using System;
using Map.Rendering;

namespace Map.MapModes
{
    /// <summary>
    /// Development map mode handler - visualizes province development levels
    /// Uses existing texture system with proper shader-based rendering
    /// Performance: Monthly updates, efficient rendering using red-orange-yellow gradient
    /// </summary>
    public class DevelopmentMapMode : BaseMapModeHandler
    {
        public override MapMode Mode => MapMode.Development;
        public override string Name => "Development";
        public override int ShaderModeID => 2;

        // Development color gradient - Red to Yellow showing economic progress
        private static readonly Color32 VeryLowDev = new Color32(139, 0, 0, 255);      // Dark red
        private static readonly Color32 LowDev = new Color32(220, 20, 20, 255);        // Red
        private static readonly Color32 MediumDev = new Color32(255, 140, 0, 255);     // Orange
        private static readonly Color32 HighDev = new Color32(255, 215, 0, 255);       // Gold
        private static readonly Color32 VeryHighDev = new Color32(255, 255, 0, 255);   // Bright yellow

        // Special colors
        private static readonly Color32 OceanColor = new Color32(25, 25, 112, 255);    // Dark blue
        private static readonly Color32 UnknownColor = new Color32(64, 64, 64, 255);   // Dark gray

        public override void OnActivate(Material mapMaterial, MapModeDataTextures dataTextures)
        {
            // Clean slate - disable all map mode keywords
            DisableAllMapModeKeywords(mapMaterial);

            // Enable development mode
            EnableMapModeKeyword(mapMaterial, "MAP_MODE_DEVELOPMENT");
            SetShaderMode(mapMaterial, ShaderModeID);

            LogActivation("Development visualization - showing economic progress");
        }

        public override void OnDeactivate(Material mapMaterial)
        {
            LogDeactivation("Development mode deactivated");
        }

        public override void UpdateTextures(MapModeDataTextures dataTextures, ProvinceQueries provinceQueries, CountryQueries countryQueries, ProvinceMapping provinceMapping)
        {
            if (dataTextures?.ProvinceDevelopmentTexture == null)
            {
                DominionLogger.LogError("DevelopmentMapMode: Development texture not available");
                return;
            }

            var startTime = Time.realtimeSinceStartup;

            // Get Core data
            using var allProvinces = provinceQueries.GetAllProvinceIds(Allocator.Temp);

            if (allProvinces.Length == 0)
            {
                DominionLogger.LogWarning("DevelopmentMapMode: No provinces available");
                return;
            }

            // Analyze development distribution for optimal color mapping
            var devStats = AnalyzeDevelopmentDistribution(allProvinces, provinceQueries);

            // CRITICAL: Update the GPU texture with development data (dual-layer architecture)
            UpdateDevelopmentTexture(dataTextures, allProvinces, provinceQueries, devStats, provinceMapping);

            var elapsed = (Time.realtimeSinceStartup - startTime) * 1000f;

            DominionLogger.Log($"DevelopmentMapMode: Updated {devStats.ValidProvinces} provinces in {elapsed:F2}ms " +
                             $"[Range: {devStats.MinDev}-{devStats.MaxDev}, Avg: {devStats.AvgDev:F1}] - populated GPU texture");
        }

        public override string GetProvinceTooltip(ushort provinceId, ProvinceQueries provinceQueries, CountryQueries countryQueries)
        {
            if (provinceQueries.IsOcean(provinceId))
            {
                return "Ocean - No development";
            }

            var development = provinceQueries.GetDevelopment(provinceId);
            var owner = provinceQueries.GetOwner(provinceId);

            string ownerName = owner == 0 ? "Unowned" : countryQueries.GetTag(owner);
            string devCategory = GetDevelopmentCategory(development);

            return $"Province {provinceId}\n" +
                   $"Owner: {ownerName}\n" +
                   $"Development: {development} ({devCategory})\n" +
                   $"Economic Value: {GetEconomicValue(development)}";
        }

        public override UpdateFrequency GetUpdateFrequency()
        {
            return UpdateFrequency.Monthly; // Development changes slowly over time
        }

        /// <summary>
        /// Analyze development distribution across all valid provinces
        /// </summary>
        private DevelopmentStats AnalyzeDevelopmentDistribution(NativeArray<ushort> provinces, ProvinceQueries queries)
        {
            var stats = new DevelopmentStats
            {
                MinDev = byte.MaxValue,
                MaxDev = byte.MinValue
            };

            int totalDev = 0;
            int validCount = 0;

            for (int i = 0; i < provinces.Length; i++)
            {
                var provinceId = provinces[i];

                // Skip ocean and invalid provinces
                if (queries.IsOcean(provinceId) || !queries.Exists(provinceId))
                    continue;

                var development = queries.GetDevelopment(provinceId);

                totalDev += development;
                validCount++;

                if (development < stats.MinDev) stats.MinDev = development;
                if (development > stats.MaxDev) stats.MaxDev = development;
            }

            stats.ValidProvinces = validCount;
            stats.AvgDev = validCount > 0 ? (float)totalDev / validCount : 0f;

            return stats;
        }

        /// <summary>
        /// Map development value to color using smooth gradient
        /// </summary>
        private Color32 GetDevelopmentColor(byte development, byte minDev, byte maxDev)
        {
            if (maxDev == minDev)
            {
                return MediumDev; // Uniform development
            }

            // Normalize to 0-1 range
            float normalized = (float)(development - minDev) / (maxDev - minDev);

            // Apply 5-point gradient
            if (normalized <= 0.2f)
            {
                return Color32.Lerp(VeryLowDev, LowDev, normalized * 5f);
            }
            else if (normalized <= 0.4f)
            {
                return Color32.Lerp(LowDev, MediumDev, (normalized - 0.2f) * 5f);
            }
            else if (normalized <= 0.6f)
            {
                return Color32.Lerp(MediumDev, HighDev, (normalized - 0.4f) * 5f);
            }
            else if (normalized <= 0.8f)
            {
                return Color32.Lerp(HighDev, VeryHighDev, (normalized - 0.6f) * 5f);
            }
            else
            {
                return VeryHighDev;
            }
        }

        /// <summary>
        /// Get human-readable development category
        /// </summary>
        private string GetDevelopmentCategory(byte development)
        {
            return development switch
            {
                >= 50 => "Very High",
                >= 30 => "High",
                >= 20 => "Medium",
                >= 10 => "Low",
                _ => "Very Low"
            };
        }

        /// <summary>
        /// Calculate economic value representation
        /// </summary>
        private string GetEconomicValue(byte development)
        {
            var value = development * 100; // Simple multiplier
            return $"{value:N0} ducats";
        }

        /// <summary>
        /// Update the development texture with Core simulation data
        /// Following texture-based architecture: Core data → GPU texture
        /// </summary>
        private void UpdateDevelopmentTexture(MapModeDataTextures dataTextures, NativeArray<ushort> provinces,
                                            ProvinceQueries provinceQueries, DevelopmentStats stats, ProvinceMapping provinceMapping)
        {
            var texture = dataTextures.ProvinceDevelopmentTexture;
            if (texture == null)
            {
                DominionLogger.LogError("DevelopmentMapMode: ProvinceDevelopmentTexture is null");
                return;
            }

            // Get texture dimensions
            int width = texture.width;
            int height = texture.height;
            var pixels = new Color32[width * height];

            // Initialize with ocean color
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = OceanColor;
            }

            // Update each province's pixels with development colors
            for (int i = 0; i < provinces.Length; i++)
            {
                var provinceId = provinces[i];

                // Skip ocean and invalid provinces
                if (provinceQueries.IsOcean(provinceId) || !provinceQueries.Exists(provinceId))
                    continue;

                var development = provinceQueries.GetDevelopment(provinceId);
                var color = GetDevelopmentColor(development, stats.MinDev, stats.MaxDev);

                // Get all pixels for this province (following existing architecture)
                var provincePixels = provinceMapping.GetProvincePixels(provinceId);
                if (provincePixels != null)
                {
                    foreach (var pixel in provincePixels)
                    {
                        if (pixel.x >= 0 && pixel.x < width && pixel.y >= 0 && pixel.y < height)
                        {
                            int index = pixel.y * width + pixel.x;
                            if (index >= 0 && index < pixels.Length)
                            {
                                pixels[index] = color;
                            }
                        }
                    }
                }
            }

            // Apply texture changes (following texture-based architecture)
            texture.SetPixels32(pixels);
            texture.Apply(false);
        }


        /// <summary>
        /// Development statistics for analysis
        /// </summary>
        private struct DevelopmentStats
        {
            public byte MinDev;
            public byte MaxDev;
            public float AvgDev;
            public int ValidProvinces;
        }
    }
}