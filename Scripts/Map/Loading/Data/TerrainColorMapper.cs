using UnityEngine;
using System.Collections.Generic;

namespace Map.Loading.Data
{
    /// <summary>
    /// Centralized terrain index to color mapping for indexed terrain bitmaps
    /// Maps 8-bit terrain indices to RGB colors based on terrain.txt definitions
    /// </summary>
    public static class TerrainColorMapper
    {
        /// <summary>
        /// Terrain color mappings from terrain.txt (EU4 format)
        /// Index values correspond to palette indices in 8-bit terrain.bmp
        /// </summary>
        private static readonly Dictionary<byte, Color32> terrainColors = new Dictionary<byte, Color32>
        {
            // Basic terrain types
            [0] = new Color32(50, 180, 50, 255),      // grasslands
            [1] = new Color32(160, 140, 120, 255),    // hills
            [2] = new Color32(120, 120, 120, 255),    // desert_mountain (mountain)
            [3] = new Color32(255, 230, 180, 255),    // desert
            [4] = new Color32(80, 160, 80, 255),      // plains (grasslands type)
            [5] = new Color32(70, 150, 70, 255),      // terrain_5 (grasslands type)
            [6] = new Color32(100, 100, 100, 255),    // mountain
            [7] = new Color32(200, 180, 140, 255),    // desert_mountain_low (desert type)
            [8] = new Color32(140, 120, 100, 255),    // terrain_8 (hills type)
            [9] = new Color32(60, 100, 60, 255),      // marsh

            // Farmlands
            [10] = new Color32(139, 125, 107, 255),   // terrain_10 (farmlands)
            [11] = new Color32(149, 135, 117, 255),   // terrain_11 (farmlands)
            [21] = new Color32(159, 145, 127, 255),   // terrain_21 (farmlands)

            // Forests
            [12] = new Color32(0, 120, 0, 255),       // forest_12
            [13] = new Color32(10, 130, 10, 255),     // forest_13
            [14] = new Color32(20, 140, 20, 255),     // forest_14

            // Water
            [15] = new Color32(0, 100, 200, 255),     // ocean
            [17] = new Color32(20, 120, 220, 255),    // inland_ocean_17
            [35] = new Color32(100, 180, 255, 255),   // coastline

            // Snow and cold
            [16] = new Color32(200, 200, 255, 255),   // snow (mountain type)

            // Arid regions
            [19] = new Color32(220, 200, 160, 255),   // coastal_desert_18 (index 19 in file)
            [20] = new Color32(180, 160, 100, 255),   // savannah
            [22] = new Color32(200, 180, 120, 255),   // drylands
            [23] = new Color32(140, 130, 110, 255),   // highlands
            [24] = new Color32(160, 150, 130, 255),   // dry_highlands

            // Special terrains
            [254] = new Color32(0, 80, 0, 255),       // jungle
            [255] = new Color32(40, 100, 40, 255),    // woods
        };

        /// <summary>
        /// Default terrain color for unknown indices (farmlands)
        /// </summary>
        private static readonly Color32 defaultTerrainColor = new Color32(139, 125, 107, 255);

        /// <summary>
        /// Get terrain color for a given terrain index
        /// </summary>
        /// <param name="terrainIndex">8-bit terrain index from bitmap</param>
        /// <returns>Terrain color (returns default farmlands color if index not found)</returns>
        public static Color32 GetTerrainColor(byte terrainIndex)
        {
            return terrainColors.TryGetValue(terrainIndex, out Color32 color) ? color : defaultTerrainColor;
        }

        /// <summary>
        /// Try to get terrain color for a given terrain index
        /// </summary>
        /// <param name="terrainIndex">8-bit terrain index from bitmap</param>
        /// <param name="color">Output terrain color</param>
        /// <returns>True if terrain index was found, false otherwise</returns>
        public static bool TryGetTerrainColor(byte terrainIndex, out Color32 color)
        {
            return terrainColors.TryGetValue(terrainIndex, out color);
        }

        /// <summary>
        /// Get default terrain color (farmlands)
        /// Used when terrain index is not found or for fallback pixels
        /// </summary>
        public static Color32 GetDefaultTerrainColor()
        {
            return defaultTerrainColor;
        }

        /// <summary>
        /// Get all registered terrain indices
        /// Useful for validation and debugging
        /// </summary>
        public static IEnumerable<byte> GetRegisteredIndices()
        {
            return terrainColors.Keys;
        }

        /// <summary>
        /// Get total number of registered terrain types
        /// </summary>
        public static int GetTerrainTypeCount()
        {
            return terrainColors.Count;
        }
    }
}
