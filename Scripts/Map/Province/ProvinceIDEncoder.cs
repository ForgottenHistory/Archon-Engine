using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using System.Runtime.CompilerServices;

namespace Map.Province
{
    /// <summary>
    /// High-performance province ID encoding system for texture-based rendering
    /// Handles RGB color to sequential ID conversion with collision detection
    /// </summary>
    public static class ProvinceIDEncoder
    {
        /// <summary>
        /// Province ID encoding result
        /// </summary>
        public struct EncodingResult
        {
            public bool IsSuccess;
            public int ProvinceCount;
            public NativeHashMap<Color32, ushort> ColorToID;
            public NativeHashMap<ushort, Color32> IDToColor;
            public string ErrorMessage;

            public void Dispose()
            {
                if (ColorToID.IsCreated) ColorToID.Dispose();
                if (IDToColor.IsCreated) IDToColor.Dispose();
            }
        }

        /// <summary>
        /// Pack 16-bit province ID into R16G16 format
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Color32 PackProvinceID(ushort provinceID)
        {
            byte r = (byte)(provinceID & 0xFF);          // Low 8 bits (0-255)
            byte g = (byte)((provinceID >> 8) & 0xFF);   // High 8 bits (256-65535)
            return new Color32(r, g, 0, 255);
        }

        /// <summary>
        /// Unpack province ID from R16G16 format
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ushort UnpackProvinceID(Color32 packedColor)
        {
            return (ushort)(packedColor.r + (packedColor.g << 8));
        }

        /// <summary>
        /// Encode unique colors to sequential province IDs with full validation
        /// </summary>
        /// <param name="uniqueColors">Array of unique province colors</param>
        /// <param name="reserveOceanID">Reserve ID 0 for ocean/no province</param>
        /// <returns>Encoding result with lookup tables</returns>
        public static EncodingResult EncodeProvinceIDs(NativeArray<Color32> uniqueColors, bool reserveOceanID = true)
        {
            var result = new EncodingResult();

            // Validate input
            if (uniqueColors.Length == 0)
            {
                result.ErrorMessage = "No unique colors provided";
                return result;
            }

            if (uniqueColors.Length >= 65535)
            {
                result.ErrorMessage = $"Too many unique colors ({uniqueColors.Length}). Maximum is 65534.";
                return result;
            }

            // Initialize lookup tables
            int capacity = uniqueColors.Length + (reserveOceanID ? 1 : 0);
            result.ColorToID = new NativeHashMap<Color32, ushort>(capacity, Allocator.Persistent);
            result.IDToColor = new NativeHashMap<ushort, Color32>(capacity, Allocator.Persistent);

            // Reserve ID 0 for ocean if requested
            ushort nextID = reserveOceanID ? (ushort)1 : (ushort)0;

            if (reserveOceanID)
            {
                Color32 oceanColor = new Color32(0, 0, 0, 255); // Black for ocean
                result.ColorToID.TryAdd(oceanColor, 0);
                result.IDToColor.TryAdd(0, oceanColor);
            }

            // Process each unique color
            for (int i = 0; i < uniqueColors.Length; i++)
            {
                Color32 color = uniqueColors[i];

                // Skip if we've already seen this color (deduplication)
                if (result.ColorToID.ContainsKey(color))
                {
                    continue;
                }

                // Assign sequential ID
                result.ColorToID.TryAdd(color, nextID);
                result.IDToColor.TryAdd(nextID, color);

                nextID++;
            }

            // Final validation
            if (!ValidateEncodingIntegrity(result.ColorToID, result.IDToColor))
            {
                result.ErrorMessage = "Encoding integrity validation failed";
                result.Dispose();
                return result;
            }

            result.IsSuccess = true;
            // Don't count ocean (ID 0) as a province
            result.ProvinceCount = reserveOceanID ? result.ColorToID.Count - 1 : result.ColorToID.Count;

            ArchonLogger.Log($"Province ID encoding complete: {result.ProvinceCount} provinces encoded", "map_textures");
            return result;
        }

        /// <summary>
        /// Create province ID encoding from color frequency map
        /// </summary>
        /// <param name="colorFrequencies">Dictionary of colors and their pixel counts</param>
        /// <param name="sortByFrequency">Sort IDs by frequency (most common gets lowest ID)</param>
        /// <returns>Encoding result</returns>
        public static EncodingResult EncodeProvinceIDsFromFrequencies(
            NativeHashMap<Color32, int> colorFrequencies,
            bool sortByFrequency = false)
        {
            var result = new EncodingResult();

            if (colorFrequencies.Count == 0)
            {
                result.ErrorMessage = "No color frequencies provided";
                return result;
            }

            // Extract colors
            var colors = new NativeList<Color32>(colorFrequencies.Count, Allocator.Temp);
            var frequencies = new NativeList<int>(colorFrequencies.Count, Allocator.Temp);

            foreach (var kvp in colorFrequencies)
            {
                colors.Add(kvp.Key);
                frequencies.Add(kvp.Value);
            }

            // Sort by frequency if requested (descending order)
            if (sortByFrequency)
            {
                SortColorsByFrequency(colors, frequencies);
            }

            // Convert to array and encode
            var colorArray = colors.AsArray();
            result = EncodeProvinceIDs(colorArray, true);

            colors.Dispose();
            frequencies.Dispose();

            return result;
        }

