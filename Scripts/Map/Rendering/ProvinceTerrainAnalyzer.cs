using UnityEngine;
using Map.Rendering.Terrain;
using Core.Modding;

namespace Map.Rendering
{
    /// <summary>
    /// ENGINE: Analyzes terrain.bmp to determine dominant terrain type per province
    /// Uses GPU compute shader for efficient majority voting across all pixels
    /// Refactored to use specialized components for RGB lookup, BMP reading, and overrides
    /// </summary>
    public class ProvinceTerrainAnalyzer : MonoBehaviour
    {
        // Loaded via ModLoader
        private ComputeShader terrainAnalyzerCompute;

        [Header("Debug")]
        [SerializeField] private bool logAnalysis = true;

        // Kernel indices
        private int countVotesKernel;
        private int determineWinnerKernel;

        // Cached shader property IDs
        private static readonly int ProvinceIDTextureID = Shader.PropertyToID("ProvinceIDTexture");
        private static readonly int TerrainTypeTextureID = Shader.PropertyToID("TerrainTypeTexture");
        private static readonly int VoteMatrixID = Shader.PropertyToID("VoteMatrix");
        private static readonly int ProvinceTerrainTypesID = Shader.PropertyToID("ProvinceTerrainTypes");
        private static readonly int MapWidthID = Shader.PropertyToID("MapWidth");
        private static readonly int MapHeightID = Shader.PropertyToID("MapHeight");
        private static readonly int ProvinceCountID = Shader.PropertyToID("ProvinceCount");
        private static readonly int ProvinceIDToIndexBufferID = Shader.PropertyToID("ProvinceIDToIndexBuffer");

        // Specialized components
        private TerrainRGBLookup rgbLookup;
        private TerrainOverrideApplicator overrideApplicator;
        private bool isInitialized = false;

        /// <summary>
        /// Initialize terrain analyzer. Called by ArchonEngine after GameSettings is registered.
        /// </summary>
        public void Initialize(string dataDirectory)
        {
            if (isInitialized) return;
            isInitialized = true;

            if (terrainAnalyzerCompute == null)
            {
                // Load compute shader - check mods first, then fall back to Resources
                terrainAnalyzerCompute = ModLoader.LoadAssetWithFallback<ComputeShader>(
                    "ProvinceTerrainAnalyzer",
                    "Shaders/ProvinceTerrainAnalyzer"
                );

                if (terrainAnalyzerCompute == null)
                {
                    ArchonLogger.LogError("ProvinceTerrainAnalyzer: Compute shader not found!", "map_rendering");
                    return;
                }
            }

            // Find kernel indices
            countVotesKernel = terrainAnalyzerCompute.FindKernel("CountVotes");
            determineWinnerKernel = terrainAnalyzerCompute.FindKernel("DetermineWinner");

            rgbLookup = new TerrainRGBLookup();
            if (!rgbLookup.Initialize(dataDirectory, logAnalysis))
            {
                Debug.LogError("ProvinceTerrainAnalyzer: Failed to initialize TerrainRGBLookup!");
            }

            overrideApplicator = new TerrainOverrideApplicator(dataDirectory, logAnalysis);
        }

        /// <summary>
        /// Analyze terrain and return results as array
        /// Returns uint[] where [arrayIndex] = terrainTypeIndex (0-255)
        /// Main entry point used by MapDataLoader
        /// </summary>
        public uint[] AnalyzeAndGetTerrainTypes(
            RenderTexture provinceIDTexture,
            Texture2D terrainTypeTexture,
            ushort[] provinceIDs)
        {
            // GPU majority voting directly from the R8 terrain type texture (already on GPU)
            uint[] terrainTypes = AnalyzeProvinceTerrainGPU(
                provinceIDTexture, terrainTypeTexture, provinceIDs);

            if (terrainTypes == null)
                return null;

            if (logAnalysis)
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.Append("ProvinceTerrainAnalyzer: Terrain type array samples - ");
                for (int i = 1; i <= Mathf.Min(10, provinceIDs.Length); i++)
                {
                    sb.Append($"P{i}=T{terrainTypes[i]} ");
                }
                ArchonLogger.Log(sb.ToString(), "map_rendering");

                ArchonLogger.Log($"ProvinceTerrainAnalyzer: Returning terrain type array ({provinceIDs.Length} entries)", "map_rendering");
            }

            return terrainTypes;
        }

