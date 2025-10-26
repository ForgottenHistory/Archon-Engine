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
        private RenderTexture highlightTexture;
        private RenderTexture fogOfWarTexture;

        // Shader property IDs
        private static readonly int BorderTexID = Shader.PropertyToID("_BorderTexture");
        private static readonly int HighlightTexID = Shader.PropertyToID("_HighlightTexture");
        private static readonly int FogOfWarTexID = Shader.PropertyToID("_FogOfWarTexture");

        public RenderTexture BorderTexture => borderTexture;
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
            CreateHighlightTexture();
            CreateFogOfWarTexture();
        }

        /// <summary>
        /// Create border render texture in R16G16_UNorm format
        /// R channel = country borders, G channel = province borders
        /// Uses explicit GraphicsFormat to prevent TYPELESS format
        /// </summary>
        private void CreateBorderTexture()
        {
            var descriptor = new RenderTextureDescriptor(mapWidth, mapHeight,
                UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16_UNorm, 0);
            descriptor.enableRandomWrite = true;
            descriptor.useMipMap = false;
            descriptor.autoGenerateMips = false;

            borderTexture = new RenderTexture(descriptor);
            borderTexture.name = "Border_RenderTexture";
            borderTexture.filterMode = FilterMode.Point;
            borderTexture.wrapMode = TextureWrapMode.Clamp;
            borderTexture.Create();

            // Clear to black (no borders)
            RenderTexture.active = borderTexture;
            GL.Clear(true, true, Color.black);
            RenderTexture.active = null;

            if (logCreation)
            {
                ArchonLogger.Log($"DynamicTextureSet: Created Border RenderTexture {mapWidth}x{mapHeight} R16G16_UNorm", "map_initialization");
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
            material.SetTexture(HighlightTexID, highlightTexture);
            material.SetTexture(FogOfWarTexID, fogOfWarTexture);

            if (logCreation)
            {
                ArchonLogger.Log("DynamicTextureSet: Bound dynamic textures to material", "map_initialization");
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
        /// Release all textures
        /// </summary>
        public void Release()
        {
            if (borderTexture != null) borderTexture.Release();
            if (highlightTexture != null) highlightTexture.Release();
            if (fogOfWarTexture != null) fogOfWarTexture.Release();
        }
    }
}
