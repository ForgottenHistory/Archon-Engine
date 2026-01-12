using UnityEngine;

namespace Map.Rendering.Highlight
{
    /// <summary>
    /// ENGINE: Default GPU compute-based highlight renderer.
    /// Provides fill and border-only highlight modes using compute shaders.
    /// </summary>
    public class DefaultHighlightRenderer : HighlightRendererBase
    {
        public override string RendererId => "Default";
        public override string DisplayName => "Default (GPU Compute)";
        public override bool RequiresPerFrameUpdate => false;

        private ComputeShader highlightCompute;

        // Kernel indices
        private int clearHighlightKernel = -1;
        private int highlightProvinceKernel = -1;
        private int highlightProvinceBordersKernel = -1;
        private int highlightCountryKernel = -1;

        // Settings
        private float borderThickness = 2.0f;

        public DefaultHighlightRenderer(ComputeShader computeShader = null)
        {
            this.highlightCompute = computeShader;
        }

        protected override void OnInitialize()
        {
            // Get compute shader from context if not provided
            if (highlightCompute == null)
            {
                highlightCompute = context.HighlightCompute;
            }

            if (highlightCompute == null)
            {
                ArchonLogger.LogWarning("DefaultHighlightRenderer: No compute shader available", "map_rendering");
                return;
            }

            // Find kernels
            if (highlightCompute.HasKernel("ClearHighlight"))
                clearHighlightKernel = highlightCompute.FindKernel("ClearHighlight");

            if (highlightCompute.HasKernel("HighlightProvince"))
                highlightProvinceKernel = highlightCompute.FindKernel("HighlightProvince");

            if (highlightCompute.HasKernel("HighlightProvinceBorders"))
                highlightProvinceBordersKernel = highlightCompute.FindKernel("HighlightProvinceBorders");

            if (highlightCompute.HasKernel("HighlightCountry"))
                highlightCountryKernel = highlightCompute.FindKernel("HighlightCountry");

            ArchonLogger.Log($"DefaultHighlightRenderer: Initialized kernels - Clear:{clearHighlightKernel}, Fill:{highlightProvinceKernel}, Borders:{highlightProvinceBordersKernel}, Country:{highlightCountryKernel}", "map_initialization");
        }

        public override void HighlightProvince(ushort provinceID, Color color, HighlightMode mode)
        {
            if (!isInitialized || highlightCompute == null)
            {
                ArchonLogger.LogWarning("DefaultHighlightRenderer: Not initialized", "map_rendering");
                return;
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
            currentMode = mode;

            // Select kernel based on mode
            int kernelToUse = (mode == HighlightMode.Fill) ? highlightProvinceKernel : highlightProvinceBordersKernel;

            if (kernelToUse < 0)
            {
                ArchonLogger.LogWarning($"DefaultHighlightRenderer: Kernel not available for mode {mode}", "map_rendering");
                return;
            }

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

            // Dispatch
            var (threadGroupsX, threadGroupsY) = CalculateThreadGroups(textureManager.MapWidth, textureManager.MapHeight);
            highlightCompute.Dispatch(kernelToUse, threadGroupsX, threadGroupsY, 1);
        }

        public override void HighlightCountry(ushort countryID, Color color)
        {
            if (!isInitialized || highlightCompute == null || highlightCountryKernel < 0)
            {
                ArchonLogger.LogWarning("DefaultHighlightRenderer: Not initialized or country kernel not available", "map_rendering");
                return;
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

            // Dispatch
            var (threadGroupsX, threadGroupsY) = CalculateThreadGroups(textureManager.MapWidth, textureManager.MapHeight);
            highlightCompute.Dispatch(highlightCountryKernel, threadGroupsX, threadGroupsY, 1);
        }

        public override void ClearHighlight()
        {
            if (!isInitialized || highlightCompute == null || clearHighlightKernel < 0)
                return;

            // Set textures
            highlightCompute.SetTexture(clearHighlightKernel, "HighlightTexture", textureManager.HighlightTexture);

            // Set dimensions
            highlightCompute.SetInt("MapWidth", textureManager.MapWidth);
            highlightCompute.SetInt("MapHeight", textureManager.MapHeight);

            // Dispatch
            var (threadGroupsX, threadGroupsY) = CalculateThreadGroups(textureManager.MapWidth, textureManager.MapHeight);
            highlightCompute.Dispatch(clearHighlightKernel, threadGroupsX, threadGroupsY, 1);

            // Reset state
            currentHighlightedProvince = 0;
            currentHighlightColor = Color.clear;
        }

        public override void ApplyToMaterial(Material material, HighlightStyleParams styleParams)
        {
            if (material == null) return;

            // Apply common style parameters
            ApplyCommonStyleParams(material, styleParams);

            // Update internal settings
            borderThickness = Mathf.Clamp(styleParams.BorderThickness, 1f, 5f);

            // Bind highlight texture
            if (textureManager?.HighlightTexture != null)
            {
                material.SetTexture("_HighlightTexture", textureManager.HighlightTexture);
            }
        }

        /// <summary>
        /// Set border thickness for BorderOnly mode.
        /// </summary>
        public void SetBorderThickness(float thickness)
        {
            borderThickness = Mathf.Clamp(thickness, 1f, 5f);
        }

        public override void Dispose()
        {
            highlightCompute = null;
            clearHighlightKernel = -1;
            highlightProvinceKernel = -1;
            highlightProvinceBordersKernel = -1;
            highlightCountryKernel = -1;
        }
    }
}
