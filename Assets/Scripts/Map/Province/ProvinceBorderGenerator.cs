using UnityEngine;
using System.Collections.Generic;

public static class ProvinceBorderGenerator
{
    public static void GenerateBorders(
        Dictionary<Color, ProvinceData> provinces,
        GameObject provincesContainer,
        Texture2D provinceMap,
        float mapWidth,
        float mapHeight,
        float provinceHeight,
        Color borderColor,
        Material borderMaterial)
    {
        GameObject bordersContainer = new GameObject("Borders");
        bordersContainer.transform.parent = provincesContainer.transform;

        // We'll create border lines between provinces
        List<Vector3> borderVertices = new List<Vector3>();
        List<int> borderTriangles = new List<int>();

        int meshCount = 0;
        int vertexCount = 0;
        int maxVerticesPerMesh = 60000;

        ProvinceCoordinateConverter.CalculatePixelToWorldFactors(provinceMap, mapWidth, mapHeight, out float pixelToWorldX, out float pixelToWorldZ);

        Debug.Log("Generating province borders...");

        // For each province, find its border pixels
        foreach (var province in provinces.Values)
        {
            HashSet<Vector2Int> borderPixels = FindProvinceBorderPixels(province);

            foreach (var borderPixel in borderPixels)
            {
                // Check if we need to create a new mesh
                if (vertexCount + 4 > maxVerticesPerMesh)
                {
                    if (borderVertices.Count > 0)
                    {
                        CreateBorderMeshObject(bordersContainer, borderVertices, borderTriangles, meshCount++, borderColor, borderMaterial);
                        borderVertices.Clear();
                        borderTriangles.Clear();
                        vertexCount = 0;
                    }
                }

                // Create a quad for this border pixel
                // For borders, we want to cover the full pixel area like merged rectangles do
                Vector3 pixelBottomLeft = ProvinceCoordinateConverter.PixelToWorldPosition(borderPixel, provinceMap, mapWidth, mapHeight, provinceHeight);
                Vector3 pixelTopRight = ProvinceCoordinateConverter.PixelToWorldPosition(new Vector2Int(borderPixel.x + 1, borderPixel.y + 1), provinceMap, mapWidth, mapHeight, provinceHeight);

                float yPos = provinceHeight + 0.01f; // Slightly above provinces to avoid z-fighting
                int baseIndex = borderVertices.Count;

                // Add vertices covering the full pixel area
                borderVertices.Add(new Vector3(pixelBottomLeft.x - pixelToWorldX * 0.5f, yPos, pixelBottomLeft.z - pixelToWorldZ * 0.5f));
                borderVertices.Add(new Vector3(pixelTopRight.x - pixelToWorldX * 0.5f, yPos, pixelBottomLeft.z - pixelToWorldZ * 0.5f));
                borderVertices.Add(new Vector3(pixelTopRight.x - pixelToWorldX * 0.5f, yPos, pixelTopRight.z - pixelToWorldZ * 0.5f));
                borderVertices.Add(new Vector3(pixelBottomLeft.x - pixelToWorldX * 0.5f, yPos, pixelTopRight.z - pixelToWorldZ * 0.5f));

                // Add triangles
                borderTriangles.Add(baseIndex);
                borderTriangles.Add(baseIndex + 2);
                borderTriangles.Add(baseIndex + 1);
                borderTriangles.Add(baseIndex);
                borderTriangles.Add(baseIndex + 3);
                borderTriangles.Add(baseIndex + 2);

                vertexCount += 4;
            }
        }

        // Create final mesh if there are remaining vertices
        if (borderVertices.Count > 0)
        {
            CreateBorderMeshObject(bordersContainer, borderVertices, borderTriangles, meshCount, borderColor, borderMaterial);
        }

        Debug.Log($"Created {meshCount + 1} border mesh objects");
    }

    private static HashSet<Vector2Int> FindProvinceBorderPixels(ProvinceData province)
    {
        HashSet<Vector2Int> borderPixels = new HashSet<Vector2Int>();

        foreach (var pixel in province.pixels)
        {
            // Check 4 adjacent pixels
            Vector2Int[] neighbors = {
                new Vector2Int(pixel.x + 1, pixel.y),
                new Vector2Int(pixel.x - 1, pixel.y),
                new Vector2Int(pixel.x, pixel.y + 1),
                new Vector2Int(pixel.x, pixel.y - 1)
            };

            foreach (var neighbor in neighbors)
            {
                // Check if neighbor is outside the province
                if (!province.pixelSet.Contains(neighbor))
                {
                    // This pixel is on the border
                    borderPixels.Add(pixel);
                    break;
                }
            }
        }

        return borderPixels;
    }

    private static void CreateBorderMeshObject(GameObject parent, List<Vector3> vertices, List<int> triangles, int index, Color borderColor, Material borderMaterial)
    {
        GameObject borderObj = new GameObject($"BorderMesh_{index}");
        borderObj.transform.parent = parent.transform;

        MeshFilter meshFilter = borderObj.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = borderObj.AddComponent<MeshRenderer>();

        Mesh borderMesh = new Mesh();

        if (vertices.Count > 65535)
        {
            borderMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        }

        borderMesh.vertices = vertices.ToArray();
        borderMesh.triangles = triangles.ToArray();
        borderMesh.RecalculateNormals();
        borderMesh.RecalculateBounds();

        meshFilter.mesh = borderMesh;

        // Set up the border material
        Material mat = borderMaterial != null ?
            new Material(borderMaterial) :
            new Material(Shader.Find("Unlit/Color"));
        mat.color = borderColor;
        meshRenderer.material = mat;
        meshRenderer.receiveShadows = false;
        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }
}