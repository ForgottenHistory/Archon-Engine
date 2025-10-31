using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Map.Rendering.Border
{
    /// <summary>
    /// Processes border pixels using median filtering and pixel chaining
    /// Extracted from BorderCurveExtractor for single responsibility
    ///
    /// Contains logic for:
    /// - Median filtering (removes single-pixel noise from province ID texture)
    /// - Pixel chaining (converts scattered border pixels into ordered polylines)
    /// - Junction-aware chaining (starts/ends chains at junction points)
    /// </summary>
    public class MedianFilterProcessor
    {
        private readonly int mapWidth;
        private readonly int mapHeight;
        private readonly Dictionary<Vector2, HashSet<ushort>> junctionPixels;

        public MedianFilterProcessor(int width, int height, Dictionary<Vector2, HashSet<ushort>> junctions)
        {
            mapWidth = width;
            mapHeight = height;
            junctionPixels = junctions;
        }

        /// <summary>
        /// Apply 3x3 median filter to province ID texture
        /// Removes single-pixel noise by replacing each pixel with most common ID in neighborhood
        /// </summary>
        public Color32[] ApplyMedianFilter(Color32[] pixels)
        {
            Color32[] filtered = new Color32[pixels.Length];

            for (int y = 0; y < mapHeight; y++)
            {
                for (int x = 0; x < mapWidth; x++)
                {
                    int centerIdx = y * mapWidth + x;

                    // Collect province IDs in 3x3 window
                    var neighborIDs = new Dictionary<ushort, int>(); // provinceID -> count

                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx;
                            int ny = y + dy;

                            // Skip out of bounds
                            if (nx < 0 || nx >= mapWidth || ny < 0 || ny >= mapHeight)
                                continue;

                            int neighborIdx = ny * mapWidth + nx;
                            ushort provinceID = Province.ProvinceIDEncoder.UnpackProvinceID(pixels[neighborIdx]);

                            if (provinceID > 0) // Skip empty pixels
                            {
                                if (!neighborIDs.ContainsKey(provinceID))
                                    neighborIDs[provinceID] = 0;
                                neighborIDs[provinceID]++;
                            }
                        }
                    }

                    // Find most common province ID (median/mode)
                    ushort mostCommonID = 0;
                    int maxCount = 0;
                    foreach (var kvp in neighborIDs)
                    {
                        if (kvp.Value > maxCount)
                        {
                            maxCount = kvp.Value;
                            mostCommonID = kvp.Key;
                        }
                    }

                    // Use most common ID, or keep original if no neighbors found
                    if (mostCommonID > 0)
                        filtered[centerIdx] = Province.ProvinceIDEncoder.PackProvinceID(mostCommonID);
                    else
                        filtered[centerIdx] = pixels[centerIdx];
                }
            }

            return filtered;
        }

        /// <summary>
        /// Chain scattered border pixels into multiple ordered polylines
        /// Repeatedly calls ChainBorderPixelsSingle until all pixels are consumed
        /// </summary>
        public List<List<Vector2>> ChainBorderPixelsMultiple(List<Vector2> scatteredPixels)
        {
            List<List<Vector2>> allChains = new List<List<Vector2>>();

            if (scatteredPixels.Count == 0)
                return allChains;

            HashSet<Vector2> remaining = new HashSet<Vector2>(scatteredPixels);

            // Keep creating chains until all pixels are used
            while (remaining.Count > 0)
            {
                List<Vector2> currentChain = ChainBorderPixelsSingle(remaining);
                if (currentChain.Count > 0)
                {
                    allChains.Add(currentChain);
                }
                else
                {
                    // Safety: if ChainBorderPixelsSingle returns empty, break to prevent infinite loop
                    break;
                }
            }

            return allChains;
        }

        /// <summary>
        /// Chain scattered pixels into single ordered polyline using greedy nearest-neighbor
        /// STRATEGY: Starts from junction pixel if available (ensures chains end at junctions)
        /// Uses strict 8-connectivity (max distance sqrt(2) ≈ 1.414)
        /// </summary>
        private List<Vector2> ChainBorderPixelsSingle(HashSet<Vector2> remaining)
        {
            if (remaining.Count == 0)
                return new List<Vector2>();

            List<Vector2> orderedPath = new List<Vector2>();

            // STRATEGY: Start from a junction pixel if one exists
            // This ensures chains naturally end at junctions
            Vector2 current = Vector2.zero;
            bool foundJunctionStart = false;

            foreach (var pixel in remaining)
            {
                if (junctionPixels.ContainsKey(pixel))
                {
                    current = pixel;
                    foundJunctionStart = true;
                    break;
                }
            }

            // If no junction found, start with any pixel
            if (!foundJunctionStart)
            {
                current = remaining.First();
            }

            orderedPath.Add(current);
            remaining.Remove(current);

            // Greedy nearest-neighbor chaining with STRICT adjacency
            while (remaining.Count > 0)
            {
                Vector2 nearest = Vector2.zero;
                float minDistSq = float.MaxValue;
                bool foundAdjacent = false;

                // Find nearest adjacent pixel (MUST be 8-neighbor, max distance sqrt(2))
                foreach (var candidate in remaining)
                {
                    float dx = candidate.x - current.x;
                    float dy = candidate.y - current.y;
                    float distSq = dx * dx + dy * dy;

                    // ONLY accept adjacent pixels (8-connectivity: max distance sqrt(2) ≈ 1.414)
                    if (distSq <= 2.0f) // sqrt(2)^2 = 2.0
                    {
                        if (distSq < minDistSq)
                        {
                            nearest = candidate;
                            minDistSq = distSq;
                            foundAdjacent = true;
                        }
                    }
                }

                // Stop conditions:
                // 1. No adjacent pixel found (gap)
                // 2. Current pixel is a junction (endpoint reached)
                if (!foundAdjacent)
                {
                    break; // Gap detected, stop chain
                }

                // Add to path
                orderedPath.Add(nearest);
                remaining.Remove(nearest);
                current = nearest;

                // DON'T stop at junctions - continue chaining through them
                // This ensures chains connect all the way through junction points
            }

            return orderedPath;
        }
    }
}
