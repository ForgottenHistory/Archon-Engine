using System;
using System.IO;
using Unity.Collections;
using UnityEngine;
using Map.Loading.Bitmaps;
using Map.Province;
using Core.Systems;
using Core.Data;

namespace Map.Simulation
{
    /// <summary>
    /// Loads province bitmap data and initializes ProvinceSimulation
    /// Bridges existing ParadoxParser with new dual-layer architecture
    /// Task 1.2: Bitmap to Simulation Conversion
    /// </summary>
    public static class SimulationMapLoader
    {
        /// <summary>
        /// Result of loading and converting bitmap to simulation
        /// </summary>
        public struct SimulationLoadResult
        {
            public bool IsSuccess;
            public string ErrorMessage;
            public ProvinceSimulation Simulation;
            public SimulationMapData MapData;

            public void Dispose()
            {
                Simulation?.Dispose();
                MapData.Dispose();
            }
        }

        /// <summary>
        /// Map data needed for GPU texture generation (Task 1.3)
        /// Stores province pixel boundaries for later GPU processing
        /// </summary>
        public struct SimulationMapData
        {
            public int Width;
            public int Height;
            public NativeArray<ProvinceBounds> ProvinceBounds; // Pixel boundaries per province
            public NativeHashMap<ushort, Color32> ProvinceColors; // Province ID -> RGB color
            public bool IsValid;

            public void Dispose()
            {
                if (ProvinceBounds.IsCreated) ProvinceBounds.Dispose();
                if (ProvinceColors.IsCreated) ProvinceColors.Dispose();
            }
        }

        /// <summary>
        /// Province pixel boundary information for GPU texture generation
        /// </summary>
        public struct ProvinceBounds
        {
            public ushort ProvinceID;
            public int MinX, MinY, MaxX, MaxY; // Bounding rectangle
            public int PixelCount; // Total pixels
            public int CenterX, CenterY; // Approximate center

            public int Width => MaxX - MinX + 1;
            public int Height => MaxY - MinY + 1;
        }

        /// <summary>
        /// Load provinces.bmp and create ProvinceSimulation with full integration
        /// Combines Tasks 1.2.1-1.2.6 from texture-based-map-guide.md
        /// </summary>
        /// <param name="bmpFilePath">Path to provinces.bmp</param>
        /// <param name="definitionCsvPath">Path to definition.csv (optional)</param>
        /// <param name="maxProvinces">Maximum provinces to support (default 10000)</param>
        public static SimulationLoadResult LoadSimulationFromBitmap(
            string bmpFilePath,
            string definitionCsvPath = null,
            int maxProvinces = 10000)
        {
            var result = new SimulationLoadResult();

            // Validate inputs
            if (!File.Exists(bmpFilePath))
            {
                result.ErrorMessage = $"BMP file not found: {bmpFilePath}";
                return result;
            }

            bool useDefinitionCsv = !string.IsNullOrEmpty(definitionCsvPath) && File.Exists(definitionCsvPath);

            ArchonLogger.Log($"Loading simulation from bitmap: {bmpFilePath}", "map_rendering");
            if (useDefinitionCsv)
                ArchonLogger.Log($"Using definition CSV: {definitionCsvPath}", "map_rendering");

            try
            {
                // Load file data
                var bmpData = new NativeArray<byte>(File.ReadAllBytes(bmpFilePath), Allocator.TempJob);
                NativeArray<byte> csvData = default;

                if (useDefinitionCsv)
                    csvData = new NativeArray<byte>(File.ReadAllBytes(definitionCsvPath), Allocator.TempJob);

                try
                {
                    if (useDefinitionCsv)
                    {
                        result = ProcessWithDefinitionCsv(bmpData, csvData, maxProvinces);
                    }
                    else
                    {
                        result = ProcessBitmapOnly(bmpData, maxProvinces);
                    }
                }
                finally
                {
                    if (bmpData.IsCreated) bmpData.Dispose();
                    if (csvData.IsCreated) csvData.Dispose();
                }
            }
            catch (Exception e)
            {
                result.ErrorMessage = $"Failed to load bitmap: {e.Message}";
                ArchonLogger.LogError($"SimulationMapLoader error: {e}", "map_rendering");
            }

            return result;
        }

