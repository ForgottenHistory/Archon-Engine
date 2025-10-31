#if FALSE // DISABLED: Legacy rendering system - incompatible with polyline-based borders
using System.Collections.Generic;
using UnityEngine;

namespace Map.Rendering
{
    /// <summary>
    /// Renders pre-computed smooth border curves using GPU compute shader
    /// Rasterizes curves into BorderTexture for display
    /// </summary>
    public class BorderCurveRenderer
    {
        private readonly ComputeShader rasterizerShader;
        private readonly MapTextureManager textureManager;
        private readonly BorderCurveCache cache;
        private readonly BorderDistanceFieldGenerator distanceFieldGenerator;

        private int rasterizeKernel;
        private const int THREAD_GROUP_SIZE = 64;

        // GPU buffers for Bézier curve segments
        private ComputeBuffer curvePointsBuffer;  // Contains BezierSegment structs
        private ComputeBuffer curveTypesBuffer;
        private ComputeBuffer curveThicknessBuffer;

        // Spatial acceleration buffers
        private SpatialHashGrid spatialGrid;
        private ComputeBuffer gridCellRangesBuffer;    // Per-cell (startIndex, count)
        private ComputeBuffer gridSegmentIndicesBuffer; // Flat segment indices

        private bool buffersInitialized = false;
        private bool spatialGridInitialized = false;

        public BorderCurveRenderer(ComputeShader shader, MapTextureManager textures, BorderCurveCache curveCache, BorderDistanceFieldGenerator distanceField)
        {
            rasterizerShader = shader;
            textureManager = textures;
            cache = curveCache;
            distanceFieldGenerator = distanceField;

            if (shader != null)
            {
                rasterizeKernel = shader.FindKernel("RasterizeCurves");
                ArchonLogger.Log("BorderCurveRenderer: Initialized rasterizer kernel", "map_initialization");
            }
            else
            {
                ArchonLogger.LogError("BorderCurveRenderer: Rasterizer shader is null!", "map_initialization");
            }
        }

        /// <summary>
        /// Upload curve data to GPU buffers
        /// Called once after border extraction completes
        /// </summary>
        public void UploadCurveData()
        {
            if (rasterizerShader == null)
            {
                ArchonLogger.LogError("BorderCurveRenderer: Cannot upload - shader is null", "map_rendering");
                return;
            }

            // Release old buffers
            ReleaseBuffers();

            // Collect all Bézier curve segments and their styles
            var allSegments = new List<BezierSegment>();
            var curveTypes = new List<int>();
            var curveThickness = new List<float>();

            int type0Count = 0, type1Count = 0, type2Count = 0;

            foreach (var (segments, style) in cache.GetAllBordersForRendering())
            {
                if (segments == null || segments.Count == 0)
                    continue;

                // Map border type to int
                int typeValue = style.type == BorderType.Country ? 2 : (style.type == BorderType.Province ? 1 : 0);

                // Track type distribution for debugging
                if (typeValue == 0) type0Count++;
                else if (typeValue == 1) type1Count++;
                else if (typeValue == 2) type2Count++;

                // Add all segments from this border
                foreach (var segment in segments)
                {
                    allSegments.Add(segment);
                    curveTypes.Add(typeValue);
                    curveThickness.Add(style.thickness);
                }
            }

            ArchonLogger.Log($"BorderCurveRenderer: Curve types - Hidden: {type0Count}, Province: {type1Count}, Country: {type2Count}", "map_initialization");

            // DEBUG: Check if first segment coordinates are in bounds
            if (allSegments.Count > 0)
            {
                var firstSeg = allSegments[0];
                bool inBounds = firstSeg.P0.x >= 0 && firstSeg.P0.x < textureManager.MapWidth &&
                                firstSeg.P0.y >= 0 && firstSeg.P0.y < textureManager.MapHeight;
                ArchonLogger.Log($"BorderCurveRenderer: First segment P0 ({firstSeg.P0.x:F2}, {firstSeg.P0.y:F2}), MapSize: {textureManager.MapWidth}x{textureManager.MapHeight}, InBounds: {inBounds}", "map_initialization");
            }

            if (allSegments.Count == 0)
            {
                ArchonLogger.LogWarning("BorderCurveRenderer: No curve segments to upload", "map_rendering");
                return;
            }

            // DEBUG: Log first segment details
            if (allSegments.Count > 0)
            {
                var seg = allSegments[0];
                ArchonLogger.Log($"BorderCurveRenderer: First Bézier segment - P0:({seg.P0.x:F1},{seg.P0.y:F1}) P1:({seg.P1.x:F1},{seg.P1.y:F1}) P2:({seg.P2.x:F1},{seg.P2.y:F1}) P3:({seg.P3.x:F1},{seg.P3.y:F1})", "map_initialization");
            }

            // Create GPU buffers for Bézier segments
            // BezierSegment struct size: 4 Vector2 (32 bytes) + 1 int borderType (4) + 2 uint provinceIDs (8) + 1 uint connectivityFlags (4) = 48 bytes
            // CRITICAL: Must match BezierSegment C# struct layout (now uses uint not ushort for province IDs)
            int segmentStride = sizeof(float) * 8 + sizeof(int) + sizeof(uint) * 3;
            curvePointsBuffer = new ComputeBuffer(allSegments.Count, segmentStride);
            curveTypesBuffer = new ComputeBuffer(allSegments.Count, sizeof(int));
            curveThicknessBuffer = new ComputeBuffer(allSegments.Count, sizeof(float));

            // Upload data
            curvePointsBuffer.SetData(allSegments);
            curveTypesBuffer.SetData(curveTypes.ToArray());
            curveThicknessBuffer.SetData(curveThickness.ToArray());

            // DEBUG: Log buffer details
            ArchonLogger.Log($"BorderCurveRenderer: Uploaded buffers - Bézier segments: {allSegments.Count}", "map_initialization");
            ArchonLogger.Log($"  Type distribution: Type0(None)={type0Count}, Type1(Province)={type1Count}, Type2(Country)={type2Count}", "map_initialization");

            buffersInitialized = true;

            ArchonLogger.Log($"BorderCurveRenderer: Uploaded {allSegments.Count} Bézier segments to GPU", "map_initialization");

            // Build spatial acceleration structure
            BuildSpatialGrid(allSegments);
        }

