using Unity.Collections;
using UnityEngine;
using ParadoxParser.Core;
using static ParadoxParser.Core.ParadoxParser;
using System.Collections.Generic;
using System;

namespace ParadoxParser.Utilities
{
    /// <summary>
    /// Generic helper utilities for extracting data from ParadoxParser structures
    /// Follows performance architecture principles to prevent late-game collapse
    /// </summary>
    public static class ParadoxDataExtractionHelpers
    {
        /// <summary>
        /// Extract string value from parsed data with proper quote handling
        /// </summary>
        public static string ExtractStringValue(ParsedValue value, NativeArray<byte> sourceData)
        {
            if (!value.IsLiteral)
                return string.Empty;

            var bytes = new byte[value.RawData.Length];
            for (int i = 0; i < value.RawData.Length; i++)
            {
                bytes[i] = value.RawData[i];
            }

            string result = System.Text.Encoding.UTF8.GetString(bytes);

            // Remove quotes if present
            if (result.StartsWith("\"") && result.EndsWith("\""))
            {
                result = result.Substring(1, result.Length - 2);
            }

            return result;
        }

        /// <summary>
        /// Extract string value from key bytes
        /// </summary>
        public static string ExtractKeyString(NativeSlice<byte> keyBytes, NativeArray<byte> sourceData)
        {
            var tempValue = new ParsedValue
            {
                Type = ParsedValueType.Literal,
                RawData = keyBytes
            };
            return ExtractStringValue(tempValue, sourceData);
        }

        /// <summary>
        /// Extract integer value from parsed data
        /// </summary>
        public static int ExtractIntValue(ParsedValue value, NativeArray<byte> sourceData)
        {
            if (!value.IsLiteral)
                return 0;

            string str = ExtractStringValue(value, sourceData);
            return int.TryParse(str, out int result) ? result : 0;
        }

        /// <summary>
        /// Extract ushort value from parsed data
        /// </summary>
        public static ushort ExtractUshortValue(ParsedValue value, NativeArray<byte> sourceData)
        {
            if (!value.IsLiteral)
                return 0;

            string str = ExtractStringValue(value, sourceData);
            return ushort.TryParse(str, out ushort result) ? result : (ushort)0;
        }

