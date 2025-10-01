using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using UnityEngine;
using Newtonsoft.Json.Linq;
using Core.Data;
using Utils;

namespace Core.Loaders
{
    /// <summary>
    /// Converts JSON5 country files to burst-compatible structs
    /// Phase 1 of the hybrid JSON5 + Burst architecture for countries
    /// </summary>
    public static class Json5CountryConverter
    {
        /// <summary>
        /// Load all country JSON5 files and convert to burst-compatible structs
        /// </summary>
        /// <param name="countriesDirectory">Directory containing country JSON5 files</param>
        /// <param name="tagMapping">Optional mapping of filenames to country tags from 00_countries.txt</param>
        public static Json5CountryLoadResult LoadCountryJson5Files(string countriesDirectory, Dictionary<string, string> tagMapping = null)
        {
            if (!Directory.Exists(countriesDirectory))
            {
                return Json5CountryLoadResult.Failed($"Country directory not found: {countriesDirectory}");
            }

            // Get all JSON5 files
            string[] files = Directory.GetFiles(countriesDirectory, "*.json5");

            if (files.Length == 0)
            {
                return Json5CountryLoadResult.Failed("No country JSON5 files found");
            }

            DominionLogger.Log($"Loading {files.Length} country JSON5 files...");

            var rawDataList = new List<RawCountryData>();
            int failedCount = 0;

            foreach (string filePath in files)
            {
                try
                {
                    var countryData = LoadSingleCountryFile(filePath, tagMapping);
                    if (!string.IsNullOrEmpty(countryData.tag.ToString()) && countryData.tag.ToString() != "---")
                    {
                        rawDataList.Add(countryData);
                    }
                    else
                    {
                        failedCount++;
                    }
                }
                catch (Exception e)
                {
                    DominionLogger.LogError($"Failed to load country file {filePath}: {e.Message}");
                    failedCount++;
                }
            }

            if (rawDataList.Count == 0)
            {
                return Json5CountryLoadResult.Failed("No valid country data loaded");
            }

            // Convert to NativeArray for burst processing
            // Use Allocator.Persistent because data survives >4 frames in coroutine processing
            var nativeArray = new NativeArray<RawCountryData>(rawDataList.ToArray(), Allocator.Persistent);

            DominionLogger.Log($"JSON5 country loading complete: {rawDataList.Count} loaded, {failedCount} failed");

            return Json5CountryLoadResult.Success(nativeArray, rawDataList.Count, failedCount);
        }

