using UnityEngine;
using Utils;

namespace Map.Rendering
{
    /// <summary>
    /// Manages runtime-generated dynamic textures
    /// Border, Highlight, and Fog of War RenderTextures for effects
    /// Extracted from MapTextureManager for single responsibility
    ///
    /// BORDER TEXTURE ARCHITECTURE (Clean Separation):
    /// - DistanceFieldBorderTexture: Used by ShaderDistanceField mode (smooth JFA borders)
    /// - PixelPerfectBorderTexture: Used by ShaderPixelPerfect mode (sharp 1px borders)
    /// Each mode has its own dedicated texture - no sharing/reusing between modes.
    /// </summary>
    public class DynamicTextureSet
    {
        private readonly int mapWidth;
        private readonly int mapHeight;
        private readonly bool logCreation;

        // Dynamic render textures - each border mode has dedicated texture
        private RenderTexture distanceFieldBorderTexture;   // Mode 1: JFA distance field (R=country, G=province)
        private RenderTexture pixelPerfectBorderTexture;    // Mode 2: Sharp 1px borders (R=country, G=province)
        private RenderTexture highlightTexture;
        private RenderTexture fogOfWarTexture;

        // Shader property IDs - dedicated names per mode (no sharing)
        private static readonly int DistanceFieldBorderTexID = Shader.PropertyToID("_DistanceFieldBorderTexture");
        private static readonly int PixelPerfectBorderTexID = Shader.PropertyToID("_PixelPerfectBorderTexture");
        private static readonly int HighlightTexID = Shader.PropertyToID("_HighlightTexture");
        private static readonly int FogOfWarTexID = Shader.PropertyToID("_FogOfWarTexture");

        // Public accessors
        public RenderTexture DistanceFieldBorderTexture => distanceFieldBorderTexture;
        public RenderTexture PixelPerfectBorderTexture => pixelPerfectBorderTexture;

        // Legacy accessor for backwards compatibility during transition
        [System.Obsolete("Use DistanceFieldBorderTexture or PixelPerfectBorderTexture instead")]
        public RenderTexture DistanceFieldTexture => distanceFieldBorderTexture;
        [System.Obsolete("Use PixelPerfectBorderTexture instead")]
        public RenderTexture DualBorderTexture => pixelPerfectBorderTexture;

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
            CreateDistanceFieldBorderTexture();
            CreatePixelPerfectBorderTexture();
            CreateHighlightTexture();
            CreateFogOfWarTexture();
        }

        /// <summary>
        /// Create distance field border texture for JFA-based smooth border rendering (Mode 1)
        /// R channel = country border distance, G channel = province border distance
        /// Uses R8G8B8A8_UNorm for maximum UAV compatibility across platforms
        /// </summary>
        private void CreateDistanceFieldBorderTexture()
        {
            var descriptor = new RenderTextureDescriptor(mapWidth, mapHeight,
                UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm, 0);
            descriptor.enableRandomWrite = true;  // UAV support for compute shader writes
            descriptor.useMipMap = false;
            descriptor.autoGenerateMips = false;

            distanceFieldBorderTexture = new RenderTexture(descriptor);
            distanceFieldBorderTexture.name = "DistanceFieldBorder_RenderTexture";
            distanceFieldBorderTexture.filterMode = FilterMode.Bilinear; // Bilinear + smoothstep gradient = crisp thin borders
            distanceFieldBorderTexture.wrapMode = TextureWrapMode.Clamp;
            distanceFieldBorderTexture.Create();

            // Clear to black (no borders)
            RenderTexture.active = distanceFieldBorderTexture;
            GL.Clear(true, true, Color.black);
            RenderTexture.active = null;

            if (logCreation)
            {
                ArchonLogger.Log($"DynamicTextureSet: Created DistanceFieldBorder {mapWidth}x{mapHeight} R8G8B8A8_UNorm (Bilinear)", "map_initialization");
            }
        }

