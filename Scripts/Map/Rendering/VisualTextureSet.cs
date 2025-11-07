using UnityEngine;
using Utils;

namespace Map.Rendering
{
    /// <summary>
    /// Manages visual enhancement textures
    /// Terrain, Heightmap, and Normal Map textures for visual fidelity
    /// Extracted from MapTextureManager for single responsibility
    /// </summary>
    public class VisualTextureSet
    {
        private readonly int mapWidth;
        private readonly int mapHeight;
        private readonly int normalMapWidth;
        private readonly int normalMapHeight;
        private readonly bool logCreation;

        // Visual enhancement textures
        private Texture2D provinceTerrainTexture;
        private Texture2D terrainTypeTexture;
        private Texture2DArray terrainDetailArray;
        private Texture2D detailNoiseTexture;
        private Texture2D heightmapTexture;
        private Texture2D normalMapTexture;

        // Shader property IDs
        private static readonly int ProvinceTerrainTexID = Shader.PropertyToID("_ProvinceTerrainTexture");
        private static readonly int TerrainTypeTexID = Shader.PropertyToID("_TerrainTypeTexture");
        private static readonly int TerrainDetailArrayID = Shader.PropertyToID("_TerrainDetailArray");
        private static readonly int DetailNoiseTexID = Shader.PropertyToID("_DetailNoiseTexture");
        private static readonly int HeightmapTexID = Shader.PropertyToID("_HeightmapTexture");
        private static readonly int NormalMapTexID = Shader.PropertyToID("_NormalMapTexture");

        public Texture2D ProvinceTerrainTexture => provinceTerrainTexture;
        public Texture2D TerrainTypeTexture => terrainTypeTexture;
        public Texture2DArray TerrainDetailArray => terrainDetailArray;
        public Texture2D HeightmapTexture => heightmapTexture;
        public Texture2D NormalMapTexture => normalMapTexture;

        public VisualTextureSet(int width, int height, int normalWidth, int normalHeight, bool logCreation = true)
        {
            this.mapWidth = width;
            this.mapHeight = height;
            this.normalMapWidth = normalWidth;
            this.normalMapHeight = normalHeight;
            this.logCreation = logCreation;
        }

        /// <summary>
        /// Create all visual textures
        /// </summary>
        public void CreateTextures()
        {
            CreateProvinceTerrainTexture();
            // NOTE: TerrainTypeTexture generated AFTER terrain.bmp is loaded
            // via GenerateTerrainTypeTexture() called by TerrainBitmapLoader
            LoadTerrainDetailArray();
            GenerateDetailNoiseTexture();
            CreateHeightmapTexture();
            CreateNormalMapTexture();
        }

        /// <summary>
        /// Create province terrain texture in RGBA32 format
        /// </summary>
        private void CreateProvinceTerrainTexture()
        {
            provinceTerrainTexture = new Texture2D(mapWidth, mapHeight, TextureFormat.RGBA32, false);
            provinceTerrainTexture.name = "ProvinceTerrain_Texture";

            // Initialize with default land color
            var pixels = new Color32[mapWidth * mapHeight];
            Color32 landColor = new Color32(139, 125, 107, 255);
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = landColor;
            }

            provinceTerrainTexture.SetPixels32(pixels);
            provinceTerrainTexture.Apply(false);

            if (logCreation)
            {
                ArchonLogger.Log($"VisualTextureSet: Created Province Terrain texture {mapWidth}x{mapHeight} RGBA32", "map_initialization");
            }
        }

        /// <summary>
        /// Create heightmap texture in R8 format
        /// Uses bilinear filtering for smooth height interpolation
        /// </summary>
        private void CreateHeightmapTexture()
        {
            heightmapTexture = new Texture2D(mapWidth, mapHeight, TextureFormat.R8, false);
            heightmapTexture.name = "Heightmap_Texture";
            heightmapTexture.filterMode = FilterMode.Bilinear;
            heightmapTexture.wrapMode = TextureWrapMode.Clamp;
            heightmapTexture.anisoLevel = 0;

            // Initialize with mid-height (sea level)
            var pixels = new Color[mapWidth * mapHeight];
            Color midHeight = new Color(0.5f, 0, 0, 1);
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = midHeight;
            }

            heightmapTexture.SetPixels(pixels);
            heightmapTexture.Apply(false);