        /// <summary>
        /// Process bitmap with definition.csv using ProvinceMapParser
        /// Task 1.2.1: Load provinces.bmp using existing optimized ParadoxParser
        /// </summary>
        private static SimulationLoadResult ProcessWithDefinitionCsv(
            NativeArray<byte> bmpData,
            NativeArray<byte> csvData,
            int maxProvinces)
        {
            var result = new SimulationLoadResult();

            // Use existing ProvinceMapParser
            var mapResult = ProvinceMapParser.ParseProvinceMap(bmpData, csvData, Allocator.TempJob);

            if (!mapResult.IsSuccess)
            {
                result.ErrorMessage = "Failed to parse province map with definition CSV";
                return result;
            }

            try
            {
                // Task 1.2.2: Extract unique province IDs (1-65534, reserve 0 for ocean)
                var uniqueProvinces = ExtractUniqueProvinceIDs(mapResult);

                // Task 1.2.4: Validate province count fits in memory target
                if (!ValidateProvinceCount(uniqueProvinces.Length, maxProvinces, out string validationError))
                {
                    result.ErrorMessage = validationError;
                    uniqueProvinces.Dispose();
                    return result;
                }

                // Task 1.2.3: Create ProvinceID→ArrayIndex mapping and initialize simulation
                result.Simulation = CreateSimulationFromProvinceIDs(uniqueProvinces, maxProvinces);

                // Task 1.2.6: Store province pixel boundaries for GPU texture generation
                result.MapData = CreateMapDataFromParserResult(mapResult, uniqueProvinces);

                result.IsSuccess = true;
                uniqueProvinces.Dispose();

                ArchonLogger.Log($"Simulation loaded with {result.Simulation.ProvinceCount} provinces", "map_rendering");
                ArchonLogger.Log($"Memory usage: {result.Simulation.GetMemoryUsage().totalBytes / 1024f:F1} KB", "map_rendering");
            }
            finally
            {
                mapResult.Dispose();
            }

            return result;
        }

        /// <summary>
        /// Process bitmap without CSV using existing ProvinceMapLoader approach
        /// Fallback method when no definition.csv is available
        /// </summary>
        private static SimulationLoadResult ProcessBitmapOnly(NativeArray<byte> bmpData, int maxProvinces)
        {
            var result = new SimulationLoadResult();

            // Parse BMP header first to validate
            var header = BMPParser.ParseHeader(bmpData);
            if (!header.IsValid)
            {
                result.ErrorMessage = "Invalid BMP file format";
                return result;
            }

            var pixelData = BMPParser.GetPixelData(bmpData, header);
            if (!pixelData.IsSuccess)
            {
                result.ErrorMessage = "Failed to extract pixel data from BMP";
                return result;
            }

            try
            {
                // Extract unique colors and assign province IDs
                var colorMapping = ExtractColorsAndAssignIDs(pixelData, maxProvinces);
                if (!colorMapping.IsSuccess)
                {
                    result.ErrorMessage = colorMapping.ErrorMessage;
                    colorMapping.Dispose();
                    return result;
                }

                try
                {
                    // Create simulation from color mapping
                    result.Simulation = CreateSimulationFromColorMapping(colorMapping);
                    result.MapData = CreateMapDataFromPixelData(pixelData, colorMapping);
                    result.IsSuccess = true;

                    ArchonLogger.Log($"Simulation loaded with {result.Simulation.ProvinceCount} provinces (no CSV)", "map_rendering");
                }
                finally
                {
                    colorMapping.Dispose();
                }
            }
            finally
            {
                pixelData.Dispose();
            }

            return result;
        }

