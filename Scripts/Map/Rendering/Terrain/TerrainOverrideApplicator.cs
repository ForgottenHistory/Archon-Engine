using System.Collections.Generic;
using UnityEngine;
using Core.Loaders;
using Newtonsoft.Json.Linq;
using Utils;

namespace Map.Rendering.Terrain
{
    /// <summary>
    /// ENGINE: Applies terrain overrides from terrain.json5
    /// EU4 uses terrain_override arrays to force specific provinces to specific terrain types
    /// regardless of what the terrain.bmp shows
    /// </summary>
    public class TerrainOverrideApplicator
    {
        private bool logProgress;

        public TerrainOverrideApplicator(bool logProgress = true)
        {
            this.logProgress = logProgress;
        }

        /// <summary>
        /// Apply terrain overrides from terrain.json5 to terrain assignments
        /// Modifies terrainAssignments array in-place
        /// </summary>
        /// <param name="terrainAssignments">Array indexed by array position (0-provinceCount)</param>
        /// <param name="provinceIDs">Array of province IDs matching terrainAssignments indices</param>
        /// <param name="rgbLookup">TerrainRGBLookup for category→index mapping</param>
        public void ApplyOverrides(uint[] terrainAssignments, ushort[] provinceIDs, TerrainRGBLookup rgbLookup)
        {
            if (!rgbLookup.IsInitialized)
            {
                ArchonLogger.LogError("TerrainOverrideApplicator: TerrainRGBLookup not initialized!", "map_rendering");
                return;
            }

            try
            {
                // Build provinceID → array index lookup
                // terrainAssignments is indexed by array position (0-provinceCount), not province ID
                var provinceIDToIndex = new Dictionary<ushort, int>();
                for (int i = 0; i < provinceIDs.Length; i++)
                {
                    provinceIDToIndex[provinceIDs[i]] = i;
                }

                // Load terrain.json5 for overrides
                string terrainJsonPath = System.IO.Path.Combine(Application.dataPath, "Data", "map", "terrain.json5");

                if (!System.IO.File.Exists(terrainJsonPath))
                {
                    ArchonLogger.LogWarning($"TerrainOverrideApplicator: terrain.json5 not found at {terrainJsonPath}", "map_rendering");
                    return;
                }

                JObject terrainData = Json5Loader.LoadJson5File(terrainJsonPath);

                // Build terrain type name → index mapping from terrain_rgb.json5
                // Terrain indices = ORDER in terrain_rgb.json5
                var categoryToIndex = BuildCategoryToIndexMapping();

                if (categoryToIndex == null)
                {
                    ArchonLogger.LogError("TerrainOverrideApplicator: Failed to build category→index mapping", "map_rendering");
                    return;
                }

                if (logProgress)
                {
                    ArchonLogger.Log($"TerrainOverrideApplicator: Loaded {categoryToIndex.Count} terrain type mappings from terrain_rgb.json5", "map_rendering");
                }

                // Parse "categories" section for terrain_override arrays
                JObject categoriesSection = Json5Loader.GetObject(terrainData, "categories");
                if (categoriesSection == null)
                {
                    ArchonLogger.LogWarning("TerrainOverrideApplicator: No 'categories' section in terrain.json5", "map_rendering");
                    return;
                }

                int overridesApplied = 0;

                foreach (var categoryProperty in categoriesSection.Properties())
                {
                    string categoryName = categoryProperty.Name;

                    if (categoryProperty.Value is JObject categoryObj)
                    {
                        // Get terrain_override array
                        var overrideProvinces = Json5Loader.GetIntArray(categoryObj, "terrain_override");

                        if (overrideProvinces.Count > 0)
                        {
                            // Find the terrain index for this category
                            // Look for matching entry in categoryToIndex
                            uint terrainIndex = 0;
                            if (categoryToIndex.TryGetValue(categoryName, out terrainIndex))
                            {
                                // Apply overrides for all provinces in the list
                                foreach (int provinceID in overrideProvinces)
                                {
                                    // Convert province ID to array index
                                    ushort pid = (ushort)provinceID;
                                    if (provinceIDToIndex.TryGetValue(pid, out int arrayIndex))
                                    {
                                        terrainAssignments[arrayIndex] = terrainIndex;
                                        overridesApplied++;
                                    }
                                }
                            }
                            else if (logProgress)
                            {
                                ArchonLogger.LogWarning($"TerrainOverrideApplicator: No terrain index mapping for category '{categoryName}' (has {overrideProvinces.Count} overrides)", "map_rendering");
                            }
                        }
                    }
                }

                if (logProgress)
                {
                    ArchonLogger.Log($"TerrainOverrideApplicator: Applied {overridesApplied} terrain overrides from terrain.json5", "map_rendering");
                }
            }
            catch (System.Exception e)
            {
                ArchonLogger.LogError($"TerrainOverrideApplicator: Failed to load terrain overrides: {e.Message}", "map_rendering");
            }
        }

        /// <summary>
        /// Build terrain type name → index mapping from terrain_rgb.json5
        /// Returns dictionary: typeName → terrainTypeIndex
        /// </summary>
        private Dictionary<string, uint> BuildCategoryToIndexMapping()
        {
            var categoryToIndex = new Dictionary<string, uint>();

            try
            {
                string terrainRgbPath = System.IO.Path.Combine(Application.dataPath, "Data", "map", "terrain_rgb.json5");
                if (!System.IO.File.Exists(terrainRgbPath))
                {
                    ArchonLogger.LogError($"TerrainOverrideApplicator: terrain_rgb.json5 not found at {terrainRgbPath}", "map_rendering");
                    return null;
                }

                JObject terrainRgbData = Json5Loader.LoadJson5File(terrainRgbPath);

                // Build type name → index mapping (index = order in terrain_rgb.json5)
                uint terrainTypeIndex = 0;
                foreach (var terrainProperty in terrainRgbData.Properties())
                {
                    if (terrainProperty.Value is JObject terrainObj)
                    {
                        string typeName = terrainObj["type"]?.ToString();
                        if (!string.IsNullOrEmpty(typeName))
                        {
                            // Map each terrain type name to its index (first occurrence wins)
                            if (!categoryToIndex.ContainsKey(typeName))
                            {
                                categoryToIndex[typeName] = terrainTypeIndex;
                            }
                            terrainTypeIndex++;
                        }
                    }
                }

                return categoryToIndex;
            }
            catch (System.Exception e)
            {
                ArchonLogger.LogError($"TerrainOverrideApplicator: Failed to build category mapping: {e.Message}", "map_rendering");
                return null;
            }
        }
    }
}
