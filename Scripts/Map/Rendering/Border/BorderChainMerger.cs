using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Unity.Collections;
using Utils;

namespace Map.Rendering.Border
{
    /// <summary>
    /// Merges multiple border pixel chains into single connected polylines
    /// Extracted from BorderCurveExtractor for single responsibility
    ///
    /// Contains logic for:
    /// - Merging multiple chains by finding closest endpoints
    /// - U-turn detection and rejection to prevent bad geometry
    /// - Chain simplification (keeping only longest chains)
    /// - Junction bridging (connecting gaps to junctions via BFS)
    /// </summary>
    public class BorderChainMerger
    {
        private readonly int mapWidth;
        private readonly int mapHeight;
        private readonly Color32[] provinceIDPixels;
        private readonly Dictionary<Vector2, HashSet<ushort>> junctionPixels;

        public BorderChainMerger(int width, int height, Color32[] idPixels, Dictionary<Vector2, HashSet<ushort>> junctions)
        {
            mapWidth = width;
            mapHeight = height;
            provinceIDPixels = idPixels;
            junctionPixels = junctions;
        }

        /// <summary>
        /// Merge multiple chains into single polyline using intelligent endpoint matching
        /// Includes U-turn detection to prevent bad geometry
        /// </summary>
        public List<Vector2> MergeChains(List<List<Vector2>> chains)
        {
            if (chains.Count == 0)
                return new List<Vector2>();

            if (chains.Count == 1)
                return chains[0];

            // Start with longest chain
            int longestIdx = 0;
            int longestCount = chains[0].Count;
            for (int i = 1; i < chains.Count; i++)
            {
                if (chains[i].Count > longestCount)
                {
                    longestCount = chains[i].Count;
                    longestIdx = i;
                }
            }

            List<Vector2> merged = new List<Vector2>(chains[longestIdx]);
            HashSet<int> usedIndices = new HashSet<int> { longestIdx };

            // Keep merging closest chains until all are connected
            while (usedIndices.Count < chains.Count)
            {
                float bestDist = float.MaxValue;
                int bestChainIdx = -1;
                bool appendToEnd = true;
                bool reverseChain = false;

                // Find closest unused chain
                for (int i = 0; i < chains.Count; i++)
                {
                    if (usedIndices.Contains(i)) continue;

                    var chain = chains[i];
                    Vector2 mergedStart = merged[0];
                    Vector2 mergedEnd = merged[merged.Count - 1];
                    Vector2 chainStart = chain[0];
                    Vector2 chainEnd = chain[chain.Count - 1];

                    // Check all 4 connection possibilities
                    float d1 = Vector2.Distance(mergedEnd, chainStart); // Append chain normally
                    float d2 = Vector2.Distance(mergedEnd, chainEnd);   // Append chain reversed
                    float d3 = Vector2.Distance(mergedStart, chainEnd); // Prepend chain normally
                    float d4 = Vector2.Distance(mergedStart, chainStart); // Prepend chain reversed

                    float minDist = Mathf.Min(d1, Mathf.Min(d2, Mathf.Min(d3, d4)));

                    if (minDist < bestDist)
                    {
                        bestDist = minDist;
                        bestChainIdx = i;

                        if (minDist == d1) { appendToEnd = true; reverseChain = false; }
                        else if (minDist == d2) { appendToEnd = true; reverseChain = true; }
                        else if (minDist == d3) { appendToEnd = false; reverseChain = false; }
                        else { appendToEnd = false; reverseChain = true; }
                    }
                }

                // CRITICAL: Only merge if chains are close (within 5 pixels)
                // Prevents creating U-turn artifacts from connecting distant chains
                const float MAX_MERGE_DISTANCE = 5.0f;

                if (bestChainIdx >= 0 && bestDist <= MAX_MERGE_DISTANCE)
                {
                    var chainToMerge = chains[bestChainIdx];
                    if (reverseChain)
                    {
                        chainToMerge = new List<Vector2>(chainToMerge);
                        chainToMerge.Reverse();
                    }

                    // CRITICAL: Check if merge would create backtracking (U-turn)
                    bool wouldCreateUturn = false;

                    // Check angle at connection point (sharp turn check)
                    if (merged.Count >= 2 && chainToMerge.Count >= 2)
                    {
                        if (appendToEnd)
                        {
                            Vector2 beforeConnection = merged[merged.Count - 2];
                            Vector2 connectionPoint = merged[merged.Count - 1];
                            Vector2 afterConnection = chainToMerge[1];

                            float angle = BorderGeometryUtils.CalculateAngle(beforeConnection, connectionPoint, afterConnection);
                            if (angle > 90f) // Sharp turn at connection
                            {
                                wouldCreateUturn = true;
                            }
                        }
                        else
                        {
                            Vector2 beforeConnection = chainToMerge[chainToMerge.Count - 2];
                            Vector2 connectionPoint = chainToMerge[chainToMerge.Count - 1];
                            Vector2 afterConnection = merged[1];

                            float angle = BorderGeometryUtils.CalculateAngle(beforeConnection, connectionPoint, afterConnection);
                            if (angle > 90f) // Sharp turn at connection
                            {
                                wouldCreateUturn = true;
                            }
                        }
                    }

                    if (!wouldCreateUturn)
                    {
                        if (appendToEnd)
                            merged.AddRange(chainToMerge);
                        else
                            merged.InsertRange(0, chainToMerge);

                        usedIndices.Add(bestChainIdx);
                    }
                    else
                    {
                        // Reject merge - would create U-turn
                        break;
                    }
                }
                else
                {
                    // Can't merge - chains too far apart or no more chains
                    break;
                }
            }

            return merged;
        }

