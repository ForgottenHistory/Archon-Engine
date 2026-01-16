using UnityEngine;
using System.Collections.Generic;
using System.IO;
using Core.Loaders;
using Newtonsoft.Json.Linq;
using Utils;

namespace Map.Loading.Data
{
    /// <summary>
    /// Centralized terrain index to color mapping for indexed terrain bitmaps.
    /// Loads terrain colors from terrain.json5 at runtime.
    /// Uses same JSON parsing as TerrainLoader to guarantee identical index assignment.
    /// Maps: terrain name ↔ index ↔ RGB color
    /// </summary>
    public static class TerrainColorMapper
    {
        private static Dictionary<byte, Color32> terrainColors;      // index → color
        private static Dictionary<int, byte> colorToIndex;           // RGB packed → index
        private static Dictionary<string, byte> nameToIndex;         // name → index
        private static Dictionary<byte, string> indexToName;         // index → name
        private static Color32 defaultTerrainColor = new Color32(86, 124, 27, 255);
        private static bool isInitialized = false;

        /// <summary>
        /// Initialize terrain colors from terrain.json5 file.
        /// Uses Json5Loader (same as TerrainLoader) to guarantee identical iteration order.
        /// </summary>
        public static void Initialize(string dataDirectory)
        {
            terrainColors = new Dictionary<byte, Color32>();
            colorToIndex = new Dictionary<int, byte>();
            nameToIndex = new Dictionary<string, byte>();
            indexToName = new Dictionary<byte, string>();

            string terrainPath = Path.Combine(dataDirectory, "map", "terrain.json5");

            if (!File.Exists(terrainPath))
            {
                ArchonLogger.LogWarning($"TerrainColorMapper: terrain.json5 not found at {terrainPath}", "map_initialization");
                isInitialized = true;
                return;
            }

            try
            {
                LoadTerrainJson5(terrainPath);
                ArchonLogger.Log($"TerrainColorMapper: Loaded {terrainColors.Count} terrain colors from terrain.json5", "map_initialization");
            }
            catch (System.Exception e)
            {
                ArchonLogger.LogError($"TerrainColorMapper: Failed to parse terrain.json5: {e.Message}", "map_initialization");
            }

            isInitialized = true;
        }

        /// <summary>
        /// Parse terrain.json5 using Json5Loader - identical to TerrainLoader.
        /// JObject.Properties() preserves JSON key order, ensuring consistent index assignment.
        /// </summary>
        private static void LoadTerrainJson5(string filePath)
        {
            var json = Json5Loader.LoadJson5File(filePath);
            var categories = json["categories"] as JObject;

            if (categories == null)
            {
                ArchonLogger.LogWarning("TerrainColorMapper: No 'categories' section found in terrain.json5", "map_initialization");
                return;
            }

            byte terrainId = 0;
            foreach (var property in categories.Properties())
            {
                string key = property.Name;
                var terrainObj = property.Value as JObject;

                if (terrainObj == null)
                    continue;

                // Parse color array
                var colorArray = terrainObj["color"] as JArray;
                if (colorArray == null || colorArray.Count < 3)
                {
                    ArchonLogger.LogWarning($"TerrainColorMapper: No color defined for terrain '{key}'", "map_initialization");
                    terrainId++;
                    continue;
                }

                byte r = (byte)colorArray[0].Value<int>();
                byte g = (byte)colorArray[1].Value<int>();
                byte b = (byte)colorArray[2].Value<int>();

                Color32 color = new Color32(r, g, b, 255);

                // Build all lookups
                terrainColors[terrainId] = color;
                nameToIndex[key] = terrainId;
                indexToName[terrainId] = key;

                int packedColor = (r << 16) | (g << 8) | b;
                colorToIndex[packedColor] = terrainId;

                terrainId++;
            }

            // Set default to first terrain
            if (terrainColors.Count > 0 && terrainColors.TryGetValue(0, out Color32 first))
            {
                defaultTerrainColor = first;
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
        /// Try to get terrain index from terrain name (e.g., "ocean", "grasslands")
        /// </summary>
        public static bool TryGetTerrainIndexByName(string terrainName, out byte index)
        {
            EnsureInitialized();
            return nameToIndex.TryGetValue(terrainName, out index);
        }

        /// <summary>
        /// Try to get terrain name from terrain index
        /// </summary>
        public static bool TryGetTerrainName(byte terrainIndex, out string name)
        {
            EnsureInitialized();
            return indexToName.TryGetValue(terrainIndex, out name);
        }

        /// <summary>
        /// Get terrain name from index, or "unknown" if not found
        /// </summary>
        public static string GetTerrainName(byte terrainIndex)
        {
            EnsureInitialized();
            return indexToName.TryGetValue(terrainIndex, out string name) ? name : "unknown";
        }

        /// <summary>
        /// Get default terrain color
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
        /// Check if initialized - logs warning if not
        /// </summary>
        private static void EnsureInitialized()
        {
            if (!isInitialized)
            {
                ArchonLogger.LogWarning("TerrainColorMapper: Not initialized! Call Initialize() first.", "map_initialization");
                // Initialize empty dictionaries to prevent null reference
                terrainColors ??= new Dictionary<byte, Color32>();
                colorToIndex ??= new Dictionary<int, byte>();
                nameToIndex ??= new Dictionary<string, byte>();
                indexToName ??= new Dictionary<byte, string>();
                isInitialized = true;
            }
        }
    }
}
