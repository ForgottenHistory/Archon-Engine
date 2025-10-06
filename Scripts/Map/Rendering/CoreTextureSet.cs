using UnityEngine;
using Utils;

namespace Map.Rendering
{
    /// <summary>
    /// Manages core gameplay-critical textures
    /// Province ID, Owner, Color, and Development textures
    /// Extracted from MapTextureManager for single responsibility
    /// </summary>
    public class CoreTextureSet
    {
        private readonly int mapWidth;
        private readonly int mapHeight;
        private readonly bool logCreation;

        // Core gameplay textures
        private RenderTexture provinceIDTexture;
        private RenderTexture provinceOwnerTexture;
        private Texture2D provinceColorTexture;
        private Texture2D provinceDevelopmentTexture;

        // Shader property IDs
        private static readonly int ProvinceIDTexID = Shader.PropertyToID("_ProvinceIDTexture");
        private static readonly int ProvinceOwnerTexID = Shader.PropertyToID("_ProvinceOwnerTexture");
        private static readonly int ProvinceColorTexID = Shader.PropertyToID("_ProvinceColorTexture");
        private static readonly int ProvinceDevelopmentTexID = Shader.PropertyToID("_ProvinceDevelopmentTexture");

        public RenderTexture ProvinceIDTexture => provinceIDTexture;
        public RenderTexture ProvinceOwnerTexture => provinceOwnerTexture;
        public Texture2D ProvinceColorTexture => provinceColorTexture;
        public Texture2D ProvinceDevelopmentTexture => provinceDevelopmentTexture;

        public CoreTextureSet(int width, int height, bool logCreation = true)
        {
            this.mapWidth = width;
            this.mapHeight = height;
            this.logCreation = logCreation;
        }

        /// <summary>
        /// Create all core textures
        /// </summary>
        public void CreateTextures()
        {
            CreateProvinceIDTexture();
            CreateProvinceOwnerTexture();
            CreateProvinceColorTexture();
            CreateProvinceDevelopmentTexture();
        }

        /// <summary>
        /// Create province ID texture as RenderTexture for GPU accessibility
        /// Uses explicit GraphicsFormat.R8G8B8A8_UNorm to prevent TYPELESS format
        /// </summary>
        private void CreateProvinceIDTexture()
        {
            var descriptor = new RenderTextureDescriptor(mapWidth, mapHeight,
                UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm, 0);
            descriptor.enableRandomWrite = true;
            descriptor.useMipMap = false;
            descriptor.autoGenerateMips = false;

            provinceIDTexture = new RenderTexture(descriptor);
            provinceIDTexture.name = "ProvinceID_RenderTexture";
            provinceIDTexture.filterMode = FilterMode.Point;
            provinceIDTexture.wrapMode = TextureWrapMode.Clamp;
            provinceIDTexture.Create();

            // Clear to black
            RenderTexture.active = provinceIDTexture;
            GL.Clear(true, true, Color.black);
            RenderTexture.active = null;

            if (logCreation)
            {
                ArchonLogger.LogMapInit($"CoreTextureSet: Created Province ID texture {mapWidth}x{mapHeight} ARGB32 RenderTexture");
            }
        }

        /// <summary>
        /// Create province owner render texture for 16-bit country IDs
        /// Uses explicit GraphicsFormat.R32_SFloat to prevent TYPELESS format
        /// </summary>
        private void CreateProvinceOwnerTexture()
        {
            var descriptor = new RenderTextureDescriptor(mapWidth, mapHeight,
                UnityEngine.Experimental.Rendering.GraphicsFormat.R32_SFloat, 0);
            descriptor.enableRandomWrite = true;
            descriptor.useMipMap = false;
            descriptor.autoGenerateMips = false;

            provinceOwnerTexture = new RenderTexture(descriptor);
            provinceOwnerTexture.name = "ProvinceOwner_RenderTexture";
            provinceOwnerTexture.filterMode = FilterMode.Point;
            provinceOwnerTexture.wrapMode = TextureWrapMode.Clamp;
            provinceOwnerTexture.Create();

            // Clear to black
            RenderTexture.active = provinceOwnerTexture;
            GL.Clear(true, true, Color.black);
            RenderTexture.active = null;

            if (logCreation)
            {
                ArchonLogger.LogMapInit($"CoreTextureSet: Created Province Owner RenderTexture {mapWidth}x{mapHeight} R32_SFloat");
            }
        }

