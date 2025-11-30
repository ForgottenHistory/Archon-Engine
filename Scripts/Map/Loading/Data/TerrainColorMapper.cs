using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Utils;

namespace Map.Loading.Data
{
    /// <summary>
    /// Centralized terrain index to color mapping for indexed terrain bitmaps
    /// Loads terrain colors from terrain_rgb.json5 at runtime
    /// Maps terrain names to indices and RGB colors
    /// </summary>
    public static class TerrainColorMapper
    {
        private static Dictionary<byte, Color32> terrainColors;
        private static Dictionary<int, byte> colorToIndex;  // RGB packed -> index
        private static Color32 defaultTerrainColor = new Color32(86, 124, 27, 255); // grasslands
        private static bool isInitialized = false;

        /// <summary>
        /// Initialize terrain colors from terrain_rgb.json5 file
        /// Must be called before using any terrain color lookups
        /// </summary>
        public static void Initialize(string dataDirectory)
        {
            terrainColors = new Dictionary<byte, Color32>();
            colorToIndex = new Dictionary<int, byte>();

            string terrainRgbPath = Path.Combine(dataDirectory, "map", "terrain_rgb.json5");

            if (!File.Exists(terrainRgbPath))
            {
                ArchonLogger.LogWarning($"TerrainColorMapper: terrain_rgb.json5 not found at {terrainRgbPath}, using defaults", "map_initialization");
                LoadDefaultColors();
                isInitialized = true;
                return;
            }

            try
            {
                string json5Content = File.ReadAllText(terrainRgbPath);
                ParseTerrainRgbJson5(json5Content);
                ArchonLogger.Log($"TerrainColorMapper: Loaded {terrainColors.Count} terrain colors from {terrainRgbPath}", "map_initialization");
            }
            catch (System.Exception e)
            {
                ArchonLogger.LogError($"TerrainColorMapper: Failed to parse terrain_rgb.json5: {e.Message}", "map_initialization");
                LoadDefaultColors();
            }

            isInitialized = true;
        }

        /// <summary>
        /// Parse terrain_rgb.json5 content and populate color mappings
        /// Simple regex-based parser for the specific JSON5 format
        /// </summary>
        private static void ParseTerrainRgbJson5(string content)
        {
            // Match pattern: name: { type: "...", color: [r, g, b] }
            var regex = new Regex(@"(\w+):\s*\{\s*type:\s*""[^""]*"",\s*color:\s*\[(\d+),\s*(\d+),\s*(\d+)\]\s*\}");
            var matches = regex.Matches(content);

            byte index = 0;
            foreach (Match match in matches)
            {
                string name = match.Groups[1].Value;
                byte r = byte.Parse(match.Groups[2].Value);
                byte g = byte.Parse(match.Groups[3].Value);
                byte b = byte.Parse(match.Groups[4].Value);

                Color32 color = new Color32(r, g, b, 255);
                terrainColors[index] = color;

                // Build reverse lookup
                int packedColor = (r << 16) | (g << 8) | b;
                colorToIndex[packedColor] = index;

                index++;
            }

            // Set default to first terrain (grasslands)
            if (terrainColors.Count > 0 && terrainColors.TryGetValue(0, out Color32 first))
            {
                defaultTerrainColor = first;
            }
        }

        /// <summary>
        /// Load hardcoded default colors as fallback
        /// </summary>
        private static void LoadDefaultColors()
        {
            terrainColors = new Dictionary<byte, Color32>
            {
                [0] = new Color32(86, 124, 27, 255),      // grasslands
                [1] = new Color32(0, 86, 6, 255),         // hills
                [2] = new Color32(112, 74, 31, 255),      // desert_mountain
                [3] = new Color32(206, 169, 99, 255),     // desert
                [4] = new Color32(200, 214, 107, 255),    // plains
                [5] = new Color32(65, 42, 17, 255),       // mountain
                [6] = new Color32(75, 147, 174, 255),     // marsh
                [7] = new Color32(42, 55, 22, 255),       // forest
                [8] = new Color32(8, 31, 130, 255),       // ocean
                [9] = new Color32(255, 255, 255, 255),    // snow
                [10] = new Color32(55, 90, 220, 255),     // inland_ocean
                [11] = new Color32(203, 191, 103, 255),   // coastal_desert
                [12] = new Color32(180, 160, 80, 255),    // savannah
                [13] = new Color32(23, 23, 23, 255),      // highlands
                [14] = new Color32(254, 254, 254, 255),   // jungle
            };

            // Build reverse lookup
            colorToIndex = new Dictionary<int, byte>();
            foreach (var kvp in terrainColors)
            {
                int packed = (kvp.Value.r << 16) | (kvp.Value.g << 8) | kvp.Value.b;
                colorToIndex[packed] = kvp.Key;
            }
        }

        /// <summary>
        /// Get terrain color for a given terrain index
        /// </summary>
        public static Color32 GetTerrainColor(byte terrainIndex)
        {
            EnsureInitialized();
            return terrainColors.TryGetValue(terrainIndex, out Color32 color) ? color : defaultTerrainColor;
        }

        /// <summary>
        /// Try to get terrain color for a given terrain index
        /// </summary>
        public static bool TryGetTerrainColor(byte terrainIndex, out Color32 color)
        {
            EnsureInitialized();
            return terrainColors.TryGetValue(terrainIndex, out color);
        }

        /// <summary>
        /// Try to get terrain index from RGB color
        /// </summary>
        public static bool TryGetTerrainIndex(Color32 color, out byte index)
        {
            EnsureInitialized();
            int packed = (color.r << 16) | (color.g << 8) | color.b;
            return colorToIndex.TryGetValue(packed, out index);
        }

        /// <summary>
        /// Get default terrain color (grasslands)
        /// </summary>
        public static Color32 GetDefaultTerrainColor()
        {
            EnsureInitialized();
            return defaultTerrainColor;
        }

        /// <summary>
        /// Get all registered terrain indices
        /// </summary>
        public static IEnumerable<byte> GetRegisteredIndices()
        {
            EnsureInitialized();
            return terrainColors.Keys;
        }

        /// <summary>
        /// Get total number of registered terrain types
        /// </summary>
        public static int GetTerrainTypeCount()
        {
            EnsureInitialized();
            return terrainColors.Count;
        }

        /// <summary>
        /// Check if initialized, use defaults if not
        /// </summary>
        private static void EnsureInitialized()
        {
            if (!isInitialized)
            {
                ArchonLogger.LogWarning("TerrainColorMapper: Not initialized, using default colors", "map_initialization");
                LoadDefaultColors();
                isInitialized = true;
            }
        }
    }
}
