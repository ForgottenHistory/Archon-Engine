using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using ProvinceSystem.Jobs;

namespace ProvinceSystem.Services
{
    /// <summary>
    /// Service for generating province meshes using Job System
    /// </summary>
    public class ProvinceMeshService
    {
        private float pixelToWorldX;
        private float pixelToWorldZ;
        private float mapWidth;
        private float mapHeight;
        private int textureWidth;
        private int textureHeight;
        
        public enum MeshMethod
        {
            PixelPerfect,
            MergedRectangles,
            SingleQuad
        }
        
        public void Initialize(float mapWidth, float mapHeight, int textureWidth, int textureHeight)
        {
            this.mapWidth = mapWidth;
            this.mapHeight = mapHeight;
            this.textureWidth = textureWidth;
            this.textureHeight = textureHeight;
            
            pixelToWorldX = mapWidth / textureWidth;
            pixelToWorldZ = mapHeight / textureHeight;
        }
        
        /// <summary>
        /// Generate mesh for a province using optimized rectangles with Job System
        /// </summary>
        public Mesh GenerateProvinceMeshOptimized(ProvinceDataService.ProvinceData province, float provinceHeight, MeshMethod method)
        {
            switch (method)
            {
                case MeshMethod.SingleQuad:
                    return GenerateSingleQuadMesh(province, provinceHeight);
                    
                case MeshMethod.PixelPerfect:
                    return GeneratePixelPerfectMesh(province, provinceHeight);
                    
                case MeshMethod.MergedRectangles:
                default:
                    return GenerateMergedRectangleMeshWithJobs(province, provinceHeight);
            }
        }
        
        private Mesh GenerateMergedRectangleMeshWithJobs(ProvinceDataService.ProvinceData province, float provinceHeight)
        {
            // Convert province color to uint key
            uint colorKey = ColorToUInt(province.color);
            
            // Create native collections for job
            var provincePixels = new NativeParallelMultiHashMap<uint, int2>(province.pixels.Count, Allocator.TempJob);
            var mergedRectangles = new NativeList<int4>(Allocator.TempJob);
            
            // Fill province pixels
            foreach (var pixel in province.pixels)
            {
                provincePixels.Add(colorKey, new int2(pixel.x, pixel.y));
            }
            
            // Create and run merge job
            var mergeJob = new RectangleMergeJob
            {
                provincePixels = provincePixels,
                colorKey = colorKey,
                width = textureWidth,
                height = textureHeight,
                mergedRectangles = mergedRectangles
            };
            
            JobHandle mergeHandle = mergeJob.Schedule();
            mergeHandle.Complete();
            
            // Build mesh from merged rectangles
            Mesh mesh = BuildMeshFromRectangles(mergedRectangles, provinceHeight);
            
            // Cleanup
            provincePixels.Dispose();
            mergedRectangles.Dispose();
            
            return mesh;
        }
        
