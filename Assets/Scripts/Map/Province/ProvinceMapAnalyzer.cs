using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class ProvinceMapAnalyzer
{
    public static Dictionary<Color, ProvinceData> AnalyzeProvinceMap(
        Texture2D provinceMap,
        Texture2D politicalMap,
        bool useProvinceMapColors,
        bool combineSmallProvinces,
        int minPixelsForProvince,
        bool limitProvinceCount,
        int maxProvincesToGenerate,
        float mapWidth,
        float mapHeight,
        float provinceHeight)
    {
        var provinces = new Dictionary<Color, ProvinceData>();
        int width = provinceMap.width;
        int height = provinceMap.height;
        int provinceId = 1;

        // Check if we have a political map for colors
        bool hasPoliticalMap = (politicalMap != null &&
                                politicalMap.width == width &&
                                politicalMap.height == height);

        if (!hasPoliticalMap && !useProvinceMapColors)
        {
            Debug.LogWarning("No political map provided and useProvinceMapColors is false. Using province map colors.");
            useProvinceMapColors = true;
        }

        // First pass: collect all pixels for each province
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Color pixelColor = provinceMap.GetPixel(x, y);

                // Skip near-black pixels (usually ocean or borders)
                if (pixelColor.r < 0.01f && pixelColor.g < 0.01f && pixelColor.b < 0.01f)
                    continue;

                // Round colors to avoid floating point issues
                pixelColor = new Color(
                    Mathf.Round(pixelColor.r * 255f) / 255f,
                    Mathf.Round(pixelColor.g * 255f) / 255f,
                    Mathf.Round(pixelColor.b * 255f) / 255f,
                    1f
                );

                if (!provinces.ContainsKey(pixelColor))
                {
                    // Determine display color
                    Color displayColor = pixelColor;
                    if (hasPoliticalMap)
                    {
                        // Use the color from the political map at this position
                        displayColor = politicalMap.GetPixel(x, y);
                    }
                    else if (useProvinceMapColors)
                    {
                        displayColor = pixelColor;
                    }

                    provinces[pixelColor] = new ProvinceData
                    {
                        color = pixelColor,
                        displayColor = displayColor,
                        id = provinceId++,
                        name = $"Province_{provinceId}",
                        pixelSet = new HashSet<Vector2Int>()
                    };
                }

                provinces[pixelColor].pixels.Add(new Vector2Int(x, y));
                provinces[pixelColor].pixelSet.Add(new Vector2Int(x, y));

                // Update display color if using political map (in case province spans multiple countries)
                // This will use the color at the province center later
                if (hasPoliticalMap && provinces[pixelColor].pixels.Count == 1)
                {
                    provinces[pixelColor].displayColor = politicalMap.GetPixel(x, y);
                }
            }
        }

        // Remove provinces that are too small
        if (combineSmallProvinces)
        {
            var smallProvinces = provinces.Where(p => p.Value.pixels.Count < minPixelsForProvince).ToList();
            foreach (var kvp in smallProvinces)
            {
                provinces.Remove(kvp.Key);
            }
        }

        // Limit province count if requested
        if (limitProvinceCount && provinces.Count > maxProvincesToGenerate)
        {
            var largestProvinces = provinces.OrderByDescending(p => p.Value.pixels.Count)
                                          .Take(maxProvincesToGenerate)
                                          .ToDictionary(p => p.Key, p => p.Value);
            provinces = largestProvinces;
            Debug.Log($"Limited to {maxProvincesToGenerate} largest provinces");
        }

        // Calculate centers and bounds, and update display colors from political map
        foreach (var province in provinces.Values)
        {
            CalculateProvinceCenter(province);
            CalculateProvinceBounds(province, provinceMap, mapWidth, mapHeight, provinceHeight);

            // If we have a political map, use the color at the province center
            if (hasPoliticalMap)
            {
                int centerX = Mathf.RoundToInt(province.center.x);
                int centerY = Mathf.RoundToInt(province.center.y);
                centerX = Mathf.Clamp(centerX, 0, width - 1);
                centerY = Mathf.Clamp(centerY, 0, height - 1);
                province.displayColor = politicalMap.GetPixel(centerX, centerY);
            }
        }

        Debug.Log($"Found {provinces.Count} provinces in bitmap");

        if (hasPoliticalMap)
        {
            // Count unique countries (unique display colors)
            HashSet<Color> uniqueCountries = new HashSet<Color>();
            foreach (var province in provinces.Values)
            {
                Color roundedColor = new Color(
                    Mathf.Round(province.displayColor.r * 255f) / 255f,
                    Mathf.Round(province.displayColor.g * 255f) / 255f,
                    Mathf.Round(province.displayColor.b * 255f) / 255f,
                    1f
                );
                uniqueCountries.Add(roundedColor);
            }
            Debug.Log($"Found {uniqueCountries.Count} unique countries/colors in political map");
        }

        return provinces;
    }

    private static void CalculateProvinceCenter(ProvinceData province)
    {
        Vector2 sum = Vector2.zero;
        foreach (var pixel in province.pixels)
        {
            sum += new Vector2(pixel.x, pixel.y);
        }
        province.center = sum / province.pixels.Count;
    }

    private static void CalculateProvinceBounds(ProvinceData province, Texture2D provinceMap, float mapWidth, float mapHeight, float provinceHeight)
    {
        if (province.pixels.Count == 0) return;

        int minX = int.MaxValue, maxX = int.MinValue;
        int minY = int.MaxValue, maxY = int.MinValue;

        foreach (var pixel in province.pixels)
        {
            minX = Mathf.Min(minX, pixel.x);
            maxX = Mathf.Max(maxX, pixel.x);
            minY = Mathf.Min(minY, pixel.y);
            maxY = Mathf.Max(maxY, pixel.y);
        }

        Vector3 min = ProvinceCoordinateConverter.PixelToWorldPosition(new Vector2Int(minX, minY), provinceMap, mapWidth, mapHeight, provinceHeight);
        Vector3 max = ProvinceCoordinateConverter.PixelToWorldPosition(new Vector2Int(maxX, maxY), provinceMap, mapWidth, mapHeight, provinceHeight);

        province.bounds = new Bounds();
        province.bounds.SetMinMax(min, max);
    }
}