using UnityEngine;
using Unity.Collections;
using System.Collections.Generic;
using Core.Queries;
using Map.Rendering;

namespace Map.MapModes
{
    /// <summary>
    /// ENGINE LAYER - Generic gradient-based map mode handler
    ///
    /// Architecture:
    /// - Pure mechanism, no game-specific knowledge
    /// - Concrete GAME modes override: GetGradient(), GetValueForProvince()
    /// - Uses province palette for memory-efficient map mode rendering
    /// - CPU computes per-province colors (~100k), GPU does per-pixel lookup (~100M)
    ///
    /// Performance:
    /// - Province palette: 100k provinces * 16 modes = ~6.4MB (vs 6.24GB full-res)
    /// - CPU gradient calculation: ~1-2ms for 100k provinces
    /// - Mode switching is instant (just changes shader int)
    ///
    /// Usage (GAME Layer):
    /// public class FarmDensityMapMode : GradientMapMode
    /// {
    ///     protected override ColorGradient GetGradient() => new ColorGradient(...);
    ///     protected override float GetValueForProvince(ushort id) => buildingSystem.GetFarmCount(id);
    /// }
    /// </summary>
    public abstract class GradientMapMode : IMapModeHandler
    {
        // Special colors (can be overridden by subclasses)
        protected virtual Color32 OceanColor => new Color32(25, 25, 112, 255);    // Dark blue
        protected virtual Color32 UnownedColor => new Color32(128, 128, 128, 255); // Gray for unowned land

        // Dirty flag for optimization - skip updates when data hasn't changed
        private bool isDirty = true;

        // Province palette integration
        private MapModeManager mapModeManager;
        private int paletteIndex = -1;
        private bool isRegistered = false;

        // Province color cache for batch updates
        private Dictionary<int, Color32> provinceColors = new Dictionary<int, Color32>();

        /// <summary>
        /// Mark this map mode as dirty (needs recalculation)
        /// Call this when underlying data changes (province ownership, development, etc.)
        /// The texture will be updated on the next frame via RequestDeferredUpdate(),
        /// but ONLY if this map mode is currently active.
        /// </summary>
        public void MarkDirty()
        {
            isDirty = true;

            // Only request update if this mode is currently active
            // This prevents expensive texture rebuilds for inactive map modes
            mapModeManager?.RequestDeferredUpdate(this);
        }

        /// <summary>
        /// Check if this map mode is currently the active one
        /// </summary>
        public bool IsActive => mapModeManager != null && mapModeManager.GetHandler(mapModeManager.CurrentMode) == this;

        /// <summary>
        /// Register with MapModeManager to get a palette slot.
        /// Called during initialization.
        /// </summary>
        protected void RegisterWithMapModeManager(MapModeManager manager)
        {
            if (isRegistered) return;

            mapModeManager = manager;
            if (mapModeManager == null)
            {
                ArchonLogger.LogError($"{Name}: MapModeManager is null, cannot register", "map_modes");
                return;
            }

            // Register and get palette index
            paletteIndex = mapModeManager.RegisterCustomMapMode(ShaderModeID);
            if (paletteIndex < 0)
            {
                ArchonLogger.LogError($"{Name}: Failed to register with MapModeManager", "map_modes");
                return;
            }

            isRegistered = true;
            isDirty = true; // Force initial update

            var (maxProvinces, maxModes, rowsPerMode) = mapModeManager.GetPaletteInfo();
            ArchonLogger.Log($"{Name}: Registered with MapModeManager at palette index {paletteIndex} (max {maxProvinces} provinces)", "map_modes");
        }

        /// <summary>
        /// Called when map mode is activated.
        /// Subclasses should call base.OnMapModeActivated() if they override OnActivate.
        /// </summary>
        protected void OnMapModeActivated()
        {
            if (!isRegistered)
            {
                ArchonLogger.LogWarning($"{Name}: Activated but not registered with MapModeManager", "map_modes");
            }
        }

        // IMapModeHandler implementation
        public abstract MapMode Mode { get; }
        public abstract string Name { get; }
        public abstract int ShaderModeID { get; }
        public virtual bool RequiresFrequentUpdates => GetUpdateFrequency() >= UpdateFrequency.Daily;
        public abstract void OnActivate(Material mapMaterial, MapModeDataTextures dataTextures);
        public abstract void OnDeactivate(Material mapMaterial);
        public abstract string GetProvinceTooltip(ushort provinceId, ProvinceQueries provinceQueries, CountryQueries countryQueries);
        public abstract UpdateFrequency GetUpdateFrequency();

        /// <summary>
        /// Get the color gradient to use for this map mode
        /// </summary>
        protected abstract ColorGradient GetGradient();

        /// <summary>
        /// Get the numeric value for a province (will be normalized and mapped to gradient)
        /// Return 0 or negative to skip the province
        /// </summary>
        protected abstract float GetValueForProvince(ushort provinceId, ProvinceQueries provinceQueries, object gameProvinceSystem);

        /// <summary>
        /// Get human-readable category name for a value (used in tooltips)
        /// Override for custom categorization
        /// </summary>
        protected virtual string GetValueCategory(float value)
        {
            // Default 5-tier categorization
            if (value >= GetCategoryThreshold(4)) return "Very High";
            if (value >= GetCategoryThreshold(3)) return "High";
            if (value >= GetCategoryThreshold(2)) return "Medium";
            if (value >= GetCategoryThreshold(1)) return "Low";
            return "Very Low";
        }

        /// <summary>
        /// Override to provide custom category thresholds
        /// Default assumes 0-100 range
        /// </summary>
        protected virtual float GetCategoryThreshold(int tier)
        {
            return tier switch
            {
                1 => 10f,
                2 => 20f,
                3 => 30f,
                4 => 50f,
                _ => 0f
            };
        }

