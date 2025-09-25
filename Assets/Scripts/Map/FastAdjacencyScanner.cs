using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;
using Unity.Jobs;
using Unity.Burst;
using Unity.Mathematics;

namespace ProvinceSystem
{
    /// <summary>
    /// Fast adjacency scanner that mimics how Paradox games detect neighbors
    /// Single-pass algorithm that finds borders as it scans the bitmap
    /// </summary>
    public class FastAdjacencyScanner : MonoBehaviour
    {
        [Header("References")]
        public Texture2D provinceMap;

        [Header("Settings")]
        public bool scanOnStart = false;
        public bool ignoreDiagonals = false; // Only check 4-connectivity vs 8-connectivity
        public float blackThreshold = 10f; // RGB values below this are considered borders/ocean

        [Header("Debug")]
        public bool showDebugInfo = true;

        // Results
        private Dictionary<Color32, HashSet<Color32>> colorAdjacencies;
        private Dictionary<int, HashSet<int>> idAdjacencies;
        private Dictionary<Color32, int> colorToId;
        private float lastScanTime;

        public class ScanResult
        {
            public Dictionary<Color32, HashSet<Color32>> adjacencies;
            public int provinceCount;
            public int connectionCount;
            public float scanTime;
        }

        void Start()
        {
            if (scanOnStart && provinceMap != null)
            {
                ScanForAdjacencies();
            }
        }

        /// <summary>
        /// Main scanning method - single pass through the bitmap
        /// This is how Paradox likely does it
        /// </summary>
        [ContextMenu("Scan For Adjacencies")]
        public ScanResult ScanForAdjacencies()
        {
            if (provinceMap == null)
            {
                Debug.LogError("No province map assigned!");
                return null;
            }

            float startTime = Time.realtimeSinceStartup;

            Color32[] pixels = provinceMap.GetPixels32();
            int width = provinceMap.width;
            int height = provinceMap.height;

            colorAdjacencies = new Dictionary<Color32, HashSet<Color32>>(new Color32Comparer());
            HashSet<Color32> uniqueColors = new HashSet<Color32>(new Color32Comparer());

            // Single pass through the bitmap
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int index = y * width + x;
                    Color32 currentColor = pixels[index];

                    // Skip black/ocean pixels
                    if (IsBlackOrOcean(currentColor))
                        continue;

                    // Track unique province colors
                    uniqueColors.Add(currentColor);

                    // Ensure this province has an entry
                    if (!colorAdjacencies.ContainsKey(currentColor))
                    {
                        colorAdjacencies[currentColor] = new HashSet<Color32>(new Color32Comparer());
                    }

                    // Check RIGHT neighbor (if not at edge)
                    if (x < width - 1)
                    {
                        Color32 rightColor = pixels[index + 1];
                        CheckAndAddAdjacency(currentColor, rightColor);
                    }

                    // Check BOTTOM neighbor (if not at edge)
                    if (y < height - 1)
                    {
                        Color32 bottomColor = pixels[index + width];
                        CheckAndAddAdjacency(currentColor, bottomColor);
                    }

                    // Optional: Check diagonal neighbors
                    if (!ignoreDiagonals)
                    {
                        // Check BOTTOM-RIGHT
                        if (x < width - 1 && y < height - 1)
                        {
                            Color32 bottomRightColor = pixels[index + width + 1];
                            CheckAndAddAdjacency(currentColor, bottomRightColor);
                        }

                        // Check BOTTOM-LEFT
                        if (x > 0 && y < height - 1)
                        {
                            Color32 bottomLeftColor = pixels[index + width - 1];
                            CheckAndAddAdjacency(currentColor, bottomLeftColor);
                        }
                    }

                    // Note: We only check right and bottom (and their diagonals) to avoid 
                    // duplicate checks. The left and top neighbors were already checked
                    // when processing those pixels.
                }
            }

            lastScanTime = Time.realtimeSinceStartup - startTime;