        /// <summary>
        /// Validate encoding integrity (bidirectional lookup consistency)
        /// </summary>
        private static bool ValidateEncodingIntegrity(
            NativeHashMap<Color32, ushort> colorToID,
            NativeHashMap<ushort, Color32> idToColor)
        {
            // Check that all color->ID mappings have corresponding ID->color mappings
            foreach (var kvp in colorToID)
            {
                if (!idToColor.ContainsKey(kvp.Value))
                {
                    ArchonLogger.LogError($"Missing reverse mapping for color {kvp.Key} -> ID {kvp.Value}", "map_textures");
                    return false;
                }

                if (!idToColor[kvp.Value].Equals(kvp.Key))
                {
                    ArchonLogger.LogError($"Inconsistent mapping: {kvp.Key} -> {kvp.Value} -> {idToColor[kvp.Value]}", "map_textures");
                    return false;
                }
            }

            // Check that all ID->color mappings have corresponding color->ID mappings
            foreach (var kvp in idToColor)
            {
                if (!colorToID.ContainsKey(kvp.Value))
                {
                    ArchonLogger.LogError($"Missing forward mapping for ID {kvp.Key} -> color {kvp.Value}", "map_textures");
                    return false;
                }

                if (colorToID[kvp.Value] != kvp.Key)
                {
                    ArchonLogger.LogError($"Inconsistent mapping: {kvp.Key} -> {kvp.Value} -> {colorToID[kvp.Value]}", "map_textures");
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Sort colors by frequency in descending order (most common first).
        /// Uses insertion sort which is efficient for typical province counts.
        /// </summary>
        private static void SortColorsByFrequency(NativeList<Color32> colors, NativeList<int> frequencies)
        {
            // Simple insertion sort (efficient for typical province counts)
            for (int i = 1; i < colors.Length; i++)
            {
                Color32 keyColor = colors[i];
                int keyFreq = frequencies[i];
                int j = i - 1;

                // Move elements with lower frequency to the right
                while (j >= 0 && frequencies[j] < keyFreq)
                {
                    colors[j + 1] = colors[j];
                    frequencies[j + 1] = frequencies[j];
                    j--;
                }

                colors[j + 1] = keyColor;
                frequencies[j + 1] = keyFreq;
            }
        }

        /// <summary>
        /// Check if province ID is within valid range
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsValidProvinceID(ushort provinceID)
        {
            // ID 0 is reserved for ocean, 65535 for invalid/error
            return provinceID > 0 && provinceID < 65535;
        }

        /// <summary>
        /// Get the maximum theoretical province count
        /// </summary>
        public static int GetMaxProvinceCount()
        {
            return 65534; // 0 reserved for ocean, 65535 reserved for error
        }

        /// <summary>
        /// Create test pattern for validation
        /// </summary>
        public static void CreateTestPattern(int width, int height, out NativeArray<Color32> testPixels)
        {
            testPixels = new NativeArray<Color32>(width * height, Allocator.Temp);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int index = y * width + x;

                    // Create a test pattern with unique colors
                    byte r = (byte)((x * 255) / width);
                    byte g = (byte)((y * 255) / height);
                    byte b = (byte)(((x + y) * 255) / (width + height));

                    testPixels[index] = new Color32(r, g, b, 255);
                }
            }
        }

        /// <summary>
        /// Analyze province ID distribution for debugging
        /// </summary>
        public static void AnalyzeIDDistribution(NativeHashMap<ushort, Color32> idToColor)
        {
            if (idToColor.Count == 0)
            {
                ArchonLogger.Log("No province IDs to analyze", "map_textures");
                return;
            }

            int minID = int.MaxValue;
            int maxID = int.MinValue;
            int totalIDs = idToColor.Count;

            foreach (var kvp in idToColor)
            {
                minID = math.min(minID, kvp.Key);
                maxID = math.max(maxID, kvp.Key);
            }

            ArchonLogger.Log($"Province ID Distribution Analysis:", "map_textures");
            ArchonLogger.Log($"  Total Provinces: {totalIDs}", "map_textures");
            ArchonLogger.Log($"  ID Range: {minID} to {maxID}", "map_textures");
            ArchonLogger.Log($"  ID Span: {maxID - minID + 1}", "map_textures");
            ArchonLogger.Log($"  Efficiency: {(float)totalIDs / (maxID - minID + 1) * 100f:F1}%", "map_textures");

            // Check for ID 0 (ocean)
            if (idToColor.ContainsKey(0))
            {
                ArchonLogger.Log($"  Ocean Color: {idToColor[0]}", "map_textures");
            }

            // Sample some province colors
            int sampleCount = math.min(5, totalIDs);
            ArchonLogger.Log($"  Sample Province Colors:", "map_textures");
            int samples = 0;
            foreach (var kvp in idToColor)
            {
                if (samples >= sampleCount) break;
                ArchonLogger.Log($"    ID {kvp.Key}: {kvp.Value}", "map_textures");
                samples++;
            }
        }
    }
}