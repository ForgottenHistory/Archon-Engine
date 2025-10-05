using UnityEngine;
using Utils;

namespace Map.Rendering
{
    /// <summary>
    /// Manages runtime-generated dynamic textures
    /// Border and Highlight RenderTextures for effects
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

        // Shader property IDs
        private static readonly int BorderTexID = Shader.PropertyToID("_BorderTexture");
        private static readonly int HighlightTexID = Shader.PropertyToID("_HighlightTexture");

        public RenderTexture BorderTexture => borderTexture;
        public RenderTexture HighlightTexture => highlightTexture;

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
        }

        /// <summary>
        /// Create border render texture in RG16 format
        /// R channel = country borders, G channel = province borders
        /// </summary>
        private void CreateBorderTexture()
        {
            borderTexture = new RenderTexture(mapWidth, mapHeight, 0, RenderTextureFormat.RG16);
            borderTexture.name = "Border_RenderTexture";
            borderTexture.filterMode = FilterMode.Point;
            borderTexture.wrapMode = TextureWrapMode.Clamp;
            borderTexture.useMipMap = false;
            borderTexture.autoGenerateMips = false;
            borderTexture.enableRandomWrite = true;
            borderTexture.Create();

            // Clear to black (no borders)
            RenderTexture.active = borderTexture;
            GL.Clear(true, true, Color.black);
            RenderTexture.active = null;

            if (logCreation)
            {
                ArchonLogger.LogMapInit($"DynamicTextureSet: Created Border RenderTexture {mapWidth}x{mapHeight} RG16");
            }
        }

        /// <summary>
        /// Create highlight render texture for selection effects
        /// </summary>
        private void CreateHighlightTexture()
        {
            highlightTexture = new RenderTexture(mapWidth, mapHeight, 0, RenderTextureFormat.ARGB32);
            highlightTexture.name = "Highlight_RenderTexture";
            highlightTexture.filterMode = FilterMode.Point;
            highlightTexture.wrapMode = TextureWrapMode.Clamp;
            highlightTexture.useMipMap = false;
            highlightTexture.autoGenerateMips = false;
            highlightTexture.enableRandomWrite = true;
            highlightTexture.Create();

            // Clear to transparent
            RenderTexture.active = highlightTexture;
            GL.Clear(true, true, Color.clear);
            RenderTexture.active = null;

            if (logCreation)
            {
                ArchonLogger.LogMapInit($"DynamicTextureSet: Created Highlight RenderTexture {mapWidth}x{mapHeight} ARGB32");
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

            if (logCreation)
            {
                ArchonLogger.LogMapInit("DynamicTextureSet: Bound dynamic textures to material");
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
        }
    }
}
