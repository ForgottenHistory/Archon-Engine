using UnityEngine;
using Unity.Collections;

namespace Map.Rendering
{
    /// <summary>
    /// Manages all textures required for texture-based map rendering
    /// Handles province IDs, colors, owners, borders, and selection highlights
    /// </summary>
    public class MapTextureManager : MonoBehaviour
    {
        [Header("Map Dimensions")]
        [SerializeField] private int mapWidth = 5632;
        [SerializeField] private int mapHeight = 2048;

        [Header("Debug")]
        [SerializeField] private bool logTextureCreation = true;

        // Core map textures
        private Texture2D provinceIDTexture;      // R16G16 format for province IDs
        private Texture2D provinceOwnerTexture;   // R16 format for province owners
        private Texture2D provinceColorTexture;   // RGBA32 format for province colors

        // Dynamic render textures
        private RenderTexture borderTexture;      // R8 format for borders
        private RenderTexture highlightTexture;   // RGBA32 for selection highlights

        // Texture property IDs for shader efficiency
        private static readonly int ProvinceIDTexID = Shader.PropertyToID("_ProvinceIDTex");
        private static readonly int ProvinceOwnerTexID = Shader.PropertyToID("_ProvinceOwnerTex");
        private static readonly int ProvinceColorTexID = Shader.PropertyToID("_ProvinceColorTex");
        private static readonly int BorderTexID = Shader.PropertyToID("_BorderTex");
        private static readonly int HighlightTexID = Shader.PropertyToID("_HighlightTex");

        public int MapWidth => mapWidth;
        public int MapHeight => mapHeight;

        public Texture2D ProvinceIDTexture => provinceIDTexture;
        public Texture2D ProvinceOwnerTexture => provinceOwnerTexture;
        public Texture2D ProvinceColorTexture => provinceColorTexture;
        public RenderTexture BorderTexture => borderTexture;
        public RenderTexture HighlightTexture => highlightTexture;

        void Awake()
        {
            InitializeTextures();
        }

        /// <summary>
        /// Initialize all map textures with proper formats and settings
        /// </summary>
        private void InitializeTextures()
        {
            CreateProvinceIDTexture();
            CreateProvinceOwnerTexture();
            CreateProvinceColorTexture();
            CreateBorderTexture();
            CreateHighlightTexture();

            if (logTextureCreation)
            {
                Debug.Log($"MapTextureManager initialized with {mapWidth}x{mapHeight} textures");
            }
        }

        /// <summary>
        /// Create province ID texture in R16G16 format for 65k province support
        /// </summary>
        private void CreateProvinceIDTexture()
        {
            provinceIDTexture = new Texture2D(mapWidth, mapHeight, TextureFormat.RG16, false);
            provinceIDTexture.name = "ProvinceID_Texture";

            ConfigureMapTexture(provinceIDTexture);

            // Initialize with zero (no province)
            var pixels = new Color32[mapWidth * mapHeight];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = new Color32(0, 0, 0, 255); // ID 0 = no province
            }

            provinceIDTexture.SetPixels32(pixels);
            provinceIDTexture.Apply(false);

            if (logTextureCreation)
            {
                Debug.Log($"Created Province ID texture: {mapWidth}x{mapHeight} RG16 format");
            }
        }

        /// <summary>
        /// Create province owner texture in R16 format for country IDs
        /// </summary>
        private void CreateProvinceOwnerTexture()
        {
            provinceOwnerTexture = new Texture2D(mapWidth, mapHeight, TextureFormat.R16, false);
            provinceOwnerTexture.name = "ProvinceOwner_Texture";

            ConfigureMapTexture(provinceOwnerTexture);

            // Initialize with zero (no owner)
            var pixels = new Color32[mapWidth * mapHeight];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = new Color32(0, 0, 0, 255); // Owner ID 0 = unowned
            }

            provinceOwnerTexture.SetPixels32(pixels);
            provinceOwnerTexture.Apply(false);