        /// <summary>
        /// Simple chain merging without U-turn angle check
        /// Uses self-intersection detection instead (more robust)
        /// </summary>
        public List<Vector2> MergeChainsSimple(List<List<Vector2>> chains, ushort provinceA = 0, ushort provinceB = 0)
        {
            if (chains.Count == 0)
                return new List<Vector2>();
            if (chains.Count == 1)
                return chains[0];

            // Start with longest chain
            int longestIdx = 0;
            int longestCount = chains[0].Count;
            for (int i = 1; i < chains.Count; i++)
            {
                if (chains[i].Count > longestCount)
                {
                    longestCount = chains[i].Count;
                    longestIdx = i;
                }
            }

            List<Vector2> merged = new List<Vector2>(chains[longestIdx]);
            HashSet<int> usedIndices = new HashSet<int> { longestIdx };

            // Merge remaining chains by closest endpoint (no U-turn detection)
            while (usedIndices.Count < chains.Count)
            {
                float bestDist = float.MaxValue;
                int bestChainIdx = -1;
                bool appendToEnd = true;
                bool reverseChain = false;

                for (int i = 0; i < chains.Count; i++)
                {
                    if (usedIndices.Contains(i)) continue;

                    var chain = chains[i];
                    Vector2 mergedStart = merged[0];
                    Vector2 mergedEnd = merged[merged.Count - 1];
                    Vector2 chainStart = chain[0];
                    Vector2 chainEnd = chain[chain.Count - 1];

                    float d1 = Vector2.Distance(mergedEnd, chainStart);
                    float d2 = Vector2.Distance(mergedEnd, chainEnd);
                    float d3 = Vector2.Distance(mergedStart, chainEnd);
                    float d4 = Vector2.Distance(mergedStart, chainStart);

                    float minDist = Mathf.Min(d1, Mathf.Min(d2, Mathf.Min(d3, d4)));

                    if (minDist < bestDist)
                    {
                        bestDist = minDist;
                        bestChainIdx = i;

                        if (minDist == d1) { appendToEnd = true; reverseChain = false; }
                        else if (minDist == d2) { appendToEnd = true; reverseChain = true; }
                        else if (minDist == d3) { appendToEnd = false; reverseChain = false; }
                        else { appendToEnd = false; reverseChain = true; }
                    }
                }

                // Merge if within reasonable distance
                const float MAX_MERGE_DISTANCE = 5.0f;
                if (bestChainIdx >= 0 && bestDist <= MAX_MERGE_DISTANCE)
                {
                    var chainToMerge = chains[bestChainIdx];
                    if (reverseChain)
                    {
                        chainToMerge = new List<Vector2>(chainToMerge);
                        chainToMerge.Reverse();
                    }

                    // CRITICAL: Test merge and check if it creates self-intersection (U-turn)
                    // U-turns always self-intersect, legitimate borders don't
                    List<Vector2> testMerge = new List<Vector2>(merged);
                    if (appendToEnd)
                        testMerge.AddRange(chainToMerge);
                    else
                        testMerge.InsertRange(0, chainToMerge);

                    bool wouldSelfIntersect = BorderGeometryUtils.PathSelfIntersects(testMerge);

                    if (!wouldSelfIntersect)
                    {
                        if (appendToEnd)
                            merged.AddRange(chainToMerge);
                        else
                            merged.InsertRange(0, chainToMerge);

                        usedIndices.Add(bestChainIdx);
                    }
                    else
                    {
                        // Log rejection (for first 10 cases or debug borders)
                        if (usedIndices.Count < 10 || provinceA == 1160 || provinceB == 1160)
                        {
                            ArchonLogger.Log($"SELF-INTERSECTION DETECTED {provinceA}-{provinceB}: Rejected chain {bestChainIdx} ({chainToMerge.Count}px) - would create U-turn", "map_initialization");
                        }
                        break; // Skip this chain - would create U-turn
                    }
                }
                else
                {
                    break; // Too far apart
                }
            }

            return merged;
        }