        /// <summary>
        /// Format value for display in tooltip
        /// Override for custom formatting (e.g., "10.5 gold", "1000 soldiers")
        /// </summary>
        protected virtual string FormatValue(float value)
        {
            return value.ToString("F1");
        }

        public virtual void UpdateTextures(MapModeDataTextures dataTextures, ProvinceQueries provinceQueries,
                                          CountryQueries countryQueries, ProvinceMapping provinceMapping,
                                          object gameProvinceSystem = null)
        {
            // Skip if not dirty
            if (!isDirty) return;

            // Ensure we're registered
            if (!isRegistered)
            {
                ArchonLogger.LogWarning($"{Name}: Not registered, skipping texture update", "map_modes");
                return;
            }

            var startTime = Time.realtimeSinceStartup;

            // Get all provinces
            using var allProvinces = provinceQueries.GetAllProvinceIds(Allocator.Temp);

            if (allProvinces.Length == 0)
            {
                ArchonLogger.LogWarning($"{Name}: No provinces available", "map_modes");
                return;
            }

            // Calculate province colors and update palette
            var stats = CalculateProvinceColors(allProvinces, provinceQueries, gameProvinceSystem);

            // Update palette texture with computed colors
            mapModeManager.UpdateProvinceColors(paletteIndex, provinceColors);

            // Clear dirty flag
            isDirty = false;

            var elapsed = (Time.realtimeSinceStartup - startTime) * 1000f;
            ArchonLogger.Log($"{Name}: Updated {stats.ValidProvinces} provinces in {elapsed:F2}ms " +
                           $"[Range: {stats.MinValue:F1}-{stats.MaxValue:F1}]", "map_modes");
        }

        /// <summary>
        /// Calculate province colors using gradient evaluation
        /// </summary>
        private ValueStats CalculateProvinceColors(NativeArray<ushort> provinces, ProvinceQueries provinceQueries,
                                                   object gameProvinceSystem)
        {
            var stats = new ValueStats
            {
                MinValue = float.MaxValue,
                MaxValue = float.MinValue
            };

            // Temporary storage for raw values
            var rawValues = new Dictionary<ushort, float>();
            float totalValue = 0f;
            int validCount = 0;

            // First pass: collect raw values and find min/max
            for (int i = 0; i < provinces.Length; i++)
            {
                var provinceId = provinces[i];
                if (provinceId == 0) continue;

                float value = GetValueForProvince(provinceId, provinceQueries, gameProvinceSystem);

                if (value < 0f) continue; // Skip invalid

                rawValues[provinceId] = value;

                if (value > 0f)
                {
                    totalValue += value;
                    validCount++;

                    if (value < stats.MinValue) stats.MinValue = value;
                    if (value > stats.MaxValue) stats.MaxValue = value;
                }
            }

            stats.ValidProvinces = validCount;
            stats.AvgValue = validCount > 0 ? totalValue / validCount : 0f;

            // Handle edge cases
            if (stats.MinValue == float.MaxValue) stats.MinValue = 0f;
            if (stats.MaxValue == float.MinValue) stats.MaxValue = 1f;

            // Second pass: normalize values and compute gradient colors
            float valueRange = stats.MaxValue - stats.MinValue;
            bool uniformValues = valueRange < 0.001f;
            var gradient = GetGradient();

            provinceColors.Clear();

            foreach (var kvp in rawValues)
            {
                ushort provinceId = kvp.Key;
                float value = kvp.Value;

                // Normalize to [0,1]
                float normalized;
                if (value == 0f)
                {
                    normalized = 0f;
                }
                else
                {
                    normalized = uniformValues ? 0.5f : Mathf.Clamp01((value - stats.MinValue) / valueRange);
                }

                // Evaluate gradient and store color
                Color32 color = gradient.Evaluate(normalized);
                provinceColors[provinceId] = color;
            }

            return stats;
        }

        /// <summary>
        /// Helper method for subclasses to disable all map mode keywords (legacy compatibility)
        /// </summary>
        protected void DisableAllMapModeKeywords(Material mapMaterial)
        {
            // No longer needed - mode switching is int-based now
            // Kept for GAME layer backward compatibility
        }

        /// <summary>
        /// Helper method for subclasses to enable a specific keyword (legacy compatibility)
        /// </summary>
        protected void EnableMapModeKeyword(Material mapMaterial, string keyword)
        {
            // No longer needed - mode switching is int-based now
            // Kept for GAME layer backward compatibility
        }

        /// <summary>
        /// Helper method for subclasses to set shader mode (legacy compatibility)
        /// </summary>
        protected void SetShaderMode(Material mapMaterial, int modeID)
        {
            // Now handled by MapModeManager.SetMapMode()
            // Kept for GAME layer backward compatibility
            mapMaterial.SetInt("_MapMode", modeID);
        }

        /// <summary>
        /// Helper method for subclasses to log activation
        /// </summary>
        protected void LogActivation(string message)
        {
            ArchonLogger.Log($"{Name}: {message}", "map_modes");
        }

        /// <summary>
        /// Helper method for subclasses to log deactivation
        /// </summary>
        protected void LogDeactivation(string message = null)
        {
            ArchonLogger.Log($"{Name}: Deactivated{(message != null ? " - " + message : "")}", "map_modes");
        }

        /// <summary>
        /// Statistics for value distribution analysis
        /// </summary>
        protected struct ValueStats
        {
            public float MinValue;
            public float MaxValue;
            public float AvgValue;
            public int ValidProvinces;
        }

        /// <summary>
        /// Dispose resources
        /// </summary>
        public void Dispose()
        {
            provinceColors?.Clear();
            provinceColors = null;
            mapModeManager = null;
            isRegistered = false;
        }
    }
}
