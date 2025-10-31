using UnityEngine;
using Utils;

namespace Map.Rendering
{
    /// <summary>
    /// Manages runtime-generated dynamic textures
    /// Border, Highlight, and Fog of War RenderTextures for effects
    /// Extracted from MapTextureManager for single responsibility
    /// </summary>
    public class DynamicTextureSet
    {
        private readonly int mapWidth;
        private readonly int mapHeight;
        private readonly bool logCreation;

        // Dynamic render textures
        private RenderTexture distanceFieldTexture;     // JFA distance field for smooth borders (ShaderDistanceField mode)
        private RenderTexture dualBorderTexture;        // Dual-channel pixel-perfect borders (ShaderPixelPerfect mode): R=country, G=province
        private RenderTexture highlightTexture;
        private RenderTexture fogOfWarTexture;

        // Shader property IDs (keep shader names unchanged for compatibility)
        private static readonly int BorderTexID = Shader.PropertyToID("_BorderTexture");
        private static readonly int BorderMaskTexID = Shader.PropertyToID("_BorderMaskTexture");
        private static readonly int HighlightTexID = Shader.PropertyToID("_HighlightTexture");
        private static readonly int FogOfWarTexID = Shader.PropertyToID("_FogOfWarTexture");

        // Public accessors
        public RenderTexture DistanceFieldTexture => distanceFieldTexture;
        public RenderTexture DualBorderTexture => dualBorderTexture;
        public RenderTexture HighlightTexture => highlightTexture;
        public RenderTexture FogOfWarTexture => fogOfWarTexture;

        public DynamicTextureSet(int width, int height, bool logCreation = true)
        {
            this.mapWidth = width;
            this.mapHeight = height;
            this.logCreation = logCreation;
        }

        /// <summary>
        /// Create all dynamic textures
        /// </summary>
        public void CreateTextures()
        {
            CreateDistanceFieldTexture();
            CreateDualBorderTexture();
            CreateHighlightTexture();
            CreateFogOfWarTexture();
        }

        /// <summary>
        /// Create distance field texture for JFA-based smooth border rendering
        /// R channel = country border distance, G channel = province border distance
        /// Uses R8G8B8A8_UNorm for maximum UAV compatibility across platforms
        /// (R16G16_UNorm causes TYPELESS format on some platforms - see explicit-graphics-format.md)
        /// </summary>
        private void CreateDistanceFieldTexture()
        {
            var descriptor = new RenderTextureDescriptor(mapWidth, mapHeight,
                UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm, 0);
            descriptor.enableRandomWrite = true;  // UAV support for compute shader writes
            descriptor.useMipMap = false;
            descriptor.autoGenerateMips = false;

            distanceFieldTexture = new RenderTexture(descriptor);
            distanceFieldTexture.name = "DistanceField_RenderTexture";
            distanceFieldTexture.filterMode = FilterMode.Bilinear; // Bilinear + smoothstep gradient = crisp thin borders
            distanceFieldTexture.wrapMode = TextureWrapMode.Clamp;
            distanceFieldTexture.Create();

            // Clear to black (no borders)
            RenderTexture.active = distanceFieldTexture;
            GL.Clear(true, true, Color.black);
            RenderTexture.active = null;

            if (logCreation)
            {
                ArchonLogger.Log($"DynamicTextureSet: Created DistanceField RenderTexture {mapWidth}x{mapHeight} R8G8B8A8_UNorm (UAV-compatible)", "map_initialization");
            }
        }

        /// <summary>
        /// Create dual-channel border texture for pixel-perfect border rendering
        /// R channel = country borders (different owners)
        /// G channel = province borders (same owner, different provinces)
        /// Memory: ~16MB for 8192x4096 map (R8G8B8A8 for UAV compatibility)
        ///
        /// IMPORTANT: Uses R8G8B8A8_UNorm instead of R8_UNorm to prevent TYPELESS format
        /// R8_UNorm with enableRandomWrite becomes TYPELESS on some platforms
        /// See: explicit-graphics-format.md decision doc
        /// </summary>
        private void CreateDualBorderTexture()
        {
            var descriptor = new RenderTextureDescriptor(mapWidth, mapHeight,
                UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm, 0);
            descriptor.enableRandomWrite = true;  // UAV support for compute shader writes
            descriptor.useMipMap = false;
            descriptor.autoGenerateMips = false;

            dualBorderTexture = new RenderTexture(descriptor);
            dualBorderTexture.name = "DualBorder_RenderTexture";
            dualBorderTexture.wrapMode = TextureWrapMode.Clamp;
            dualBorderTexture.Create();

            // CRITICAL: Set filterMode AFTER Create() to ensure it takes effect
            // RenderTexture filterMode must be set after creation for proper GPU state
            dualBorderTexture.filterMode = FilterMode.Point; // Point filtering for pixel-perfect borders (no interpolation)

            // Clear to black (no borders detected yet)
            RenderTexture.active = dualBorderTexture;
            GL.Clear(true, true, Color.black);
            RenderTexture.active = null;

            if (logCreation)
            {
                ArchonLogger.Log($"DynamicTextureSet: Created DualBorder RenderTexture {mapWidth}x{mapHeight} - Requested: R8G8B8A8_UNorm, Actual: {dualBorderTexture.graphicsFormat}", "map_initialization");
            }
        }

