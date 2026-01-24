using UnityEngine;
using Map.Rendering;
using Map.Rendering.Highlight;
using Utils;
using Core.Modding;

namespace Map.Interaction
{
    /// <summary>
    /// GPU-based province highlighting system (ENGINE LAYER - mechanism)
    /// Provides capability to highlight provinces with configurable visual styles
    /// Game layer decides WHICH provinces to highlight and WHEN
    /// </summary>
    public class ProvinceHighlighter : MonoBehaviour
    {
        // Loaded via ModLoader
        private ComputeShader highlightCompute;

        [Header("Highlight Settings")]
        [SerializeField] private HighlightMode highlightMode = HighlightMode.Fill;
        [SerializeField] private float borderThickness = 2.0f;

        [Header("Debug")]
        [SerializeField] private bool logOperations = false;

        // Kernel indices
        private int clearHighlightKernel;
        private int highlightProvinceKernel;
        private int highlightProvinceBordersKernel;
        private int highlightCountryKernel;

        // Thread group sizes (must match compute shader)
        private const int THREAD_GROUP_SIZE = 8;

        // References
        private MapTextureManager textureManager;

        // State tracking
        private ushort currentHighlightedProvince = 0;
        private Color currentHighlightColor = Color.clear;

        // Pluggable renderer support
        private bool rendererRegistered = false;

        public enum HighlightMode
        {
            Fill,        // Fill entire province with color
            BorderOnly   // Only highlight province borders
        }

        void Awake()
        {
            InitializeKernels();
        }

        /// <summary>
        /// Initialize compute shader kernels
        /// </summary>
        private void InitializeKernels()
        {
            if (highlightCompute == null)
            {
                // Load compute shader - check mods first, then fall back to Resources
                highlightCompute = ModLoader.LoadAssetWithFallback<ComputeShader>(
                    "ProvinceHighlight",
                    "Shaders/ProvinceHighlight"
                );

                if (highlightCompute == null)
                {
                    ArchonLogger.LogWarning("ProvinceHighlighter: Compute shader not found!", "map_interaction");
                    return;
                }
            }

            // Get kernel indices
            clearHighlightKernel = highlightCompute.FindKernel("ClearHighlight");
            highlightProvinceKernel = highlightCompute.FindKernel("HighlightProvince");
            highlightProvinceBordersKernel = highlightCompute.FindKernel("HighlightProvinceBorders");
            highlightCountryKernel = highlightCompute.FindKernel("HighlightCountry");

            if (logOperations)
            {
                ArchonLogger.Log($"ProvinceHighlighter: Initialized with kernels - " +
                    $"Clear: {clearHighlightKernel}, Fill: {highlightProvinceKernel}, Borders: {highlightProvinceBordersKernel}, Country: {highlightCountryKernel}", "map_initialization");
            }
        }

        /// <summary>
        /// Set the texture manager reference
        /// </summary>
        public void Initialize(MapTextureManager manager)
        {
            textureManager = manager;

            // Register default highlight renderer with registry
            RegisterDefaultRenderer();

            if (logOperations)
            {
                ArchonLogger.Log("ProvinceHighlighter: Initialized with texture manager", "map_initialization");
            }
        }

        /// <summary>
        /// Register ENGINE's default highlight renderer with MapRendererRegistry.
        /// GAME layer can register additional custom renderers via MapRendererRegistry.Instance.RegisterHighlightRenderer().
        /// </summary>
        private void RegisterDefaultRenderer()
        {
            if (rendererRegistered) return;

            var registry = MapRendererRegistry.Instance;
            if (registry == null)
            {
                ArchonLogger.LogWarning("ProvinceHighlighter: MapRendererRegistry not found, cannot register renderer", "map_initialization");
                return;
            }

            // Build context for renderer initialization
            var context = new HighlightRendererContext
            {
                HighlightCompute = highlightCompute,
                ProvinceMapping = null // Can be set later if needed
            };

            // Register default renderer
            var defaultRenderer = new DefaultHighlightRenderer(highlightCompute);
            defaultRenderer.Initialize(textureManager, context);
            registry.RegisterHighlightRenderer(defaultRenderer);

            rendererRegistered = true;
            ArchonLogger.Log("ProvinceHighlighter: Registered default highlight renderer", "map_initialization");
        }

        /// <summary>
        /// Get the active highlight renderer from registry.
        /// </summary>
        public IHighlightRenderer GetActiveHighlightRenderer(string rendererId = null)
        {
            var registry = MapRendererRegistry.Instance;
            if (registry == null) return null;

            return registry.GetHighlightRenderer(rendererId);
        }

