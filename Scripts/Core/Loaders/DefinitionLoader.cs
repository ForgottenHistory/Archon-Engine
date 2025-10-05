using System;
using System.IO;
using System.Collections.Generic;
using Core.Registries;
using Utils;

namespace Core.Loaders
{
    /// <summary>
    /// Loads province definitions from definition.csv
    /// This ensures ALL provinces (including uncolonized ones without JSON5 files) are registered
    /// </summary>
    public static class DefinitionLoader
    {
        public struct DefinitionEntry
        {
            public int ProvinceID;
            public byte R, G, B;
            public string Name;
            public bool IsWater; // 'x' flag in definition.csv
        }

        /// <summary>
        /// Load all province definitions from definition.csv
        /// Returns list of all provinces that should exist in the game
        /// </summary>
        public static List<DefinitionEntry> LoadDefinitions(string dataDirectory)
        {
            string definitionPath = Path.Combine(dataDirectory, "map", "definition.csv");

            if (!File.Exists(definitionPath))
            {
                ArchonLogger.LogError($"DefinitionLoader: definition.csv not found at {definitionPath}");
                return new List<DefinitionEntry>();
            }

            var entries = new List<DefinitionEntry>();
            int lineNumber = 0;
            int skippedLines = 0;

            try
            {
                string[] lines = File.ReadAllLines(definitionPath);

                foreach (string line in lines)
                {
                    lineNumber++;

                    // Skip header and empty lines
                    if (lineNumber == 1 || string.IsNullOrWhiteSpace(line))
                    {
                        skippedLines++;
                        continue;
                    }

                    // Skip comment lines
                    if (line.TrimStart().StartsWith("#"))
                    {
                        skippedLines++;
                        continue;
                    }

                    try
                    {
                        var entry = ParseDefinitionLine(line, lineNumber);
                        if (entry.ProvinceID > 0)
                        {
                            entries.Add(entry);
                        }
                        else
                        {
                            skippedLines++;
                        }
                    }
                    catch (Exception e)
                    {
                        ArchonLogger.LogWarning($"DefinitionLoader: Failed to parse line {lineNumber}: {e.Message}");
                        skippedLines++;
                    }
                }

                ArchonLogger.Log($"DefinitionLoader: Loaded {entries.Count} province definitions from definition.csv ({skippedLines} lines skipped)");
            }
            catch (Exception e)
            {
                ArchonLogger.LogError($"DefinitionLoader: Failed to read definition.csv: {e.Message}");
            }

            return entries;
        }

        private static DefinitionEntry ParseDefinitionLine(string line, int lineNumber)
        {
            // Format: ID;R;G;B;Name;x
            // Example: 1;128;34;64;Stockholm;x
            string[] parts = line.Split(';');

            if (parts.Length < 5)
            {
                return new DefinitionEntry { ProvinceID = 0 }; // Invalid
            }

            // Parse province ID
            if (!int.TryParse(parts[0].Trim(), out int provinceID) || provinceID <= 0)
            {
                return new DefinitionEntry { ProvinceID = 0 }; // Invalid
            }

            // Parse RGB
            if (!byte.TryParse(parts[1].Trim(), out byte r) ||
                !byte.TryParse(parts[2].Trim(), out byte g) ||
                !byte.TryParse(parts[3].Trim(), out byte b))
            {
                ArchonLogger.LogWarning($"DefinitionLoader: Invalid RGB values on line {lineNumber}");
                return new DefinitionEntry { ProvinceID = 0 }; // Invalid
            }

            // Parse name
            string name = parts[4].Trim();
            if (string.IsNullOrEmpty(name))
            {
                name = $"Province {provinceID}";
            }

            // Check for water flag ('x')
            bool isWater = parts.Length > 5 && parts[5].Trim().ToLower() == "x";

            return new DefinitionEntry
            {
                ProvinceID = provinceID,
                R = r,
                G = g,
                B = b,
                Name = name,
                IsWater = isWater
            };
        }

        /// <summary>
        /// Register all provinces from definitions into ProvinceRegistry
        /// Creates default ProvinceData for provinces without JSON5 files
        /// </summary>
        public static void RegisterDefinitions(List<DefinitionEntry> definitions, ProvinceRegistry registry)
        {
            int registered = 0;

            foreach (var def in definitions)
            {
                // Check if already registered (from JSON5)
                if (registry.ExistsByDefinition(def.ProvinceID))
                {
                    continue; // Already registered from JSON5, skip
                }

                // Create default province data for uncolonized/water provinces
                var provinceData = new ProvinceData
                {
                    DefinitionId = def.ProvinceID,
                    Name = def.Name,
                    Development = 0, // Uncolonized
                    Terrain = (byte)(def.IsWater ? 0 : 1), // 0 = water, 1 = land
                    Flags = (byte)(def.IsWater ? 1 : 0), // Flag for water
                    BaseTax = 0,
                    BaseProduction = 0,
                    BaseManpower = 0,
                    CenterOfTrade = 0
                };

                try
                {
                    registry.Register(def.ProvinceID, provinceData);
                    registered++;
                }
                catch (Exception e)
                {
                    ArchonLogger.LogWarning($"DefinitionLoader: Failed to register province {def.ProvinceID}: {e.Message}");
                }
            }

            ArchonLogger.Log($"DefinitionLoader: Registered {registered} default provinces from definitions (total registry size: {registry.Count})");
        }
    }
}
