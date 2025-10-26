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
        /// Parallel province adjacency scanning using Unity Job System + Burst compilation
        /// Single-pass algorithm that finds borders as it scans the bitmap
        /// </summary>
        [ContextMenu("Scan For Adjacencies")]
        public ScanResult ScanForAdjacencies()
        {
            if (provinceMap == null)
            {
                ArchonLogger.LogError("No province map assigned!", "map_rendering");
                return null;
            }

            float startTime = Time.realtimeSinceStartup;

            Color32[] pixels = provinceMap.GetPixels32();
            int width = provinceMap.width;
            int height = provinceMap.height;

            // For 13,350 provinces, estimate ~100,000 adjacency pairs to be safe
            // (13350 provinces * avg 6 neighbors / 2 for bidirectional = ~40k, but let's use 100k for safety)
            int estimatedAdjacencies = 100000;

            ArchonLogger.Log($"Scanning {width}x{height} bitmap for province adjacencies...", "map_rendering");
            ArchonLogger.Log($"Allocating hash set with capacity for {estimatedAdjacencies} adjacency pairs", "map_rendering");

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
                ArchonLogger.Log($"Adjacency scan complete in {lastScanTime:F3} seconds\n" +
                        $"Found {result.provinceCount} provinces with {result.connectionCount} unique adjacency pairs", "map_rendering");

                if (pairCount > estimatedAdjacencies * 0.8f)
                {
                    ArchonLogger.LogWarning($"Adjacency pairs ({pairCount}) approaching capacity ({estimatedAdjacencies}). " +
                                "Consider increasing estimatedAdjacencies for safety.", "map_rendering");
                }
            }

            return result;
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

            ArchonLogger.Log($"Converted {idAdjacencies.Count} color adjacencies to ID adjacencies", "map_rendering");
        }

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
                ArchonLogger.LogWarning("No ID adjacencies to export! Convert color adjacencies first.", "map_rendering");
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
            ArchonLogger.Log($"Exported adjacencies to: {path}", "map_rendering");

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