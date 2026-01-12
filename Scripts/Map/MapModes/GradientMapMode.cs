using UnityEngine;
using Unity.Collections;
using Core.Queries;
using Map.Rendering;
using Map.MapModes.Colorization;

namespace Map.MapModes
{
    /// <summary>
    /// ENGINE LAYER - Generic gradient-based map mode handler
    ///
    /// Responsibilities:
    /// - Analyze value distribution across provinces (min/max/avg)
    /// - Normalize values to 0-1 range
    /// - Apply color gradient via GPU compute shader
    /// - Provide generic tooltip formatting
    ///
    /// Architecture:
    /// - Pure mechanism, no game-specific knowledge
    /// - Concrete map modes provide: gradient colors, data access, tooltips
    /// - Reusable for any numeric province data (development, income, manpower, etc.)
    /// - GPU compute shader for colorization (1ms vs 105ms CPU)
    ///
    /// Performance:
    /// - Single pass for stats analysis (CPU)
    /// - GPU compute shader for texture update (~1ms for 11.5M pixels)
    /// - O(N) where N = province count (data gathering only)
    ///
    /// Usage (Game Layer):
    /// public class DevelopmentMapMode : GradientMapMode
    /// {
    ///     protected override ColorGradient GetGradient() => ColorGradient.RedToYellow();
    ///     protected override float GetValueForProvince(ushort id) => hegemon.GetDevelopment(id);
    ///     // ... provide game-specific data access
    /// }
    /// </summary>
    public abstract class GradientMapMode : IMapModeHandler
    {
        // Special colors (can be overridden by subclasses)
        protected virtual Color32 OceanColor => new Color32(25, 25, 112, 255);    // Dark blue
        protected virtual Color32 UnknownColor => new Color32(64, 64, 64, 255);   // Dark gray

        // Dirty flag for optimization - skip updates when data hasn't changed
        private bool isDirty = true;

        // Pluggable colorizer from registry (replaces hardcoded GradientComputeDispatcher)
        private IMapModeColorizer colorizer;
        private bool colorizerInitialized = false;

        /// <summary>
        /// Mark this map mode as dirty (needs recalculation)
        /// Call this when underlying data changes (province ownership, development, etc.)
        /// </summary>
        public void MarkDirty()
        {
            isDirty = true;
        }

        /// <summary>
        /// Called when map mode is activated - always mark dirty to ensure first update
        /// Subclasses should call base.OnMapModeActivated() if they override OnActivate
        /// </summary>
        protected void OnMapModeActivated()
        {
            isDirty = true; // Always update when activated

            // Get colorizer from registry on first activation (Pattern 20: Pluggable Implementation)
            if (colorizer == null)
            {
                var registry = MapRendererRegistry.Instance;
                if (registry != null)
                {
                    // Get default colorizer (or custom if configured)
                    colorizer = registry.GetDefaultMapModeColorizer();
                }

                // Fallback: create default colorizer directly if registry not available
                if (colorizer == null)
                {
                    ArchonLogger.LogWarning("GradientMapMode: Registry not available, creating fallback GradientMapModeColorizer", "map_modes");
                    colorizer = new GradientMapModeColorizer();
                }
            }
        }

