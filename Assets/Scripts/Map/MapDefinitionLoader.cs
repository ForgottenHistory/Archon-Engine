using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

namespace ProvinceSystem.Map
{
    /// <summary>
    /// Loads and parses Paradox-style map definition files (default.map)
    /// </summary>
    public class MapDefinitionLoader
    {
        public class MapDefinition
        {
            public int width;
            public int height;
            public int maxProvinces;
            public HashSet<int> seaProvinces = new HashSet<int>();
            public HashSet<int> lakeProvinces = new HashSet<int>();
            public HashSet<int> randomOnlyProvinces = new HashSet<int>();
            public HashSet<int> wastelandProvinces = new HashSet<int>();
            public HashSet<int> forceCoastalProvinces = new HashSet<int>();

            public Dictionary<string, string> filePaths = new Dictionary<string, string>();
            public List<CanalDefinition> canals = new List<CanalDefinition>();

            public bool IsSeaProvince(int provinceId) => seaProvinces.Contains(provinceId);
            public bool IsLakeProvince(int provinceId) => lakeProvinces.Contains(provinceId);
            public bool IsWaterProvince(int provinceId) => IsSeaProvince(provinceId) || IsLakeProvince(provinceId);
            public bool IsLandProvince(int provinceId) => !IsWaterProvince(provinceId) && !wastelandProvinces.Contains(provinceId);
        }

        public class CanalDefinition
        {
            public string name;
            public int x;
            public int y;
        }

        public static MapDefinition LoadMapDefinition(string filePath)
        {
            if (!File.Exists(filePath))
            {
                Debug.LogError($"Map definition file not found: {filePath}");
                return null;
            }

            var definition = new MapDefinition();
            string content = File.ReadAllText(filePath);

            // Parse basic properties
            definition.width = ParseInt(content, @"width\s*=\s*(\d+)");
            definition.height = ParseInt(content, @"height\s*=\s*(\d+)");
            definition.maxProvinces = ParseInt(content, @"max_provinces\s*=\s*(\d+)");

            // Parse province lists
            definition.seaProvinces = ParseProvinceList(content, "sea_starts");
            definition.lakeProvinces = ParseProvinceList(content, "lakes");
            definition.forceCoastalProvinces = ParseProvinceList(content, "force_coastal");

            // Parse random only provinces (includes wastelands)
            var randomOnly = ParseProvinceList(content, "only_used_for_random");

            // Extract wasteland provinces from comments in random only section
            string randomOnlySection = ExtractSection(content, "only_used_for_random");
            if (!string.IsNullOrEmpty(randomOnlySection))
            {
                // Find RNW Wasteland provinces section
                var wastelandMatch = Regex.Match(randomOnlySection, @"#RNW Wasteland provinces.*?(?=\}|\z)", RegexOptions.Singleline);
                if (wastelandMatch.Success)
                {
                    var wastelandIds = ExtractNumbers(wastelandMatch.Value);
                    foreach (int id in wastelandIds)
                    {
                        definition.wastelandProvinces.Add(id);
                        randomOnly.Remove(id); // Remove from random only if it's wasteland
                    }
                }
            }

            definition.randomOnlyProvinces = randomOnly;

            // Parse file paths
            definition.filePaths["definitions"] = ParseString(content, @"definitions\s*=\s*""([^""]+)""");
            definition.filePaths["provinces"] = ParseString(content, @"provinces\s*=\s*""([^""]+)""");
            definition.filePaths["positions"] = ParseString(content, @"positions\s*=\s*""([^""]+)""");
            definition.filePaths["terrain"] = ParseString(content, @"terrain\s*=\s*""([^""]+)""");
            definition.filePaths["rivers"] = ParseString(content, @"rivers\s*=\s*""([^""]+)""");
            definition.filePaths["heightmap"] = ParseString(content, @"heightmap\s*=\s*""([^""]+)""");
            definition.filePaths["adjacencies"] = ParseString(content, @"adjacencies\s*=\s*""([^""]+)""");

            // Parse canal definitions
            var canalMatches = Regex.Matches(content, @"canal_definition\s*=\s*\{[^}]+\}");
            foreach (Match match in canalMatches)
            {
                var canal = new CanalDefinition
                {
                    name = ParseString(match.Value, @"name\s*=\s*""([^""]+)"""),
                    x = ParseInt(match.Value, @"x\s*=\s*(\d+)"),
                    y = ParseInt(match.Value, @"y\s*=\s*(\d+)")
                };

                if (!string.IsNullOrEmpty(canal.name))
                {
                    definition.canals.Add(canal);
                }
            }

            Debug.Log($"Loaded map definition: {definition.width}x{definition.height}, " +
                     $"{definition.seaProvinces.Count} sea provinces, " +
                     $"{definition.lakeProvinces.Count} lake provinces, " +
                     $"{definition.wastelandProvinces.Count} wasteland provinces");

            return definition;
        }

        private static HashSet<int> ParseProvinceList(string content, string listName)
        {
            var provinces = new HashSet<int>();
            string section = ExtractSection(content, listName);

            if (!string.IsNullOrEmpty(section))
            {
                var numbers = ExtractNumbers(section);
                foreach (int num in numbers)
                {
                    provinces.Add(num);
                }
            }

            return provinces;
        }

        private static string ExtractSection(string content, string sectionName)
        {
            // Match section_name = { ... }
            var pattern = $@"{sectionName}\s*=\s*\{{([^{{}}]*(?:\{{[^{{}}]*\}}[^{{}}]*)*)\}}";
            var match = Regex.Match(content, pattern, RegexOptions.Singleline);

            if (match.Success)
            {
                return match.Groups[1].Value;
            }

            return "";
        }

        private static List<int> ExtractNumbers(string text)
        {
            var numbers = new List<int>();
            var matches = Regex.Matches(text, @"\b\d+\b");

            foreach (Match match in matches)
            {
                if (int.TryParse(match.Value, out int num))
                {
                    numbers.Add(num);
                }
            }

            return numbers;
        }

        private static int ParseInt(string content, string pattern)
        {
            var match = Regex.Match(content, pattern);
            if (match.Success && int.TryParse(match.Groups[1].Value, out int result))
            {
                return result;
            }
            return 0;
        }

        private static string ParseString(string content, string pattern)
        {
            var match = Regex.Match(content, pattern);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
            return "";
        }
    }
}