        /// <summary>
        /// Create highlight render texture for selection effects
        /// Uses explicit GraphicsFormat.R8G8B8A8_UNorm to prevent TYPELESS format
        /// </summary>
        private void CreateHighlightTexture()
        {
            var descriptor = new RenderTextureDescriptor(mapWidth, mapHeight,
                UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm, 0);
            descriptor.enableRandomWrite = true;
            descriptor.useMipMap = false;
            descriptor.autoGenerateMips = false;

            highlightTexture = new RenderTexture(descriptor);
            highlightTexture.name = "Highlight_RenderTexture";
            highlightTexture.filterMode = FilterMode.Point;
            highlightTexture.wrapMode = TextureWrapMode.Clamp;
            highlightTexture.Create();

            // Clear to transparent
            RenderTexture.active = highlightTexture;
            GL.Clear(true, true, Color.clear);
            RenderTexture.active = null;

            if (logCreation)
            {
                ArchonLogger.Log($"DynamicTextureSet: Created Highlight RenderTexture {mapWidth}x{mapHeight} R8G8B8A8_UNorm", "map_initialization");
            }
        }

        /// <summary>
        /// Create fog of war render texture in R8_UNorm format
        /// Single channel: 0.0 = unexplored, 0.5 = explored, 1.0 = visible
        /// Uses explicit GraphicsFormat.R8_UNorm to prevent TYPELESS format
        /// </summary>
        private void CreateFogOfWarTexture()
        {
            var descriptor = new RenderTextureDescriptor(mapWidth, mapHeight,
                UnityEngine.Experimental.Rendering.GraphicsFormat.R8_UNorm, 0);
            descriptor.enableRandomWrite = true;
            descriptor.useMipMap = false;
            descriptor.autoGenerateMips = false;

            fogOfWarTexture = new RenderTexture(descriptor);
            fogOfWarTexture.name = "FogOfWar_RenderTexture";
            fogOfWarTexture.filterMode = FilterMode.Point;
            fogOfWarTexture.wrapMode = TextureWrapMode.Clamp;
            fogOfWarTexture.Create();

            // Clear to black (unexplored)
            RenderTexture.active = fogOfWarTexture;
            GL.Clear(true, true, Color.black);
            RenderTexture.active = null;

            if (logCreation)
            {
                ArchonLogger.Log($"DynamicTextureSet: Created FogOfWar RenderTexture {mapWidth}x{mapHeight} R8_UNorm", "map_initialization");
            }
        }

        /// <summary>
        /// Bind dynamic textures to material
        /// </summary>
        public void BindToMaterial(Material material)
        {
            if (material == null) return;

            material.SetTexture(BorderTexID, distanceFieldTexture);
            material.SetTexture(BorderMaskTexID, dualBorderTexture);
            material.SetTexture(HighlightTexID, highlightTexture);
            material.SetTexture(FogOfWarTexID, fogOfWarTexture);

            if (logCreation)
            {
                ArchonLogger.Log("DynamicTextureSet: Bound dynamic textures to material (DistanceField and DualBorder)", "map_initialization");
            }
        }

        /// <summary>
        /// Set border visual style on material
        /// </summary>
        public void SetBorderStyle(Material material, Color countryBorderColor, float countryBorderStrength,
                                    Color provinceBorderColor, float provinceBorderStrength)
        {
            if (material == null) return;

            material.SetFloat("_CountryBorderStrength", Mathf.Clamp01(countryBorderStrength));
            material.SetColor("_CountryBorderColor", countryBorderColor);
            material.SetFloat("_ProvinceBorderStrength", Mathf.Clamp01(provinceBorderStrength));
            material.SetColor("_ProvinceBorderColor", provinceBorderColor);
        }

        /// <summary>
        /// Set AAA distance field border parameters on material
        /// Controls edge sharpness, gradient falloff, and color darkening
        /// </summary>
        public void SetDistanceFieldBorderParams(Material material,
            float edgeWidth, float gradientWidth, float edgeSmoothness,
            float edgeColorMul, float gradientColorMul,
            float edgeAlpha, float gradientAlphaInside, float gradientAlphaOutside)
        {
            if (material == null) return;

            material.SetFloat("_EdgeWidth", edgeWidth);
            material.SetFloat("_GradientWidth", gradientWidth);
            material.SetFloat("_EdgeSmoothness", edgeSmoothness);
            material.SetFloat("_EdgeColorMul", edgeColorMul);
            material.SetFloat("_GradientColorMul", gradientColorMul);
            material.SetFloat("_EdgeAlpha", edgeAlpha);
            material.SetFloat("_GradientAlphaInside", gradientAlphaInside);
            material.SetFloat("_GradientAlphaOutside", gradientAlphaOutside);

            if (logCreation)
            {
                ArchonLogger.Log($"DynamicTextureSet: Set distance field border params (edge={edgeWidth}, gradient={gradientWidth}, smoothness={edgeSmoothness})", "map_rendering");
            }
        }

        /// <summary>
        /// Release all textures
        /// </summary>
        public void Release()
        {
            if (distanceFieldTexture != null) distanceFieldTexture.Release();
            if (dualBorderTexture != null) dualBorderTexture.Release();
            if (highlightTexture != null) highlightTexture.Release();
            if (fogOfWarTexture != null) fogOfWarTexture.Release();
        }
    }
}
