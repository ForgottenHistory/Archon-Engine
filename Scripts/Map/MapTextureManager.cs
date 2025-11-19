using UnityEngine;
using Unity.Collections;

namespace Map.Rendering
{
    /// <summary>
    /// Facade coordinator for all map textures
    /// Delegates to specialized texture set managers
    /// Provides unified API for external consumers
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

        // Specialized texture set managers
        private CoreTextureSet coreTextures;
        private VisualTextureSet visualTextures;
        private DynamicTextureSet dynamicTextures;
        private PaletteTextureManager paletteManager;

        // Public accessors - delegate to texture sets
        public int MapWidth => mapWidth;
        public int MapHeight => mapHeight;

        public RenderTexture ProvinceIDTexture => coreTextures?.ProvinceIDTexture;
        public RenderTexture ProvinceOwnerTexture => coreTextures?.ProvinceOwnerTexture;
        public Texture2D ProvinceColorTexture => coreTextures?.ProvinceColorTexture;
        public RenderTexture ProvinceDevelopmentTexture => coreTextures?.ProvinceDevelopmentTexture;
        public Texture2D ProvinceTerrainTexture => visualTextures?.ProvinceTerrainTexture;
        public Texture2D TerrainTypeTexture => visualTextures?.TerrainTypeTexture;
        public Texture2D HeightmapTexture => visualTextures?.HeightmapTexture;
        public Texture2D NormalMapTexture => visualTextures?.NormalMapTexture;
        public RenderTexture DetailIndexTexture => visualTextures?.DetailIndexTexture;
        public RenderTexture DetailMaskTexture => visualTextures?.DetailMaskTexture;
        public Texture2D ProvinceColorPalette => paletteManager?.ProvinceColorPalette;
        public RenderTexture DistanceFieldTexture => dynamicTextures?.DistanceFieldTexture;
        public RenderTexture DualBorderTexture => dynamicTextures?.DualBorderTexture;
        public RenderTexture HighlightTexture => dynamicTextures?.HighlightTexture;
        public RenderTexture FogOfWarTexture => dynamicTextures?.FogOfWarTexture;

        // Expose texture sets for direct access (needed for parameter binding)
        public DynamicTextureSet DynamicTextures => dynamicTextures;

        void Awake()
        {
            InitializeTextures();
        }

        /// <summary>
        /// Initialize all texture sets
        /// </summary>
        private void InitializeTextures()
        {
            // Clamp upscale factor
            // Create specialized texture sets (all at base resolution)
            coreTextures = new CoreTextureSet(mapWidth, mapHeight, logTextureCreation);
            visualTextures = new VisualTextureSet(mapWidth, mapHeight, normalMapWidth, normalMapHeight, logTextureCreation);
            dynamicTextures = new DynamicTextureSet(mapWidth, mapHeight, logTextureCreation);
            paletteManager = new PaletteTextureManager(logTextureCreation);

            // Initialize all textures
            coreTextures.CreateTextures();
            visualTextures.CreateTextures();
            dynamicTextures.CreateTextures();
            paletteManager.CreatePalette();

            if (logTextureCreation)
            {
                ArchonLogger.Log($"MapTextureManager initialized: {mapWidth}x{mapHeight}", "map_initialization");
            }
        }

        /// <summary>
        /// Get province ID at specific coordinates using GPU readback
        /// NOTE: Slow (GPU→CPU readback) - use sparingly for mouse picking only
        /// Y coordinate is in OpenGL convention (0 = bottom), RenderTexture uses GPU convention (0 = top)
        /// </summary>
        public ushort GetProvinceID(int x, int y)
        {
            if (x < 0 || x >= mapWidth || y < 0 || y >= mapHeight) return 0;

            // Y-flip: UV coordinates from hit.textureCoord are OpenGL style (0,0 = bottom-left)
            // But RenderTexture is GPU convention (0,0 = top-left), so flip Y
            int flippedY = mapHeight - 1 - y;

            // Read single pixel from RenderTexture
            RenderTexture.active = ProvinceIDTexture;
            Texture2D temp = new Texture2D(1, 1, TextureFormat.ARGB32, false);
            temp.ReadPixels(new Rect(x, flippedY, 1, 1), 0, 0);
            temp.Apply();
            RenderTexture.active = null;

            Color32 packedColor = temp.GetPixel(0, 0);
            ushort provinceID = Province.ProvinceIDEncoder.UnpackProvinceID(packedColor);

            Object.Destroy(temp);

            return provinceID;
        }

