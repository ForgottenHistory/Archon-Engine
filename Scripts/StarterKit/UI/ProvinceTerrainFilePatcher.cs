using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Utils;

namespace StarterKit
{
    /// <summary>
    /// STARTERKIT: Comment-preserving json5 patcher for province terrain fields.
    /// Regex-replaces the terrain: "value" line without disturbing comments or formatting.
    /// Used by ProvinceTerrainEditorUI to save terrain changes back to disk.
    /// </summary>
    public static class ProvinceTerrainFilePatcher
    {
        private static readonly Regex TerrainLineRegex =
            new Regex(@"(terrain\s*:\s*"")([^""]*)("")", RegexOptions.Compiled);

        /// <summary>
        /// Find the json5 file for a province by ID.
        /// Scans {dataDirectory}/history/provinces/{id}-*.json5
        /// </summary>
        public static string FindProvinceFile(int provinceId, string dataDirectory)
        {
            string dir = Path.Combine(dataDirectory, "history", "provinces");
            if (!Directory.Exists(dir))
                return null;

            string pattern = $"{provinceId}-*.json5";
            string[] matches = Directory.GetFiles(dir, pattern);

            if (matches.Length == 0)
                return null;

            return matches[0];
        }

        /// <summary>
        /// Patch the terrain field in a json5 file, preserving comments and formatting.
        /// If no terrain field exists, inserts one after the opening brace.
        /// </summary>
        public static bool PatchTerrainField(string filePath, string newTerrainKey)
        {
            if (!File.Exists(filePath))
                return false;

            string content = File.ReadAllText(filePath);

            if (TerrainLineRegex.IsMatch(content))
            {
                content = TerrainLineRegex.Replace(content, $"$1{newTerrainKey}$3");
            }
            else
            {
                int braceIndex = content.IndexOf('{');
                if (braceIndex < 0)
                    return false;

                content = content.Insert(braceIndex + 1,
                    $"\n  terrain: \"{newTerrainKey}\",");
            }

            File.WriteAllText(filePath, content);
            return true;
        }

        /// <summary>
        /// Patch the passable field in a json5 file. Adds or updates the field.
        /// </summary>
        public static bool PatchPassableField(string filePath, bool passable)
        {
            if (!File.Exists(filePath))
                return false;

            string content = File.ReadAllText(filePath);

            var passableRegex = new Regex(@"passable\s*:\s*(true|false)");

            if (passableRegex.IsMatch(content))
            {
                content = passableRegex.Replace(content, $"passable: {(passable ? "true" : "false")}");
            }
            else
            {
                // Insert after opening brace
                int braceIndex = content.IndexOf('{');
                if (braceIndex < 0)
                    return false;

                content = content.Insert(braceIndex + 1,
                    $"\n  passable: {(passable ? "true" : "false")},");
            }

            File.WriteAllText(filePath, content);
            return true;
        }

        /// <summary>
        /// Create a new province history json5 file with the given terrain.
        /// </summary>
        public static string CreateProvinceFile(int provinceId, string terrainKey, string dataDirectory)
        {
            string dir = Path.Combine(dataDirectory, "history", "provinces");
            Directory.CreateDirectory(dir);

            string fileName = $"{provinceId}-Province_{provinceId}.json5";
            string filePath = Path.Combine(dir, fileName);

            string content = $"// Province {provinceId}: Province_{provinceId}\n{{\n  terrain: \"{terrainKey}\"\n}}\n";
            File.WriteAllText(filePath, content);

            return filePath;
        }

        /// <summary>
        /// Batch save: apply all pending terrain changes to disk.
        /// Creates new province files if they don't exist.
        /// </summary>
        /// <param name="changes">Map of provinceId to terrain key string (e.g., "mountain")</param>
        /// <param name="dataDirectory">Root data directory containing history/provinces/</param>
        /// <returns>Number of successfully patched files</returns>
        public static int SaveAll(Dictionary<ushort, string> changes, string dataDirectory)
        {
            int successCount = 0;
            int failCount = 0;

            foreach (var kvp in changes)
            {
                string filePath = FindProvinceFile(kvp.Key, dataDirectory);
                if (filePath == null)
                {
                    // Create a new province file
                    filePath = CreateProvinceFile(kvp.Key, kvp.Value, dataDirectory);
                    successCount++;
                    continue;
                }

                if (PatchTerrainField(filePath, kvp.Value))
                {
                    successCount++;
                }
                else
                {
                    ArchonLogger.LogWarning(
                        $"ProvinceTerrainFilePatcher: Failed to patch {filePath}",
                        "core_data_loading");
                    failCount++;
                }
            }

            ArchonLogger.Log(
                $"ProvinceTerrainFilePatcher: Saved {successCount}/{changes.Count} files ({failCount} failures)",
                "core_data_loading");

            return successCount;
        }
    }
}
