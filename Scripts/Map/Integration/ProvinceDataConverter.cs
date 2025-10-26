using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;
using System.Collections.Generic;
using Map.Province;
using Map.Loading;
using Utils;

namespace Map.Integration
{
    /// <summary>
    /// Converts province map load results to ProvinceDataManager format
    /// Handles pixel grouping, province registration, and initial data setup
    /// Extracted from MapDataIntegrator for single responsibility
    /// </summary>
    public class ProvinceDataConverter
    {
        /// <summary>
        /// Convert ProvinceMapLoader.LoadResult to ProvinceDataManager format
        /// Groups pixels by province ID and initializes province data entries
        /// </summary>
        public static void ConvertLoadResult(ProvinceMapLoader.LoadResult loadResult, ProvinceDataManager dataManager)
        {
            if (!loadResult.Success || dataManager == null)
            {
                ArchonLogger.LogError("ProvinceDataConverter: Invalid parameters - loadResult or dataManager is null", "map_rendering");
                return;
            }

            // Group pixels by province ID
            var provincePixelGroups = GroupPixelsByProvince(loadResult);

            // Add provinces to data manager
            foreach (var kvp in provincePixelGroups)
            {
                ushort provinceID = kvp.Key;
                List<int2> pixelList = kvp.Value;

                // Find the province color from the load result
                Color32 provinceColor = FindProvinceColor(loadResult, provinceID);

                // Convert List to NativeArray for AddProvince
                var pixels = new NativeArray<int2>(pixelList.Count, Allocator.Temp);
                for (int i = 0; i < pixelList.Count; i++)
                {
                    pixels[i] = pixelList[i];
                }

                // Add province to data manager
                dataManager.AddProvince(provinceID, provinceColor, pixels);

                // Clean up temp allocation
                pixels.Dispose();
            }

            ArchonLogger.Log($"ProvinceDataConverter: Converted {provincePixelGroups.Count} provinces to data manager", "map_rendering");
        }

        /// <summary>
        /// Group province pixels by province ID
        /// </summary>
        private static Dictionary<ushort, List<int2>> GroupPixelsByProvince(ProvinceMapLoader.LoadResult loadResult)
        {
            var provincePixelGroups = new Dictionary<ushort, List<int2>>();

            // Initialize groups for all provinces
            foreach (var kvp in loadResult.ColorToID)
            {
                provincePixelGroups[kvp.Value] = new List<int2>();
            }

            // Group pixels by province
            for (int i = 0; i < loadResult.ProvincePixels.Length; i++)
            {
                var pixel = loadResult.ProvincePixels[i];
                if (pixel.ProvinceID > 0) // Skip ocean (ID 0)
                {
                    provincePixelGroups[pixel.ProvinceID].Add(pixel.Position);
                }
            }

            return provincePixelGroups;
        }

        /// <summary>
        /// Find the color for a specific province ID from the load result
        /// </summary>
        private static Color32 FindProvinceColor(ProvinceMapLoader.LoadResult loadResult, ushort provinceID)
        {
            // Search ColorToID dictionary to find the color that maps to this province ID
            foreach (var kvp in loadResult.ColorToID)
            {
                if (kvp.Value == provinceID)
                {
                    return kvp.Key;
                }
            }

            // Fallback to default color if not found
            return new Color32(255, 0, 255, 255); // Magenta for missing provinces
        }

        /// <summary>
        /// Calculate the geometric center of a province from its pixels
        /// </summary>
        private static int2 CalculateProvinceCenter(List<int2> pixels)
        {
            if (pixels.Count == 0)
                return new int2(0, 0);

            // Calculate average position
            long sumX = 0;
            long sumY = 0;

            foreach (var pixel in pixels)
            {
                sumX += pixel.x;
                sumY += pixel.y;
            }

            return new int2(
                (int)(sumX / pixels.Count),
                (int)(sumY / pixels.Count)
            );
        }
    }
}