        /// <summary>
        /// Update province color at coordinates (delegate to core textures)
        /// </summary>
        public void SetProvinceColor(int x, int y, Color32 color)
        {
            coreTextures?.SetProvinceColor(x, y, color);
        }

        /// <summary>
        /// NOTE: SetProvinceDevelopment removed - ProvinceDevelopmentTexture is now a RenderTexture
        /// updated by GPU compute shaders. Use GradientMapMode for gradient-based colorization.
        /// </summary>

        /// <summary>
        /// Update palette color (delegate to palette manager)
        /// </summary>
        public void SetPaletteColor(byte paletteIndex, Color32 color)
        {
            paletteManager?.SetPaletteColor(paletteIndex, color);
        }

        /// <summary>
        /// Update all palette colors (delegate to palette manager)
        /// </summary>
        public void SetPaletteColors(Color32[] colors)
        {
            paletteManager?.SetPaletteColors(colors);
        }

        /// <summary>
        /// Apply all texture changes after batch updates
        /// </summary>
        public void ApplyTextureChanges()
        {
            coreTextures?.ApplyChanges();
            visualTextures?.ApplyChanges();
            paletteManager?.ApplyChanges();
        }

        /// <summary>
        /// Apply only palette changes (more efficient)
        /// </summary>
        public void ApplyPaletteChanges()
        {
            paletteManager?.ApplyChanges();
        }

        /// <summary>
        /// Generate terrain type texture from terrain color texture
        /// Must be called AFTER terrain.bmp is loaded
        /// </summary>
        public void GenerateTerrainTypeTexture()
        {
            visualTextures?.GenerateTerrainTypeTexture();
        }

        /// <summary>
        /// Generate normal map from heightmap using GPU compute shader
        /// Must be called AFTER heightmap is loaded
        /// </summary>
        public void GenerateNormalMapFromHeightmap(float heightScale = 10.0f, bool logProgress = false)
        {
            visualTextures?.GenerateNormalMapFromHeightmap(heightScale, logProgress);
        }

        /// <summary>
        /// Bind all textures to material
        /// </summary>
        public void BindTexturesToMaterial(Material material)
        {
            if (material == null) return;

            coreTextures?.BindToMaterial(material);
            visualTextures?.BindToMaterial(material);
            dynamicTextures?.BindToMaterial(material);
            paletteManager?.BindToMaterial(material);

            // NOTE: Border parameters are set by VisualStyleManager, not here
            // Removed hardcoded SetBorderStyle() call to respect visual style configuration

            if (logTextureCreation)
            {
                ArchonLogger.Log("MapTextureManager: Bound all textures to material", "map_initialization");
            }
        }

        /// <summary>
        /// Set border visual style (delegate to dynamic textures)
        /// </summary>
        public void SetBorderStyle(Material material, Color countryBorderColor, float countryBorderStrength,
                                    Color provinceBorderColor, float provinceBorderStrength)
        {
            dynamicTextures?.SetBorderStyle(material, countryBorderColor, countryBorderStrength,
                                             provinceBorderColor, provinceBorderStrength);
        }

        /// <summary>
        /// Set province terrain lookup texture (hybrid terrain system)
        /// </summary>
        public void SetProvinceTerrainLookup(RenderTexture lookupTexture)
        {
            visualTextures?.SetProvinceTerrainLookup(lookupTexture);
        }

        /// <summary>
        /// Set terrain blend map textures (Imperator Rome-style blending)
        /// </summary>
        public void SetTerrainBlendMaps(RenderTexture indexTexture, RenderTexture maskTexture)
        {
            visualTextures?.SetTerrainBlendMaps(indexTexture, maskTexture);
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
            coreTextures?.Release();
            visualTextures?.Release();
            dynamicTextures?.Release();
            paletteManager?.Release();
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

            totalMemory += pixelCount * 4; // Province ID (ARGB32)
            totalMemory += pixelCount * 4; // Province Owner (RFloat)
            totalMemory += pixelCount * 4; // Province Color (RGBA32)
            totalMemory += pixelCount * 4; // Province Development (RGBA32)
            totalMemory += pixelCount * 4; // Province Terrain (RGBA32)
            totalMemory += pixelCount * 1; // Heightmap (R8)
            totalMemory += (normalMapWidth * normalMapHeight) * 3; // Normal Map (RGB24)
            totalMemory += 256 * 4;        // Color Palette (256×1 RGBA32)
            totalMemory += pixelCount * 2; // Border (RG16)
            totalMemory += pixelCount * 4; // Highlight (ARGB32)

            ArchonLogger.Log($"Map texture memory usage: {totalMemory / 1024f / 1024f:F2} MB", "map_rendering");
        }
#endif
    }
}
