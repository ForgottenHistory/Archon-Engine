using UnityEngine;
using Map.Loading.Data;
using Utils;

namespace Map.Loading
{
    /// <summary>
    /// Generates terrain type texture (R8) from terrain color texture (RGBA32)
    /// Maps terrain colors back to terrain type indices using TerrainColorMapper
    /// Purpose: Enables terrain splatting for detail texture selection
    ///
    /// Architecture:
    /// - Input: ProvinceTerrainTexture (RGBA32) with terrain colors
    /// - Output: TerrainTypeTexture (R8) with terrain type indices (0-255)
    /// - Method: Reverse color lookup via TerrainColorMapper
    /// </summary>
    public static class TerrainTypeTextureGenerator
    {
        /// <summary>
        /// Generate terrain type texture from terrain color texture
        /// Uses reverse lookup to map colors → terrain type indices
        /// </summary>
        /// <param name="terrainColorTexture">Source terrain texture (RGBA32)</param>
        /// <param name="logProgress">Enable progress logging</param>
        /// <returns>Terrain type texture (R8 format, 0-255 terrain indices)</returns>
        public static Texture2D GenerateTerrainTypeTexture(Texture2D terrainColorTexture, bool logProgress = true)
        {
            if (terrainColorTexture == null)
            {
                ArchonLogger.LogError("TerrainTypeTextureGenerator: Cannot generate - terrain color texture is null", "map_initialization");
                return null;
            }

            int width = terrainColorTexture.width;
            int height = terrainColorTexture.height;

            if (logProgress)
            {
                ArchonLogger.Log($"TerrainTypeTextureGenerator: Starting generation {width}x{height}", "map_initialization");
            }

            // Create R8 texture for terrain type indices with EXPLICIT GraphicsFormat
            // CRITICAL: Must use explicit GraphicsFormat.R8_UNorm to avoid Unity auto-conversion
            // Using TextureFormat.R8 can result in R8G8B8A8_SRGB on some platforms
            var terrainTypeTexture = new Texture2D(
                width,
                height,
                UnityEngine.Experimental.Rendering.GraphicsFormat.R8_UNorm,
                UnityEngine.Experimental.Rendering.TextureCreationFlags.None
            );
            terrainTypeTexture.name = "TerrainType_Texture";
            terrainTypeTexture.filterMode = FilterMode.Point;  // No interpolation for indices
            terrainTypeTexture.wrapMode = TextureWrapMode.Clamp;
            terrainTypeTexture.anisoLevel = 0;

            // Get terrain colors from source texture
            Color32[] terrainColors = terrainColorTexture.GetPixels32();

            // Allocate output array (R8 = 1 byte per pixel)
            // CRITICAL: For R8_UNorm, use byte array not Color array
            byte[] terrainTypePixels = new byte[width * height];

            // Build reverse lookup map (color → terrain type index)
            var colorToIndexMap = BuildColorToIndexMap();

            int matchedPixels = 0;
            int unmatchedPixels = 0;
            byte noTerrainMarker = 255;  // 255 = no terrain detail (ocean, unknown colors)

            // Convert terrain colors to terrain type indices
            for (int i = 0; i < terrainColors.Length; i++)
            {
                Color32 color = terrainColors[i];

                // Lookup terrain type index for this color
                if (colorToIndexMap.TryGetValue(ColorToKey(color), out byte terrainType))
                {
                    // Store terrain type as raw byte (0-255)
                    terrainTypePixels[i] = terrainType;
                    matchedPixels++;
                }
                else
                {
                    // Unknown color (ocean, etc.) - mark as "no terrain detail"
                    terrainTypePixels[i] = noTerrainMarker;
                    unmatchedPixels++;
                }
            }

            // Upload raw byte data to R8 texture
            terrainTypeTexture.SetPixelData(terrainTypePixels, 0);
            terrainTypeTexture.Apply(false);

            if (logProgress)
            {
                ArchonLogger.Log($"TerrainTypeTextureGenerator: Complete - Matched: {matchedPixels}, Unmatched: {unmatchedPixels} (marked as no-terrain={noTerrainMarker})", "map_initialization");

                // Sample some pixels to verify
                byte type1 = terrainTypePixels[width * height / 4];
                byte type2 = terrainTypePixels[width * height / 2];
                byte type3 = terrainTypePixels[width * height * 3 / 4];
                ArchonLogger.Log($"TerrainTypeTextureGenerator: Samples - Type [{type1}] [{type2}] [{type3}]", "map_initialization");
            }

            return terrainTypeTexture;
        }

        /// <summary>
        /// Build reverse lookup map: Color → Terrain Type Index
        /// Uses TerrainColorMapper as source of truth
        /// </summary>
        private static System.Collections.Generic.Dictionary<int, byte> BuildColorToIndexMap()
        {
            var map = new System.Collections.Generic.Dictionary<int, byte>();

            // Iterate all registered terrain indices
            foreach (byte index in TerrainColorMapper.GetRegisteredIndices())
            {
                Color32 color = TerrainColorMapper.GetTerrainColor(index);
                int key = ColorToKey(color);

                // Store mapping (color → index)
                if (!map.ContainsKey(key))
                {
                    map[key] = index;
                }
            }

            return map;
        }

        /// <summary>
        /// Convert Color32 to integer key for dictionary lookup
        /// Packs RGB into single int (ignore alpha)
        /// </summary>
        private static int ColorToKey(Color32 color)
        {
            return (color.r << 16) | (color.g << 8) | color.b;
        }
    }
}
