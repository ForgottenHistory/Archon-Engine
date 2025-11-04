using UnityEngine;
using Utils;

namespace Map.Rendering
{
    /// <summary>
    /// GPU-driven tree instance generation using compute shaders.
    ///
    /// Architecture:
    /// - Reads terrain type texture to determine tree placement
    /// - Generates tree positions/rotations/scales procedurally on GPU
    /// - Outputs StructuredBuffer for DrawMeshInstancedIndirect
    /// - Deterministic (same terrain = same trees)
    ///
    /// Performance:
    /// - Runs once at map load (or when terrain changes)
    /// - GPU parallel processing (8x8 thread groups)
    /// - Zero CPU overhead after generation
    ///
    /// Usage:
    /// - Call GenerateTrees() to populate buffers
    /// - Use GetTreeMatrixBuffer() for indirect rendering
    /// </summary>
    public class TreeInstanceGenerator
    {
        private readonly ComputeShader treeGenerationShader;
        private readonly int generateKernel;

        // GPU buffers
        private ComputeBuffer treeMatrixBuffer;
        private ComputeBuffer treeCountBuffer;
        private ComputeBuffer terrainTypeFilterBuffer;

        // Configuration
        private readonly int maxTrees;
        private readonly int mapWidth;
        private readonly int mapHeight;
        private readonly bool logProgress;

        // Shader property IDs (cached for performance)
        private static readonly int TerrainTypeTextureID = Shader.PropertyToID("TerrainTypeTexture");
        private static readonly int HeightmapTextureID = Shader.PropertyToID("HeightmapTexture");
        private static readonly int TreeMatricesID = Shader.PropertyToID("TreeMatrices");
        private static readonly int TreeCountID = Shader.PropertyToID("TreeCount");
        private static readonly int MapWidthID = Shader.PropertyToID("MapWidth");
        private static readonly int MapHeightID = Shader.PropertyToID("MapHeight");
        private static readonly int TreeDensityID = Shader.PropertyToID("TreeDensity");
        private static readonly int MaxTreesID = Shader.PropertyToID("MaxTrees");
        private static readonly int TreeScaleID = Shader.PropertyToID("TreeScale");
        private static readonly int TreeScaleVariationID = Shader.PropertyToID("TreeScaleVariation");
        private static readonly int TreeTerrainTypeCountID = Shader.PropertyToID("TreeTerrainTypeCount");
        private static readonly int TreeTerrainTypesID = Shader.PropertyToID("TreeTerrainTypes");
        private static readonly int MapWorldWidthID = Shader.PropertyToID("MapWorldWidth");
        private static readonly int MapWorldHeightID = Shader.PropertyToID("MapWorldHeight");

        public TreeInstanceGenerator(
            ComputeShader shader,
            int mapWidth,
            int mapHeight,
            int maxTrees = 100000,
            bool logProgress = true)
        {
            this.treeGenerationShader = shader;
            this.mapWidth = mapWidth;
            this.mapHeight = mapHeight;
            this.maxTrees = maxTrees;
            this.logProgress = logProgress;

            // Find kernel
            generateKernel = shader.FindKernel("GenerateTreeInstances");

            // Create GPU buffers
            CreateBuffers();

            if (logProgress)
            {
                ArchonLogger.Log($"TreeInstanceGenerator: Initialized for {mapWidth}x{mapHeight} map, max {maxTrees} trees", "map_rendering");
            }
        }

        private void CreateBuffers()
        {
            // Tree transform matrices (float4x4 = 16 floats = 64 bytes per tree)
            treeMatrixBuffer = new ComputeBuffer(maxTrees, sizeof(float) * 16);

            // Tree count (single uint, for atomic increment and indirect args)
            treeCountBuffer = new ComputeBuffer(1, sizeof(uint));

            // Terrain type filter (list of terrain types that spawn trees)
            // Allocate for up to 256 terrain types (overkill but safe)
            terrainTypeFilterBuffer = new ComputeBuffer(256, sizeof(uint));
        }

