using System;
using System.IO;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using Newtonsoft.Json.Linq;
using Core.Data;
using Utils;

namespace Core.Loaders
{
    /// <summary>
    /// Converts JSON5 province files to burst-compatible structs
    /// Phase 1 of the hybrid JSON5 + Burst architecture
    /// </summary>
    public static class Json5ProvinceConverter
    {
        /// <summary>
        /// Load all province JSON5 files and convert to burst-compatible structs
        /// </summary>
        public static Json5ProvinceLoadResult LoadProvinceJson5Files(string dataDirectory)
        {
            string provincesDir = Path.Combine(dataDirectory, "history", "provinces");

            if (!Directory.Exists(provincesDir))
            {
                return Json5ProvinceLoadResult.Failed($"Province history directory not found: {provincesDir}");
            }

            // Get all JSON5 files
            string[] files = Directory.GetFiles(provincesDir, "*.json5");

            if (files.Length == 0)
            {
                return Json5ProvinceLoadResult.Failed("No province JSON5 files found");
            }

            DominionLogger.Log($"Loading {files.Length} province JSON5 files...");

            var rawDataList = new List<RawProvinceData>();
            int failedCount = 0;

            foreach (string filePath in files)
            {
                try
                {
                    var provinceData = LoadSingleProvinceFile(filePath);
                    if (provinceData.provinceID > 0)
                    {
                        rawDataList.Add(provinceData);
                    }
                    else
                    {
                        failedCount++;
                    }
                }
                catch (Exception e)
                {
                    DominionLogger.LogError($"Failed to load province file {filePath}: {e.Message}");
                    failedCount++;
                }
            }

            if (rawDataList.Count == 0)
            {
                return Json5ProvinceLoadResult.Failed("No valid province data loaded");
            }

            // Convert to NativeArray for burst processing
            var nativeArray = new NativeArray<RawProvinceData>(rawDataList.ToArray(), Allocator.TempJob);

            DominionLogger.Log($"JSON5 province loading complete: {rawDataList.Count} loaded, {failedCount} failed");

            return Json5ProvinceLoadResult.Success(nativeArray, rawDataList.Count, failedCount);
        }

        /// <summary>
        /// Load and parse a single province JSON5 file
        /// </summary>
        private static RawProvinceData LoadSingleProvinceFile(string filePath)
        {
            // Extract province ID from filename
            string fileName = Path.GetFileNameWithoutExtension(filePath);
            int provinceID = ExtractProvinceIDFromFilename(fileName);

            if (provinceID <= 0)
            {
                DominionLogger.LogError($"Invalid province ID in filename: {fileName}");
                return RawProvinceData.Invalid;
            }

            // Load and parse JSON5
            JObject json = Json5Loader.LoadJson5File(filePath);

            // Extract province data
            var rawData = new RawProvinceData
            {
                provinceID = provinceID,
                baseTax = Json5Loader.GetInt(json, "base_tax", 1),
                baseProduction = Json5Loader.GetInt(json, "base_production", 1),
                baseManpower = Json5Loader.GetInt(json, "base_manpower", 1),
                isCity = Json5Loader.GetBool(json, "is_city", false),
                hre = Json5Loader.GetBool(json, "hre", false),
                centerOfTrade = Json5Loader.GetInt(json, "center_of_trade", 0),
                extraCost = Json5Loader.GetInt(json, "extra_cost", 0)
            };

            // Handle optional string fields
            string owner = Json5Loader.GetString(json, "owner", "");
            if (!string.IsNullOrEmpty(owner) && owner != "---")
            {
                rawData.owner = new FixedString64Bytes(owner);
                rawData.hasOwner = true;
            }
            else
            {
                rawData.owner = new FixedString64Bytes("---");
                rawData.hasOwner = false;
            }

            string controller = Json5Loader.GetString(json, "controller", "");
            if (!string.IsNullOrEmpty(controller) && controller != "---")
            {
                rawData.controller = new FixedString64Bytes(controller);
                rawData.hasController = true;
            }
            else
            {
                rawData.controller = rawData.owner; // Default to owner
                rawData.hasController = rawData.hasOwner;
            }

            string culture = Json5Loader.GetString(json, "culture", "");
            if (!string.IsNullOrEmpty(culture))
            {
                rawData.culture = new FixedString64Bytes(culture);
                rawData.hasCulture = true;
            }
            else
            {
                rawData.culture = new FixedString64Bytes("unknown");
                rawData.hasCulture = false;
            }

            string religion = Json5Loader.GetString(json, "religion", "");
            if (!string.IsNullOrEmpty(religion))
            {
                rawData.religion = new FixedString64Bytes(religion);
                rawData.hasReligion = true;
            }
            else
            {
                rawData.religion = new FixedString64Bytes("unknown");
                rawData.hasReligion = false;
            }

            string tradeGood = Json5Loader.GetString(json, "trade_goods", "");
            if (!string.IsNullOrEmpty(tradeGood))
            {
                rawData.tradeGood = new FixedString64Bytes(tradeGood);
                rawData.hasTradeGood = true;
            }
            else
            {
                rawData.tradeGood = new FixedString64Bytes("unknown");
                rawData.hasTradeGood = false;
            }

            string capital = Json5Loader.GetString(json, "capital", "");
            if (!string.IsNullOrEmpty(capital))
            {
                rawData.capital = new FixedString64Bytes(capital);
                rawData.hasCapital = true;
            }
            else
            {
                rawData.capital = new FixedString64Bytes("");
                rawData.hasCapital = false;
            }

            return rawData;
        }

        /// <summary>
        /// Extract province ID from filename (handles "1-Uppland" and "100 - Friesland" formats)
        /// </summary>
        private static int ExtractProvinceIDFromFilename(string filename)
        {
            // Try dash format first: "1-Uppland"
            int dashIndex = filename.IndexOf('-');
            if (dashIndex > 0)
            {
                string idPart = filename.Substring(0, dashIndex).Trim();
                if (int.TryParse(idPart, out int id))
                    return id;
            }

            // Try space format: "100 - Friesland"
            int spaceIndex = filename.IndexOf(' ');
            if (spaceIndex > 0)
            {
                string idPart = filename.Substring(0, spaceIndex).Trim();
                if (int.TryParse(idPart, out int id))
                    return id;
            }

            // Try parsing the whole string as number
            if (int.TryParse(filename, out int directId))
                return directId;

            return 0; // Invalid
        }
    }
}