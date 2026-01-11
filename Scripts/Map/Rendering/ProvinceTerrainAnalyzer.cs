using UnityEngine;
using UnityEngine.Rendering;
using Map.Core;
using Map.Rendering.Terrain;

namespace Map.Rendering
{
    /// <summary>
    /// ENGINE: Analyzes terrain.bmp to determine dominant terrain type per province
    /// Uses GPU compute shader for efficient majority voting across all pixels
    /// Refactored to use specialized components for RGB lookup, BMP reading, and overrides
    /// </summary>
    public class ProvinceTerrainAnalyzer : MonoBehaviour
    {
        [Header("Compute Shader")]
        [SerializeField] private ComputeShader terrainAnalyzerCompute;

        [Header("Debug")]
        [SerializeField] private bool logAnalysis = true;

        // Kernel indices
        private int countVotesKernel;
        private int determineWinnerKernel;

        // Cached shader property IDs
        private static readonly int ProvinceIDTextureID = Shader.PropertyToID("ProvinceIDTexture");
        private static readonly int TerrainDataBufferID = Shader.PropertyToID("TerrainDataBuffer");
        private static readonly int VoteMatrixID = Shader.PropertyToID("VoteMatrix");
        private static readonly int ProvinceTerrainTypesID = Shader.PropertyToID("ProvinceTerrainTypes");
        private static readonly int MapWidthID = Shader.PropertyToID("MapWidth");
        private static readonly int MapHeightID = Shader.PropertyToID("MapHeight");
        private static readonly int ProvinceCountID = Shader.PropertyToID("ProvinceCount");
        private static readonly int ProvinceIDToIndexBufferID = Shader.PropertyToID("ProvinceIDToIndexBuffer");

        // Specialized components
        private TerrainRGBLookup rgbLookup;
        private TerrainBitmapReader bitmapReader;
        private TerrainOverrideApplicator overrideApplicator;

        void Awake()
        {
            if (terrainAnalyzerCompute == null)
            {
                Debug.LogError("ProvinceTerrainAnalyzer: No compute shader assigned!");
                return;
            }

            // Find kernel indices
            countVotesKernel = terrainAnalyzerCompute.FindKernel("CountVotes");
            determineWinnerKernel = terrainAnalyzerCompute.FindKernel("DetermineWinner");

            // Initialize components - get DataDirectory from GameSettings via MapInitializer
            var mapInitializer = FindFirstObjectByType<MapInitializer>();
            string dataDirectory = mapInitializer?.DataDirectory;

            rgbLookup = new TerrainRGBLookup();
            if (!rgbLookup.Initialize(dataDirectory, logAnalysis))
            {
                Debug.LogError("ProvinceTerrainAnalyzer: Failed to initialize TerrainRGBLookup!");
            }

            bitmapReader = new TerrainBitmapReader(logAnalysis);
            overrideApplicator = new TerrainOverrideApplicator(logAnalysis);
        }

        /// <summary>
        /// Analyze terrain and return results as array
        /// Returns uint[] where [arrayIndex] = terrainTypeIndex (0-255)
        /// Main entry point used by MapDataLoader
        /// </summary>
        public uint[] AnalyzeAndGetTerrainTypes(
            RenderTexture provinceIDTexture,
            Texture2D terrainTexture,
            ushort[] provinceIDs)
        {
            // Get terrain assignments from GPU analysis
            uint[] terrainTypes = AnalyzeProvinceTerrain(provinceIDTexture, terrainTexture, provinceIDs);
            if (terrainTypes == null)
                return null;

            // Debug: Log some sample terrain assignments
            if (logAnalysis)
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.Append("ProvinceTerrainAnalyzer: Terrain type array samples - ");
                for (int i = 1; i <= Mathf.Min(10, provinceIDs.Length); i++)
                {
                    sb.Append($"P{i}=T{terrainTypes[i]} ");
                }
                ArchonLogger.Log(sb.ToString(), "map_rendering");
            }

            if (logAnalysis)
            {
                ArchonLogger.Log($"ProvinceTerrainAnalyzer: Returning terrain type array ({provinceIDs.Length} entries)", "map_rendering");
            }

            return terrainTypes;
        }