        /// <summary>
        /// Extract unique province IDs from parser result
        /// Task 1.2.2: Extract unique province IDs (1-65534, reserve 0 for ocean)
        /// </summary>
        private static NativeArray<int> ExtractUniqueProvinceIDs(ProvinceMapParser.ProvinceMapResult mapResult)
        {
            // The parser already provides unique province IDs
            var uniqueIDs = new NativeArray<int>(mapResult.UniqueProvinceIDs.Length + 1, Allocator.TempJob);

            // Always include ocean (ID 0) first
            uniqueIDs[0] = 0;

            // Copy other province IDs, filtering to ensure they're in valid range
            int validCount = 1; // Start at 1 for ocean
            for (int i = 0; i < mapResult.UniqueProvinceIDs.Length; i++)
            {
                int id = mapResult.UniqueProvinceIDs[i];
                if (id > 0 && id < 65535) // Valid range: 1-65534
                {
                    uniqueIDs[validCount++] = id;
                }
            }

            // Return resized array with only valid IDs
            var result = new NativeArray<int>(validCount, Allocator.TempJob);
            NativeArray<int>.Copy(uniqueIDs, result, validCount);
            uniqueIDs.Dispose();

            return result;
        }

        /// <summary>
        /// Validate province count against memory and performance targets
        /// Task 1.2.5: Validate province count fits in memory target (80KB for 10k provinces)
        /// </summary>
        private static bool ValidateProvinceCount(int provinceCount, int maxProvinces, out string errorMessage)
        {
            errorMessage = null;

            if (provinceCount == 0)
            {
                errorMessage = "No provinces found in map";
                return false;
            }

            if (provinceCount > maxProvinces)
            {
                errorMessage = $"Too many provinces ({provinceCount}). Maximum supported: {maxProvinces}";
                return false;
            }

            // Memory target validation: 8 bytes per province
            int hotMemoryBytes = provinceCount * 8;
            int targetMemoryKB = 80; // 80KB for 10k provinces

            if (hotMemoryBytes > targetMemoryKB * 1024)
            {
                errorMessage = $"Province count ({provinceCount}) exceeds memory target. Hot data: {hotMemoryBytes / 1024}KB, target: {targetMemoryKB}KB";
                return false;
            }

            ArchonLogger.Log($"Province count validation passed: {provinceCount} provinces, {hotMemoryBytes / 1024f:F1}KB hot memory", "map_rendering");
            return true;
        }

        /// <summary>
        /// Create ProvinceSimulation from province IDs
        /// Task 1.2.3: Create ProvinceID→ArrayIndex mapping for O(1) lookups
        /// Task 1.2.4: Initialize ProvinceState array with default values
        /// </summary>
        private static ProvinceSimulation CreateSimulationFromProvinceIDs(NativeArray<int> provinceIDs, int capacity)
        {
            // Count non-ocean provinces
            int nonOceanCount = 0;
            for (int i = 0; i < provinceIDs.Length; i++)
            {
                if (provinceIDs[i] != 0) nonOceanCount++;
            }

            var simulation = new ProvinceSimulation(Math.Max(capacity, nonOceanCount));

            // Add provinces with default terrain based on ID
            for (int i = 0; i < provinceIDs.Length; i++)
            {
                int id = provinceIDs[i];

                // Skip ocean province (ID 0) - reserved but not stored in simulation
                if (id == 0) continue;

                TerrainType terrain = DetermineTerrainFromID(id);

                // Cast to ushort (safe since we validated range in ExtractUniqueProvinceIDs)
                simulation.AddProvince((ushort)id, terrain);
            }

            ArchonLogger.Log($"Created simulation with {simulation.ProvinceCount} provinces, capacity {capacity}", "map_rendering");
            return simulation;
        }

        /// <summary>
        /// Determine terrain type from province ID
        /// Basic heuristic - can be enhanced later with actual terrain data
        /// </summary>
        private static TerrainType DetermineTerrainFromID(int provinceID)
        {
            if (provinceID == 0) return TerrainType.Ocean;

            // Simple heuristic based on ID ranges - can be improved with actual data
            int idMod = provinceID % 100;
            if (idMod < 5) return TerrainType.Hills;
            if (idMod < 15) return TerrainType.Forest;
            if (idMod < 25) return TerrainType.Desert;
            if (idMod < 35) return TerrainType.Mountain;
            return TerrainType.Grassland; // Default
        }

