#if FALSE // DISABLED: Legacy rendering system - incompatible with polyline-based borders
using System.Collections.Generic;
using UnityEngine;

namespace Map.Rendering
{
    /// <summary>
    /// Pure SDF (Signed Distance Field) border rendering
    /// Evaluates distance to curve segments per-pixel for resolution-independent borders
    /// Supports razor-thin borders (0.1px+) with smooth anti-aliasing
    /// Automatically handles junction connectivity via distance field blending
    /// </summary>
    public class BorderSDFRenderer
    {
        // GPU buffer struct matching HLSL BezierSegment
        // MUST match shader struct layout exactly!
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct BezierSegmentGPU
        {
            public Vector2 p0;
            public Vector2 p1;
            public Vector2 p2;
            public Vector2 p3;
            public int borderType;
            public uint provinceID1;
            public uint provinceID2;
        }

        private readonly ComputeShader sdfShader;
        private readonly MapTextureManager textureManager;
        private readonly BorderCurveCache cache;
        private readonly SpatialHashGrid spatialGrid;

        private int evaluateSDFKernel;
        private const int THREAD_GROUP_SIZE = 8;

        // GPU buffers
        private ComputeBuffer segmentsBuffer;
        private ComputeBuffer gridCellsBuffer;
        private ComputeBuffer gridSegmentIndicesBuffer;

        private bool buffersInitialized = false;

        public BorderSDFRenderer(ComputeShader shader, MapTextureManager textures, BorderCurveCache curveCache, SpatialHashGrid grid)
        {
            sdfShader = shader;
            textureManager = textures;
            cache = curveCache;
            spatialGrid = grid;

            if (shader != null)
            {
                if (!shader.HasKernel("EvaluateBorderSDF"))
                {
                    ArchonLogger.LogError("BorderSDFRenderer: Shader does not have 'EvaluateBorderSDF' kernel! Check shader compilation.", "map_initialization");
                    evaluateSDFKernel = -1;
                }
                else
                {
                    evaluateSDFKernel = shader.FindKernel("EvaluateBorderSDF");
                    ArchonLogger.Log($"BorderSDFRenderer: Initialized SDF evaluation kernel (index: {evaluateSDFKernel})", "map_initialization");
                }
            }
            else
            {
                ArchonLogger.LogError("BorderSDFRenderer: SDF shader is null!", "map_initialization");
                evaluateSDFKernel = -1;
            }
        }

        /// <summary>
        /// Upload curve segment data and spatial grid to GPU
        /// Called once after border extraction completes
        /// </summary>
        public void UploadSDFData()
        {
            if (sdfShader == null)
            {
                ArchonLogger.LogError("BorderSDFRenderer: Cannot upload - shader is null", "map_rendering");
                return;
            }

            ReleaseBuffers();

            // Convert BezierSegments for GPU (upload full curve data)
            var bezierSegments = new List<BezierSegmentGPU>();

            foreach (var (segments, style) in cache.GetAllBordersForRendering())
            {
                if (segments == null || segments.Count == 0)
                    continue;

                int typeValue = style.type == BorderType.Country ? 2 : (style.type == BorderType.Province ? 1 : 0);

                foreach (var seg in segments)
                {
                    bezierSegments.Add(new BezierSegmentGPU
                    {
                        p0 = seg.P0,
                        p1 = seg.P1,
                        p2 = seg.P2,
                        p3 = seg.P3,
                        borderType = typeValue,
                        provinceID1 = seg.provinceID1,
                        provinceID2 = seg.provinceID2
                    });
                }
            }

            ArchonLogger.Log($"BorderSDFRenderer: Uploading {bezierSegments.Count} Bézier curve segments to GPU", "map_initialization");

            // DEBUG: Log first 3 segments to verify curve data
            for (int i = 0; i < Mathf.Min(3, bezierSegments.Count); i++)
            {
                var seg = bezierSegments[i];
                Vector2 lineVec = seg.p3 - seg.p0;
                float lineLen = lineVec.magnitude;
                if (lineLen > 0.01f)
                {
                    Vector2 lineDir = lineVec.normalized;
                    Vector2 perpVec = new Vector2(-lineDir.y, lineDir.x);
                    float dist1 = Mathf.Abs(Vector2.Dot(seg.p1 - seg.p0, perpVec));
                    float dist2 = Mathf.Abs(Vector2.Dot(seg.p2 - seg.p0, perpVec));
                    ArchonLogger.Log($"GPU UPLOAD {i}: P0={seg.p0} P1={seg.p1} P2={seg.p2} P3={seg.p3} | perpDist={dist1:F2}, {dist2:F2} | type={seg.borderType}", "map_initialization");
                }
            }

            // Upload segments buffer
            segmentsBuffer = new ComputeBuffer(Mathf.Max(1, bezierSegments.Count), System.Runtime.InteropServices.Marshal.SizeOf<BezierSegmentGPU>());
            if (bezierSegments.Count > 0)
            {
                segmentsBuffer.SetData(bezierSegments);
            }

            // Upload spatial grid
            UploadSpatialGrid();

            buffersInitialized = true;
        }