        /// <summary>
        /// Extract list of ushort values from a block (for province lists, etc.)
        /// </summary>
        public static List<ushort> ExtractUshortListFromBlock(ParsedKeyValue block, NativeList<ParsedKeyValue> childBlocks, NativeArray<byte> sourceData)
        {
            var values = new List<ushort>();

            if (!block.Value.IsBlock)
                return values;

            try
            {
                int startIndex = block.Value.BlockStartIndex;
                int blockLength = block.Value.BlockLength;

                if (startIndex < 0 || blockLength <= 0 || startIndex >= childBlocks.Length)
                    return values;

                int endIndex = Math.Min(startIndex + blockLength, childBlocks.Length);
                var blockChildItems = childBlocks.AsArray().GetSubArray(startIndex, endIndex - startIndex);

                foreach (var childKvp in blockChildItems)
                {
                    ushort value = 0;

                    // Try to extract from value first
                    if (childKvp.Value.IsLiteral)
                    {
                        value = ExtractUshortValue(childKvp.Value, sourceData);
                    }
                    // Fall back to key if value is empty
                    else if (childKvp.Key.Length > 0)
                    {
                        string keyStr = ExtractKeyString(childKvp.Key, sourceData);
                        ushort.TryParse(keyStr, out value);
                    }

                    if (value > 0)
                    {
                        values.Add(value);
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"ParadoxDataExtractionHelpers: Exception extracting ushort list - {e.Message}");
            }

            return values;
        }

        /// <summary>
        /// Extract color value from RGB block
        /// Format: color = { 255 0 0 }
        /// </summary>
        public static Color ExtractColorValue(ParsedKeyValue colorBlock, NativeList<ParsedKeyValue> childBlocks, NativeArray<byte> sourceData)
        {
            if (!colorBlock.Value.IsBlock)
                return Color.white;

            try
            {
                var colorComponents = ExtractUshortListFromBlock(colorBlock, childBlocks, sourceData);

                if (colorComponents.Count >= 3)
                {
                    return new Color(
                        Mathf.Clamp01(colorComponents[0] / 255.0f),
                        Mathf.Clamp01(colorComponents[1] / 255.0f),
                        Mathf.Clamp01(colorComponents[2] / 255.0f),
                        1.0f
                    );
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"ParadoxDataExtractionHelpers: Exception extracting color - {e.Message}");
            }

            return Color.white;
        }

        /// <summary>
        /// Parse Paradox date format (YYYY.MM.DD)
        /// </summary>
        public static System.DateTime ParseParadoxDate(string dateStr)
        {
            if (string.IsNullOrEmpty(dateStr) || dateStr.Length != 10)
                return new System.DateTime(1444, 11, 11); // EU4 start date as default

            if (dateStr[4] == '.' && dateStr[7] == '.')
            {
                if (int.TryParse(dateStr.Substring(0, 4), out int year) &&
                    int.TryParse(dateStr.Substring(5, 2), out int month) &&
                    int.TryParse(dateStr.Substring(8, 2), out int day))
                {
                    try
                    {
                        return new System.DateTime(year, month, day);
                    }
                    catch
                    {
                        // Invalid date, return default
                    }
                }
            }

            return new System.DateTime(1444, 11, 11);
        }

        /// <summary>
        /// Check if a key string represents a date format
        /// </summary>
        public static bool IsDateFormat(string str)
        {
            return str.Length == 10 && str[4] == '.' && str[7] == '.' &&
                   int.TryParse(str.Substring(0, 4), out _) &&
                   int.TryParse(str.Substring(5, 2), out _) &&
                   int.TryParse(str.Substring(8, 2), out _);
        }

        /// <summary>
        /// Find child block by key hash for fast lookups
        /// </summary>
        public static int FindChildBlockByHash(ParsedKeyValue parentBlock, NativeList<ParsedKeyValue> childBlocks, uint targetKeyHash)
        {
            if (!parentBlock.Value.IsBlock)
                return -1;

            try
            {
                int startIndex = parentBlock.Value.BlockStartIndex;
                int blockLength = parentBlock.Value.BlockLength;

                if (startIndex < 0 || blockLength <= 0 || startIndex >= childBlocks.Length)
                    return -1;

                int endIndex = Math.Min(startIndex + blockLength, childBlocks.Length);

                for (int i = startIndex; i < endIndex; i++)
                {
                    if (childBlocks[i].KeyHash == targetKeyHash)
                        return i;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"ParadoxDataExtractionHelpers: Exception finding child block - {e.Message}");
            }

            return -1;
        }

        /// <summary>
        /// Extract nested block data using key hash lookup
        /// </summary>
        public static ParsedKeyValue? ExtractNestedBlock(ParsedKeyValue parentBlock, NativeList<ParsedKeyValue> childBlocks, uint targetKeyHash)
        {
            int index = FindChildBlockByHash(parentBlock, childBlocks, targetKeyHash);
            return index >= 0 ? childBlocks[index] : (ParsedKeyValue?)null;
        }

        /// <summary>
        /// Pre-compute hash for common keys to avoid repeated allocations
        /// </summary>
        public static class CommonKeyHashes
        {
            // Province-related hashes
            public static readonly uint NAME_HASH;
            public static readonly uint COLOR_HASH;
            public static readonly uint CAPITAL_HASH;
            public static readonly uint AREAS_HASH;
            public static readonly uint OWNER_HASH;
            public static readonly uint CONTROLLER_HASH;
            public static readonly uint CULTURE_HASH;
            public static readonly uint RELIGION_HASH;
            public static readonly uint HISTORY_HASH;

            // Country-specific hashes
            public static readonly uint GRAPHICAL_CULTURE_HASH;
            public static readonly uint REVOLUTIONARY_COLORS_HASH;
            public static readonly uint PREFERRED_RELIGION_HASH;
            public static readonly uint HISTORICAL_IDEA_GROUPS_HASH;
            public static readonly uint HISTORICAL_UNITS_HASH;
            public static readonly uint MONARCH_NAMES_HASH;

            static CommonKeyHashes()
            {
                // Pre-compute common key hashes once at startup
                NAME_HASH = ComputeStringHash("name");
                COLOR_HASH = ComputeStringHash("color");
                CAPITAL_HASH = ComputeStringHash("capital");
                AREAS_HASH = ComputeStringHash("areas");
                OWNER_HASH = ComputeStringHash("owner");
                CONTROLLER_HASH = ComputeStringHash("controller");
                CULTURE_HASH = ComputeStringHash("culture");
                RELIGION_HASH = ComputeStringHash("religion");
                HISTORY_HASH = ComputeStringHash("history");

                // Country-specific hashes
                GRAPHICAL_CULTURE_HASH = ComputeStringHash("graphical_culture");
                REVOLUTIONARY_COLORS_HASH = ComputeStringHash("revolutionary_colors");
                PREFERRED_RELIGION_HASH = ComputeStringHash("preferred_religion");
                HISTORICAL_IDEA_GROUPS_HASH = ComputeStringHash("historical_idea_groups");
                HISTORICAL_UNITS_HASH = ComputeStringHash("historical_units");
                MONARCH_NAMES_HASH = ComputeStringHash("monarch_names");
            }

            private static uint ComputeStringHash(string key)
            {
                var keyBytes = System.Text.Encoding.UTF8.GetBytes(key);
                var keySlice = new NativeArray<byte>(keyBytes, Allocator.Temp);
                uint hash = ParadoxParser.Core.ParadoxParser.ComputeKeyHash(keySlice);
                keySlice.Dispose();
                return hash;
            }
        }

        /// <summary>
        /// Performance monitor for tracking parsing operations
        /// Follows architecture guide principles
        /// </summary>
        public static class PerformanceMonitor
        {
            public static void ValidateParsingTime(System.Diagnostics.Stopwatch stopwatch, string operationName, int targetMs)
            {
                if (stopwatch.ElapsedMilliseconds > targetMs)
                {
                    Debug.LogWarning($"{operationName}: Performance warning - operation took {stopwatch.ElapsedMilliseconds}ms (target: <{targetMs}ms)");
                }
            }

            public static void LogProgressWithTiming(string operationName, int itemsProcessed, System.Diagnostics.Stopwatch stopwatch, int interval = 100)
            {
                if (itemsProcessed % interval == 0)
                {
                    Debug.Log($"{operationName}: Processed {itemsProcessed} items, {stopwatch.ElapsedMilliseconds}ms elapsed");
                }
            }
        }
    }
}