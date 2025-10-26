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

        private bool buffersInitialized = false;

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
            // BezierSegment struct size: 8 Vector2 fields (P0,P1,P2,P3) + 1 BorderType (4 bytes) + 2 ushort (4 bytes) = 72 bytes
            int segmentStride = sizeof(float) * 8 + sizeof(int) + sizeof(ushort) * 2;
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
        }

        /// <summary>
        /// Rasterize curves into BorderTexture
        /// NOTE: This only marks border pixels as 0 (seed points)
        /// A distance field pass is needed afterwards for smooth anti-aliased borders
        /// </summary>
        public void RasterizeCurves()
        {
            if (!buffersInitialized || rasterizerShader == null)
            {
                ArchonLogger.LogWarning("BorderCurveRenderer: Cannot rasterize - buffers not initialized", "map_rendering");
                return;
            }

            // STEP 1: Clear border texture to maximum distance
            ClearBorderTexture();

            // STEP 2: Rasterize Bézier curves (evaluated in shader)
            // Bind buffers to compute shader
            rasterizerShader.SetBuffer(rasterizeKernel, "BezierSegments", curvePointsBuffer);
            rasterizerShader.SetBuffer(rasterizeKernel, "CurveTypes", curveTypesBuffer);
            rasterizerShader.SetBuffer(rasterizeKernel, "CurveThickness", curveThicknessBuffer);

            // Bind output texture
            rasterizerShader.SetTexture(rasterizeKernel, "BorderTexture", textureManager.BorderTexture);

            // Set dimensions
            rasterizerShader.SetInt("MapWidth", textureManager.MapWidth);
            rasterizerShader.SetInt("MapHeight", textureManager.MapHeight);

            int segmentCount = curvePointsBuffer.count;
            rasterizerShader.SetInt("SegmentCount", segmentCount);

            // DEBUG: Log dispatch details
            int threadGroups = (segmentCount + THREAD_GROUP_SIZE - 1) / THREAD_GROUP_SIZE;
            ArchonLogger.Log($"BorderCurveRenderer: Dispatching {threadGroups} thread groups for {segmentCount} Bézier segments", "map_rendering");

            rasterizerShader.Dispatch(rasterizeKernel, threadGroups, 1, 1);

            // CRITICAL: Force GPU synchronization to ensure rasterization completes
            // Without this, subsequent clears might happen before rasterization finishes
            var syncRequest = UnityEngine.Rendering.AsyncGPUReadback.Request(textureManager.BorderTexture);
            syncRequest.WaitForCompletion();

            // STEP 3: Skip distance field for EU5-style thin crisp borders
            // Distance field creates thick smudgy gradients - we want thin sharp lines
            // The rasterized curves are already smooth from Chaikin smoothing

            ArchonLogger.Log($"BorderCurveRenderer: Rasterized {segmentCount} curve segments (GPU synced)", "map_rendering");
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

            curvePointsBuffer = null;
            curveTypesBuffer = null;
            curveThicknessBuffer = null;

            buffersInitialized = false;
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
