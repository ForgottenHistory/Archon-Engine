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
            // Use Allocator.Persistent because data survives >4 frames in coroutine processing
            var nativeArray = new NativeArray<RawProvinceData>(rawDataList.ToArray(), Allocator.Persistent);

            DominionLogger.Log($"JSON5 province loading complete: {rawDataList.Count} loaded, {failedCount} failed");

            return Json5ProvinceLoadResult.Success(nativeArray, rawDataList.Count, failedCount);
        }

        /// <summary>
        /// Load and parse a single province JSON5 file
        /// Applies dated historical events up to start date (1444.11.11)
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

            // CRITICAL: Apply dated historical events UP TO start date (1444.11.11)
            // EU4 province files format:
            //   owner: "TIM"     <- Initial value
            //   "1442.1.1": { owner: "QOM" }  <- Event before 1444
            //   "1451.1.1": { owner: "QAR" }  <- Event after 1444 (ignore)
            // At 1444.11.11, owner should be QOM (not TIM!)
            JObject effectiveState = ApplyHistoricalEventsToStartDate(json, 1444, 11, 11);

            // Extract province data from EFFECTIVE state (after applying historical events)
            var rawData = new RawProvinceData
            {
                provinceID = provinceID,
                baseTax = Json5Loader.GetInt(effectiveState, "base_tax", 1),
                baseProduction = Json5Loader.GetInt(effectiveState, "base_production", 1),
                baseManpower = Json5Loader.GetInt(effectiveState, "base_manpower", 1),
                isCity = Json5Loader.GetBool(effectiveState, "is_city", false),
                hre = Json5Loader.GetBool(effectiveState, "hre", false),
                centerOfTrade = Json5Loader.GetInt(effectiveState, "center_of_trade", 0),
                extraCost = Json5Loader.GetInt(effectiveState, "extra_cost", 0)
            };

            // Handle optional string fields (from effective state)
            string owner = Json5Loader.GetString(effectiveState, "owner", "");
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

            string controller = Json5Loader.GetString(effectiveState, "controller", "");
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

            string culture = Json5Loader.GetString(effectiveState, "culture", "");
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

            string religion = Json5Loader.GetString(effectiveState, "religion", "");
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

            string tradeGood = Json5Loader.GetString(effectiveState, "trade_goods", "");
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

            string capital = Json5Loader.GetString(effectiveState, "capital", "");
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
        /// Apply historical dated events to province data up to specified start date
        /// Returns a new JObject with the effective state at start date
        /// </summary>
        private static JObject ApplyHistoricalEventsToStartDate(JObject provinceJson, int startYear, int startMonth, int startDay)
        {
            // Create effective state starting with all non-dated properties
            var effectiveState = new JObject();

            // Copy all initial (non-dated) properties
            foreach (var property in provinceJson.Properties())
            {
                // Skip dated properties (they look like "1442.1.1")
                if (!IsDateKey(property.Name))
                {
                    effectiveState[property.Name] = property.Value;
                }
            }

            // Find and sort all dated events
            var datedEvents = new List<(int year, int month, int day, JObject eventData)>();

            foreach (var property in provinceJson.Properties())
            {
                if (IsDateKey(property.Name))
                {
                    if (TryParseDate(property.Name, out int year, out int month, out int day))
                    {
                        // Only include events at or before start date
                        if (IsDateBeforeOrEqual(year, month, day, startYear, startMonth, startDay))
                        {
                            if (property.Value is JObject eventObj)
                            {
                                datedEvents.Add((year, month, day, eventObj));
                            }
                        }
                    }
                }
            }

            // Sort events chronologically
            datedEvents.Sort((a, b) =>
            {
                int yearCompare = a.year.CompareTo(b.year);
                if (yearCompare != 0) return yearCompare;
                int monthCompare = a.month.CompareTo(b.month);
                if (monthCompare != 0) return monthCompare;
                return a.day.CompareTo(b.day);
            });

            // Apply events in chronological order (later events override earlier ones)
            foreach (var (year, month, day, eventData) in datedEvents)
            {
                foreach (var property in eventData.Properties())
                {
                    // Override or add property from this event
                    effectiveState[property.Name] = property.Value;
                }
            }

            return effectiveState;
        }

        /// <summary>
        /// Check if a property name looks like a date (e.g., "1442.1.1")
        /// </summary>
        private static bool IsDateKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;

            // Date keys start with a digit
            if (!char.IsDigit(key[0])) return false;

            // Date keys contain dots
            if (!key.Contains('.')) return false;

            // Quick validation: should have 2 dots (Y.M.D format)
            int dotCount = 0;
            foreach (char c in key)
            {
                if (c == '.') dotCount++;
            }

            return dotCount == 2;
        }

        /// <summary>
        /// Parse EU4 date format (Y.M.D like "1442.1.1")
        /// </summary>
        private static bool TryParseDate(string dateStr, out int year, out int month, out int day)
        {
            year = 0;
            month = 0;
            day = 0;

            if (string.IsNullOrEmpty(dateStr)) return false;

            string[] parts = dateStr.Split('.');
            if (parts.Length != 3) return false;

            if (!int.TryParse(parts[0], out year)) return false;
            if (!int.TryParse(parts[1], out month)) return false;
            if (!int.TryParse(parts[2], out day)) return false;

            return year > 0 && month > 0 && day > 0;
        }

        /// <summary>
        /// Check if date1 is before or equal to date2
        /// </summary>
        private static bool IsDateBeforeOrEqual(int y1, int m1, int d1, int y2, int m2, int d2)
        {
            if (y1 < y2) return true;
            if (y1 > y2) return false;
            // Years equal, check months
            if (m1 < m2) return true;
            if (m1 > m2) return false;
            // Months equal, check days
            return d1 <= d2;
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