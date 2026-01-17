using UnityEngine;
using Unity.Collections;
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
    /// - Uses GPU compute shader for fast colorization (~1ms vs 125ms CPU)
    /// - Uses MapModeManager's texture array for instant mode switching
    ///
    /// Performance:
    /// - GPU compute shader processes 11.5M pixels in ~1ms
    /// - Texture updates only when dirty (data changed)
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

        // Texture array integration
        private MapModeManager mapModeManager;
        private int textureArrayIndex = -1;
        private int mapWidth;
        private int mapHeight;
        private bool isRegistered = false;

        // GPU compute shader resources
        private ComputeShader gradientCompute;
        private int colorizeKernel;
        private ComputeBuffer provinceValueBuffer;
        private ComputeBuffer gradientColorsBuffer;
        private RenderTexture outputTexture;
        private RenderTexture provinceIDTexture;

        // Province value cache (sent to GPU)
        private float[] provinceValues;

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
        /// Register with MapModeManager to get a texture array slot.
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

            // Register and get array index
            textureArrayIndex = mapModeManager.RegisterCustomMapMode(ShaderModeID);
            if (textureArrayIndex < 0)
            {
                ArchonLogger.LogError($"{Name}: Failed to register with MapModeManager", "map_modes");
                return;
            }

            // Get map dimensions
            (mapWidth, mapHeight) = mapModeManager.GetMapDimensions();

            // Initialize GPU resources
            InitializeGPUResources();

            isRegistered = true;
            isDirty = true; // Force initial update

            ArchonLogger.Log($"{Name}: Registered with MapModeManager at index {textureArrayIndex} ({mapWidth}x{mapHeight})", "map_modes");
        }

        /// <summary>
        /// Initialize GPU compute shader and buffers
        /// </summary>
        private void InitializeGPUResources()
        {
            // Load compute shader
            gradientCompute = Resources.Load<ComputeShader>("GradientMapMode");

            #if UNITY_EDITOR
            if (gradientCompute == null)
            {
                string[] guids = UnityEditor.AssetDatabase.FindAssets("GradientMapMode t:ComputeShader");
                if (guids.Length > 0)
                {
                    string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
                    gradientCompute = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>(path);
                }
            }
            #endif

            if (gradientCompute == null)
            {
                ArchonLogger.LogError($"{Name}: GradientMapMode compute shader not found!", "map_modes");
                return;
            }

            colorizeKernel = gradientCompute.FindKernel("ColorizeGradient");

            // Allocate province value buffer (max 65536 provinces)
            provinceValues = new float[65536];
            provinceValueBuffer = new ComputeBuffer(65536, sizeof(float));

            // Allocate gradient colors buffer (5 colors max for flexibility)
            gradientColorsBuffer = new ComputeBuffer(5, sizeof(float) * 4);

            // Create output render texture
            outputTexture = new RenderTexture(mapWidth, mapHeight, 0, RenderTextureFormat.ARGB32);
            outputTexture.enableRandomWrite = true;
            outputTexture.filterMode = FilterMode.Point;
            outputTexture.Create();

            // Get province ID texture reference
            var textureManager = Object.FindFirstObjectByType<MapTextureManager>();
            if (textureManager != null)
            {
                provinceIDTexture = textureManager.ProvinceIDTexture;
            }

            if (provinceIDTexture == null)
            {
                ArchonLogger.LogError($"{Name}: ProvinceIDTexture not found!", "map_modes");
            }
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

            // Ensure we're registered and have GPU resources
            if (!isRegistered || gradientCompute == null || provinceIDTexture == null)
            {
                ArchonLogger.LogWarning($"{Name}: Not properly initialized, skipping texture update", "map_modes");
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

            // Phase 1: Calculate province values and stats (CPU - fast, just province count iterations)
            var stats = CalculateProvinceValues(allProvinces, provinceQueries, gameProvinceSystem);

            // Phase 2: Run GPU compute shader to colorize (GPU - fast, ~1ms)
            RunGPUColorization(stats);

            // Phase 3: Copy to texture array (GPU-to-GPU, no CPU roundtrip)
            CopyToTextureArray();

            // Clear dirty flag
            isDirty = false;

            var elapsed = (Time.realtimeSinceStartup - startTime) * 1000f;
            ArchonLogger.Log($"{Name}: Updated {stats.ValidProvinces} provinces in {elapsed:F2}ms " +
                           $"[Range: {stats.MinValue:F1}-{stats.MaxValue:F1}]", "map_modes");
        }

        /// <summary>
        /// Calculate province values and normalize them for GPU
        /// </summary>
        private ValueStats CalculateProvinceValues(NativeArray<ushort> provinces, ProvinceQueries provinceQueries,
                                                   object gameProvinceSystem)
        {
            var stats = new ValueStats
            {
                MinValue = float.MaxValue,
                MaxValue = float.MinValue
            };

            // Initialize all values to -1 (skip/ocean)
            for (int i = 0; i < provinceValues.Length; i++)
            {
                provinceValues[i] = -1f;
            }

            float totalValue = 0f;
            int validCount = 0;

            // First pass: collect raw values and find min/max
            for (int i = 0; i < provinces.Length; i++)
            {
                var provinceId = provinces[i];
                if (provinceId == 0) continue;

                float value = GetValueForProvince(provinceId, provinceQueries, gameProvinceSystem);

                if (value < 0f)
                {
                    provinceValues[provinceId] = -1f; // Skip (ocean/invalid)
                    continue;
                }

                provinceValues[provinceId] = value; // Store raw value for now

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

            // Second pass: normalize values to [0,1] range for GPU
            float valueRange = stats.MaxValue - stats.MinValue;
            bool uniformValues = valueRange < 0.001f;

            for (int i = 0; i < provinces.Length; i++)
            {
                var provinceId = provinces[i];
                if (provinceId == 0) continue;

                float value = provinceValues[provinceId];
                if (value < 0f) continue; // Keep -1 for skipped provinces

                if (value == 0f)
                {
                    // Zero = show as minimum (0.0)
                    provinceValues[provinceId] = 0f;
                }
                else
                {
                    // Normalize to [0,1]
                    provinceValues[provinceId] = uniformValues ? 0f : Mathf.Clamp01((value - stats.MinValue) / valueRange);
                }
            }

            return stats;
        }

        /// <summary>
        /// Run GPU compute shader to colorize provinces
        /// </summary>
        private void RunGPUColorization(ValueStats stats)
        {
            // Upload province values to GPU
            provinceValueBuffer.SetData(provinceValues);

            // Upload gradient colors
            var gradient = GetGradient();
            var gradientColors = new Vector4[5];
            gradientColors[0] = ColorToVector4(gradient.Evaluate(0f));
            gradientColors[1] = ColorToVector4(gradient.Evaluate(0.25f));
            gradientColors[2] = ColorToVector4(gradient.Evaluate(0.5f));
            gradientColors[3] = ColorToVector4(gradient.Evaluate(0.75f));
            gradientColors[4] = ColorToVector4(gradient.Evaluate(1f));
            gradientColorsBuffer.SetData(gradientColors);

            // Set textures
            gradientCompute.SetTexture(colorizeKernel, "ProvinceIDTexture", provinceIDTexture);
            gradientCompute.SetTexture(colorizeKernel, "OutputTexture", outputTexture);

            // Set buffers
            gradientCompute.SetBuffer(colorizeKernel, "ProvinceValueBuffer", provinceValueBuffer);
            gradientCompute.SetBuffer(colorizeKernel, "GradientColors", gradientColorsBuffer);

            // Set parameters
            gradientCompute.SetInt("MapWidth", mapWidth);
            gradientCompute.SetInt("MapHeight", mapHeight);
            gradientCompute.SetVector("OceanColor", ColorToVector4(OceanColor));

            // Dispatch compute shader (8x8 thread groups)
            int groupsX = Mathf.CeilToInt(mapWidth / 8f);
            int groupsY = Mathf.CeilToInt(mapHeight / 8f);
            gradientCompute.Dispatch(colorizeKernel, groupsX, groupsY, 1);
        }

        /// <summary>
        /// Copy GPU output to texture array using GPU-to-GPU copy (no CPU roundtrip).
        /// Uses Graphics.CopyTexture which stays entirely on GPU.
        /// </summary>
        private void CopyToTextureArray()
        {
            // GPU-to-GPU copy - no ReadPixels, no CPU sync, stays on GPU
            mapModeManager.CopyRenderTextureToArray(textureArrayIndex, outputTexture);
        }

        private Vector4 ColorToVector4(Color32 color)
        {
            return new Vector4(color.r / 255f, color.g / 255f, color.b / 255f, color.a / 255f);
        }

        private Vector4 ColorToVector4(Color color)
        {
            return new Vector4(color.r, color.g, color.b, color.a);
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
            provinceValueBuffer?.Release();
            provinceValueBuffer = null;

            gradientColorsBuffer?.Release();
            gradientColorsBuffer = null;

            if (outputTexture != null)
            {
                outputTexture.Release();
                Object.Destroy(outputTexture);
                outputTexture = null;
            }

            mapModeManager = null;
            isRegistered = false;
        }
    }
}