        /// <summary>
        /// Initialize the colorizer if not already done.
        /// Called lazily when first update is needed.
        /// </summary>
        private void EnsureColorizerInitialized(MapModeDataTextures dataTextures)
        {
            if (colorizerInitialized || colorizer == null) return;

            var context = new MapModeColorizerContext
            {
                TextureManager = null, // Not needed for basic colorization
                MapWidth = dataTextures.ProvinceDevelopmentTexture?.width ?? 0,
                MapHeight = dataTextures.ProvinceDevelopmentTexture?.height ?? 0,
                MaxProvinces = 65536
            };

            colorizer.Initialize(context);
            colorizerInitialized = true;
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
            // Optimization: Skip update if data hasn't changed
            if (!isDirty)
            {
                return;
            }

            if (dataTextures?.ProvinceDevelopmentTexture == null)
            {
                ArchonLogger.LogError($"{Name}: Development texture not available", "map_modes");
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

            // Phase 1: Analyze value distribution
            var stats = AnalyzeValueDistribution(allProvinces, provinceQueries, gameProvinceSystem);

            // Phase 2: Update texture with gradient colors
            // Note: Always update even with 0 valid provinces to clear texture properly
            UpdateGradientTexture(dataTextures, allProvinces, provinceQueries, provinceMapping,
                                 gameProvinceSystem, stats);

            if (stats.ValidProvinces == 0)
            {
                ArchonLogger.Log($"{Name}: No valid provinces with data - texture cleared", "map_modes");
            }

            // Clear dirty flag - data is now up to date
            isDirty = false;

            var elapsed = (Time.realtimeSinceStartup - startTime) * 1000f;

            ArchonLogger.Log($"{Name}: Updated {stats.ValidProvinces} provinces in {elapsed:F2}ms " +
                           $"[Range: {stats.MinValue:F1}-{stats.MaxValue:F1}, Avg: {stats.AvgValue:F1}]", "map_modes");
        }

        /// <summary>
        /// Analyze value distribution across all provinces
        /// </summary>
        private ValueStats AnalyzeValueDistribution(NativeArray<ushort> provinces, ProvinceQueries provinceQueries,
                                                    object gameProvinceSystem)
        {
            var stats = new ValueStats
            {
                MinValue = float.MaxValue,
                MaxValue = float.MinValue
            };

            float totalValue = 0f;
            int validCount = 0;

            for (int i = 0; i < provinces.Length; i++)
            {
                var provinceId = provinces[i];

                // Get value from concrete implementation
                float value = GetValueForProvince(provinceId, provinceQueries, gameProvinceSystem);

                // Skip invalid provinces (zero or negative values)
                if (value <= 0f)
                    continue;

                totalValue += value;
                validCount++;

                if (value < stats.MinValue) stats.MinValue = value;
                if (value > stats.MaxValue) stats.MaxValue = value;
            }

            stats.ValidProvinces = validCount;
            stats.AvgValue = validCount > 0 ? totalValue / validCount : 0f;

            return stats;
        }

        /// <summary>
        /// Update texture with gradient colors based on province values
        /// Uses pluggable colorizer from registry (Pattern 20) for GPU compute
        /// </summary>
        private void UpdateGradientTexture(MapModeDataTextures dataTextures, NativeArray<ushort> provinces,
                                          ProvinceQueries provinceQueries, ProvinceMapping provinceMapping,
                                          object gameProvinceSystem, ValueStats stats)
        {
            var texture = dataTextures.ProvinceDevelopmentTexture;
            if (texture == null)
            {
                ArchonLogger.LogError($"{Name}: ProvinceDevelopmentTexture is null", "map_modes");
                return;
            }

            // Check colorizer is available
            if (colorizer == null)
            {
                ArchonLogger.LogError($"{Name}: Colorizer not available!", "map_modes");
                return;
            }

            // Ensure colorizer is initialized (lazy init)
            EnsureColorizerInitialized(dataTextures);

            // Get gradient from concrete implementation
            var gradient = GetGradient();

            // Calculate value range for normalization
            float valueRange = stats.MaxValue - stats.MinValue;
            bool uniformValues = valueRange < 0.001f; // Handle edge case where all values are the same

            // Build province values array (normalized to 0-1)
            // Array is indexed by provinceID, size must be at least max provinceID + 1
            int maxProvinceId = 0;
            for (int i = 0; i < provinces.Length; i++)
            {
                if (provinces[i] > maxProvinceId)
                    maxProvinceId = provinces[i];
            }

            float[] provinceValues = new float[maxProvinceId + 1];

            // Populate normalized values
            for (int i = 0; i < provinces.Length; i++)
            {
                var provinceId = provinces[i];

                // Get value from concrete implementation
                float value = GetValueForProvince(provinceId, provinceQueries, gameProvinceSystem);

                // Skip invalid provinces (use negative value to indicate "skip" to GPU shader)
                if (value <= 0f)
                {
                    provinceValues[provinceId] = -1f; // Negative = skip (compute shader will use ocean color)
                    continue;
                }

                // Normalize value to 0-1 range
                // Note: Minimum value normalizes to 0.0, which is a VALID value (not skipped)
                float normalizedValue = uniformValues ? 0.5f : (value - stats.MinValue) / valueRange;
                provinceValues[provinceId] = normalizedValue;
            }

            // Get province ID texture from data textures
            var provinceIDTexture = dataTextures.ProvinceIDTexture as RenderTexture;
            if (provinceIDTexture == null)
            {
                ArchonLogger.LogError($"{Name}: ProvinceIDTexture is not a RenderTexture!", "map_modes");
                return;
            }

            var outputTexture = texture as RenderTexture;
            if (outputTexture == null)
            {
                ArchonLogger.LogError($"{Name}: ProvinceDevelopmentTexture is not a RenderTexture!", "map_modes");
                return;
            }

            // Build style params for colorizer
            var styleParams = new ColorizationStyleParams
            {
                Gradient = gradient,
                OceanColor = OceanColor,
                NoDataColor = UnknownColor,
                DiscreteBands = 0,
                ShowValueLabels = false,
                AnimationTime = Time.time
            };

            // Dispatch via pluggable colorizer
            colorizer.Colorize(provinceIDTexture, outputTexture, provinceValues, styleParams);
        }

        /// <summary>
        /// Helper method for subclasses to disable all map mode keywords
        /// </summary>
        protected void DisableAllMapModeKeywords(Material mapMaterial)
        {
            mapMaterial.DisableKeyword("MAP_MODE_POLITICAL");
            mapMaterial.DisableKeyword("MAP_MODE_TERRAIN");
            mapMaterial.DisableKeyword("MAP_MODE_DEVELOPMENT");
        }

        /// <summary>
        /// Helper method for subclasses to enable a specific keyword
        /// </summary>
        protected void EnableMapModeKeyword(Material mapMaterial, string keyword)
        {
            mapMaterial.EnableKeyword(keyword);
        }

        /// <summary>
        /// Helper method for subclasses to set shader mode
        /// </summary>
        protected void SetShaderMode(Material mapMaterial, int modeID)
        {
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
        /// Call when map mode is no longer needed (e.g., scene cleanup)
        /// Note: Colorizer is managed by MapRendererRegistry, only clear local reference
        /// </summary>
        public void Dispose()
        {
            // Don't dispose colorizer - it's owned by MapRendererRegistry
            // Just clear local reference
            colorizer = null;
            colorizerInitialized = false;
        }
    }
}
