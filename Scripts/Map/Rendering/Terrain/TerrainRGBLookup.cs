using System.Collections.Generic;
using UnityEngine;
using Core.Loaders;
using Newtonsoft.Json.Linq;
using Utils;

namespace Map.Rendering.Terrain
{
    /// <summary>
    /// ENGINE: Loads and caches terrain_rgb.json5 mappings
    /// Provides fast RGB → Terrain Type Index lookups
    /// Terrain type indices are determined by ORDER in terrain_rgb.json5 (0, 1, 2, 3...)
    /// </summary>
    public class TerrainRGBLookup
    {
        private Dictionary<(byte r, byte g, byte b), uint> rgbToTerrainType;
        private Dictionary<uint, string> terrainTypeToName;
        private uint terrainCount;
        private bool isInitialized = false;

        /// <summary>
        /// Load terrain_rgb.json5 and build lookup tables
        /// Must be called before using any lookup methods
        /// </summary>
        public bool Initialize(bool logProgress = true)
        {
            if (isInitialized)
            {
                return true;
            }

            rgbToTerrainType = new Dictionary<(byte r, byte g, byte b), uint>();
            terrainTypeToName = new Dictionary<uint, string>();

            try
            {
                // Load RGB mappings from terrain_rgb.json5
                string terrainRgbPath = System.IO.Path.Combine(Application.dataPath, "Data", "map", "terrain_rgb.json5");
                if (!System.IO.File.Exists(terrainRgbPath))
                {
                    ArchonLogger.LogError($"TerrainRGBLookup: terrain_rgb.json5 not found at {terrainRgbPath}", "map_rendering");
                    return false;
                }

                JObject terrainRgbData = Json5Loader.LoadJson5File(terrainRgbPath);

                if (terrainRgbData == null)
                {
                    ArchonLogger.LogError("TerrainRGBLookup: Failed to parse terrain_rgb.json5", "map_rendering");
                    return false;
                }

                // Assign terrain type indices based on ORDER in terrain_rgb.json5
                uint terrainTypeIndex = 0;
                foreach (var terrainProperty in terrainRgbData.Properties())
                {
                    string terrainName = terrainProperty.Name;

                    if (terrainProperty.Value is JObject terrainObj)
                    {
                        var colorArray = Json5Loader.GetIntArray(terrainObj, "color");
                        string typeName = terrainObj["type"]?.ToString();

                        if (colorArray.Count >= 3)
                        {
                            byte r = (byte)colorArray[0];
                            byte g = (byte)colorArray[1];
                            byte b = (byte)colorArray[2];

                            // Only add if RGB doesn't already exist (handle duplicates like savannah/drylands both at 0,0,0)
                            if (!rgbToTerrainType.ContainsKey((r, g, b)))
                            {
                                rgbToTerrainType[(r, g, b)] = terrainTypeIndex;
                                terrainTypeToName[terrainTypeIndex] = terrainName;

                                if (logProgress)
                                {
                                    ArchonLogger.Log($"TerrainRGBLookup: Terrain mapping - RGB({r},{g},{b}) → T{terrainTypeIndex} ({terrainName}, type={typeName})", "map_rendering");
                                }

                                terrainTypeIndex++;
                            }
                            else if (logProgress)
                            {
                                ArchonLogger.LogWarning($"TerrainRGBLookup: Duplicate RGB({r},{g},{b}) for {terrainName} (already mapped)", "map_rendering");
                            }
                        }
                    }
                }

                terrainCount = terrainTypeIndex;
                isInitialized = true;

                if (logProgress)
                {
                    ArchonLogger.Log($"TerrainRGBLookup: Built RGB→Terrain lookup with {rgbToTerrainType.Count} mappings", "map_rendering");
                }

                return true;
            }
            catch (System.Exception e)
            {
                ArchonLogger.LogError($"TerrainRGBLookup: Failed to build RGB lookup: {e.Message}\nStack trace: {e.StackTrace}", "map_rendering");
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
