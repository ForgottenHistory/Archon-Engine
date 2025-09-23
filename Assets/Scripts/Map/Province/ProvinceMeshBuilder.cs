using UnityEngine;
using System.Collections.Generic;

public static class ProvinceMeshBuilder
{
    public enum MeshMethod
    {
        PixelPerfect,      // One quad per pixel - most accurate
        MergedRectangles,  // Merge adjacent pixels into rectangles
        SingleQuad         // Just one quad per province (bounding box)
    }

    public static Mesh GenerateProvinceMesh(ProvinceData province, MeshMethod meshMethod, Texture2D provinceMap, float mapWidth, float mapHeight, float provinceHeight)
    {
        switch (meshMethod)
        {
            case MeshMethod.PixelPerfect:
                return GeneratePixelPerfectMesh(province, provinceMap, mapWidth, mapHeight, provinceHeight);
            case MeshMethod.MergedRectangles:
                return GenerateMergedRectangleMesh(province, provinceMap, mapWidth, mapHeight, provinceHeight);
            case MeshMethod.SingleQuad:
                return GenerateSingleQuadMesh(province, provinceHeight);
            default:
                return GenerateMergedRectangleMesh(province, provinceMap, mapWidth, mapHeight, provinceHeight);
        }
    }

    private static Mesh GeneratePixelPerfectMesh(ProvinceData province, Texture2D provinceMap, float mapWidth, float mapHeight, float provinceHeight)
    {
        List<Vector3> vertices = new List<Vector3>();
        List<int> triangles = new List<int>();
        List<Vector2> uvs = new List<Vector2>();

        ProvinceCoordinateConverter.CalculatePixelToWorldFactors(provinceMap, mapWidth, mapHeight, out float pixelToWorldX, out float pixelToWorldZ);

        foreach (var pixel in province.pixels)
        {
            Vector3 worldPos = ProvinceCoordinateConverter.PixelToWorldPosition(pixel, provinceMap, mapWidth, mapHeight, provinceHeight);

            // Create a quad for this pixel
            int baseIndex = vertices.Count;

            float halfPixelX = pixelToWorldX * 0.5f;
            float halfPixelZ = pixelToWorldZ * 0.5f;

            vertices.Add(worldPos + new Vector3(-halfPixelX, 0, -halfPixelZ));
            vertices.Add(worldPos + new Vector3(halfPixelX, 0, -halfPixelZ));
            vertices.Add(worldPos + new Vector3(halfPixelX, 0, halfPixelZ));
            vertices.Add(worldPos + new Vector3(-halfPixelX, 0, halfPixelZ));

            // UVs
            uvs.Add(new Vector2(0, 0));
            uvs.Add(new Vector2(1, 0));
            uvs.Add(new Vector2(1, 1));
            uvs.Add(new Vector2(0, 1));

            // Create triangles (counter-clockwise winding because Y is flipped)
            triangles.Add(baseIndex);
            triangles.Add(baseIndex + 2);
            triangles.Add(baseIndex + 1);
            triangles.Add(baseIndex);
            triangles.Add(baseIndex + 3);
            triangles.Add(baseIndex + 2);
        }

        Mesh mesh = new Mesh();
        mesh.vertices = vertices.ToArray();
        mesh.triangles = triangles.ToArray();
        mesh.uv = uvs.ToArray();
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        return mesh;
    }

