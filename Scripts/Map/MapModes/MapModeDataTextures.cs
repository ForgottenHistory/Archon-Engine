using UnityEngine;
using Unity.Collections;
using Map.Rendering;

namespace Map.MapModes
{
    /// <summary>
    /// Manages all data textures required for the map mode system
    /// Each texture is optimized for specific data types and update patterns
    /// Performance: Specialized formats, efficient updates, proper GPU memory layout
    /// </summary>
    public class MapModeDataTextures : System.IDisposable
    {
        [Header("Map Dimensions")]
        public int MapWidth { get; private set; }
        public int MapHeight { get; private set; }

        // Core ID textures (always needed)
        public RenderTexture ProvinceIDTexture { get; private set; }  // ARGB32 RenderTexture: Province IDs (0-65535)
        public Texture2D ProvinceColorTexture { get; private set; }   // RGBA32: Province colors from bitmap

        // Mode-specific data textures (created on demand)
        public RenderTexture ProvinceOwnerTexture { get; private set; }    // RG16: Owner nation ID (UAV for compute shader)
        public Texture2D ProvinceTerrainTexture { get; private set; }  // R8: Terrain type ID
        public RenderTexture ProvinceDevelopmentTexture { get; private set; } // RGBA32: Development/gradient data (UAV for compute shader)
        public Texture2D ProvinceCultureTexture { get; private set; }  // R16: Culture ID
        public Texture2D ProvinceReligionTexture { get; private set; } // R16: Religion ID
        public Texture2D ProvinceTradeValueTexture { get; private set; } // R16: Trade value
        public Texture2D ProvinceUnrestTexture { get; private set; }   // R8: Unrest level
        public Texture2D ProvinceAutonomyTexture { get; private set; } // R8: Autonomy percentage

        // Composite data textures (for complex modes)
        public Texture2D DiplomaticRelationsTexture { get; private set; } // RG16: Relations data
        public Texture2D MilitaryStrengthTexture { get; private set; }    // RGBA32: Military details

        // Color palettes (for ID→Color mapping)
        public Texture2D CountryColorPalette { get; private set; }     // 256x1 RGBA32
        public Texture2D CultureColorPalette { get; private set; }     // 256x1 RGBA32
        public Texture2D ReligionColorPalette { get; private set; }    // 256x1 RGBA32
        public Texture2D TerrainColorPalette { get; private set; }     // 32x1 RGBA32

        // Texture property IDs for shader efficiency
        private static readonly int ProvinceIDTexID = Shader.PropertyToID("_ProvinceIDTexture");
        private static readonly int ProvinceOwnerTexID = Shader.PropertyToID("_ProvinceOwnerTexture");
        private static readonly int ProvinceColorTexID = Shader.PropertyToID("_ProvinceColorTexture");
        private static readonly int ProvinceTerrainTexID = Shader.PropertyToID("_ProvinceTerrainTexture");
        private static readonly int ProvinceDevelopmentTexID = Shader.PropertyToID("_ProvinceDevelopmentTexture");
        private static readonly int ProvinceCultureTexID = Shader.PropertyToID("_ProvinceCultureTexture");
        private static readonly int ProvinceReligionTexID = Shader.PropertyToID("_ProvinceReligionTexture");
        private static readonly int CountryColorPaletteID = Shader.PropertyToID("_CountryColorPalette");
        private static readonly int CultureColorPaletteID = Shader.PropertyToID("_CultureColorPalette");
        private static readonly int ReligionColorPaletteID = Shader.PropertyToID("_ReligionColorPalette");
        private static readonly int TerrainColorPaletteID = Shader.PropertyToID("_TerrainColorPalette");

        private bool isInitialized = false;

