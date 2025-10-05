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
        [SerializeField] private int normalMapWidth = 2816;
        [SerializeField] private int normalMapHeight = 1024;

        [Header("Debug")]
        [SerializeField] private bool logTextureCreation = true;

        // Core map textures
        private RenderTexture provinceIDTexture;      // ARGB32 RenderTexture for province IDs (GPU-accessible for compute shaders)
        private RenderTexture provinceOwnerTexture;   // RFloat format for province owners (needs UAV for compute shader writes)
        private Texture2D provinceColorTexture;   // RGBA32 format for province colors (legacy)
        private Texture2D provinceDevelopmentTexture; // RGBA32 format for development visualization
        private Texture2D provinceTerrainTexture; // RGBA32 format for terrain colors from terrain.bmp
        private Texture2D heightmapTexture;       // R8 format for heightmap data from heightmap.bmp
        private Texture2D normalMapTexture;       // RGB24 format for normal map data from world_normal.bmp (2816×1024)
        private Texture2D provinceColorPalette;   // 256×1 RGBA32 palette for efficient color lookup

        // Dynamic render textures
        private RenderTexture borderTexture;      // R8 format for borders
        private RenderTexture highlightTexture;   // RGBA32 for selection highlights

        // Temporary textures for CPU->GPU updates
        private Texture2D tempOwnerTexture;       // Temporary texture for batching owner updates before copying to RenderTexture
        private bool ownerDataDirty = false;      // Flag indicating owner texture needs to be copied to GPU

        // Texture property IDs for shader efficiency - MUST match shader property names exactly!
        private static readonly int ProvinceIDTexID = Shader.PropertyToID("_ProvinceIDTexture");
        private static readonly int ProvinceOwnerTexID = Shader.PropertyToID("_ProvinceOwnerTexture");
        private static readonly int ProvinceColorTexID = Shader.PropertyToID("_ProvinceColorTexture");
        private static readonly int ProvinceDevelopmentTexID = Shader.PropertyToID("_ProvinceDevelopmentTexture");
        private static readonly int ProvinceTerrainTexID = Shader.PropertyToID("_ProvinceTerrainTexture");
        private static readonly int HeightmapTexID = Shader.PropertyToID("_HeightmapTexture");
        private static readonly int NormalMapTexID = Shader.PropertyToID("_NormalMapTexture");
        private static readonly int ProvinceColorPaletteID = Shader.PropertyToID("_ProvinceColorPalette");
        private static readonly int BorderTexID = Shader.PropertyToID("_BorderTexture");
        private static readonly int HighlightTexID = Shader.PropertyToID("_HighlightTexture");

        public int MapWidth => mapWidth;
        public int MapHeight => mapHeight;

        public RenderTexture ProvinceIDTexture => provinceIDTexture;
        public RenderTexture ProvinceOwnerTexture => provinceOwnerTexture;
        public Texture2D ProvinceColorTexture => provinceColorTexture;
        public Texture2D ProvinceDevelopmentTexture => provinceDevelopmentTexture;
        public Texture2D ProvinceTerrainTexture => provinceTerrainTexture;
        public Texture2D HeightmapTexture => heightmapTexture;
        public Texture2D NormalMapTexture => normalMapTexture;
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
            CreateProvinceDevelopmentTexture();
            CreateProvinceTerrainTexture();
            CreateHeightmapTexture();
            CreateNormalMapTexture();
            CreateProvinceColorPalette();
            CreateBorderTexture();
            CreateHighlightTexture();

            if (logTextureCreation)
            {
                ArchonLogger.LogMapInit($"MapTextureManager initialized with {mapWidth}x{mapHeight} textures");
            }
        }

        /// <summary>
        /// Create province ID texture as RenderTexture for GPU accessibility
        /// Uses explicit GraphicsFormat.R8G8B8A8_UNorm to prevent TYPELESS format
        /// Will be populated by compute shader instead of CPU SetPixel()
        /// </summary>
        private void CreateProvinceIDTexture()
        {
            // Use RenderTextureDescriptor with explicit GraphicsFormat to avoid TYPELESS
            var descriptor = new RenderTextureDescriptor(mapWidth, mapHeight, UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm, 0);
            descriptor.enableRandomWrite = true;  // Enable UAV for compute shader writes
            descriptor.useMipMap = false;
            descriptor.autoGenerateMips = false;

            provinceIDTexture = new RenderTexture(descriptor);
            provinceIDTexture.name = "ProvinceID_RenderTexture";
            provinceIDTexture.filterMode = FilterMode.Point;     // No filtering for pixel-perfect
            provinceIDTexture.wrapMode = TextureWrapMode.Clamp;  // No wrapping
            provinceIDTexture.Create();

            // Clear to zero (no province) using GPU
            RenderTexture.active = provinceIDTexture;
            GL.Clear(true, true, Color.black);
            RenderTexture.active = null;

            if (logTextureCreation)
            {
                ArchonLogger.LogMapInit($"Created Province ID texture: {mapWidth}x{mapHeight} ARGB32 RenderTexture (GPU-accessible)");
                ArchonLogger.LogMapInit($"ProvinceIDTexture instance ID: {provinceIDTexture.GetInstanceID()}");
            }
        }

        /// <summary>
        /// Create province owner render texture for 16-bit country IDs
        /// Uses RenderTexture (not Texture2D) to allow compute shader writes
        /// Uses RFloat format: 32-bit float (needed for RWTexture2D<float> UAV support)
        /// Stores owner ID as normalized float: ownerID / 65535.0
        /// </summary>
        private void CreateProvinceOwnerTexture()
        {
            provinceOwnerTexture = new RenderTexture(mapWidth, mapHeight, 0, RenderTextureFormat.RFloat);
            provinceOwnerTexture.name = "ProvinceOwner_RenderTexture";
            provinceOwnerTexture.filterMode = FilterMode.Point;
            provinceOwnerTexture.wrapMode = TextureWrapMode.Clamp;
            provinceOwnerTexture.useMipMap = false;
            provinceOwnerTexture.autoGenerateMips = false;
            provinceOwnerTexture.enableRandomWrite = true;  // CRITICAL: Enable UAV for compute shader write access
            provinceOwnerTexture.Create();

            // Clear to black (no owner)
            RenderTexture.active = provinceOwnerTexture;
            GL.Clear(true, true, Color.black);
            RenderTexture.active = null;

            if (logTextureCreation)
            {
                ArchonLogger.LogMapInit($"Created Province Owner RenderTexture: {mapWidth}x{mapHeight} RFloat format with UAV support");
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
                ArchonLogger.LogMapInit($"Created Province Color texture: {mapWidth}x{mapHeight} RGBA32 format");
            }
        }

        /// <summary>
        /// Create province development texture in RGBA32 format for development visualization
        /// </summary>
        private void CreateProvinceDevelopmentTexture()
        {
            provinceDevelopmentTexture = new Texture2D(mapWidth, mapHeight, TextureFormat.RGBA32, false);
            provinceDevelopmentTexture.name = "ProvinceDevelopment_Texture";

            ConfigureMapTexture(provinceDevelopmentTexture);

            // Initialize with ocean color (development mode default)
            var pixels = new Color32[mapWidth * mapHeight];
            Color32 oceanColor = new Color32(25, 25, 112, 255); // Dark blue for ocean
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = oceanColor;
            }

            provinceDevelopmentTexture.SetPixels32(pixels);
            provinceDevelopmentTexture.Apply(false);

            if (logTextureCreation)
            {
                ArchonLogger.LogMapInit($"Created Province Development texture: {mapWidth}x{mapHeight} RGBA32 format");
            }
        }

        /// <summary>
        /// Create province terrain texture in RGBA32 format for terrain colors from terrain.bmp
        /// </summary>
        private void CreateProvinceTerrainTexture()
        {
            provinceTerrainTexture = new Texture2D(mapWidth, mapHeight, TextureFormat.RGBA32, false);
            provinceTerrainTexture.name = "ProvinceTerrain_Texture";

            // DEBUG: Try default Unity texture settings instead of ConfigureMapTexture
            // ConfigureMapTexture(provinceTerrainTexture);

            // Initialize with default land color (brown/tan for land)
            var pixels = new Color32[mapWidth * mapHeight];
            Color32 landColor = new Color32(139, 125, 107, 255); // Brown/tan for land
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = landColor;
            }

            provinceTerrainTexture.SetPixels32(pixels);
            provinceTerrainTexture.Apply(false);

            if (logTextureCreation)
            {
                ArchonLogger.LogMapInit($"Created Province Terrain texture: {mapWidth}x{mapHeight} RGBA32 format");
            }
        }

        /// <summary>
        /// Create heightmap texture in R8 format for terrain elevation data from heightmap.bmp
        /// Uses R8 (single channel, 8-bit) to match source BMP format and optimize memory (11.5MB)
        /// Filter mode is Bilinear for smooth height interpolation
        /// </summary>
        private void CreateHeightmapTexture()
        {
            heightmapTexture = new Texture2D(mapWidth, mapHeight, TextureFormat.R8, false);
            heightmapTexture.name = "Heightmap_Texture";

            // Heightmap uses bilinear filtering for smooth height sampling (not point filtering like IDs)
            heightmapTexture.filterMode = FilterMode.Bilinear;
            heightmapTexture.wrapMode = TextureWrapMode.Clamp;
            heightmapTexture.anisoLevel = 0;

            // Initialize with flat elevation (mid-height = 128)
            var pixels = new Color[mapWidth * mapHeight];
            Color midHeight = new Color(0.5f, 0, 0, 1); // 0.5 = 50% height = sea level
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = midHeight;
            }

            heightmapTexture.SetPixels(pixels);
            heightmapTexture.Apply(false);

            if (logTextureCreation)
            {
                ArchonLogger.LogMapInit($"Created Heightmap texture: {mapWidth}x{mapHeight} R8 format (bilinear filtering)");
            }
        }

        /// <summary>
        /// Create normal map texture in RGB24 format for surface normal data from world_normal.bmp
        /// Uses RGB24 (3 channels, 24-bit) for X/Y/Z normal components
        /// Resolution is half of main map (2816×1024) for performance
        /// Filter mode is Bilinear for smooth normal interpolation
        /// </summary>
        private void CreateNormalMapTexture()
        {
            normalMapTexture = new Texture2D(normalMapWidth, normalMapHeight, TextureFormat.RGB24, false);
            normalMapTexture.name = "NormalMap_Texture";

            // Normal maps use bilinear filtering for smooth surface normals
            normalMapTexture.filterMode = FilterMode.Bilinear;
            normalMapTexture.wrapMode = TextureWrapMode.Clamp;
            normalMapTexture.anisoLevel = 0;

            // Initialize with default "up" normal (0, 0, 1) encoded as RGB (128, 128, 255)
            var pixels = new Color32[normalMapWidth * normalMapHeight];
            Color32 defaultNormal = new Color32(128, 128, 255, 255); // Flat surface pointing up
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = defaultNormal;
            }

            normalMapTexture.SetPixels32(pixels);
            normalMapTexture.Apply(false);

            if (logTextureCreation)
            {
                ArchonLogger.LogMapInit($"Created Normal Map texture: {normalMapWidth}x{normalMapHeight} RGB24 format (bilinear filtering)");
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
                ArchonLogger.LogMapInit($"Created Province Color Palette: 256×1 RGBA32 format");
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
            borderTexture = new RenderTexture(mapWidth, mapHeight, 0, RenderTextureFormat.RG16);
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
                ArchonLogger.LogMapInit($"Created Border RenderTexture: {mapWidth}x{mapHeight} RG16 format (R=country, G=province)");
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
                ArchonLogger.LogMapInit($"Created Highlight RenderTexture: {mapWidth}x{mapHeight} ARGB32 format");
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
        /// Update province ID at specific pixel coordinates
        /// DEPRECATED: ProvinceIDTexture is now a RenderTexture populated by compute shader
        /// This method is kept for backwards compatibility but does nothing
        /// Use PopulateProvinceIDTextureFromBMP compute shader instead
        /// </summary>
        public void SetProvinceID(int x, int y, ushort provinceID)
        {
            // DEPRECATED: Cannot use SetPixel on RenderTexture
            // Province ID texture must be populated via compute shader
            ArchonLogger.LogWarning($"SetProvinceID is deprecated - use compute shader to populate ProvinceIDTexture");
        }

        /// <summary>
        /// Get province ID at specific coordinates using GPU readback
        /// NOTE: This is slow (GPU→CPU readback) - use sparingly, only for mouse picking
        /// </summary>
        /// <param name="x">X coordinate</param>
        /// <param name="y">Y coordinate</param>
        /// <returns>Province ID (0 if invalid coordinates)</returns>
        public ushort GetProvinceID(int x, int y)
        {
            if (x < 0 || x >= mapWidth || y < 0 || y >= mapHeight) return 0;

            // Read single pixel from RenderTexture (slow but necessary for mouse picking)
            RenderTexture.active = provinceIDTexture;
            Texture2D temp = new Texture2D(1, 1, TextureFormat.ARGB32, false);
            temp.ReadPixels(new Rect(x, y, 1, 1), 0, 0);
            temp.Apply();
            RenderTexture.active = null;

            Color32 packedColor = temp.GetPixel(0, 0);
            Object.Destroy(temp);

            return Province.ProvinceIDEncoder.UnpackProvinceID(packedColor);
        }

        /// <summary>
        /// Update province owner at specific coordinates
        /// DEPRECATED: Owner texture is now a RenderTexture written by compute shader (OwnerTextureDispatcher)
        /// CPU writes to RenderTexture are not supported - use compute shader for all owner updates
        /// </summary>
        public void SetProvinceOwner(int x, int y, ushort ownerID)
        {
            if (provinceOwnerTexture == null) return;

            // Encode owner ID into RG channels (0-65535 range)
            byte r = (byte)(ownerID & 0xFF);          // Low 8 bits
            byte g = (byte)((ownerID >> 8) & 0xFF);   // High 8 bits

            // Write to RenderTexture using Graphics.CopyTexture workaround
            // RenderTextures can't use SetPixel, so we use a temporary Texture2D
            if (tempOwnerTexture == null || tempOwnerTexture.width != mapWidth || tempOwnerTexture.height != mapHeight)
            {
                tempOwnerTexture = new Texture2D(mapWidth, mapHeight, TextureFormat.RG16, false);
                tempOwnerTexture.filterMode = FilterMode.Point;
                tempOwnerTexture.wrapMode = TextureWrapMode.Clamp;
            }

            tempOwnerTexture.SetPixel(x, y, new Color32(r, g, 0, 255));
            ownerDataDirty = true;
        }

        /// <summary>
        /// Update province development color at specific coordinates
        /// </summary>
        /// <param name="x">X coordinate</param>
        /// <param name="y">Y coordinate</param>
        /// <param name="developmentColor">RGBA32 development color</param>
        public void SetProvinceDevelopment(int x, int y, Color32 developmentColor)
        {
            if (x < 0 || x >= mapWidth || y < 0 || y >= mapHeight) return;

            provinceDevelopmentTexture.SetPixel(x, y, developmentColor);
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
                ArchonLogger.LogError($"Palette colors array must be exactly 256 elements, got {colors.Length}");
                return;
            }

            provinceColorPalette.SetPixels32(colors);
        }

        /// <summary>
        /// Apply all texture changes (call after batch updates)
        /// Note: provinceOwnerTexture is RenderTexture (no Apply needed - updated via compute shader)
        /// </summary>
        public void ApplyTextureChanges()
        {
            // provinceIDTexture.Apply(false); // RenderTexture - no Apply() method needed
            // provinceOwnerTexture.Apply(false); // RenderTexture - no Apply() method needed
            provinceColorTexture.Apply(false);
            provinceDevelopmentTexture.Apply(false);
            provinceTerrainTexture.Apply(false);
            provinceColorPalette.Apply(false);

            // Owner texture populated by GPU compute shader only (architecture compliance)
            // NO CPU path - removed ApplyOwnerTextureChanges() call
        }

        /// <summary>
        /// Apply owner texture changes by copying tempOwnerTexture to provinceOwnerTexture RenderTexture
        /// NOTE: This is CPU path legacy code - should be replaced with GPU-only path
        /// </summary>
        public void ApplyOwnerTextureChanges()
        {
            if (!ownerDataDirty || tempOwnerTexture == null) return;

            // Apply changes to temp texture first
            tempOwnerTexture.Apply(false);

            // Use a custom blit material to preserve RG16 data correctly
            // Graphics.Blit without material uses default shader that might corrupt data
            Material blitMaterial = new Material(Shader.Find("Hidden/BlitCopy"));
            if (blitMaterial == null || blitMaterial.shader == null)
            {
                // Fallback: try standard Blit (might corrupt RG16 data)
                ArchonLogger.LogWarning("MapTextureManager: BlitCopy shader not found, using standard Blit (might corrupt RG16 data)");
                Graphics.Blit(tempOwnerTexture, provinceOwnerTexture);
            }
            else
            {
                Graphics.Blit(tempOwnerTexture, provinceOwnerTexture, blitMaterial);
                Object.DestroyImmediate(blitMaterial);
            }

            ownerDataDirty = false;

            if (logTextureCreation)
            {
                ArchonLogger.Log("MapTextureManager: Copied owner texture updates to GPU RenderTexture via Blit");
            }
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

            // DEBUG: Verify ProvinceIDTexture was bound correctly
            var retrievedIDTexture = material.GetTexture(ProvinceIDTexID);
            if (retrievedIDTexture == provinceIDTexture)
            {
                ArchonLogger.LogMapInit($"MapTextureManager: ✓ ProvinceIDTexture bound correctly - instance {provinceIDTexture.GetInstanceID()}");
            }
            else
            {
                ArchonLogger.LogMapInitError($"MapTextureManager: ✗ ProvinceIDTexture binding FAILED - set {provinceIDTexture?.GetInstanceID()}, got {retrievedIDTexture?.GetInstanceID()}");
            }

            material.SetTexture(ProvinceOwnerTexID, provinceOwnerTexture);
            material.SetTexture(ProvinceColorTexID, provinceColorTexture);
            material.SetTexture(ProvinceDevelopmentTexID, provinceDevelopmentTexture);
            material.SetTexture(ProvinceTerrainTexID, provinceTerrainTexture);
            material.SetTexture(HeightmapTexID, heightmapTexture);
            material.SetTexture(NormalMapTexID, normalMapTexture);

            // DEBUG: Verify the texture was actually set
            var retrievedTexture = material.GetTexture(ProvinceTerrainTexID);
            if (retrievedTexture == provinceTerrainTexture)
            {
                ArchonLogger.LogMapInit($"MapTextureManager: Successfully verified terrain texture binding - instances match");
            }
            else
            {
                ArchonLogger.LogMapInitError($"MapTextureManager: Terrain texture binding FAILED - set {provinceTerrainTexture?.GetInstanceID()}, got {retrievedTexture?.GetInstanceID()}");
            }
            material.SetTexture(ProvinceColorPaletteID, provinceColorPalette);
            material.SetTexture(BorderTexID, borderTexture);
            material.SetTexture(HighlightTexID, highlightTexture);

            // Set default border parameters (GAME layer can override via SetBorderStyle)
            material.SetFloat("_CountryBorderStrength", 1.0f);
            material.SetColor("_CountryBorderColor", Color.black);
            material.SetFloat("_ProvinceBorderStrength", 0.5f);
            material.SetColor("_ProvinceBorderColor", new Color(0.3f, 0.3f, 0.3f));

            // Debug: Verify terrain texture binding
            if (provinceTerrainTexture != null)
            {
                ArchonLogger.LogMapInit($"MapTextureManager: Bound terrain texture instance {provinceTerrainTexture.GetInstanceID()} ({provinceTerrainTexture.name}) size {provinceTerrainTexture.width}x{provinceTerrainTexture.height} to material");
            }
            else
            {
                ArchonLogger.LogMapInitWarning("MapTextureManager: Terrain texture is null when binding to material!");
            }
        }

        /// <summary>
        /// Set border visual style (EXTENSION POINT for GAME layer)
        /// Allows GAME layer to control border appearance without modifying ENGINE
        /// </summary>
        /// <param name="material">Material to update</param>
        /// <param name="countryBorderColor">Color for country borders</param>
        /// <param name="countryBorderStrength">Strength of country borders (0-1)</param>
        /// <param name="provinceBorderColor">Color for province borders</param>
        /// <param name="provinceBorderStrength">Strength of province borders (0-1)</param>
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
            if (provinceIDTexture != null) provinceIDTexture.Release();  // RenderTexture uses Release()
            if (provinceOwnerTexture != null) provinceOwnerTexture.Release();  // RenderTexture uses Release(), not DestroyImmediate()
            if (provinceColorTexture != null) DestroyImmediate(provinceColorTexture);
            if (provinceDevelopmentTexture != null) DestroyImmediate(provinceDevelopmentTexture);
            if (provinceTerrainTexture != null) DestroyImmediate(provinceTerrainTexture);
            if (heightmapTexture != null) DestroyImmediate(heightmapTexture);
            if (normalMapTexture != null) DestroyImmediate(normalMapTexture);
            if (provinceColorPalette != null) DestroyImmediate(provinceColorPalette);
            if (borderTexture != null) borderTexture.Release();
            if (highlightTexture != null) highlightTexture.Release();
            if (tempOwnerTexture != null) DestroyImmediate(tempOwnerTexture);
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

            ArchonLogger.Log($"Map texture memory usage: {totalMemory / 1024f / 1024f:F2} MB");
        }
#endif
    }
}