            // Count total connections
            int totalConnections = 0;
            foreach (var adjacencySet in colorAdjacencies.Values)
            {
                totalConnections += adjacencySet.Count;
            }

            var result = new ScanResult
            {
                adjacencies = colorAdjacencies,
                provinceCount = uniqueColors.Count,
                connectionCount = totalConnections / 2, // Each connection is counted twice
                scanTime = lastScanTime
            };

            if (showDebugInfo)
            {
                Debug.Log($"Adjacency scan complete in {lastScanTime:F3} seconds\n" +
                         $"Found {result.provinceCount} provinces with {result.connectionCount} connections");
            }

            return result;
        }

        /// <summary>
        /// Parallel version using Unity Job System for even faster scanning
        /// </summary>
        [ContextMenu("Scan For Adjacencies (Parallel)")]
        public ScanResult ScanForAdjacenciesParallel()
        {
            if (provinceMap == null)
            {
                Debug.LogError("No province map assigned!");
                return null;
            }

            float startTime = Time.realtimeSinceStartup;

            Color32[] pixels = provinceMap.GetPixels32();
            int width = provinceMap.width;
            int height = provinceMap.height;

            // For 13,350 provinces, estimate ~100,000 adjacency pairs to be safe
            // (13350 provinces * avg 6 neighbors / 2 for bidirectional = ~40k, but let's use 100k for safety)
            int estimatedAdjacencies = 100000;

            Debug.Log($"Scanning {width}x{height} bitmap for province adjacencies...");
            Debug.Log($"Allocating hash set with capacity for {estimatedAdjacencies} adjacency pairs");

            // Use native collections for job
            var nativePixels = new NativeArray<Color32>(pixels, Allocator.TempJob);
            var adjacencyPairs = new NativeParallelHashSet<ulong>(estimatedAdjacencies, Allocator.TempJob);

            var scanJob = new AdjacencyScanJob
            {
                pixels = nativePixels,
                width = width,
                height = height,
                blackThreshold = blackThreshold,
                ignoreDiagonals = ignoreDiagonals,
                adjacencyPairs = adjacencyPairs.AsParallelWriter()
            };

            // Run job in parallel
            int batchSize = Mathf.Max(1, (width * height) / (SystemInfo.processorCount * 4));
            JobHandle handle = scanJob.Schedule(width * height, batchSize);
            handle.Complete();

            // Convert results back to managed collections
            colorAdjacencies = new Dictionary<Color32, HashSet<Color32>>(new Color32Comparer());
            HashSet<Color32> uniqueColors = new HashSet<Color32>(new Color32Comparer());

            int pairCount = 0;
            foreach (ulong pair in adjacencyPairs)
            {
                pairCount++;
                uint color1 = (uint)(pair >> 32);
                uint color2 = (uint)(pair & 0xFFFFFFFF);

                Color32 c1 = UIntToColor32(color1);
                Color32 c2 = UIntToColor32(color2);

                uniqueColors.Add(c1);
                uniqueColors.Add(c2);

                if (!colorAdjacencies.ContainsKey(c1))
                    colorAdjacencies[c1] = new HashSet<Color32>(new Color32Comparer());
                if (!colorAdjacencies.ContainsKey(c2))
                    colorAdjacencies[c2] = new HashSet<Color32>(new Color32Comparer());

                colorAdjacencies[c1].Add(c2);
                colorAdjacencies[c2].Add(c1);
            }

            // Cleanup
            nativePixels.Dispose();
            adjacencyPairs.Dispose();

            lastScanTime = Time.realtimeSinceStartup - startTime;

            var result = new ScanResult
            {
                adjacencies = colorAdjacencies,
                provinceCount = uniqueColors.Count,
                connectionCount = pairCount,
                scanTime = lastScanTime
            };

            if (showDebugInfo)
            {
                Debug.Log($"Parallel adjacency scan complete in {lastScanTime:F3} seconds\n" +
                        $"Found {result.provinceCount} provinces with {result.connectionCount} unique adjacency pairs");

                if (pairCount > estimatedAdjacencies * 0.8f)
                {
                    Debug.LogWarning($"Adjacency pairs ({pairCount}) approaching capacity ({estimatedAdjacencies}). " +
                                "Consider increasing estimatedAdjacencies for safety.");
                }
            }

            return result;
        }

        private void CheckAndAddAdjacency(Color32 color1, Color32 color2)
        {
            // Skip if same color or if second color is black/ocean
            if (ColorsEqual(color1, color2) || IsBlackOrOcean(color2))
                return;

            // Add bidirectional adjacency
            if (!colorAdjacencies.ContainsKey(color2))
            {
                colorAdjacencies[color2] = new HashSet<Color32>(new Color32Comparer());
            }

            colorAdjacencies[color1].Add(color2);
            colorAdjacencies[color2].Add(color1);
        }

        private bool IsBlackOrOcean(Color32 color)
        {
            return color.r < blackThreshold &&
                   color.g < blackThreshold &&
                   color.b < blackThreshold;
        }

        private bool ColorsEqual(Color32 a, Color32 b)
        {
            return a.r == b.r && a.g == b.g && a.b == b.b;
        }

        private uint Color32ToUInt(Color32 c)
        {
            return ((uint)c.r << 24) | ((uint)c.g << 16) | ((uint)c.b << 8) | (uint)c.a;
        }

        private Color32 UIntToColor32(uint value)
        {
            byte r = (byte)((value >> 24) & 0xFF);
            byte g = (byte)((value >> 16) & 0xFF);
            byte b = (byte)((value >> 8) & 0xFF);
            byte a = (byte)(value & 0xFF);
            return new Color32(r, g, b, a);
        }

        /// <summary>
        /// Convert color adjacencies to ID adjacencies using a color->ID map
        /// </summary>
        public void ConvertToIdAdjacencies(Dictionary<Color32, int> colorToIdMap)
        {
            this.colorToId = colorToIdMap;
            idAdjacencies = new Dictionary<int, HashSet<int>>();

            foreach (var kvp in colorAdjacencies)
            {
                if (!colorToIdMap.TryGetValue(kvp.Key, out int provinceId))
                    continue;

                idAdjacencies[provinceId] = new HashSet<int>();

                foreach (Color32 neighborColor in kvp.Value)
                {
                    if (colorToIdMap.TryGetValue(neighborColor, out int neighborId))
                    {
                        idAdjacencies[provinceId].Add(neighborId);
                    }
                }
            }

            Debug.Log($"Converted {idAdjacencies.Count} color adjacencies to ID adjacencies");
        }

        /// <summary>
        /// Build color to ID map from ProvinceDataService
        /// </summary>
        /*
        public void BuildColorToIdMapFromDataService(ProvinceSystem.Services.ProvinceDataService dataService)
        {
            var colorToIdMap = new Dictionary<Color32, int>(new Color32Comparer());

            foreach (var province in dataService.GetAllProvinces().Values)
            {
                Color32 color32 = province.color;
                colorToIdMap[color32] = province.id;
            }

            ConvertToIdAdjacencies(colorToIdMap);
            Debug.Log($"Built color->ID map with {colorToIdMap.Count} entries from ProvinceDataService");
        }
        */

        /// <summary>
        /// Get neighbors for a specific province color
        /// </summary>
        public HashSet<Color32> GetNeighborsForColor(Color32 provinceColor)
        {
            if (colorAdjacencies != null && colorAdjacencies.ContainsKey(provinceColor))
            {
                return new HashSet<Color32>(colorAdjacencies[provinceColor], new Color32Comparer());
            }
            return new HashSet<Color32>(new Color32Comparer());
        }

        /// <summary>
        /// Get neighbors for a specific province ID
        /// </summary>
        public HashSet<int> GetNeighborsForId(int provinceId)
        {
            if (idAdjacencies != null && idAdjacencies.ContainsKey(provinceId))
            {
                return new HashSet<int>(idAdjacencies[provinceId]);
            }
            return new HashSet<int>();
        }

        /// <summary>
        /// Export adjacencies to CSV format (like Paradox adjacencies.csv)
        /// </summary>
        [ContextMenu("Export Adjacencies")]
        public void ExportAdjacencies()
        {
            if (idAdjacencies == null || idAdjacencies.Count == 0)
            {
                Debug.LogWarning("No ID adjacencies to export! Convert color adjacencies first.");
                return;
            }

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine("From;To;Type;Through;Comment");

            HashSet<string> exported = new HashSet<string>();

            foreach (var kvp in idAdjacencies)
            {
                foreach (int neighborId in kvp.Value)
                {
                    // Avoid duplicates (1->2 is same as 2->1)
                    string key = kvp.Key < neighborId ?
                        $"{kvp.Key}_{neighborId}" :
                        $"{neighborId}_{kvp.Key}";

                    if (!exported.Contains(key))
                    {
                        sb.AppendLine($"{kvp.Key};{neighborId};land;;");
                        exported.Add(key);
                    }
                }
            }

            string path = "Assets/adjacencies.csv";
            System.IO.File.WriteAllText(path, sb.ToString());
            Debug.Log($"Exported adjacencies to: {path}");

#if UNITY_EDITOR
            UnityEditor.AssetDatabase.Refresh();
#endif
        }

        public Dictionary<Color32, HashSet<Color32>> ColorAdjacencies => colorAdjacencies;
        public Dictionary<int, HashSet<int>> IdAdjacencies => idAdjacencies;
        public float LastScanTime => lastScanTime;
    }

    /// <summary>
    /// Burst-compiled job for parallel adjacency scanning
    /// </summary>
    [BurstCompile]
    public struct AdjacencyScanJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<Color32> pixels;
        [ReadOnly] public int width;
        [ReadOnly] public int height;
        [ReadOnly] public float blackThreshold;
        [ReadOnly] public bool ignoreDiagonals;

        [NativeDisableParallelForRestriction]
        public NativeParallelHashSet<ulong>.ParallelWriter adjacencyPairs;

        public void Execute(int index)
        {
            int x = index % width;
            int y = index / width;

            Color32 current = pixels[index];

            // Skip black/ocean
            if (current.r < blackThreshold &&
                current.g < blackThreshold &&
                current.b < blackThreshold)
                return;

            uint currentUint = Color32ToUInt(current);

            // Check right
            if (x < width - 1)
            {
                CheckNeighbor(currentUint, pixels[index + 1]);
            }

            // Check bottom
            if (y < height - 1)
            {
                CheckNeighbor(currentUint, pixels[index + width]);
            }

            // Check diagonals if enabled
            if (!ignoreDiagonals)
            {
                if (x < width - 1 && y < height - 1)
                    CheckNeighbor(currentUint, pixels[index + width + 1]);

                if (x > 0 && y < height - 1)
                    CheckNeighbor(currentUint, pixels[index + width - 1]);
            }
        }

        private void CheckNeighbor(uint current, Color32 neighbor)
        {
            // Skip if black/ocean
            if (neighbor.r < blackThreshold &&
                neighbor.g < blackThreshold &&
                neighbor.b < blackThreshold)
                return;

            uint neighborUint = Color32ToUInt(neighbor);

            // Skip if same color
            if (current == neighborUint)
                return;

            // Create a unique pair ID (always put smaller value first to avoid duplicates)
            ulong pairId = current < neighborUint ?
                ((ulong)current << 32) | neighborUint :
                ((ulong)neighborUint << 32) | current;

            adjacencyPairs.Add(pairId);
        }

        private uint Color32ToUInt(Color32 c)
        {
            return ((uint)c.r << 24) | ((uint)c.g << 16) | ((uint)c.b << 8) | (uint)c.a;
        }
    }
}