        /// <summary>
        /// Bridge disconnected border pixels to junction using BFS pathfinding
        /// Finds shortest path along valid border pixels to connect gaps
        /// </summary>
        public int BridgeToJunction(Vector2 junctionPos, ushort provinceA, ushort provinceB, List<Vector2> borderPixels)
        {
            int jx = (int)junctionPos.x;
            int jy = (int)junctionPos.y;

            // Check if junction already has an 8-connected neighbor in borderPixels
            bool hasConnection = false;
            int[] dx = { 1, -1, 0, 0, 1, -1, 1, -1 };
            int[] dy = { 0, 0, 1, -1, 1, 1, -1, -1 };

            for (int i = 0; i < 8; i++)
            {
                Vector2 neighbor = new Vector2(jx + dx[i], jy + dy[i]);
                if (borderPixels.Contains(neighbor))
                {
                    hasConnection = true;
                    break;
                }
            }

            // If already connected, no bridge needed
            if (hasConnection)
                return 0;

            // BFS to find shortest path from any existing border pixel to junction
            var queue = new Queue<Vector2>();
            var visited = new HashSet<Vector2>();
            var parent = new Dictionary<Vector2, Vector2>();

            // Start BFS from junction
            queue.Enqueue(junctionPos);
            visited.Add(junctionPos);

            Vector2 connectionPoint = Vector2.zero;
            bool foundPath = false;

            // BFS search (max distance: 10 pixels to avoid runaway searches)
            int maxDistance = 10;
            int currentDistance = 0;

            while (queue.Count > 0 && currentDistance < maxDistance)
            {
                int levelSize = queue.Count;
                currentDistance++;

                for (int level = 0; level < levelSize; level++)
                {
                    Vector2 current = queue.Dequeue();
                    int cx = (int)current.x;
                    int cy = (int)current.y;

                    // Check 8-neighbors
                    for (int i = 0; i < 8; i++)
                    {
                        int nx = cx + dx[i];
                        int ny = cy + dy[i];

                        // Bounds check
                        if (nx < 0 || nx >= mapWidth || ny < 0 || ny >= mapHeight)
                            continue;

                        Vector2 neighborPos = new Vector2(nx, ny);

                        // Skip if already visited
                        if (visited.Contains(neighborPos))
                            continue;

                        // If this pixel is already in borderPixels, we found a connection!
                        if (borderPixels.Contains(neighborPos))
                        {
                            connectionPoint = neighborPos;
                            parent[neighborPos] = current;
                            foundPath = true;
                            break;
                        }

                        // Check if this pixel is a valid border pixel (borders both A and B or is part of junction)
                        int index = ny * mapWidth + nx;
                        ushort pixelProvince = Province.ProvinceIDEncoder.UnpackProvinceID(provinceIDPixels[index]);

                        bool isValidBridge = false;

                        // Valid if it's part of A or B and borders the other
                        if (pixelProvince == provinceA && HasNeighborProvince(nx, ny, provinceB))
                        {
                            isValidBridge = true;
                        }
                        else if (pixelProvince == provinceB && HasNeighborProvince(nx, ny, provinceA))
                        {
                            isValidBridge = true;
                        }
                        // Also valid if it's part of a junction (touches A and B)
                        else if (junctionPixels.ContainsKey(neighborPos))
                        {
                            var junctionProvinces = junctionPixels[neighborPos];
                            if (junctionProvinces.Contains(provinceA) && junctionProvinces.Contains(provinceB))
                            {
                                isValidBridge = true;
                            }
                        }

                        if (isValidBridge)
                        {
                            visited.Add(neighborPos);
                            parent[neighborPos] = current;
                            queue.Enqueue(neighborPos);
                        }
                    }

                    if (foundPath)
                        break;
                }

                if (foundPath)
                    break;
            }

            // If no path found, return 0
            if (!foundPath)
                return 0;

            // Trace back path and add bridge pixels
            int bridgeCount = 0;
            Vector2 tracePos = connectionPoint;

            while (parent.ContainsKey(tracePos) && tracePos != junctionPos)
            {
                Vector2 nextPos = parent[tracePos];
                if (!borderPixels.Contains(nextPos) && nextPos != junctionPos)
                {
                    borderPixels.Add(nextPos);
                    bridgeCount++;
                }
                tracePos = nextPos;
            }

            return bridgeCount;
        }

        /// <summary>
        /// Check if pixel at (x,y) has a neighbor with targetProvinceID
        /// </summary>
        private bool HasNeighborProvince(int x, int y, ushort targetProvinceID)
        {
            int[] dx = { 1, -1, 0, 0, 1, -1, 1, -1 };
            int[] dy = { 0, 0, 1, -1, 1, 1, -1, -1 };

            for (int i = 0; i < 8; i++)
            {
                int nx = x + dx[i];
                int ny = y + dy[i];

                if (nx < 0 || nx >= mapWidth || ny < 0 || ny >= mapHeight)
                    continue;

                int index = ny * mapWidth + nx;
                ushort neighborProvince = Province.ProvinceIDEncoder.UnpackProvinceID(provinceIDPixels[index]);

                if (neighborProvince == targetProvinceID)
                    return true;
            }

            return false;
        }
    }
}