        /// <summary>
        /// Upload spatial hash grid data to GPU for acceleration
        /// Uses SpatialHashGrid's GPU-ready data structures
        /// </summary>
        private void UploadSpatialGrid()
        {
            // Get GPU-ready data from spatial hash grid
            var cellRanges = spatialGrid.GetCellRanges();
            var segmentIndices = spatialGrid.GetFlatSegmentIndices();

            ArchonLogger.Log($"BorderSDFRenderer: Spatial grid - {spatialGrid.GridWidth}×{spatialGrid.GridHeight} cells, {segmentIndices.Length} segment references", "map_initialization");

            // Upload grid cell ranges
            int cellCount = spatialGrid.GridWidth * spatialGrid.GridHeight;
            gridCellsBuffer = new ComputeBuffer(Mathf.Max(1, cellCount), System.Runtime.InteropServices.Marshal.SizeOf<SpatialHashGrid.CellRange>());
            if (cellRanges.Length > 0)
            {
                gridCellsBuffer.SetData(cellRanges);
            }

            // Upload segment indices (uint array)
            gridSegmentIndicesBuffer = new ComputeBuffer(Mathf.Max(1, segmentIndices.Length), sizeof(uint));
            if (segmentIndices.Length > 0)
            {
                gridSegmentIndicesBuffer.SetData(segmentIndices);
            }

            // Set grid parameters in shader
            sdfShader.SetInt("GridWidth", spatialGrid.GridWidth);
            sdfShader.SetInt("GridHeight", spatialGrid.GridHeight);
            sdfShader.SetFloat("GridCellSize", spatialGrid.CellSize);
        }

        /// <summary>
        /// Render borders using SDF evaluation
        /// </summary>
        public void RenderBorders(float countryBorderWidth = 0.5f, float provinceBorderWidth = 0.5f, float antiAliasRadius = 0.5f)
        {
            if (!buffersInitialized || sdfShader == null)
            {
                ArchonLogger.LogWarning("BorderSDFRenderer: Cannot render - not initialized", "map_rendering");
                return;
            }

            if (evaluateSDFKernel < 0)
            {
                ArchonLogger.LogError("BorderSDFRenderer: Cannot render - kernel is invalid", "map_rendering");
                return;
            }

            // Set shader parameters
            if (textureManager.BorderTexture == null)
            {
                ArchonLogger.LogError("BorderSDFRenderer: textureManager.BorderTexture is NULL!", "map_rendering");
                return;
            }
            ArchonLogger.Log($"BorderSDFRenderer: Setting BorderTexture - Name={textureManager.BorderTexture.name}, Size={textureManager.BorderTexture.width}x{textureManager.BorderTexture.height}, Format={textureManager.BorderTexture.graphicsFormat}", "map_rendering");

            sdfShader.SetTexture(evaluateSDFKernel, "BorderTexture", textureManager.BorderTexture);
            sdfShader.SetBuffer(evaluateSDFKernel, "Segments", segmentsBuffer);
            sdfShader.SetBuffer(evaluateSDFKernel, "GridCells", gridCellsBuffer);
            sdfShader.SetBuffer(evaluateSDFKernel, "GridSegmentIndices", gridSegmentIndicesBuffer);

            sdfShader.SetInt("MapWidth", textureManager.MapWidth);
            sdfShader.SetInt("MapHeight", textureManager.MapHeight);

            sdfShader.SetFloat("CountryBorderWidth", countryBorderWidth);
            sdfShader.SetFloat("ProvinceBorderWidth", provinceBorderWidth);
            sdfShader.SetFloat("AntiAliasRadius", antiAliasRadius);

            // Clear border texture first
            ClearBorderTexture();

            // Dispatch compute shader
            int threadGroupsX = Mathf.CeilToInt(textureManager.MapWidth / (float)THREAD_GROUP_SIZE);
            int threadGroupsY = Mathf.CeilToInt(textureManager.MapHeight / (float)THREAD_GROUP_SIZE);

            ArchonLogger.Log($"BorderSDFRenderer: Dispatching SDF shader - Kernel={evaluateSDFKernel}, Groups=({threadGroupsX}, {threadGroupsY}), Map={textureManager.MapWidth}x{textureManager.MapHeight}", "map_rendering");
            ArchonLogger.Log($"BorderSDFRenderer: BorderWidth Country={countryBorderWidth}, Province={provinceBorderWidth}, AA={antiAliasRadius}", "map_rendering");

            float startTime = Time.realtimeSinceStartup;
            sdfShader.Dispatch(evaluateSDFKernel, threadGroupsX, threadGroupsY, 1);

            // CRITICAL: Force GPU to complete the dispatch
            var syncRequest = UnityEngine.Rendering.AsyncGPUReadback.Request(textureManager.BorderTexture);
            syncRequest.WaitForCompletion();

            float elapsed = (Time.realtimeSinceStartup - startTime) * 1000f;

            ArchonLogger.Log($"BorderSDFRenderer: Rendered borders in {elapsed:F2}ms", "map_rendering");
        }

        /// <summary>
        /// Clear border texture to prepare for SDF rendering
        /// </summary>
        private void ClearBorderTexture()
        {
            RenderTexture rt = textureManager.BorderTexture;
            RenderTexture.active = rt;
            GL.Clear(true, true, Color.clear);
            RenderTexture.active = null;
        }

        /// <summary>
        /// Release GPU buffers
        /// </summary>
        public void ReleaseBuffers()
        {
            segmentsBuffer?.Release();
            gridCellsBuffer?.Release();
            gridSegmentIndicesBuffer?.Release();

            segmentsBuffer = null;
            gridCellsBuffer = null;
            gridSegmentIndicesBuffer = null;

            buffersInitialized = false;
        }

        /// <summary>
        /// Cleanup on destroy
        /// </summary>
        ~BorderSDFRenderer()
        {
            ReleaseBuffers();
        }
    }
}
#endif