            if (logTextureCreation)
            {
                Debug.Log($"Created Province Owner texture: {mapWidth}x{mapHeight} R16 format");
            }
        }

        /// <summary>
        /// Create province color texture in RGBA32 format for display colors
        /// </summary>
        private void CreateProvinceColorTexture()
        {
            provinceColorTexture = new Texture2D(mapWidth, mapHeight, TextureFormat.RGBA32, false);
            provinceColorTexture.name = "ProvinceColor_Texture";

            ConfigureMapTexture(provinceColorTexture);

            // Initialize with default color (black)
            var pixels = new Color32[mapWidth * mapHeight];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = new Color32(0, 0, 0, 255); // Black default
            }

            provinceColorTexture.SetPixels32(pixels);
            provinceColorTexture.Apply(false);

            if (logTextureCreation)
            {
                Debug.Log($"Created Province Color texture: {mapWidth}x{mapHeight} RGBA32 format");
            }
        }

        /// <summary>
        /// Create border render texture in R8 format
        /// </summary>
        private void CreateBorderTexture()
        {
            borderTexture = new RenderTexture(mapWidth, mapHeight, 0, RenderTextureFormat.R8);
            borderTexture.name = "Border_RenderTexture";
            borderTexture.filterMode = FilterMode.Point;
            borderTexture.wrapMode = TextureWrapMode.Clamp;
            borderTexture.useMipMap = false;
            borderTexture.autoGenerateMips = false;
            borderTexture.Create();

            // Clear to black (no borders)
            RenderTexture.active = borderTexture;
            GL.Clear(true, true, Color.black);
            RenderTexture.active = null;

            if (logTextureCreation)
            {
                Debug.Log($"Created Border RenderTexture: {mapWidth}x{mapHeight} R8 format");
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
            highlightTexture.Create();

            // Clear to transparent
            RenderTexture.active = highlightTexture;
            GL.Clear(true, true, Color.clear);
            RenderTexture.active = null;

            if (logTextureCreation)
            {
                Debug.Log($"Created Highlight RenderTexture: {mapWidth}x{mapHeight} ARGB32 format");
            }
        }

        /// <summary>
        /// Configure common settings for map textures
        /// </summary>
        private void ConfigureMapTexture(Texture2D texture)
        {
            texture.filterMode = FilterMode.Point;     // No filtering for pixel-perfect
            texture.wrapMode = TextureWrapMode.Clamp;  // No wrapping
            texture.anisoLevel = 0;                    // No anisotropic filtering
        }

        /// <summary>
        /// Update province ID at specific pixel coordinates using proper R16G16 packing
        /// </summary>
        /// <param name="x">X coordinate</param>
        /// <param name="y">Y coordinate</param>
        /// <param name="provinceID">Province ID (0-65535)</param>
        public void SetProvinceID(int x, int y, ushort provinceID)
        {
            if (x < 0 || x >= mapWidth || y < 0 || y >= mapHeight) return;

            // Use ProvinceIDEncoder for consistent packing
            Color32 packedColor = Province.ProvinceIDEncoder.PackProvinceID(provinceID);
            provinceIDTexture.SetPixel(x, y, packedColor);
        }

        /// <summary>
        /// Get province ID at specific coordinates
        /// </summary>
        /// <param name="x">X coordinate</param>
        /// <param name="y">Y coordinate</param>
        /// <returns>Province ID (0 if invalid coordinates)</returns>
        public ushort GetProvinceID(int x, int y)
        {
            if (x < 0 || x >= mapWidth || y < 0 || y >= mapHeight) return 0;

            Color32 packedColor = provinceIDTexture.GetPixel(x, y);
            return Province.ProvinceIDEncoder.UnpackProvinceID(packedColor);
        }

        /// <summary>
        /// Update province owner at specific coordinates
        /// </summary>
        public void SetProvinceOwner(int x, int y, ushort ownerID)
        {
            if (x < 0 || x >= mapWidth || y < 0 || y >= mapHeight) return;

            byte r = (byte)(ownerID & 0xFF);
            provinceOwnerTexture.SetPixel(x, y, new Color32(r, 0, 0, 255));
        }

        /// <summary>
        /// Update province display color at specific coordinates
        /// </summary>
        public void SetProvinceColor(int x, int y, Color32 color)
        {
            if (x < 0 || x >= mapWidth || y < 0 || y >= mapHeight) return;

            provinceColorTexture.SetPixel(x, y, color);
        }

        /// <summary>
        /// Apply all texture changes (call after batch updates)
        /// </summary>
        public void ApplyTextureChanges()
        {
            provinceIDTexture.Apply(false);
            provinceOwnerTexture.Apply(false);
            provinceColorTexture.Apply(false);
        }

        /// <summary>
        /// Bind all textures to material for rendering
        /// </summary>
        /// <param name="material">Material to bind textures to</param>
        public void BindTexturesToMaterial(Material material)
        {
            if (material == null) return;

            material.SetTexture(ProvinceIDTexID, provinceIDTexture);
            material.SetTexture(ProvinceOwnerTexID, provinceOwnerTexture);
            material.SetTexture(ProvinceColorTexID, provinceColorTexture);
            material.SetTexture(BorderTexID, borderTexture);
            material.SetTexture(HighlightTexID, highlightTexture);
        }

        /// <summary>
        /// Update map dimensions and recreate textures
        /// </summary>
        public void ResizeTextures(int newWidth, int newHeight)
        {
            mapWidth = newWidth;
            mapHeight = newHeight;

            ReleaseTextures();
            InitializeTextures();
        }

        /// <summary>
        /// Release all texture memory
        /// </summary>
        private void ReleaseTextures()
        {
            if (provinceIDTexture != null) DestroyImmediate(provinceIDTexture);
            if (provinceOwnerTexture != null) DestroyImmediate(provinceOwnerTexture);
            if (provinceColorTexture != null) DestroyImmediate(provinceColorTexture);
            if (borderTexture != null) borderTexture.Release();
            if (highlightTexture != null) highlightTexture.Release();
        }

        void OnDestroy()
        {
            ReleaseTextures();
        }

#if UNITY_EDITOR
        [ContextMenu("Log Texture Memory Usage")]
        private void LogMemoryUsage()
        {
            long totalMemory = 0;
            int pixelCount = mapWidth * mapHeight;

            totalMemory += pixelCount * 2; // Province ID (RG16)
            totalMemory += pixelCount * 2; // Province Owner (R16)
            totalMemory += pixelCount * 4; // Province Color (RGBA32)
            totalMemory += pixelCount * 1; // Border (R8)
            totalMemory += pixelCount * 4; // Highlight (ARGB32)

            Debug.Log($"Map texture memory usage: {totalMemory / 1024f / 1024f:F2} MB");
        }
#endif
    }
}