    private static Mesh GenerateMergedRectangleMesh(ProvinceData province, Texture2D provinceMap, float mapWidth, float mapHeight, float provinceHeight)
    {
        List<Vector3> vertices = new List<Vector3>();
        List<int> triangles = new List<int>();
        List<Vector2> uvs = new List<Vector2>();

        ProvinceCoordinateConverter.CalculatePixelToWorldFactors(provinceMap, mapWidth, mapHeight, out float pixelToWorldX, out float pixelToWorldZ);

        // Create rectangles by scanning horizontally
        HashSet<Vector2Int> processed = new HashSet<Vector2Int>();

        foreach (var pixel in province.pixels)
        {
            if (processed.Contains(pixel)) continue;

            // Find the extent of this horizontal run
            int runLength = 1;
            while (province.pixelSet.Contains(new Vector2Int(pixel.x + runLength, pixel.y)) &&
                   !processed.Contains(new Vector2Int(pixel.x + runLength, pixel.y)))
            {
                runLength++;
            }

            // Now try to extend this rectangle vertically
            int runHeight = 1;
            bool canExtend = true;

            while (canExtend && runHeight < 10) // Limit height for performance
            {
                for (int x = pixel.x; x < pixel.x + runLength; x++)
                {
                    Vector2Int testPixel = new Vector2Int(x, pixel.y + runHeight);
                    if (!province.pixelSet.Contains(testPixel) || processed.Contains(testPixel))
                    {
                        canExtend = false;
                        break;
                    }
                }
                if (canExtend) runHeight++;
            }

            // Mark all pixels in this rectangle as processed
            for (int y = pixel.y; y < pixel.y + runHeight; y++)
            {
                for (int x = pixel.x; x < pixel.x + runLength; x++)
                {
                    processed.Add(new Vector2Int(x, y));
                }
            }

            // Create a quad for this rectangle
            Vector3 bottomLeft = ProvinceCoordinateConverter.PixelToWorldPosition(pixel, provinceMap, mapWidth, mapHeight, provinceHeight);
            Vector3 topRight = ProvinceCoordinateConverter.PixelToWorldPosition(new Vector2Int(pixel.x + runLength, pixel.y + runHeight), provinceMap, mapWidth, mapHeight, provinceHeight);

            int baseIndex = vertices.Count;

            vertices.Add(new Vector3(bottomLeft.x - pixelToWorldX * 0.5f, provinceHeight, bottomLeft.z - pixelToWorldZ * 0.5f));
            vertices.Add(new Vector3(topRight.x - pixelToWorldX * 0.5f, provinceHeight, bottomLeft.z - pixelToWorldZ * 0.5f));
            vertices.Add(new Vector3(topRight.x - pixelToWorldX * 0.5f, provinceHeight, topRight.z - pixelToWorldZ * 0.5f));
            vertices.Add(new Vector3(bottomLeft.x - pixelToWorldX * 0.5f, provinceHeight, topRight.z - pixelToWorldZ * 0.5f));

            // UVs
            uvs.Add(new Vector2(0, 0));
            uvs.Add(new Vector2(1, 0));
            uvs.Add(new Vector2(1, 1));
            uvs.Add(new Vector2(0, 1));

            // Create triangles (counter-clockwise winding because Y is flipped)
            triangles.Add(baseIndex);
            triangles.Add(baseIndex + 2);
            triangles.Add(baseIndex + 1);
            triangles.Add(baseIndex);
            triangles.Add(baseIndex + 3);
            triangles.Add(baseIndex + 2);
        }

        Mesh mesh = new Mesh();

        // Use 32-bit index buffer if needed (for provinces with many vertices)
        if (vertices.Count > 65535)
        {
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        }

        mesh.vertices = vertices.ToArray();
        mesh.triangles = triangles.ToArray();
        mesh.uv = uvs.ToArray();
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        return mesh;
    }

    private static Mesh GenerateSingleQuadMesh(ProvinceData province, float provinceHeight)
    {
        // Simple single quad based on bounds
        Mesh mesh = new Mesh();

        Vector3 center = province.bounds.center;
        Vector3 size = province.bounds.size;

        // Ensure the quad is at the correct height
        center.y = provinceHeight;

        // Create vertices for a quad
        Vector3[] vertices = new Vector3[4];
        vertices[0] = center + new Vector3(-size.x / 2, 0, -size.z / 2);
        vertices[1] = center + new Vector3(size.x / 2, 0, -size.z / 2);
        vertices[2] = center + new Vector3(size.x / 2, 0, size.z / 2);
        vertices[3] = center + new Vector3(-size.x / 2, 0, size.z / 2);

        // Create triangles (counter-clockwise winding because Y is flipped)
        int[] triangles = new int[] { 0, 2, 1, 0, 3, 2 };

        // Create UVs
        Vector2[] uvs = new Vector2[4];
        uvs[0] = new Vector2(0, 0);
        uvs[1] = new Vector2(1, 0);
        uvs[2] = new Vector2(1, 1);
        uvs[3] = new Vector2(0, 1);

        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.uv = uvs;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        return mesh;
    }
}