        /// <summary>
        /// Build spatial hash grid for accelerating curve lookups
        /// Divides map into uniform grid cells, each storing which segments intersect it
        /// </summary>
        private void BuildSpatialGrid(List<BezierSegment> allSegments)
        {
            float startTime = Time.realtimeSinceStartup;

            // Create spatial grid (64px cells)
            spatialGrid = new SpatialHashGrid(textureManager.MapWidth, textureManager.MapHeight, cellSize: 64);

            // Add all segments to grid
            for (int i = 0; i < allSegments.Count; i++)
            {
                spatialGrid.AddSegment((uint)i, allSegments[i]);
            }

            // Finalize grid (prepares GPU-ready data)
            spatialGrid.Finalize();

            // Upload to GPU buffers
            var cellRanges = spatialGrid.GetCellRanges();
            var segmentIndices = spatialGrid.GetFlatSegmentIndices();

            // CellRange struct: 2 uints (startIndex, count) = 8 bytes
            gridCellRangesBuffer = new ComputeBuffer(cellRanges.Length, sizeof(uint) * 2);
            gridCellRangesBuffer.SetData(cellRanges);

            // Segment indices: uint = 4 bytes (GPU alignment requirement)
            gridSegmentIndicesBuffer = new ComputeBuffer(segmentIndices.Length, sizeof(uint));
            gridSegmentIndicesBuffer.SetData(segmentIndices);

            spatialGridInitialized = true;

            float elapsedMs = (Time.realtimeSinceStartup - startTime) * 1000f;
            ArchonLogger.Log($"BorderCurveRenderer: Built spatial grid in {elapsedMs:F1}ms - {spatialGrid.GetCellCount()} cells, {spatialGrid.GetTotalSegmentReferences()} segment refs", "map_initialization");
        }

        /// <summary>
        /// Rasterize curves into BorderTexture
        /// DEPRECATED: This was for the old rasterization approach
        /// Now using vector curve rendering in fragment shader instead
        /// </summary>
        public void RasterizeCurves()
        {
            // DISABLED: Vector curve rendering happens in fragment shader
            // This old rasterization approach is no longer used
            // The Bézier segment buffer is bound to the material for runtime evaluation
            ArchonLogger.Log("BorderCurveRenderer: RasterizeCurves() is deprecated - using vector curve fragment shader instead", "map_rendering");
        }

        /// <summary>
        /// Clear border texture before rasterizing
        /// CRITICAL: Initialize to high distance values (far from border)
        /// Border pixels will be set to 0 (on border) by the compute shader
        /// </summary>
        private void ClearBorderTexture()
        {
            if (textureManager.BorderTexture == null)
                return;

            // Clear to white (1.0, 1.0, 1.0, 1.0) = maximum distance from borders
            // The shader expects: 0 = on border, 1.0 = far from border
            RenderTexture.active = textureManager.BorderTexture;
            GL.Clear(true, true, Color.white);
            RenderTexture.active = null;
        }

        /// <summary>
        /// DEBUG: Render curve points as colored dots to visualize the extracted curves
        /// Call this to see if curve extraction is producing smooth points
        /// RED = Country borders, GREEN = Province borders
        /// </summary>
        public Texture2D RenderCurvePointsDebug(int width, int height)
        {
            var debugTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var pixels = new Color32[width * height];

            // Clear to black
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = new Color32(0, 0, 0, 255);
            }

            bool firstCurveLogged = false;

