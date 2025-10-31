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
        private RenderTexture borderTexture;
        private RenderTexture borderMaskTexture;
        private RenderTexture borderDistanceTexture;  // 1/4 resolution distance field for AAA-quality borders
        private RenderTexture highlightTexture;
        private RenderTexture fogOfWarTexture;

        // Shader property IDs
        private static readonly int BorderTexID = Shader.PropertyToID("_BorderTexture");
        private static readonly int BorderMaskTexID = Shader.PropertyToID("_BorderMaskTexture");
        private static readonly int BorderDistanceTexID = Shader.PropertyToID("_BorderDistanceTexture");
        private static readonly int HighlightTexID = Shader.PropertyToID("_HighlightTexture");
        private static readonly int FogOfWarTexID = Shader.PropertyToID("_FogOfWarTexture");

        public RenderTexture BorderTexture => borderTexture;
        public RenderTexture BorderMaskTexture => borderMaskTexture;
        public RenderTexture BorderDistanceTexture => borderDistanceTexture;
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
            CreateBorderTexture();
            CreateBorderMaskTexture();
            CreateBorderDistanceTexture();
            CreateHighlightTexture();
            CreateFogOfWarTexture();
        }

        /// <summary>
        /// Create border render texture in R8G8B8A8_UNorm format
        /// R channel = country borders, G channel = province borders
        /// Uses R8G8B8A8_UNorm for maximum UAV compatibility across platforms
        /// (R16G16_UNorm causes TYPELESS format on some platforms - see explicit-graphics-format.md)
        /// </summary>
        private void CreateBorderTexture()
        {
            var descriptor = new RenderTextureDescriptor(mapWidth, mapHeight,
                UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm, 0);
            descriptor.enableRandomWrite = true;  // UAV support for compute shader writes
            descriptor.useMipMap = false;
            descriptor.autoGenerateMips = false;

            borderTexture = new RenderTexture(descriptor);
            borderTexture.name = "Border_RenderTexture";
            borderTexture.filterMode = FilterMode.Bilinear; // Bilinear + smoothstep gradient = crisp thin borders
            borderTexture.wrapMode = TextureWrapMode.Clamp;
            borderTexture.Create();

            // Clear to black (no borders)
            RenderTexture.active = borderTexture;
            GL.Clear(true, true, Color.black);
            RenderTexture.active = null;

            if (logCreation)
            {
                ArchonLogger.Log($"DynamicTextureSet: Created Border RenderTexture {mapWidth}x{mapHeight} R8G8B8A8_UNorm (UAV-compatible)", "map_initialization");
            }
        }

        /// <summary>
        /// Create border mask texture - 1 byte per pixel indicating if pixel is near a border
        /// 0 = interior pixel (far from borders), 255 = border pixel (within 2-3 pixels of border)
        /// This enables sparse shader-based border detection (only process border pixels)
        /// Memory: ~16MB for 8192x4096 map (R8G8B8A8 for UAV compatibility)
        ///
        /// IMPORTANT: Uses R8G8B8A8_UNorm instead of R8_UNorm to prevent TYPELESS format
        /// R8_UNorm with enableRandomWrite becomes TYPELESS on some platforms
        /// See: explicit-graphics-format.md decision doc
        /// </summary>
        private void CreateBorderMaskTexture()
        {
            var descriptor = new RenderTextureDescriptor(mapWidth, mapHeight,
                UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm, 0);
            descriptor.enableRandomWrite = true;  // UAV support for compute shader writes
            descriptor.useMipMap = false;
            descriptor.autoGenerateMips = false;

            borderMaskTexture = new RenderTexture(descriptor);
            borderMaskTexture.name = "BorderMask_RenderTexture";
            borderMaskTexture.wrapMode = TextureWrapMode.Clamp;
            borderMaskTexture.Create();

            // CRITICAL: Set filterMode AFTER Create() to ensure it takes effect
            // RenderTexture filterMode must be set after creation for proper GPU state
            borderMaskTexture.filterMode = FilterMode.Point; // Point filtering for pixel-perfect borders (no interpolation)

            // Clear to black (no borders detected yet)
            RenderTexture.active = borderMaskTexture;
            GL.Clear(true, true, Color.black);
            RenderTexture.active = null;

            if (logCreation)
            {
                ArchonLogger.Log($"DynamicTextureSet: Created BorderMask RenderTexture {mapWidth}x{mapHeight} - Requested: R8G8B8A8_UNorm, Actual: {borderMaskTexture.graphicsFormat}", "map_initialization");
            }
        }

        /// <summary>
        /// Create border distance field texture at 1/4 resolution for AAA-quality border rendering
        /// Distance field stores continuous distance values (0.0 = at border, 1.0 = far from border)
        /// 1/4 resolution (e.g., 1408x512 for 5632x2048 map) = 94% memory savings
        /// 9-tap multi-sampling + bilinear filtering compensates for reduced resolution
        /// Memory: ~0.7MB R8 or ~1.4MB R8G8 (vs 46MB full-resolution BorderMask)
        ///
        /// Format: R8G8_UNorm (dual channel)
        ///   R channel = country border distance (0.0 = at country border, 1.0 = far)
        ///   G channel = province border distance (0.0 = at province border, 1.0 = far)
        ///
        /// AAA Pattern: Distance field + multi-tap filtering + two-layer rendering
        /// See: border-rendering-approaches-analysis.md for full technical breakdown
        /// </summary>
        private void CreateBorderDistanceTexture()
        {
            // Calculate 1/4 resolution to match Imperator's downsampling ratio
            // Imperator: 8192×4096 → 2048×1024 (1/4 res, 4×4 blocks) = 2.1M pixels
            // Us: 5632×2048 → 1408×512 (1/4 res, 4×4 blocks) = 0.72M pixels
            // The 4×4 averaging creates the blur that makes borders smooth!
            int distanceWidth = (mapWidth + 3) / 4;   // Integer division with ceiling
            int distanceHeight = (mapHeight + 3) / 4;

            var descriptor = new RenderTextureDescriptor(distanceWidth, distanceHeight,
                UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8_UNorm, 0);
            descriptor.enableRandomWrite = true;  // UAV support for compute shader writes
            descriptor.useMipMap = false;
            descriptor.autoGenerateMips = false;

            borderDistanceTexture = new RenderTexture(descriptor);
            borderDistanceTexture.name = "BorderDistance_RenderTexture";
            borderDistanceTexture.filterMode = FilterMode.Bilinear; // Critical: bilinear for smooth distance gradients
            borderDistanceTexture.wrapMode = TextureWrapMode.Clamp;
            borderDistanceTexture.Create();

            // Clear to white (maximum distance = far from borders)
            RenderTexture.active = borderDistanceTexture;
            GL.Clear(true, true, Color.white);
            RenderTexture.active = null;

            float memorySizeMB = (distanceWidth * distanceHeight * 2) / (1024f * 1024f); // 2 bytes per pixel (R8G8)

            if (logCreation)
            {
                ArchonLogger.Log($"DynamicTextureSet: Created BorderDistance RenderTexture {distanceWidth}x{distanceHeight} (1/2 resolution) R8G8_UNorm - {memorySizeMB:F2}MB - Actual format: {borderDistanceTexture.graphicsFormat}", "map_initialization");
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

            material.SetTexture(BorderTexID, borderTexture);
            material.SetTexture(BorderMaskTexID, borderMaskTexture);
            material.SetTexture(BorderDistanceTexID, borderDistanceTexture);
            material.SetTexture(HighlightTexID, highlightTexture);
            material.SetTexture(FogOfWarTexID, fogOfWarTexture);

            if (logCreation)
            {
                ArchonLogger.Log("DynamicTextureSet: Bound dynamic textures to material (including BorderMask and BorderDistance)", "map_initialization");
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
            if (borderTexture != null) borderTexture.Release();
            if (borderMaskTexture != null) borderMaskTexture.Release();
            if (borderDistanceTexture != null) borderDistanceTexture.Release();
            if (highlightTexture != null) highlightTexture.Release();
            if (fogOfWarTexture != null) fogOfWarTexture.Release();
        }
    }
}