        /// <summary>
        /// Create province color texture in RGBA32 format
        /// </summary>
        private void CreateProvinceColorTexture()
        {
            provinceColorTexture = new Texture2D(mapWidth, mapHeight, TextureFormat.RGBA32, false);
            provinceColorTexture.name = "ProvinceColor_Texture";
            provinceColorTexture.filterMode = FilterMode.Point;
            provinceColorTexture.wrapMode = TextureWrapMode.Clamp;
            provinceColorTexture.anisoLevel = 0;

            // Initialize with black
            var pixels = new Color32[mapWidth * mapHeight];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = new Color32(0, 0, 0, 255);
            }

            provinceColorTexture.SetPixels32(pixels);
            provinceColorTexture.Apply(false);

            if (logCreation)
            {
                ArchonLogger.LogMapInit($"CoreTextureSet: Created Province Color texture {mapWidth}x{mapHeight} RGBA32");
            }
        }

        /// <summary>
        /// Create province development texture in RGBA32 format
        /// </summary>
        private void CreateProvinceDevelopmentTexture()
        {
            provinceDevelopmentTexture = new Texture2D(mapWidth, mapHeight, TextureFormat.RGBA32, false);
            provinceDevelopmentTexture.name = "ProvinceDevelopment_Texture";
            provinceDevelopmentTexture.filterMode = FilterMode.Point;
            provinceDevelopmentTexture.wrapMode = TextureWrapMode.Clamp;
            provinceDevelopmentTexture.anisoLevel = 0;

            // Initialize with ocean color
            var pixels = new Color32[mapWidth * mapHeight];
            Color32 oceanColor = new Color32(25, 25, 112, 255);
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = oceanColor;
            }

            provinceDevelopmentTexture.SetPixels32(pixels);
            provinceDevelopmentTexture.Apply(false);

            if (logCreation)
            {
                ArchonLogger.LogMapInit($"CoreTextureSet: Created Province Development texture {mapWidth}x{mapHeight} RGBA32");
            }
        }

        /// <summary>
        /// Bind core textures to material
        /// </summary>
        public void BindToMaterial(Material material)
        {
            if (material == null) return;

            material.SetTexture(ProvinceIDTexID, provinceIDTexture);
            material.SetTexture(ProvinceOwnerTexID, provinceOwnerTexture);
            material.SetTexture(ProvinceColorTexID, provinceColorTexture);
            material.SetTexture(ProvinceDevelopmentTexID, provinceDevelopmentTexture);

            if (logCreation)
            {
                ArchonLogger.LogMapInit("CoreTextureSet: Bound core textures to material");
            }
        }

        /// <summary>
        /// Update province color at coordinates
        /// </summary>
        public void SetProvinceColor(int x, int y, Color32 color)
        {
            if (x < 0 || x >= mapWidth || y < 0 || y >= mapHeight) return;
            provinceColorTexture.SetPixel(x, y, color);
        }

        /// <summary>
        /// Update province development at coordinates
        /// </summary>
        public void SetProvinceDevelopment(int x, int y, Color32 color)
        {
            if (x < 0 || x >= mapWidth || y < 0 || y >= mapHeight) return;
            provinceDevelopmentTexture.SetPixel(x, y, color);
        }

        /// <summary>
        /// Apply texture changes (call after batch updates)
        /// </summary>
        public void ApplyChanges()
        {
            provinceColorTexture.Apply(false);
            provinceDevelopmentTexture.Apply(false);
        }

        /// <summary>
        /// Release all textures
        /// </summary>
        public void Release()
        {
            if (provinceIDTexture != null) provinceIDTexture.Release();
            if (provinceOwnerTexture != null) provinceOwnerTexture.Release();
            if (provinceColorTexture != null) Object.DestroyImmediate(provinceColorTexture);
            if (provinceDevelopmentTexture != null) Object.DestroyImmediate(provinceDevelopmentTexture);
        }
    }
}
