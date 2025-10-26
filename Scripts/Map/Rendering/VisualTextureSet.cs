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
        private Texture2D heightmapTexture;
        private Texture2D normalMapTexture;

        // Shader property IDs
        private static readonly int ProvinceTerrainTexID = Shader.PropertyToID("_ProvinceTerrainTexture");
        private static readonly int HeightmapTexID = Shader.PropertyToID("_HeightmapTexture");
        private static readonly int NormalMapTexID = Shader.PropertyToID("_NormalMapTexture");

        public Texture2D ProvinceTerrainTexture => provinceTerrainTexture;
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
        /// Bind visual textures to material
        /// </summary>
        public void BindToMaterial(Material material)
        {
            if (material == null) return;

            material.SetTexture(ProvinceTerrainTexID, provinceTerrainTexture);
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
            if (heightmapTexture != null) Object.DestroyImmediate(heightmapTexture);
            if (normalMapTexture != null) Object.DestroyImmediate(normalMapTexture);
        }
    }
}
