using UnityEngine;
using System;
using System.IO;

public static class BMPLoader
{
    public static Texture2D LoadBMP(string filePath)
    {
        byte[] data = File.ReadAllBytes(filePath);
        return LoadBMP(data);
    }

    public static Texture2D LoadBMP(byte[] data)
    {
        if (data.Length < 54)
        {
            Debug.LogError("Invalid BMP file - too small");
            return null;
        }

        // Check BMP signature
        if (data[0] != 'B' || data[1] != 'M')
        {
            Debug.LogError("Invalid BMP file - wrong signature");
            return null;
        }

        // Read BMP header
        int fileSize = BitConverter.ToInt32(data, 2);
        int pixelArrayOffset = BitConverter.ToInt32(data, 10);
        int headerSize = BitConverter.ToInt32(data, 14);
        int width = BitConverter.ToInt32(data, 18);
        int height = BitConverter.ToInt32(data, 22);
        short colorPlanes = BitConverter.ToInt16(data, 26);
        short bitsPerPixel = BitConverter.ToInt16(data, 28);
        int compression = BitConverter.ToInt32(data, 30);

        Debug.Log($"BMP Info: {width}x{height}, {bitsPerPixel}bpp, compression: {compression}");

        // Only support uncompressed 24-bit BMPs for now
        if (compression != 0)
        {
            Debug.LogError($"Unsupported BMP compression: {compression}");
            return null;
        }

        if (bitsPerPixel != 24 && bitsPerPixel != 32)
        {
            Debug.LogError($"Unsupported BMP bit depth: {bitsPerPixel}");
            return null;
        }

        // Calculate row padding
        int bytesPerPixel = bitsPerPixel / 8;
        int rowSize = ((width * bytesPerPixel + 3) / 4) * 4; // Row size must be multiple of 4
        int paddingSize = rowSize - (width * bytesPerPixel);

        // Create texture
        Texture2D texture = new Texture2D(width, Mathf.Abs(height), TextureFormat.RGB24, false);
        Color[] pixels = new Color[width * Mathf.Abs(height)];

        // Read pixel data
        bool isTopDown = height < 0;
        int absHeight = Mathf.Abs(height);

        for (int y = 0; y < absHeight; y++)
        {
            int rowY = isTopDown ? (absHeight - 1 - y) : y; // Flip the reading direction
            int rowOffset = pixelArrayOffset + (y * rowSize);

            for (int x = 0; x < width; x++)
            {
                int pixelOffset = rowOffset + (x * bytesPerPixel);

                if (pixelOffset + bytesPerPixel > data.Length)
                {
                    Debug.LogError("BMP data corruption detected");
                    return null;
                }

                float b = data[pixelOffset] / 255f;
                float g = data[pixelOffset + 1] / 255f;
                float r = data[pixelOffset + 2] / 255f;

                pixels[rowY * width + x] = new Color(r, g, b, 1f);
            }
        }

        texture.SetPixels(pixels);
        texture.Apply();
        texture.filterMode = FilterMode.Point;
        texture.wrapMode = TextureWrapMode.Clamp;

        return texture;
    }
}