        /// <summary>
        /// Initialize textures for map mode system using existing textures from MapTextureManager
        /// </summary>
        public void Initialize(MapTextureManager textureManager)
        {
            if (isInitialized)
            {
                ArchonLogger.LogWarning("MapModeDataTextures already initialized", "map_modes");
                return;
            }

            if (textureManager == null)
            {
                ArchonLogger.LogError("MapModeDataTextures: Cannot initialize without MapTextureManager", "map_modes");
                return;
            }

            // Use existing textures from MapTextureManager (single source of truth)
            ProvinceIDTexture = textureManager.ProvinceIDTexture;
            ProvinceOwnerTexture = textureManager.ProvinceOwnerTexture;
            ProvinceColorTexture = textureManager.ProvinceColorTexture;

            MapWidth = ProvinceIDTexture.width;
            MapHeight = ProvinceIDTexture.height;

            // Use the proper development texture from MapTextureManager
            ProvinceDevelopmentTexture = textureManager.ProvinceDevelopmentTexture;

            // Use the terrain texture from MapTextureManager (critical for unowned provinces)
            ProvinceTerrainTexture = textureManager.ProvinceTerrainTexture;

            // Create color palettes only
            CreateColorPalettes();

            isInitialized = true;
            ArchonLogger.Log($"MapModeDataTextures initialized: {MapWidth}x{MapHeight} - using existing texture system", "map_initialization");
        }

        // REMOVED: CreateDataTextures method
        // We use the existing texture system instead of creating new textures

        /// <summary>
        /// Create color palette textures for ID-to-color mapping
        /// </summary>
        private void CreateColorPalettes()
        {
            CountryColorPalette = CreatePaletteTexture("CountryColors", 1024);
            CultureColorPalette = CreatePaletteTexture("CultureColors", 256);
            ReligionColorPalette = CreatePaletteTexture("ReligionColors", 256);
            TerrainColorPalette = CreatePaletteTexture("TerrainColors", 32);

            InitializeDefaultPalettes();
        }

        /// <summary>
        /// Create a texture with proper map settings
        /// </summary>
        private Texture2D CreateTexture(string name, TextureFormat format, int width, int height)
        {
            var texture = new Texture2D(width, height, format, false);
            texture.name = name;

            // Critical settings for map textures
            texture.filterMode = FilterMode.Point;      // No interpolation
            texture.wrapMode = TextureWrapMode.Clamp;   // No wrapping
            texture.anisoLevel = 0;                     // No anisotropic filtering

            return texture;
        }

        /// <summary>
        /// Create a color palette texture (1D texture for color lookup)
        /// </summary>
        private Texture2D CreatePaletteTexture(string name, int size)
        {
            var texture = new Texture2D(size, 1, TextureFormat.RGBA32, false, false);  // linear=false (sRGB)
            texture.name = name;
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.anisoLevel = 0;

            return texture;
        }

        /// <summary>
        /// Initialize color palettes with default colors
        /// </summary>
        private void InitializeDefaultPalettes()
        {
            InitializeCountryPalette();
            InitializeCulturePalette();
            InitializeReligionPalette();
            InitializeTerrainPalette();
        }

        private void InitializeCountryPalette()
        {
            // Initialize with ocean color - PoliticalMapMode will populate with real country colors
            var colors = new Color32[1024];
            Color32 oceanColor = new Color32(25, 25, 112, 255); // Dark blue ocean
            for (int i = 0; i < 1024; i++)
            {
                colors[i] = oceanColor;
            }
            CountryColorPalette.SetPixels32(colors);
            CountryColorPalette.Apply(false);
        }

        private void InitializeCulturePalette()
        {
            var colors = new Color32[256];
            for (int i = 0; i < 256; i++)
            {
                colors[i] = GenerateCultureColor(i);
            }
            CultureColorPalette.SetPixels32(colors);
            CultureColorPalette.Apply(false);
        }

        private void InitializeReligionPalette()
        {
            var colors = new Color32[256];
            for (int i = 0; i < 256; i++)
            {
                colors[i] = GenerateReligionColor(i);
            }
            ReligionColorPalette.SetPixels32(colors);
            ReligionColorPalette.Apply(false);
        }

