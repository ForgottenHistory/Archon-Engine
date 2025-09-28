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
        private Texture2D provinceColorTexture;   // RGBA32 format for province colors (legacy)
        private Texture2D provinceColorPalette;   // 256×1 RGBA32 palette for efficient color lookup

        // Dynamic render textures
        private RenderTexture borderTexture;      // R8 format for borders
        private RenderTexture highlightTexture;   // RGBA32 for selection highlights

        // Texture property IDs for shader efficiency - MUST match shader property names exactly!
        private static readonly int ProvinceIDTexID = Shader.PropertyToID("_ProvinceIDTexture");
        private static readonly int ProvinceOwnerTexID = Shader.PropertyToID("_ProvinceOwnerTexture");
        private static readonly int ProvinceColorTexID = Shader.PropertyToID("_ProvinceColorTexture");
        private static readonly int ProvinceColorPaletteID = Shader.PropertyToID("_ProvinceColorPalette");
        private static readonly int BorderTexID = Shader.PropertyToID("_BorderTexture");
        private static readonly int HighlightTexID = Shader.PropertyToID("_HighlightTexture");

        public int MapWidth => mapWidth;
        public int MapHeight => mapHeight;

        public Texture2D ProvinceIDTexture => provinceIDTexture;
        public Texture2D ProvinceOwnerTexture => provinceOwnerTexture;
        public Texture2D ProvinceColorTexture => provinceColorTexture;
        public Texture2D ProvinceColorPalette => provinceColorPalette;
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
            CreateProvinceColorPalette();
            CreateBorderTexture();
            CreateHighlightTexture();

            if (logTextureCreation)
            {
                DominionLogger.Log($"MapTextureManager initialized with {mapWidth}x{mapHeight} textures");
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
                DominionLogger.Log($"Created Province ID texture: {mapWidth}x{mapHeight} RG16 format");
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
                DominionLogger.Log($"Created Province Owner texture: {mapWidth}x{mapHeight} R16 format");
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
                DominionLogger.Log($"Created Province Color texture: {mapWidth}x{mapHeight} RGBA32 format");
            }
        }

        /// <summary>
        /// Create province color palette texture (256×1 RGBA32) for efficient color lookup
        /// Task 1.3: Create province color palette texture (256×1 RGBA32)
        /// </summary>
        private void CreateProvinceColorPalette()
        {
            provinceColorPalette = new Texture2D(256, 1, TextureFormat.RGBA32, false);
            provinceColorPalette.name = "ProvinceColorPalette";

            // Configure for palette lookup (critical settings)
            provinceColorPalette.filterMode = FilterMode.Point;     // No interpolation for exact color lookup
            provinceColorPalette.wrapMode = TextureWrapMode.Clamp;  // Clamp to prevent wraparound
            provinceColorPalette.anisoLevel = 0;                    // No anisotropic filtering

            // Initialize with default colors
            var colors = new Color32[256];

            // Initialize with reasonable default colors for different country/province types
            for (int i = 0; i < 256; i++)
            {
                colors[i] = GenerateDefaultPaletteColor(i);
            }

            provinceColorPalette.SetPixels32(colors);
            provinceColorPalette.Apply(false);

            if (logTextureCreation)
            {
                DominionLogger.Log($"Created Province Color Palette: 256×1 RGBA32 format");
            }
        }

        /// <summary>
        /// Generate a default color for palette index
        /// Creates visually distinct colors for different country/owner IDs
        /// </summary>
        private Color32 GenerateDefaultPaletteColor(int index)
        {
            if (index == 0) return new Color32(64, 64, 64, 255); // Dark gray for unowned

            // Generate colors using HSV to ensure good visual separation
            float hue = (index * 137.508f) % 360f; // Golden angle for good distribution
            float saturation = 0.7f + (index % 3) * 0.1f; // Vary saturation slightly
            float value = 0.8f + (index % 2) * 0.2f; // Vary brightness slightly

            Color color = Color.HSVToRGB(hue / 360f, saturation, value);
            return new Color32(
                (byte)(color.r * 255),
                (byte)(color.g * 255),
                (byte)(color.b * 255),
                255
            );
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
            borderTexture.enableRandomWrite = true;  // CRITICAL: Enable UAV for compute shader write access
            borderTexture.Create();

            // Clear to black (no borders)
            RenderTexture.active = borderTexture;
            GL.Clear(true, true, Color.black);
            RenderTexture.active = null;

            if (logTextureCreation)
            {
                DominionLogger.Log($"Created Border RenderTexture: {mapWidth}x{mapHeight} R8 format");
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
            highlightTexture.enableRandomWrite = true;  // Enable UAV for potential compute shader use
            highlightTexture.Create();

            // Clear to transparent
            RenderTexture.active = highlightTexture;
            GL.Clear(true, true, Color.clear);
            RenderTexture.active = null;

            if (logTextureCreation)
            {
                DominionLogger.Log($"Created Highlight RenderTexture: {mapWidth}x{mapHeight} ARGB32 format");
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
        /// Update color in palette for specific owner/country ID
        /// Task 1.3: Support for dynamic color palette updates
        /// </summary>
        /// <param name="paletteIndex">Palette index (0-255, typically owner/country ID)</param>
        /// <param name="color">Color to assign</param>
        public void SetPaletteColor(byte paletteIndex, Color32 color)
        {
            provinceColorPalette.SetPixel(paletteIndex, 0, color);
        }

        /// <summary>
        /// Update multiple palette colors at once for efficiency
        /// </summary>
        /// <param name="colors">Array of 256 colors for the full palette</param>
        public void SetPaletteColors(Color32[] colors)
        {
            if (colors.Length != 256)
            {
                DominionLogger.LogError($"Palette colors array must be exactly 256 elements, got {colors.Length}");
                return;
            }

            provinceColorPalette.SetPixels32(colors);
        }

        /// <summary>
        /// Apply all texture changes (call after batch updates)
        /// </summary>
        public void ApplyTextureChanges()
        {
            provinceIDTexture.Apply(false);
            provinceOwnerTexture.Apply(false);
            provinceColorTexture.Apply(false);
            provinceColorPalette.Apply(false);
        }

        /// <summary>
        /// Apply only palette changes (more efficient when only colors changed)
        /// </summary>
        public void ApplyPaletteChanges()
        {
            provinceColorPalette.Apply(false);
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
            material.SetTexture(ProvinceColorPaletteID, provinceColorPalette);
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
            if (provinceColorPalette != null) DestroyImmediate(provinceColorPalette);
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
            totalMemory += 256 * 4;        // Color Palette (256×1 RGBA32)
            totalMemory += pixelCount * 1; // Border (R8)
            totalMemory += pixelCount * 4; // Highlight (ARGB32)

            DominionLogger.Log($"Map texture memory usage: {totalMemory / 1024f / 1024f:F2} MB");
        }
#endif
    }
}