        /// <summary>
        /// Highlight a province with specified color and mode
        /// ENGINE LAYER API - Game layer calls this to highlight provinces
        /// </summary>
        /// <param name="provinceID">Province to highlight (0 = clear)</param>
        /// <param name="color">Highlight color (use alpha for transparency)</param>
        /// <param name="mode">Fill entire province or borders only</param>
        public void HighlightProvince(ushort provinceID, Color color, HighlightMode mode)
        {
            if (highlightCompute == null)
            {
                if (logOperations)
                {
                    ArchonLogger.LogWarning("ProvinceHighlighter: Compute shader not loaded. Cannot highlight.", "map_interaction");
                }
                return;
            }

            if (textureManager == null)
            {
                textureManager = FindFirstObjectByType<MapTextureManager>();
                if (textureManager == null)
                {
                    ArchonLogger.LogError("ProvinceHighlighter: MapTextureManager not found!", "map_interaction");
                    return;
                }
            }

            // If provinceID is 0 or color is transparent, clear highlight
            if (provinceID == 0 || color.a < 0.01f)
            {
                ClearHighlight();
                return;
            }

            // Update state
            currentHighlightedProvince = provinceID;
            currentHighlightColor = color;
            highlightMode = mode;

            // Select kernel based on mode
            int kernelToUse = (mode == HighlightMode.Fill) ? highlightProvinceKernel : highlightProvinceBordersKernel;

            // Set textures
            highlightCompute.SetTexture(kernelToUse, "ProvinceIDTexture", textureManager.ProvinceIDTexture);
            highlightCompute.SetTexture(kernelToUse, "HighlightTexture", textureManager.HighlightTexture);

            // Set dimensions
            highlightCompute.SetInt("MapWidth", textureManager.MapWidth);
            highlightCompute.SetInt("MapHeight", textureManager.MapHeight);

            // Set highlight parameters
            highlightCompute.SetInt("TargetProvinceID", provinceID);
            highlightCompute.SetVector("HighlightColor", color);
            highlightCompute.SetFloat("BorderThickness", borderThickness);

            // Calculate thread groups (round up division)
            int threadGroupsX = (textureManager.MapWidth + THREAD_GROUP_SIZE - 1) / THREAD_GROUP_SIZE;
            int threadGroupsY = (textureManager.MapHeight + THREAD_GROUP_SIZE - 1) / THREAD_GROUP_SIZE;

            // Dispatch compute shader
            highlightCompute.Dispatch(kernelToUse, threadGroupsX, threadGroupsY, 1);

            if (logOperations)
            {
                ArchonLogger.Log($"ProvinceHighlighter: Highlighted province {provinceID} in {mode} mode with color {color}", "map_interaction");
            }
        }

        /// <summary>
        /// Highlight a province using current mode (convenience overload)
        /// </summary>
        public void HighlightProvince(ushort provinceID, Color color)
        {
            HighlightProvince(provinceID, color, highlightMode);
        }

        /// <summary>
        /// Highlight all provinces owned by a specific country
        /// ENGINE LAYER API - Game layer calls this for country selection or diplomatic views
        /// </summary>
        /// <param name="countryID">Country ID to highlight (owner ID from ProvinceOwnerTexture)</param>
        /// <param name="color">Highlight color (use alpha for transparency)</param>
        public void HighlightCountry(ushort countryID, Color color)
        {
            if (highlightCompute == null)
            {
                if (logOperations)
                {
                    ArchonLogger.LogWarning("ProvinceHighlighter: Compute shader not loaded. Cannot highlight country.", "map_interaction");
                }
                return;
            }

            if (textureManager == null)
            {
                textureManager = FindFirstObjectByType<MapTextureManager>();
                if (textureManager == null)
                {
                    ArchonLogger.LogError("ProvinceHighlighter: MapTextureManager not found!", "map_interaction");
                    return;
                }
            }

            // If countryID is 0 or color is transparent, clear highlight
            if (countryID == 0 || color.a < 0.01f)
            {
                ClearHighlight();
                return;
            }

            // Set textures
            highlightCompute.SetTexture(highlightCountryKernel, "ProvinceOwnerTexture", textureManager.ProvinceOwnerTexture);
            highlightCompute.SetTexture(highlightCountryKernel, "HighlightTexture", textureManager.HighlightTexture);

            // Set dimensions
            highlightCompute.SetInt("MapWidth", textureManager.MapWidth);
            highlightCompute.SetInt("MapHeight", textureManager.MapHeight);

            // Set highlight parameters
            highlightCompute.SetInt("TargetCountryID", countryID);
            highlightCompute.SetVector("HighlightColor", color);

            // Calculate thread groups (round up division)
            int threadGroupsX = (textureManager.MapWidth + THREAD_GROUP_SIZE - 1) / THREAD_GROUP_SIZE;
            int threadGroupsY = (textureManager.MapHeight + THREAD_GROUP_SIZE - 1) / THREAD_GROUP_SIZE;

            // Dispatch compute shader
            highlightCompute.Dispatch(highlightCountryKernel, threadGroupsX, threadGroupsY, 1);

            if (logOperations)
            {
                ArchonLogger.Log($"ProvinceHighlighter: Highlighted country {countryID} with color {color}", "map_interaction");
            }
        }