        /// <summary>
        /// Core GPU-based terrain analysis using compute shader.
        /// Samples terrain type directly from the R8 texture (already on GPU).
        /// No CPU conversion needed — eliminates the old 5.8s RGB→index bottleneck.
        /// </summary>
        private uint[] AnalyzeProvinceTerrainGPU(
            RenderTexture provinceIDTexture,
            Texture2D terrainTypeTexture,
            ushort[] provinceIDs)
        {
            if (terrainAnalyzerCompute == null)
            {
                ArchonLogger.LogError("ProvinceTerrainAnalyzer: No compute shader - cannot analyze terrain", "map_rendering");
                return null;
            }

            int mapWidth = terrainTypeTexture.width;
            int mapHeight = terrainTypeTexture.height;
            int provinceCount = provinceIDs.Length;

            if (logAnalysis)
            {
                ArchonLogger.Log($"ProvinceTerrainAnalyzer: Starting terrain analysis for {provinceCount} provinces ({mapWidth}x{mapHeight} map)", "map_rendering");
            }

            // Create Province ID → Array Index lookup buffer
            const int MAX_PROVINCE_ID = 65536;
            uint[] provinceIDToIndex = new uint[MAX_PROVINCE_ID];
            for (int i = 0; i < provinceIDs.Length; i++)
            {
                provinceIDToIndex[provinceIDs[i]] = (uint)i;
            }
            ComputeBuffer provinceIDToIndexBuffer = new ComputeBuffer(MAX_PROVINCE_ID, sizeof(uint));
            provinceIDToIndexBuffer.SetData(provinceIDToIndex);

            if (logAnalysis)
            {
                ArchonLogger.Log($"ProvinceTerrainAnalyzer: Created ProvinceID→Index lookup ({MAX_PROVINCE_ID} entries, {MAX_PROVINCE_ID * sizeof(uint) / 1024}KB)", "map_rendering");
            }

            // Create vote matrix buffer: [arrayIndex * 256 + terrainType] = voteCount
            int voteMatrixSize = provinceCount * 256;
            ComputeBuffer voteMatrix = new ComputeBuffer(voteMatrixSize, sizeof(uint));

            // Initialize vote matrix to zero
            uint[] zeroData = new uint[voteMatrixSize];
            voteMatrix.SetData(zeroData);

            // Create output buffer for final terrain assignments
            ComputeBuffer provinceTerrainTypes = new ComputeBuffer(provinceCount, sizeof(uint));

            try
            {
                // PASS 1: Count Votes — sample terrain type directly from R8 texture
                terrainAnalyzerCompute.SetTexture(countVotesKernel, ProvinceIDTextureID, provinceIDTexture);
                terrainAnalyzerCompute.SetTexture(countVotesKernel, TerrainTypeTextureID, terrainTypeTexture);
                terrainAnalyzerCompute.SetBuffer(countVotesKernel, VoteMatrixID, voteMatrix);
                terrainAnalyzerCompute.SetBuffer(countVotesKernel, ProvinceIDToIndexBufferID, provinceIDToIndexBuffer);
                terrainAnalyzerCompute.SetInt(MapWidthID, mapWidth);
                terrainAnalyzerCompute.SetInt(MapHeightID, mapHeight);
                terrainAnalyzerCompute.SetInt(ProvinceCountID, provinceCount);

                int threadGroupsX = (mapWidth + 7) / 8;
                int threadGroupsY = (mapHeight + 7) / 8;
                terrainAnalyzerCompute.Dispatch(countVotesKernel, threadGroupsX, threadGroupsY, 1);

                if (logAnalysis)
                {
                    ArchonLogger.Log($"ProvinceTerrainAnalyzer: Pass 1 dispatched ({threadGroupsX}x{threadGroupsY} thread groups, {threadGroupsX * threadGroupsY * 64} threads)", "map_rendering");
                }

                // PASS 2: Determine Winner
                terrainAnalyzerCompute.SetBuffer(determineWinnerKernel, VoteMatrixID, voteMatrix);
                terrainAnalyzerCompute.SetBuffer(determineWinnerKernel, ProvinceTerrainTypesID, provinceTerrainTypes);
                terrainAnalyzerCompute.SetInt(ProvinceCountID, provinceCount);

                int threadGroupsForProvinces = (provinceCount + 255) / 256;
                terrainAnalyzerCompute.Dispatch(determineWinnerKernel, threadGroupsForProvinces, 1, 1);

                if (logAnalysis)
                {
                    ArchonLogger.Log($"ProvinceTerrainAnalyzer: Pass 2 dispatched ({threadGroupsForProvinces} thread groups)", "map_rendering");
                }

                // Read results back to CPU
                uint[] results = new uint[provinceCount];
                provinceTerrainTypes.GetData(results);

                if (logAnalysis)
                {
                    ArchonLogger.Log($"ProvinceTerrainAnalyzer: GPU analysis complete, retrieved {results.Length} terrain assignments", "map_rendering");
                }

                // Apply terrain overrides from terrain.json5
                overrideApplicator.ApplyOverrides(results, provinceIDs, rgbLookup);

                return results;
            }
            finally
            {
                voteMatrix?.Release();
                provinceTerrainTypes?.Release();
                provinceIDToIndexBuffer?.Release();

                if (logAnalysis)
                {
                    ArchonLogger.Log("ProvinceTerrainAnalyzer: Analysis complete, buffers released", "map_rendering");
                }
            }
        }
    }
}