        /// <summary>
        /// Create map data from ProvinceMapParser result
        /// Task 1.2.6: Store province pixel boundaries for GPU texture generation
        /// </summary>
        private static SimulationMapData CreateMapDataFromParserResult(
            ProvinceMapParser.ProvinceMapResult mapResult,
            NativeArray<int> provinceIDs)
        {
            var mapData = new SimulationMapData
            {
                Width = mapResult.PixelData.Header.Width,
                Height = mapResult.PixelData.Header.Height,
                ProvinceBounds = new NativeArray<ProvinceBounds>(provinceIDs.Length, Allocator.Persistent),
                ProvinceColors = new NativeHashMap<ushort, Color32>(provinceIDs.Length, Allocator.Persistent),
                IsValid = true
            };

            // Calculate bounds and colors for each province
            for (int i = 0; i < provinceIDs.Length; i++)
            {
                int provinceID = provinceIDs[i];
                var stats = ProvinceMapParser.CalculateProvinceStats(mapResult, provinceID);

                if (stats.IsValid)
                {
                    mapData.ProvinceBounds[i] = new ProvinceBounds
                    {
                        ProvinceID = (ushort)provinceID,
                        MinX = stats.MinX,
                        MinY = stats.MinY,
                        MaxX = stats.MaxX,
                        MaxY = stats.MaxY,
                        PixelCount = stats.PixelCount,
                        CenterX = stats.Centroid.x,
                        CenterY = stats.Centroid.y
                    };

                    // Get color from mapping
                    if (mapResult.ProvinceIDToColor.TryGetValue(provinceID, out int rgbPacked))
                    {
                        var color = new Color32(
                            (byte)((rgbPacked >> 16) & 0xFF), // R
                            (byte)((rgbPacked >> 8) & 0xFF),  // G
                            (byte)(rgbPacked & 0xFF),         // B
                            255 // A
                        );
                        mapData.ProvinceColors.TryAdd((ushort)provinceID, color);
                    }
                }
                else
                {
                    ArchonLogger.LogWarning($"Invalid stats for province {provinceID}", "map_rendering");
                }
            }

            return mapData;
        }

        /// <summary>
        /// Color mapping result for bitmap-only processing
        /// </summary>
        private struct ColorMappingResult
        {
            public bool IsSuccess;
            public string ErrorMessage;
            public NativeHashMap<Color32, ushort> ColorToID;
            public NativeArray<ushort> UniqueIDs;

            public void Dispose()
            {
                if (ColorToID.IsCreated) ColorToID.Dispose();
                if (UniqueIDs.IsCreated) UniqueIDs.Dispose();
            }
        }

        /// <summary>
        /// Extract colors from pixel data and assign province IDs
        /// Used when no definition.csv is available
        /// </summary>
        private static ColorMappingResult ExtractColorsAndAssignIDs(BMPParser.BMPPixelData pixelData, int maxProvinces)
        {
            var result = new ColorMappingResult();
            var uniqueColors = new NativeHashSet<Color32>(maxProvinces, Allocator.TempJob);

            // Scan all pixels to find unique colors
            int width = pixelData.Header.Width;
            int height = pixelData.Header.Height;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (BMPParser.TryGetPixelRGB(pixelData, x, y, out byte r, out byte g, out byte b))
                    {
                        var color = new Color32(r, g, b, 255);
                        uniqueColors.Add(color);
                    }
                }
            }

            if (uniqueColors.Count == 0)
            {
                result.ErrorMessage = "No valid pixels found in bitmap";
                uniqueColors.Dispose();
                return result;
            }

            if (uniqueColors.Count > maxProvinces)
            {
                result.ErrorMessage = $"Too many unique colors ({uniqueColors.Count}). Maximum: {maxProvinces}";
                uniqueColors.Dispose();
                return result;
            }

            // Convert to arrays and assign IDs
            var colorArray = uniqueColors.ToNativeArray(Allocator.TempJob);
            uniqueColors.Dispose();

            result.ColorToID = new NativeHashMap<Color32, ushort>(colorArray.Length, Allocator.TempJob);
            result.UniqueIDs = new NativeArray<ushort>(colorArray.Length, Allocator.TempJob);

            for (int i = 0; i < colorArray.Length; i++)
            {
                var color = colorArray[i];
                ushort id;

                // Reserve ID 0 for ocean (black)
                if (color.r == 0 && color.g == 0 && color.b == 0)
                {
                    id = 0;
                }
                else
                {
                    id = (ushort)(i + 1); // Start from 1
                }

                result.ColorToID.TryAdd(color, id);
                result.UniqueIDs[i] = id;
            }