        /// <summary>
        /// Analyze terrain from Texture2D
        /// Converts RGB pixels to terrain type indices, then analyzes via GPU
        /// </summary>
        private uint[] AnalyzeProvinceTerrain(
            RenderTexture provinceIDTexture,
            Texture2D terrainTexture,
            ushort[] provinceIDs)
        {
            // Step 1: Convert RGB terrain texture to terrain type indices (0-255)
            uint[] terrainTypeIndices = bitmapReader.ConvertRGBToTerrainTypes(
                terrainTexture,
                rgbLookup,
                terrainTexture.width,
                terrainTexture.height
            );

            if (terrainTypeIndices == null)
            {
                ArchonLogger.LogError("ProvinceTerrainAnalyzer: Failed to convert terrain RGB to indices", "map_rendering");
                return null;
            }

            // Step 2: Analyze using GPU compute shader with terrain indices
            uint[] results = AnalyzeProvinceTerrainGPU(provinceIDTexture, terrainTypeIndices, terrainTexture.width, terrainTexture.height, provinceIDs);

            return results;
        }

        /// <summary>
        /// Core GPU-based terrain analysis using compute shader
        /// Performs majority voting to determine dominant terrain type per province
        /// </summary>
        private uint[] AnalyzeProvinceTerrainGPU(
            RenderTexture provinceIDTexture,
            uint[] terrainTypeIndices,
            int mapWidth,
            int mapHeight,
            ushort[] provinceIDs)
        {
            if (terrainAnalyzerCompute == null)
            {
                Debug.LogError("ProvinceTerrainAnalyzer: No compute shader - cannot analyze terrain");
                return null;
            }

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
                ArchonLogger.Log($"ProvinceTerrainAnalyzer: Created ProvinceID→Index lookup (65536 entries, {MAX_PROVINCE_ID * sizeof(uint) / 1024}KB)", "map_rendering");
            }

            // Create vote matrix buffer: [arrayIndex * 256 + terrainType] = voteCount
            int voteMatrixSize = provinceCount * 256;
            ComputeBuffer voteMatrix = new ComputeBuffer(voteMatrixSize, sizeof(uint));

            // Initialize vote matrix to zero
            uint[] zeroData = new uint[voteMatrixSize];
            voteMatrix.SetData(zeroData);

            // Create output buffer for final terrain assignments
            ComputeBuffer provinceTerrainTypes = new ComputeBuffer(provinceCount, sizeof(uint));

            // Create terrain data buffer (terrain type indices for each pixel)
            ComputeBuffer terrainDataBuffer = new ComputeBuffer(terrainTypeIndices.Length, sizeof(uint));
            terrainDataBuffer.SetData(terrainTypeIndices);

            try
            {
                // ====================================================================
                // PASS 1: Count Votes (Majority Voting per Province)
                // ====================================================================
                terrainAnalyzerCompute.SetTexture(countVotesKernel, ProvinceIDTextureID, provinceIDTexture);
                terrainAnalyzerCompute.SetBuffer(countVotesKernel, TerrainDataBufferID, terrainDataBuffer);
                terrainAnalyzerCompute.SetBuffer(countVotesKernel, VoteMatrixID, voteMatrix);
                terrainAnalyzerCompute.SetBuffer(countVotesKernel, ProvinceIDToIndexBufferID, provinceIDToIndexBuffer);
                terrainAnalyzerCompute.SetInt(MapWidthID, mapWidth);
                terrainAnalyzerCompute.SetInt(MapHeightID, mapHeight);
                terrainAnalyzerCompute.SetInt(ProvinceCountID, provinceCount);

                // Dispatch Pass 1 (8x8 thread groups covering entire map)
                int threadGroupsX = (mapWidth + 7) / 8;
                int threadGroupsY = (mapHeight + 7) / 8;
                terrainAnalyzerCompute.Dispatch(countVotesKernel, threadGroupsX, threadGroupsY, 1);

                if (logAnalysis)
                {
                    ArchonLogger.Log($"ProvinceTerrainAnalyzer: Pass 1 dispatched ({threadGroupsX}x{threadGroupsY} thread groups, {threadGroupsX * threadGroupsY * 64} threads)", "map_rendering");
                }

                // ====================================================================
                // PASS 2: Determine Winner (Majority Vote)
                // ====================================================================
                terrainAnalyzerCompute.SetBuffer(determineWinnerKernel, VoteMatrixID, voteMatrix);
                terrainAnalyzerCompute.SetBuffer(determineWinnerKernel, ProvinceTerrainTypesID, provinceTerrainTypes);
                terrainAnalyzerCompute.SetInt(ProvinceCountID, provinceCount);

                // Calculate thread groups (256 threads per province)
                int threadGroupsForProvinces = (provinceCount + 255) / 256;

                // Dispatch Pass 2
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
                // Always release GPU buffers
                voteMatrix?.Release();
                provinceTerrainTypes?.Release();
                provinceIDToIndexBuffer?.Release();
                terrainDataBuffer?.Release();

                if (logAnalysis)
                {
                    ArchonLogger.Log("ProvinceTerrainAnalyzer: Analysis complete, buffers released", "map_rendering");
                }
            }
        }
    }
}
