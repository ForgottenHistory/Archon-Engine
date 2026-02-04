using System;
using System.IO;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
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
                return Json5ProvinceLoadResult.Failure($"Province history directory not found: {provincesDir}");
            }

            // Get all JSON5 files
            string[] files = Directory.GetFiles(provincesDir, "*.json5");

            if (files.Length == 0)
            {
                return Json5ProvinceLoadResult.Failure("No province JSON5 files found");
            }

            ArchonLogger.Log($"Loading {files.Length} province JSON5 files...", "core_data_loading");

            var rawDataBag = new ConcurrentBag<RawProvinceData>();
            int failedCount = 0;

            Parallel.ForEach(files, filePath =>
            {
                try
                {
                    var provinceData = LoadSingleProvinceFile(filePath);
                    if (provinceData.provinceID > 0)
                    {
                        rawDataBag.Add(provinceData);
                    }
                    else
                    {
                        Interlocked.Increment(ref failedCount);
                    }
                }
                catch (Exception e)
                {
                    ArchonLogger.LogError($"Failed to load province file {filePath}: {e.Message}", "core_data_loading");
                    Interlocked.Increment(ref failedCount);
                }
            });

            var rawDataList = new List<RawProvinceData>(rawDataBag);

            if (rawDataList.Count == 0)
            {
                return Json5ProvinceLoadResult.Failure("No valid province data loaded");
            }

            // Convert to NativeArray for burst processing
            // Use Allocator.Persistent because data survives >4 frames in coroutine processing
            var nativeArray = new NativeArray<RawProvinceData>(rawDataList.ToArray(), Allocator.Persistent);

            ArchonLogger.Log($"JSON5 province loading complete: {rawDataList.Count} loaded, {failedCount} failed", "core_data_loading");

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
                ArchonLogger.LogError($"Invalid province ID in filename: {fileName}", "core_data_loading");
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
            // ENGINE layer only parses generic fields (provinceID, owner, controller)
            // Game-specific fields (culture, religion, development, etc.) should be
            // parsed by game layer loaders and stored in ProvinceColdData.CustomData
            var rawData = new RawProvinceData
            {
                provinceID = provinceID
            };

            // Handle owner (generic - all games have ownership)
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

            // Handle controller (generic - defaults to owner)
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
                if (!Json5Loader.IsDateKey(property.Name))
                {
                    effectiveState[property.Name] = property.Value;
                }
            }

            // Find and sort all dated events
            var datedEvents = new List<(int year, int month, int day, JObject eventData)>();

            foreach (var property in provinceJson.Properties())
            {
                if (Json5Loader.IsDateKey(property.Name))
                {
                    if (Json5Loader.TryParseDate(property.Name, out int year, out int month, out int day))
                    {
                        // Only include events at or before start date
                        if (Json5Loader.IsDateBeforeOrEqual(year, month, day, startYear, startMonth, startDay))
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