        /// <summary>
        /// Clear all province highlights
        /// </summary>
        public void ClearHighlight()
        {
            if (highlightCompute == null || textureManager == null)
                return;

            // Set textures
            highlightCompute.SetTexture(clearHighlightKernel, "HighlightTexture", textureManager.HighlightTexture);

            // Set dimensions
            highlightCompute.SetInt("MapWidth", textureManager.MapWidth);
            highlightCompute.SetInt("MapHeight", textureManager.MapHeight);

            // Calculate thread groups
            int threadGroupsX = (textureManager.MapWidth + THREAD_GROUP_SIZE - 1) / THREAD_GROUP_SIZE;
            int threadGroupsY = (textureManager.MapHeight + THREAD_GROUP_SIZE - 1) / THREAD_GROUP_SIZE;

            // Dispatch clear kernel
            highlightCompute.Dispatch(clearHighlightKernel, threadGroupsX, threadGroupsY, 1);

            // Reset state
            currentHighlightedProvince = 0;
            currentHighlightColor = Color.clear;

            if (logOperations)
            {
                ArchonLogger.Log("ProvinceHighlighter: Cleared all highlights", "map_interaction");
            }
        }

        /// <summary>
        /// Set highlight mode (fill vs border only)
        /// </summary>
        public void SetHighlightMode(HighlightMode mode)
        {
            highlightMode = mode;

            // Re-apply current highlight with new mode if there is one
            if (currentHighlightedProvince != 0)
            {
                HighlightProvince(currentHighlightedProvince, currentHighlightColor, mode);
            }
        }

        /// <summary>
        /// Set border thickness for BorderOnly mode
        /// </summary>
        public void SetBorderThickness(float thickness)
        {
            borderThickness = Mathf.Clamp(thickness, 1f, 5f);

            // Re-apply current highlight with new thickness if in border mode
            if (currentHighlightedProvince != 0 && highlightMode == HighlightMode.BorderOnly)
            {
                HighlightProvince(currentHighlightedProvince, currentHighlightColor, highlightMode);
            }
        }

        /// <summary>
        /// Get the currently highlighted province ID
        /// </summary>
        public ushort GetHighlightedProvince() => currentHighlightedProvince;

        /// <summary>
        /// Check if a province is currently highlighted
        /// </summary>
        public bool IsProvinceHighlighted(ushort provinceID) => currentHighlightedProvince == provinceID;

        #if UNITY_EDITOR
        /// <summary>
        /// Debug: Test highlighting a random province
        /// </summary>
        [ContextMenu("Debug - Highlight Random Province")]
        private void DebugHighlightRandom()
        {
            ushort randomProvince = (ushort)UnityEngine.Random.Range(1, 3000);
            Color randomColor = new Color(
                UnityEngine.Random.value,
                UnityEngine.Random.value,
                UnityEngine.Random.value,
                0.5f // Semi-transparent
            );

            HighlightProvince(randomProvince, randomColor);
            ArchonLogger.Log($"ProvinceHighlighter: Debug highlighted province {randomProvince}", "map_interaction");
        }

        /// <summary>
        /// Debug: Clear highlights
        /// </summary>
        [ContextMenu("Debug - Clear Highlight")]
        private void DebugClearHighlight()
        {
            ClearHighlight();
        }

        /// <summary>
        /// Debug: Toggle between fill and border modes
        /// </summary>
        [ContextMenu("Debug - Toggle Highlight Mode")]
        private void DebugToggleMode()
        {
            highlightMode = (highlightMode == HighlightMode.Fill) ? HighlightMode.BorderOnly : HighlightMode.Fill;

            if (currentHighlightedProvince != 0)
            {
                HighlightProvince(currentHighlightedProvince, currentHighlightColor, highlightMode);
            }

            ArchonLogger.Log($"ProvinceHighlighter: Toggled to {highlightMode} mode", "map_interaction");
        }
        #endif
    }
}