        /// <summary>
        /// Generate tree instances based on terrain types.
        /// Runs compute shader to populate tree matrix buffer.
        /// </summary>
        /// <param name="terrainTypeTexture">R8 texture with terrain type indices</param>
        /// <param name="heightmapTexture">R8 texture with heightmap data</param>
        /// <param name="treeTerrainTypes">Which terrain types spawn trees (GAME layer policy)</param>
        /// <param name="treeDensity">Trees per 100 world units squared</param>
        /// <param name="treeScale">Base tree scale</param>
        /// <param name="treeScaleVariation">Random scale variation (0-1)</param>
        /// <param name="mapWorldWidth">Map width in world space units</param>
        /// <param name="mapWorldHeight">Map height in world space units</param>
        public void GenerateTrees(
            Texture2D terrainTypeTexture,
            Texture2D heightmapTexture,
            uint[] treeTerrainTypes,
            float treeDensity = 10.0f,
            float treeScale = 1.0f,
            float treeScaleVariation = 0.3f,
            float mapWorldWidth = 0,
            float mapWorldHeight = 0)
        {
            if (treeGenerationShader == null)
            {
                ArchonLogger.LogError("TreeInstanceGenerator: Compute shader is null", "map_rendering");
                return;
            }

            // Clear tree count
            treeCountBuffer.SetData(new uint[] { 0 });

            // Upload terrain type filter
            terrainTypeFilterBuffer.SetData(treeTerrainTypes);

            // Bind textures
            treeGenerationShader.SetTexture(generateKernel, TerrainTypeTextureID, terrainTypeTexture);
            treeGenerationShader.SetTexture(generateKernel, HeightmapTextureID, heightmapTexture);

            // Bind buffers
            treeGenerationShader.SetBuffer(generateKernel, TreeMatricesID, treeMatrixBuffer);
            treeGenerationShader.SetBuffer(generateKernel, TreeCountID, treeCountBuffer);
            treeGenerationShader.SetBuffer(generateKernel, TreeTerrainTypesID, terrainTypeFilterBuffer);

            // Set parameters
            treeGenerationShader.SetInt(MapWidthID, mapWidth);
            treeGenerationShader.SetInt(MapHeightID, mapHeight);
            treeGenerationShader.SetFloat(TreeDensityID, treeDensity);
            treeGenerationShader.SetInt(MaxTreesID, maxTrees);
            treeGenerationShader.SetFloat(TreeScaleID, treeScale);
            treeGenerationShader.SetFloat(TreeScaleVariationID, treeScaleVariation);
            treeGenerationShader.SetInt(TreeTerrainTypeCountID, treeTerrainTypes.Length);

            // Set world space dimensions (if 0, default to texture dimensions for backwards compatibility)
            float worldWidth = mapWorldWidth > 0 ? mapWorldWidth : mapWidth;
            float worldHeight = mapWorldHeight > 0 ? mapWorldHeight : mapHeight;
            treeGenerationShader.SetFloat(MapWorldWidthID, worldWidth);
            treeGenerationShader.SetFloat(MapWorldHeightID, worldHeight);

            // Dispatch compute shader (8x8 thread groups)
            int threadGroupsX = Mathf.CeilToInt(mapWidth / 8.0f);
            int threadGroupsY = Mathf.CeilToInt(mapHeight / 8.0f);

            if (logProgress)
            {
                ArchonLogger.Log($"TreeInstanceGenerator: Dispatching compute shader ({threadGroupsX}x{threadGroupsY} thread groups)", "map_rendering");
            }

            treeGenerationShader.Dispatch(generateKernel, threadGroupsX, threadGroupsY, 1);

            // ALWAYS read back tree count (critical for debugging)
            uint[] countData = new uint[1];
            treeCountBuffer.GetData(countData);

            if (logProgress)
            {
                ArchonLogger.Log($"TreeInstanceGenerator: Generated {countData[0]} tree instances (density: {treeDensity})", "map_rendering");

                // Debug: Read back first tree position to verify placement
                if (countData[0] > 0)
                {
                    float[] firstMatrix = new float[16];
                    treeMatrixBuffer.GetData(firstMatrix, 0, 0, 16);
                    // Matrix is column-major, position is in columns 3 (index 12, 13, 14)
                    float x = firstMatrix[12];
                    float y = firstMatrix[13];
                    float z = firstMatrix[14];
                    ArchonLogger.Log($"TreeInstanceGenerator: First tree at ({x:F2}, {y:F2}, {z:F2})", "map_rendering");
                }
            }
            else
            {
                // Log even if logProgress is false, because 0 trees is a critical issue
                if (countData[0] == 0)
                {
                    ArchonLogger.LogWarning($"TreeInstanceGenerator: Generated 0 trees! Check terrain type filter and density settings.", "map_rendering");
                }
            }
        }

        /// <summary>
        /// Get the tree matrix buffer for rendering (DrawMeshInstancedIndirect)
        /// </summary>
        public ComputeBuffer GetTreeMatrixBuffer() => treeMatrixBuffer;

        /// <summary>
        /// Get the tree count buffer for indirect args
        /// </summary>
        public ComputeBuffer GetTreeCountBuffer() => treeCountBuffer;

        /// <summary>
        /// Release GPU buffers
        /// </summary>
        public void Release()
        {
            treeMatrixBuffer?.Release();
            treeCountBuffer?.Release();
            terrainTypeFilterBuffer?.Release();

            if (logProgress)
            {
                ArchonLogger.Log("TreeInstanceGenerator: Released GPU buffers", "map_rendering");
            }
        }
    }
}