        private void InitializeTerrainPalette()
        {
            var colors = new Color32[32];
            colors[0] = new Color32(25, 25, 112, 255);   // Ocean - Dark blue
            colors[1] = new Color32(34, 139, 34, 255);   // Grassland - Forest green
            colors[2] = new Color32(139, 69, 19, 255);   // Hills - Saddle brown
            colors[3] = new Color32(105, 105, 105, 255); // Mountains - Dim gray
            colors[4] = new Color32(238, 203, 173, 255); // Desert - Peach puff
            colors[5] = new Color32(220, 220, 220, 255); // Coastal - Light gray

            // Fill remaining with variations
            for (int i = 6; i < 32; i++)
            {
                colors[i] = GenerateTerrainColor(i);
            }

            TerrainColorPalette.SetPixels32(colors);
            TerrainColorPalette.Apply(false);
        }

        /// <summary>
        /// Generate visually distinct country colors using HSV
        /// </summary>
        private Color32 GenerateCountryColor(int index)
        {
            if (index == 0) return new Color32(64, 64, 64, 255); // Unowned = dark gray

            float hue = (index * 137.508f) % 360f; // Golden angle for good distribution
            float saturation = 0.7f + (index % 3) * 0.1f;
            float value = 0.8f + (index % 2) * 0.2f;

            Color color = Color.HSVToRGB(hue / 360f, saturation, value);
            return new Color32(
                (byte)(color.r * 255),
                (byte)(color.g * 255),
                (byte)(color.b * 255),
                255
            );
        }

        /// <summary>
        /// Generate culture colors with warm tones
        /// </summary>
        private Color32 GenerateCultureColor(int index)
        {
            if (index == 0) return new Color32(128, 128, 128, 255); // Unknown culture

            float hue = (index * 43.7f) % 360f; // Different angle than countries
            float saturation = 0.6f + (index % 4) * 0.1f;
            float value = 0.7f + (index % 3) * 0.1f;

            Color color = Color.HSVToRGB(hue / 360f, saturation, value);
            return new Color32(
                (byte)(color.r * 255),
                (byte)(color.g * 255),
                (byte)(color.b * 255),
                255
            );
        }

        /// <summary>
        /// Generate religion colors with cooler tones
        /// </summary>
        private Color32 GenerateReligionColor(int index)
        {
            if (index == 0) return new Color32(96, 96, 96, 255); // Unknown religion

            float hue = (180 + index * 23.6f) % 360f; // Cool color range
            float saturation = 0.5f + (index % 5) * 0.1f;
            float value = 0.6f + (index % 4) * 0.1f;

            Color color = Color.HSVToRGB(hue / 360f, saturation, value);
            return new Color32(
                (byte)(color.r * 255),
                (byte)(color.g * 255),
                (byte)(color.b * 255),
                255
            );
        }

        /// <summary>
        /// Generate terrain variation colors
        /// </summary>
        private Color32 GenerateTerrainColor(int index)
        {
            // Generate earth-tone variations
            float variation = (index % 8) / 8.0f;
            Color32 baseColor = new Color32(101, 67, 33, 255); // Brown base

            byte r = (byte)Mathf.Clamp(baseColor.r + variation * 50 - 25, 0, 255);
            byte g = (byte)Mathf.Clamp(baseColor.g + variation * 40 - 20, 0, 255);
            byte b = (byte)Mathf.Clamp(baseColor.b + variation * 30 - 15, 0, 255);

            return new Color32(r, g, b, 255);
        }

