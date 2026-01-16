using System.Collections.Generic;
using UnityEngine;
using Core;
using Core.Loaders;
using Newtonsoft.Json.Linq;
using Utils;

namespace Map.Rendering.Terrain
{
    /// <summary>
    /// ENGINE: Loads terrain.json5 and provides fast RGB → Terrain Type Index lookups.
    /// Uses terrain.json5 as single source of truth (same as TerrainLoader and TerrainColorMapper).
    /// Terrain type indices are determined by ORDER in terrain.json5 categories section.
    /// </summary>
    public class TerrainRGBLookup
    {
        private Dictionary<(byte r, byte g, byte b), uint> rgbToTerrainType;
        private Dictionary<uint, string> terrainTypeToName;
        private Dictionary<uint, bool> terrainTypeOwnable; // is_water=true means not ownable
        private uint terrainCount;
        private bool isInitialized = false;

        /// <summary>
        /// Load terrain.json5 and build lookup tables.
        /// Uses same file and parsing as TerrainLoader to guarantee identical index assignment.
        /// </summary>
        public bool Initialize(string dataDirectory = null, bool logProgress = true)
        {
            if (isInitialized)
            {
                return true;
            }

            rgbToTerrainType = new Dictionary<(byte r, byte g, byte b), uint>();
            terrainTypeToName = new Dictionary<uint, string>();
            terrainTypeOwnable = new Dictionary<uint, bool>();

            try
            {
                if (string.IsNullOrEmpty(dataDirectory))
                {
                    dataDirectory = GameSettings.Instance?.DataDirectory
                        ?? System.IO.Path.Combine(Application.dataPath, "Data");
                }

                // Load from terrain.json5 (single source of truth)
                string terrainPath = System.IO.Path.Combine(dataDirectory, "map", "terrain.json5");
                if (!System.IO.File.Exists(terrainPath))
                {
                    ArchonLogger.LogError($"TerrainRGBLookup: terrain.json5 not found at {terrainPath}", "map_rendering");
                    return false;
                }

                JObject terrainData = Json5Loader.LoadJson5File(terrainPath);
                if (terrainData == null)
                {
                    ArchonLogger.LogError("TerrainRGBLookup: Failed to parse terrain.json5", "map_rendering");
                    return false;
                }

                // Parse categories section (same as TerrainLoader)
                var categories = terrainData["categories"] as JObject;
                if (categories == null)
                {
                    ArchonLogger.LogError("TerrainRGBLookup: No 'categories' section in terrain.json5", "map_rendering");
                    return false;
                }

                // Assign terrain type indices based on ORDER in categories
                uint terrainTypeIndex = 0;
                foreach (var property in categories.Properties())
                {
                    string terrainName = property.Name;
                    var terrainObj = property.Value as JObject;

                    if (terrainObj == null)
                        continue;

                    // Parse color array
                    var colorArray = terrainObj["color"] as JArray;
                    if (colorArray == null || colorArray.Count < 3)
                    {
                        ArchonLogger.LogWarning($"TerrainRGBLookup: No color for terrain '{terrainName}'", "map_rendering");
                        terrainTypeIndex++;
                        continue;
                    }

                    byte r = (byte)colorArray[0].Value<int>();
                    byte g = (byte)colorArray[1].Value<int>();
                    byte b = (byte)colorArray[2].Value<int>();

                    // Check explicit ownable field, fallback to !is_water
                    bool ownable = true;
                    if (terrainObj["ownable"] != null)
                    {
                        ownable = terrainObj["ownable"].Value<bool>();
                    }
                    else
                    {
                        bool isWater = terrainObj["is_water"]?.Value<bool>() ?? false;
                        ownable = !isWater;
                    }

                    if (!rgbToTerrainType.ContainsKey((r, g, b)))
                    {
                        rgbToTerrainType[(r, g, b)] = terrainTypeIndex;
                        terrainTypeToName[terrainTypeIndex] = terrainName;
                        terrainTypeOwnable[terrainTypeIndex] = ownable;

                        if (logProgress)
                        {
                            string ownableStr = ownable ? "" : ", ownable=false";
                            ArchonLogger.Log($"TerrainRGBLookup: RGB({r},{g},{b}) → T{terrainTypeIndex} ({terrainName}{ownableStr})", "map_rendering");
                        }
                    }
                    else if (logProgress)
                    {
                        ArchonLogger.LogWarning($"TerrainRGBLookup: Duplicate RGB({r},{g},{b}) for {terrainName}", "map_rendering");
                    }

                    terrainTypeIndex++;
                }

                terrainCount = terrainTypeIndex;
                isInitialized = true;

                if (logProgress)
                {
                    ArchonLogger.Log($"TerrainRGBLookup: Loaded {terrainCount} terrain types from terrain.json5", "map_rendering");
                }

                return true;
            }
            catch (System.Exception e)
            {
                ArchonLogger.LogError($"TerrainRGBLookup: Failed to initialize: {e.Message}", "map_rendering");
                return false;
            }
        }

        /// <summary>
        /// Get terrain type index for given RGB color
        /// Returns 0 (default terrain) if color not found
        /// </summary>
        public uint GetTerrainTypeIndex(byte r, byte g, byte b)
        {
            if (!isInitialized)
            {
                ArchonLogger.LogError("TerrainRGBLookup: Not initialized! Call Initialize() first.", "map_rendering");
                return 0;
            }

            if (rgbToTerrainType.TryGetValue((r, g, b), out uint terrainType))
            {
                return terrainType;
            }

            return 0; // Default to terrain 0 (grasslands) if unknown
        }

        /// <summary>
        /// Get terrain type name for given index (for debugging)
        /// </summary>
        public string GetTerrainTypeName(uint index)
        {
            if (!isInitialized)
            {
                return "uninitialized";
            }

            if (terrainTypeToName.TryGetValue(index, out string name))
            {
                return name;
            }

            return "unknown";
        }

        /// <summary>
        /// Get total number of terrain types defined
        /// </summary>
        public uint GetTerrainCount()
        {
            if (!isInitialized)
            {
                return 0;
            }

            return terrainCount;
        }

        /// <summary>
        /// Check if terrain type is ownable (can be colonized/owned)
        /// Returns true by default if terrain index not found
        /// </summary>
        public bool IsTerrainOwnable(uint terrainIndex)
        {
            if (!isInitialized)
            {
                return true; // Default to ownable if not initialized
            }

            if (terrainTypeOwnable.TryGetValue(terrainIndex, out bool ownable))
            {
                return ownable;
            }

            return true; // Default to ownable if terrain not defined
        }

        /// <summary>
        /// Check if terrain type is ownable by ushort terrain ID
        /// </summary>
        public bool IsTerrainOwnable(ushort terrainIndex)
        {
            return IsTerrainOwnable((uint)terrainIndex);
        }

        /// <summary>
        /// Get the RGB→Terrain dictionary (for bulk operations)
        /// </summary>
        public Dictionary<(byte r, byte g, byte b), uint> GetRGBToTerrainDictionary()
        {
            if (!isInitialized)
            {
                ArchonLogger.LogError("TerrainRGBLookup: Not initialized! Call Initialize() first.", "map_rendering");
                return null;
            }

            return rgbToTerrainType;
        }

        /// <summary>
        /// Check if lookup is initialized
        /// </summary>
        public bool IsInitialized => isInitialized;
    }
}