        private Mesh BuildMeshFromRectangles(NativeList<int4> rectangles, float provinceHeight)
        {
            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();
            List<Vector2> uvs = new List<Vector2>();
            
            for (int i = 0; i < rectangles.Length; i++)
            {
                int4 rect = rectangles[i];
                
                // Convert pixel coordinates to world space
                Vector3 bottomLeft = PixelToWorldPosition(new Vector2Int(rect.x, rect.y));
                Vector3 topRight = PixelToWorldPosition(new Vector2Int(rect.x + rect.z, rect.y + rect.w));
                
                int baseIndex = vertices.Count;
                
                // Add vertices for rectangle
                vertices.Add(new Vector3(bottomLeft.x - pixelToWorldX * 0.5f, provinceHeight, bottomLeft.z - pixelToWorldZ * 0.5f));
                vertices.Add(new Vector3(topRight.x - pixelToWorldX * 0.5f, provinceHeight, bottomLeft.z - pixelToWorldZ * 0.5f));
                vertices.Add(new Vector3(topRight.x - pixelToWorldX * 0.5f, provinceHeight, topRight.z - pixelToWorldZ * 0.5f));
                vertices.Add(new Vector3(bottomLeft.x - pixelToWorldX * 0.5f, provinceHeight, topRight.z - pixelToWorldZ * 0.5f));
                
                // UVs
                uvs.Add(new Vector2(0, 0));
                uvs.Add(new Vector2(1, 0));
                uvs.Add(new Vector2(1, 1));
                uvs.Add(new Vector2(0, 1));
                
                // Triangles
                triangles.Add(baseIndex);
                triangles.Add(baseIndex + 2);
                triangles.Add(baseIndex + 1);
                triangles.Add(baseIndex);
                triangles.Add(baseIndex + 3);
                triangles.Add(baseIndex + 2);
            }
            
            Mesh mesh = new Mesh();
            
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
        
        private Mesh GeneratePixelPerfectMesh(ProvinceDataService.ProvinceData province, float provinceHeight)
        {
            List<Vector3> vertices = new List<Vector3>(province.pixels.Count * 4);
            List<int> triangles = new List<int>(province.pixels.Count * 6);
            List<Vector2> uvs = new List<Vector2>(province.pixels.Count * 4);
            
            foreach (var pixel in province.pixels)
            {
                Vector3 worldPos = PixelToWorldPosition(pixel);
                worldPos.y = provinceHeight;
                
                int baseIndex = vertices.Count;
                
                float halfPixelX = pixelToWorldX * 0.5f;
                float halfPixelZ = pixelToWorldZ * 0.5f;
                
                vertices.Add(worldPos + new Vector3(-halfPixelX, 0, -halfPixelZ));
                vertices.Add(worldPos + new Vector3(halfPixelX, 0, -halfPixelZ));
                vertices.Add(worldPos + new Vector3(halfPixelX, 0, halfPixelZ));
                vertices.Add(worldPos + new Vector3(-halfPixelX, 0, halfPixelZ));
                
                uvs.Add(new Vector2(0, 0));
                uvs.Add(new Vector2(1, 0));
                uvs.Add(new Vector2(1, 1));
                uvs.Add(new Vector2(0, 1));
                
                triangles.Add(baseIndex);
                triangles.Add(baseIndex + 2);
                triangles.Add(baseIndex + 1);
                triangles.Add(baseIndex);
                triangles.Add(baseIndex + 3);
                triangles.Add(baseIndex + 2);
            }
            
            Mesh mesh = new Mesh();
            
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
        
        private Mesh GenerateSingleQuadMesh(ProvinceDataService.ProvinceData province, float provinceHeight)
        {
            Mesh mesh = new Mesh();
            
            Vector3 center = province.bounds.center;
            Vector3 size = province.bounds.size;
            center.y = provinceHeight;
            
            Vector3[] vertices = new Vector3[4];
            vertices[0] = center + new Vector3(-size.x/2, 0, -size.z/2);
            vertices[1] = center + new Vector3(size.x/2, 0, -size.z/2);
            vertices[2] = center + new Vector3(size.x/2, 0, size.z/2);
            vertices[3] = center + new Vector3(-size.x/2, 0, size.z/2);
            
            int[] triangles = new int[] { 0, 2, 1, 0, 3, 2 };
            
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
        
        private Vector3 PixelToWorldPosition(Vector2Int pixel)
        {
            float x = (pixel.x / (float)textureWidth - 0.5f) * mapWidth;
            float z = (pixel.y / (float)textureHeight - 0.5f) * mapHeight;
            return new Vector3(x, 0, z);
        }
        
        private uint ColorToUInt(Color c)
        {
            Color32 c32 = c;
            return ((uint)c32.r << 24) | ((uint)c32.g << 16) | ((uint)c32.b << 8) | (uint)c32.a;
        }
    }
}