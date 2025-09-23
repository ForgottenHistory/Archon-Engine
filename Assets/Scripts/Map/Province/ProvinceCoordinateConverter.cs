using UnityEngine;

public static class ProvinceCoordinateConverter
{
    public static Vector3 PixelToWorldPosition(Vector2Int pixel, Texture2D provinceMap, float mapWidth, float mapHeight, float provinceHeight)
    {
        // Apply same coordinate corrections as SimpleBMPMapViewer
        // Flip X coordinate to match the texture correction
        float correctedX = provinceMap.width - 1 - pixel.x;
        // Flip Y coordinate to match the texture orientation (upside down fix)
        float correctedY = provinceMap.height - 1 - pixel.y;

        float x = (correctedX / (float)provinceMap.width - 0.5f) * mapWidth;
        float z = (correctedY / (float)provinceMap.height - 0.5f) * mapHeight;
        return new Vector3(x, provinceHeight, z);
    }

    public static void CalculatePixelToWorldFactors(Texture2D provinceMap, float mapWidth, float mapHeight, out float pixelToWorldX, out float pixelToWorldZ)
    {
        pixelToWorldX = mapWidth / provinceMap.width;
        pixelToWorldZ = mapHeight / provinceMap.height;
    }
}