        /// <summary>
        /// Load and parse a single country JSON5 file
        /// </summary>
        private static RawCountryData LoadSingleCountryFile(string filePath, Dictionary<string, string> tagMapping = null)
        {
            // Get filename without extension
            string fileName = Path.GetFileNameWithoutExtension(filePath);

            // Try to get tag from mapping first, fallback to filename extraction
            string countryTag = null;
            bool foundInMapping = false;
            if (tagMapping != null)
            {
                // Look for matching filename in tag mapping (e.g., "Shimazu.txt" -> "SMZ")
                // Extract the actual filename from the path to avoid false matches
                // (e.g., "Bar" shouldn't match "Malabar.txt")
                var matchingEntry = tagMapping.FirstOrDefault(kvp =>
                {
                    string pathFileName = Path.GetFileNameWithoutExtension(kvp.Value);
                    return pathFileName.Equals(fileName, StringComparison.OrdinalIgnoreCase);
                });

                if (matchingEntry.Key != null)
                {
                    countryTag = matchingEntry.Key;
                    foundInMapping = true;
                }
            }

            // Fallback to extracting from filename if no mapping found
            if (string.IsNullOrEmpty(countryTag))
            {
                countryTag = ExtractCountryTagFromFilename(fileName);
            }

            if (string.IsNullOrEmpty(countryTag))
            {
                DominionLogger.LogError($"Invalid country tag in filename: {fileName}");
                return RawCountryData.Invalid;
            }

            // Load and parse JSON5
            JObject json = Json5Loader.LoadJson5File(filePath);

            // Extract country data
            var rawData = new RawCountryData
            {
                tag = new FixedString64Bytes(countryTag),
                historicalScore = Json5Loader.GetInt(json, "historical_score", 0)
            };

            // Handle optional string fields
            string graphicalCulture = Json5Loader.GetString(json, "graphical_culture", "");
            if (!string.IsNullOrEmpty(graphicalCulture))
            {
                rawData.graphicalCulture = new FixedString64Bytes(graphicalCulture);
                rawData.hasGraphicalCulture = true;
            }
            else
            {
                rawData.graphicalCulture = new FixedString64Bytes("unknown");
                rawData.hasGraphicalCulture = false;
            }

            string preferredReligion = Json5Loader.GetString(json, "preferred_religion", "");
            if (!string.IsNullOrEmpty(preferredReligion))
            {
                rawData.preferredReligion = new FixedString64Bytes(preferredReligion);
                rawData.hasPreferredReligion = true;
            }
            else
            {
                rawData.preferredReligion = new FixedString64Bytes("unknown");
                rawData.hasPreferredReligion = false;
            }

            // Handle historical score
            rawData.hasHistoricalScore = rawData.historicalScore > 0;

            // Extract color array [R, G, B]
            var colorArray = Json5Loader.GetIntArray(json, "color");
            if (colorArray != null && colorArray.Count >= 3)
            {
                rawData.colorR = (byte)Mathf.Clamp(colorArray[0], 0, 255);
                rawData.colorG = (byte)Mathf.Clamp(colorArray[1], 0, 255);
                rawData.colorB = (byte)Mathf.Clamp(colorArray[2], 0, 255);
            }
            else
            {
                // Default gray color
                rawData.colorR = 128;
                rawData.colorG = 128;
                rawData.colorB = 128;
            }

            // Extract revolutionary colors [R, G, B]
            var revolutionaryColorArray = Json5Loader.GetIntArray(json, "revolutionary_colors");
            if (revolutionaryColorArray != null && revolutionaryColorArray.Count >= 3)
            {
                rawData.revolutionaryColorR = (byte)Mathf.Clamp(revolutionaryColorArray[0], 0, 255);
                rawData.revolutionaryColorG = (byte)Mathf.Clamp(revolutionaryColorArray[1], 0, 255);
                rawData.revolutionaryColorB = (byte)Mathf.Clamp(revolutionaryColorArray[2], 0, 255);
                rawData.hasRevolutionaryColors = true;
            }
            else
            {
                // Default to same as regular color
                rawData.revolutionaryColorR = rawData.colorR;
                rawData.revolutionaryColorG = rawData.colorG;
                rawData.revolutionaryColorB = rawData.colorB;
                rawData.hasRevolutionaryColors = false;
            }

            return rawData;
        }

        /// <summary>
        /// Extract country tag from filename (e.g., "Sweden.json5" -> "SWE")
        /// For now, use first 3 characters of filename, but this should be
        /// replaced with proper tag mapping
        /// </summary>
        private static string ExtractCountryTagFromFilename(string filename)
        {
            if (string.IsNullOrEmpty(filename))
                return "";

            // TODO: Implement proper filename-to-tag mapping
            // For now, create a 3-letter tag from filename
            string tag = filename.Length >= 3 ? filename.Substring(0, 3).ToUpper() : filename.ToUpper();

            // Handle some special cases for known countries
            switch (filename.ToLower())
            {
                case "sweden": return "SWE";
                case "france": return "FRA";
                case "england": return "ENG";
                case "spain": return "SPA";
                case "austria": return "HAB";
                case "ottoman": return "TUR";
                case "russia": return "RUS";
                case "poland": return "POL";
                case "brandenburg": return "BRA";
                case "portugal": return "POR";
                case "castile": return "CAS";
                case "aragon": return "ARA";
                case "denmark": return "DEN";
                case "norway": return "NOR";
                case "scotland": return "SCO";
                case "ireland": return "IRE";
                case "hungary": return "HUN";
                case "bohemia": return "BOH";
                case "venice": return "VEN";
                case "genoa": return "GEN";
                case "florence": return "FLO";
                case "naples": return "NAP";
                case "papal": return "PAP";
                case "milan": return "MLO";
                case "savoy": return "SAV";
                case "burgundy": return "BUR";
                case "brittany": return "BRI";
                case "provence": return "PRO";
                case "lithuania": return "LIT";
                case "teutonic": return "TEU";
                case "livonian": return "LIV";
                case "golden": return "GOL"; // Golden Horde
                case "crimea": return "CRI";
                case "kazan": return "KAZ";
                case "muscovy": return "MOS";
                case "novgorod": return "NOV";
                case "pskov": return "PSK";
                case "tver": return "TVE";
                case "ryazan": return "RYA";
                default:
                    return tag;
            }
        }
    }
}