            // Draw Bézier curve segments by sampling them
            foreach (var (borderKey, style) in cache.GetAllBorderStyles())
            {
                if (!style.visible)
                    continue;

                var (provinceA, provinceB) = borderKey;
                var segments = cache.GetCurve(provinceA, provinceB);
                if (segments == null || segments.Count == 0)
                    continue;

                // DEBUG: Log first curve to see segment count
                if (!firstCurveLogged && segments.Count > 0)
                {
                    firstCurveLogged = true;
                    ArchonLogger.Log($"DEBUG First Border {provinceA}<->{provinceB}: {segments.Count} Bézier segments", "map_rendering");
                    var firstSeg = segments[0];
                    ArchonLogger.Log($"  First segment: P0=({firstSeg.P0.x:F3},{firstSeg.P0.y:F3}) P3=({firstSeg.P3.x:F3},{firstSeg.P3.y:F3})", "map_rendering");
                }

                // Choose color based on border type
                Color32 dotColor;
                if (style.type == BorderType.Country)
                    dotColor = new Color32(255, 0, 0, 255); // Red for country
                else if (style.type == BorderType.Province)
                    dotColor = new Color32(0, 255, 0, 255); // Green for province
                else
                    continue;

                // Sample each Bézier segment at multiple points
                foreach (var segment in segments)
                {
                    // Sample curve at 10 points along its length
                    for (int i = 0; i <= 10; i++)
                    {
                        float t = i / 10f;
                        Vector2 point = segment.Evaluate(t);
                        float fx = point.x;
                        float fy = height - 1 - point.y; // Flip Y coordinate

                        // Draw 5x5 dot with intensity based on sub-pixel position
                        // This shows if we have sub-pixel precision or just integers
                        for (int dy = -2; dy <= 2; dy++)
                        {
                            for (int dx = -2; dx <= 2; dx++)
                            {
                                int px = Mathf.FloorToInt(fx) + dx;
                                int py = Mathf.FloorToInt(fy) + dy;

                                if (px >= 0 && px < width && py >= 0 && py < height)
                                {
                                    // Calculate distance from sub-pixel center
                                    float distX = px + 0.5f - fx;
                                    float distY = py + 0.5f - fy;
                                    float dist = Mathf.Sqrt(distX * distX + distY * distY);

                                    // Intensity falloff based on distance
                                    float intensity = Mathf.Max(0, 1.0f - dist / 2.5f);
                                    if (intensity > 0.01f)
                                    {
                                        int index = py * width + px;
                                        // Blend with existing color
                                        Color32 existing = pixels[index];
                                        pixels[index] = new Color32(
                                            (byte)Mathf.Max(existing.r, dotColor.r * intensity),
                                            (byte)Mathf.Max(existing.g, dotColor.g * intensity),
                                            (byte)Mathf.Max(existing.b, dotColor.b * intensity),
                                            255
                                        );
                                    }
                                }
                            }
                        }
                    }
                }
            }

            debugTexture.SetPixels32(pixels);
            debugTexture.Apply();
            return debugTexture;
        }

        /// <summary>
        /// Release GPU buffers
        /// </summary>
        private void ReleaseBuffers()
        {
            curvePointsBuffer?.Release();
            curveTypesBuffer?.Release();
            curveThicknessBuffer?.Release();
            gridCellRangesBuffer?.Release();
            gridSegmentIndicesBuffer?.Release();

            curvePointsBuffer = null;
            curveTypesBuffer = null;
            curveThicknessBuffer = null;
            gridCellRangesBuffer = null;
            gridSegmentIndicesBuffer = null;

            buffersInitialized = false;
            spatialGridInitialized = false;
        }

        /// <summary>
        /// Get the Bézier segments buffer for binding to shaders
        /// Returns null if not initialized
        /// </summary>
        public ComputeBuffer GetBezierSegmentsBuffer()
        {
            return curvePointsBuffer;
        }

        /// <summary>
        /// Get the count of Bézier segments in the buffer
        /// </summary>
        public int GetSegmentCount()
        {
            return curvePointsBuffer != null ? curvePointsBuffer.count : 0;
        }

        /// <summary>
        /// Check if buffers are ready for rendering
        /// </summary>
        public bool IsInitialized()
        {
            return buffersInitialized;
        }

        /// <summary>
        /// Check if spatial grid is ready for accelerated rendering
        /// </summary>
        public bool IsSpatialGridInitialized()
        {
            return spatialGridInitialized;
        }

        /// <summary>
        /// Get spatial grid parameters
        /// </summary>
        public (int gridWidth, int gridHeight, int cellSize) GetSpatialGridParams()
        {
            if (spatialGrid == null)
                return (0, 0, 0);
            return (spatialGrid.GridWidth, spatialGrid.GridHeight, spatialGrid.CellSize);
        }

        /// <summary>
        /// Get spatial grid cell ranges buffer
        /// </summary>
        public ComputeBuffer GetGridCellRangesBuffer()
        {
            return gridCellRangesBuffer;
        }

        /// <summary>
        /// Get spatial grid segment indices buffer
        /// </summary>
        public ComputeBuffer GetGridSegmentIndicesBuffer()
        {
            return gridSegmentIndicesBuffer;
        }

        /// <summary>
        /// Clean up resources
        /// </summary>
        public void Dispose()
        {
            ReleaseBuffers();
        }
    }
}
#endif