            colorArray.Dispose();
            result.IsSuccess = true;
            return result;
        }

        /// <summary>
        /// Create simulation from color mapping (bitmap-only path)
        /// </summary>
        private static ProvinceSimulation CreateSimulationFromColorMapping(ColorMappingResult colorMapping)
        {
            // Count non-ocean provinces for capacity
            int nonOceanCount = 0;
            for (int i = 0; i < colorMapping.UniqueIDs.Length; i++)
            {
                if (colorMapping.UniqueIDs[i] != 0) nonOceanCount++;
            }

            var simulation = new ProvinceSimulation(Math.Max(nonOceanCount, 1));

            for (int i = 0; i < colorMapping.UniqueIDs.Length; i++)
            {
                ushort id = colorMapping.UniqueIDs[i];

                // Skip ocean province (ID 0) - reserved but not stored in simulation
                // Ocean is handled at GPU texture level, not simulation level
                if (id == 0) continue;

                TerrainType terrain = DetermineTerrainFromID(id);
                simulation.AddProvince(id, terrain);
            }

            return simulation;
        }

        /// <summary>
        /// Create map data from pixel data (bitmap-only path)
        /// </summary>
        private static SimulationMapData CreateMapDataFromPixelData(
            BMPParser.BMPPixelData pixelData,
            ColorMappingResult colorMapping)
        {
            var mapData = new SimulationMapData
            {
                Width = pixelData.Header.Width,
                Height = pixelData.Header.Height,
                ProvinceBounds = new NativeArray<ProvinceBounds>(colorMapping.UniqueIDs.Length, Allocator.Persistent),
                ProvinceColors = new NativeHashMap<ushort, Color32>(colorMapping.UniqueIDs.Length, Allocator.Persistent),
                IsValid = true
            };

            // Calculate bounds for each province by scanning pixels
            for (int i = 0; i < colorMapping.UniqueIDs.Length; i++)
            {
                ushort provinceID = colorMapping.UniqueIDs[i];

                // Find the color for this province
                Color32 provinceColor = default;
                foreach (var kvp in colorMapping.ColorToID)
                {
                    if (kvp.Value == provinceID)
                    {
                        provinceColor = kvp.Key;
                        break;
                    }
                }

                // Calculate bounds by scanning all pixels
                var bounds = CalculateBoundsForColor(pixelData, provinceColor, provinceID);
                mapData.ProvinceBounds[i] = bounds;
                mapData.ProvinceColors.TryAdd(provinceID, provinceColor);
            }

            return mapData;
        }

        /// <summary>
        /// Calculate bounds for a specific color in pixel data
        /// </summary>
        private static ProvinceBounds CalculateBoundsForColor(
            BMPParser.BMPPixelData pixelData,
            Color32 targetColor,
            ushort provinceID)
        {
            int width = pixelData.Header.Width;
            int height = pixelData.Header.Height;

            int minX = int.MaxValue, minY = int.MaxValue;
            int maxX = int.MinValue, maxY = int.MinValue;
            int pixelCount = 0;
            long sumX = 0, sumY = 0;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (BMPParser.TryGetPixelRGB(pixelData, x, y, out byte r, out byte g, out byte b))
                    {
                        var color = new Color32(r, g, b, 255);
                        if (color.r == targetColor.r && color.g == targetColor.g && color.b == targetColor.b)
                        {
                            minX = Math.Min(minX, x);
                            minY = Math.Min(minY, y);
                            maxX = Math.Max(maxX, x);
                            maxY = Math.Max(maxY, y);
                            pixelCount++;
                            sumX += x;
                            sumY += y;
                        }
                    }
                }
            }

            return new ProvinceBounds
            {
                ProvinceID = provinceID,
                MinX = minX == int.MaxValue ? 0 : minX,
                MinY = minY == int.MaxValue ? 0 : minY,
                MaxX = maxX == int.MinValue ? 0 : maxX,
                MaxY = maxY == int.MinValue ? 0 : maxY,
                PixelCount = pixelCount,
                CenterX = pixelCount > 0 ? (int)(sumX / pixelCount) : 0,
                CenterY = pixelCount > 0 ? (int)(sumY / pixelCount) : 0
            };
        }
    }
}