        /// <summary>
        /// Bind all textures to the material for rendering
        /// </summary>
        public void BindToMaterial(Material material)
        {
            if (!isInitialized)
            {
                ArchonLogger.LogError("MapModeDataTextures not initialized", "map_modes");
                return;
            }

            // Core textures
            material.SetTexture(ProvinceIDTexID, ProvinceIDTexture);
            material.SetTexture(ProvinceColorTexID, ProvinceColorTexture);

            // Data textures
            material.SetTexture(ProvinceOwnerTexID, ProvinceOwnerTexture);
            material.SetTexture(ProvinceTerrainTexID, ProvinceTerrainTexture);
            material.SetTexture(ProvinceDevelopmentTexID, ProvinceDevelopmentTexture);
            material.SetTexture(ProvinceCultureTexID, ProvinceCultureTexture);
            material.SetTexture(ProvinceReligionTexID, ProvinceReligionTexture);

            // Color palettes
            material.SetTexture(CountryColorPaletteID, CountryColorPalette);
            material.SetTexture(CultureColorPaletteID, CultureColorPalette);
            material.SetTexture(ReligionColorPaletteID, ReligionColorPalette);
            material.SetTexture(TerrainColorPaletteID, TerrainColorPalette);

            // Debug: Verify binding
            var boundTexture = material.GetTexture(CountryColorPaletteID);
            ArchonLogger.Log($"MapModeDataTextures: Bound _CountryColorPalette - Expected: {CountryColorPalette?.GetInstanceID()}, Got: {boundTexture?.GetInstanceID()}", "map_initialization");
        }

        /// <summary>
        /// Update a specific data texture with new pixel data
        /// </summary>
        public void UpdateTexture(Texture2D texture, Color32[] pixels)
        {
            if (texture == null)
            {
                ArchonLogger.LogError("Cannot update null texture", "map_modes");
                return;
            }

            texture.SetPixels32(pixels);
            texture.Apply(false);
        }

        /// <summary>
        /// Update a color palette texture
        /// </summary>
        public void UpdatePalette(Texture2D palette, Color32[] colors)
        {
            if (palette == null)
            {
                ArchonLogger.LogError("Cannot update null palette", "map_modes");
                return;
            }

            palette.SetPixels32(colors);
            palette.Apply(false);
        }

        /// <summary>
        /// Clean up all textures
        /// </summary>
        public void Dispose()
        {
            if (!isInitialized) return;

            // Destroy textures
            // NOTE: ProvinceIDTexture, ProvinceOwnerTexture, ProvinceTerrainTexture, and ProvinceDevelopmentTexture
            // are owned by MapTextureManager - do NOT destroy them here
            // if (ProvinceIDTexture != null) Object.DestroyImmediate(ProvinceIDTexture);
            // if (ProvinceOwnerTexture != null) Object.DestroyImmediate(ProvinceOwnerTexture);
            // if (ProvinceTerrainTexture != null) Object.DestroyImmediate(ProvinceTerrainTexture);
            // if (ProvinceDevelopmentTexture != null) Object.DestroyImmediate(ProvinceDevelopmentTexture);
            if (ProvinceCultureTexture != null) Object.DestroyImmediate(ProvinceCultureTexture);
            if (ProvinceReligionTexture != null) Object.DestroyImmediate(ProvinceReligionTexture);
            if (ProvinceTradeValueTexture != null) Object.DestroyImmediate(ProvinceTradeValueTexture);
            if (ProvinceUnrestTexture != null) Object.DestroyImmediate(ProvinceUnrestTexture);
            if (ProvinceAutonomyTexture != null) Object.DestroyImmediate(ProvinceAutonomyTexture);
            if (DiplomaticRelationsTexture != null) Object.DestroyImmediate(DiplomaticRelationsTexture);
            if (MilitaryStrengthTexture != null) Object.DestroyImmediate(MilitaryStrengthTexture);

            // Destroy palettes
            if (CountryColorPalette != null) Object.DestroyImmediate(CountryColorPalette);
            if (CultureColorPalette != null) Object.DestroyImmediate(CultureColorPalette);
            if (ReligionColorPalette != null) Object.DestroyImmediate(ReligionColorPalette);
            if (TerrainColorPalette != null) Object.DestroyImmediate(TerrainColorPalette);

            isInitialized = false;
            ArchonLogger.Log("MapModeDataTextures disposed", "map_modes");
        }
    }
}