        /// <summary>
        /// Create pixel-perfect border texture for sharp 1px border rendering (Mode 2)
        /// R channel = country borders (different owners)
        /// G channel = province borders (same owner, different provinces)
        /// Uses R8G8B8A8_UNorm for UAV compatibility, Point filtering for sharp edges
        /// </summary>
        private void CreatePixelPerfectBorderTexture()
        {
            var descriptor = new RenderTextureDescriptor(mapWidth, mapHeight,
                UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm, 0);
            descriptor.enableRandomWrite = true;  // UAV support for compute shader writes
            descriptor.useMipMap = false;
            descriptor.autoGenerateMips = false;

            pixelPerfectBorderTexture = new RenderTexture(descriptor);
            pixelPerfectBorderTexture.name = "PixelPerfectBorder_RenderTexture";
            pixelPerfectBorderTexture.wrapMode = TextureWrapMode.Clamp;
            pixelPerfectBorderTexture.Create();

            // CRITICAL: Set filterMode AFTER Create() to ensure it takes effect
            pixelPerfectBorderTexture.filterMode = FilterMode.Point; // Point filtering for pixel-perfect borders

            // Clear to black (no borders detected yet)
            RenderTexture.active = pixelPerfectBorderTexture;
            GL.Clear(true, true, Color.black);
            RenderTexture.active = null;

            if (logCreation)
            {
                ArchonLogger.Log($"DynamicTextureSet: Created PixelPerfectBorder {mapWidth}x{mapHeight} R8G8B8A8_UNorm (Point)", "map_initialization");
            }
        }

        /// <summary>
        /// Create highlight render texture for selection effects
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
                ArchonLogger.Log($"DynamicTextureSet: Created Highlight {mapWidth}x{mapHeight} R8G8B8A8_UNorm", "map_initialization");
            }
        }

        /// <summary>
        /// Create fog of war render texture
        /// Single channel: 0.0 = unexplored, 0.5 = explored, 1.0 = visible
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
                ArchonLogger.Log($"DynamicTextureSet: Created FogOfWar {mapWidth}x{mapHeight} R8_UNorm", "map_initialization");
            }
        }

        /// <summary>
        /// Bind all dynamic textures to material
        /// Both border textures are always bound - shader selects which to use based on _BorderRenderingMode
        /// </summary>
        public void BindToMaterial(Material material)
        {
            if (material == null) return;

            // Bind both border textures - shader picks which to use based on mode
            material.SetTexture(DistanceFieldBorderTexID, distanceFieldBorderTexture);
            material.SetTexture(PixelPerfectBorderTexID, pixelPerfectBorderTexture);

            // Bind other textures
            material.SetTexture(HighlightTexID, highlightTexture);
            material.SetTexture(FogOfWarTexID, fogOfWarTexture);

            if (logCreation)
            {
                ArchonLogger.Log("DynamicTextureSet: Bound all dynamic textures to material", "map_initialization");
            }
        }

        /// <summary>
        /// Bind distance field border texture to material (Mode 1: ShaderDistanceField)
        /// </summary>
        public void BindDistanceFieldTextures(Material material)
        {
            if (material == null) return;
            material.SetTexture(DistanceFieldBorderTexID, distanceFieldBorderTexture);

            if (logCreation)
            {
                ArchonLogger.Log("DynamicTextureSet: Bound DistanceFieldBorderTexture", "map_rendering");
            }
        }

        /// <summary>
        /// Bind pixel perfect border texture to material (Mode 2: ShaderPixelPerfect)
        /// </summary>
        public void BindPixelPerfectTextures(Material material)
        {
            if (material == null) return;
            material.SetTexture(PixelPerfectBorderTexID, pixelPerfectBorderTexture);

            if (logCreation)
            {
                ArchonLogger.Log("DynamicTextureSet: Bound PixelPerfectBorderTexture", "map_rendering");
            }
        }

        /// <summary>
        /// Set border visual style on material (colors and strengths)
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
        }

        /// <summary>
        /// Release all textures
        /// </summary>
        public void Release()
        {
            if (distanceFieldBorderTexture != null) distanceFieldBorderTexture.Release();
            if (pixelPerfectBorderTexture != null) pixelPerfectBorderTexture.Release();
            if (highlightTexture != null) highlightTexture.Release();
            if (fogOfWarTexture != null) fogOfWarTexture.Release();
        }
    }
}
