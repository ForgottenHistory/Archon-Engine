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
        private string dataDirectory;

        public TerrainOverrideApplicator(string dataDirectory = null, bool logProgress = true)
        {
            this.dataDirectory = dataDirectory ?? System.IO.Path.Combine(Application.dataPath, "Data");
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
                string terrainJsonPath = System.IO.Path.Combine(dataDirectory, "map", "terrain.json5");

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
                    ArchonLogger.Log($"TerrainOverrideApplicator: Loaded {categoryToIndex.Count} category mappings from terrain.json5", "map_rendering");
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
        /// Build category name → index mapping from terrain.json5 categories section
        /// Returns dictionary: categoryName → terrainTypeIndex (based on ORDER in categories)
        /// </summary>
        private Dictionary<string, uint> BuildCategoryToIndexMapping()
        {
            var categoryToIndex = new Dictionary<string, uint>();

            try
            {
                string terrainPath = System.IO.Path.Combine(dataDirectory, "map", "terrain.json5");
                if (!System.IO.File.Exists(terrainPath))
                {
                    ArchonLogger.LogError($"TerrainOverrideApplicator: terrain.json5 not found at {terrainPath}", "map_rendering");
                    return null;
                }

                JObject terrainData = Json5Loader.LoadJson5File(terrainPath);
                JObject categories = terrainData["categories"] as JObject;

                if (categories == null)
                {
                    ArchonLogger.LogError("TerrainOverrideApplicator: No 'categories' section in terrain.json5", "map_rendering");
                    return null;
                }

                // Build category name → index mapping (index = order in categories section)
                // This matches exactly how TerrainRGBLookup assigns indices
                uint terrainTypeIndex = 0;
                foreach (var categoryProperty in categories.Properties())
                {
                    string categoryName = categoryProperty.Name;
                    categoryToIndex[categoryName] = terrainTypeIndex;
                    terrainTypeIndex++;
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