            if (logCreation)
            {
                ArchonLogger.Log($"VisualTextureSet: Created Heightmap texture {mapWidth}x{mapHeight} R8 (bilinear)", "map_initialization");
            }
        }

        /// <summary>
        /// Create normal map texture in RGB24 format
        /// Half resolution (2816×1024) for performance
        /// Uses bilinear filtering for smooth normal interpolation
        /// </summary>
        private void CreateNormalMapTexture()
        {
            normalMapTexture = new Texture2D(normalMapWidth, normalMapHeight, TextureFormat.RGB24, false);
            normalMapTexture.name = "NormalMap_Texture";
            normalMapTexture.filterMode = FilterMode.Bilinear;
            normalMapTexture.wrapMode = TextureWrapMode.Clamp;
            normalMapTexture.anisoLevel = 0;

            // Initialize with default "up" normal (0, 0, 1) encoded as RGB (128, 128, 255)
            var pixels = new Color32[normalMapWidth * normalMapHeight];
            Color32 defaultNormal = new Color32(128, 128, 255, 255);
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = defaultNormal;
            }

            normalMapTexture.SetPixels32(pixels);
            normalMapTexture.Apply(false);

            if (logCreation)
            {
                ArchonLogger.Log($"VisualTextureSet: Created Normal Map texture {normalMapWidth}x{normalMapHeight} RGB24 (bilinear)", "map_initialization");
            }
        }

        /// <summary>
        /// Load terrain detail texture array from Assets/Data/textures/terrain_detail/
        /// </summary>
        private void LoadTerrainDetailArray()
        {
            terrainDetailArray = Loading.DetailTextureArrayLoader.LoadDetailTextureArray(
                textureSize: 512,  // 512x512 per detail texture
                logProgress: logCreation
            );

            if (logCreation && terrainDetailArray != null)
            {
                ArchonLogger.Log($"VisualTextureSet: Loaded terrain detail array {terrainDetailArray.width}x{terrainDetailArray.height} with {terrainDetailArray.depth} layers", "map_initialization");
            }
        }

        /// <summary>
        /// Generate noise texture for anti-tiling (Inigo Quilez method)
        /// </summary>
        private void GenerateDetailNoiseTexture()
        {
            detailNoiseTexture = Loading.NoiseTextureGenerator.GenerateNoiseTexture(
                size: 256,  // 256x256 tileable noise
                logProgress: logCreation
            );

            if (logCreation && detailNoiseTexture != null)
            {
                ArchonLogger.Log($"VisualTextureSet: Generated detail noise texture {detailNoiseTexture.width}x{detailNoiseTexture.height} R8", "map_initialization");
            }
        }

        /// <summary>
        /// Generate terrain type texture from terrain color texture
        /// Must be called AFTER terrain.bmp is loaded into provinceTerrainTexture
        /// </summary>
        public void GenerateTerrainTypeTexture()
        {
            if (provinceTerrainTexture == null)
            {
                ArchonLogger.LogError("VisualTextureSet: Cannot generate terrain type texture - terrain texture not loaded", "map_initialization");
                return;
            }

            terrainTypeTexture = Loading.TerrainTypeTextureGenerator.GenerateTerrainTypeTexture(
                provinceTerrainTexture,
                logCreation
            );

            if (logCreation && terrainTypeTexture != null)
            {
                ArchonLogger.Log($"VisualTextureSet: Generated terrain type texture {terrainTypeTexture.width}x{terrainTypeTexture.height} R8", "map_initialization");
            }
        }

        /// <summary>
        /// Bind visual textures to material
        /// </summary>
        public void BindToMaterial(Material material)
        {
            if (material == null) return;

            material.SetTexture(ProvinceTerrainTexID, provinceTerrainTexture);
            material.SetTexture(TerrainTypeTexID, terrainTypeTexture);
            material.SetTexture(TerrainDetailArrayID, terrainDetailArray);
            material.SetTexture(DetailNoiseTexID, detailNoiseTexture);
            material.SetTexture(HeightmapTexID, heightmapTexture);
            material.SetTexture(NormalMapTexID, normalMapTexture);

            if (logCreation)
            {
                var retrievedTexture = material.GetTexture(ProvinceTerrainTexID);
                if (retrievedTexture == provinceTerrainTexture)
                {
                    ArchonLogger.Log("VisualTextureSet: Successfully verified terrain texture binding", "map_initialization");
                }
                else
                {
                    ArchonLogger.LogError($"VisualTextureSet: Terrain texture binding FAILED", "map_initialization");
                }
            }
        }

        /// <summary>
        /// Generate normal map from heightmap using GPU compute shader
        /// Should be called after heightmap is populated
        /// </summary>
        public void GenerateNormalMapFromHeightmap(float heightScale = 10.0f, bool logProgress = false)
        {
            if (heightmapTexture == null)
            {
                ArchonLogger.LogError("VisualTextureSet: Cannot generate normal map - heightmap texture is null", "map_initialization");
                return;
            }

            if (normalMapTexture == null)
            {
                ArchonLogger.LogError("VisualTextureSet: Cannot generate normal map - normal map texture is null", "map_initialization");
                return;
            }

            // Create normal map generator
            var normalMapGenerator = new NormalMapGenerator();

            // Generate normal map from heightmap on GPU
            normalMapGenerator.GenerateNormalMap(
                heightmapTexture,
                normalMapTexture,
                heightScale,
                logProgress || logCreation
            );

            if (logCreation)
            {
                ArchonLogger.Log($"VisualTextureSet: Generated normal map from heightmap (scale: {heightScale})", "map_initialization");
            }
        }

        /// <summary>
        /// Apply texture changes (call after batch updates)
        /// </summary>
        public void ApplyChanges()
        {
            provinceTerrainTexture.Apply(false);
            // Heightmap and normal map typically populated once during loading
        }

        /// <summary>
        /// Release all textures
        /// </summary>
        public void Release()
        {
            if (provinceTerrainTexture != null) Object.DestroyImmediate(provinceTerrainTexture);
            if (terrainTypeTexture != null) Object.DestroyImmediate(terrainTypeTexture);
            if (terrainDetailArray != null) Object.DestroyImmediate(terrainDetailArray);
            if (detailNoiseTexture != null) Object.DestroyImmediate(detailNoiseTexture);
            if (heightmapTexture != null) Object.DestroyImmediate(heightmapTexture);
            if (normalMapTexture != null) Object.DestroyImmediate(normalMapTexture);
        }
    }
}
