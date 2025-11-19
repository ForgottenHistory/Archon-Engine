using UnityEngine;
using UnityEngine.Rendering;
using Core.Loaders;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Linq;

namespace Map.Rendering
{
    /// <summary>
    /// ENGINE: Analyzes terrain.bmp to determine dominant terrain type per province
    /// Uses GPU compute shader for efficient majority voting across all pixels
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
        private static readonly int TerrainTextureID = Shader.PropertyToID("TerrainTexture");
        private static readonly int VoteMatrixID = Shader.PropertyToID("VoteMatrix");
        private static readonly int ProvinceTerrainTypesID = Shader.PropertyToID("ProvinceTerrainTypes");
        private static readonly int MapWidthID = Shader.PropertyToID("MapWidth");
        private static readonly int MapHeightID = Shader.PropertyToID("MapHeight");
        private static readonly int ProvinceCountID = Shader.PropertyToID("ProvinceCount");
        private static readonly int ProvinceIDToIndexBufferID = Shader.PropertyToID("ProvinceIDToIndexBuffer");

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
        }

        /// <summary>
        /// Analyze terrain.bmp to determine dominant terrain type for each province
        /// Returns array: [provinceID] = terrainTypeIndex (0-255)
        /// </summary>
        public uint[] AnalyzeProvinceTerrain(
            RenderTexture provinceIDTexture,
            RenderTexture terrainTexture,
            ushort[] provinceIDs)
        {
            if (terrainAnalyzerCompute == null)
            {
                Debug.LogError("ProvinceTerrainAnalyzer: No compute shader - cannot analyze terrain");
                return null;
            }

            int mapWidth = provinceIDTexture.width;
            int mapHeight = provinceIDTexture.height;
            int provinceCount = provinceIDs.Length;

            if (logAnalysis)
            {
                ArchonLogger.Log($"ProvinceTerrainAnalyzer: Starting terrain analysis for {provinceCount} provinces ({mapWidth}x{mapHeight} map)", "map_rendering");
            }

            // Create Province ID → Array Index lookup buffer
            // Size by max province ID (65536), value is array index (0 to provinceCount-1)
            // Invalid IDs map to 0 (ocean)
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
            // Each province has 256 vote counters (one per terrain type)
            int voteMatrixSize = provinceCount * 256;
            ComputeBuffer voteMatrix = new ComputeBuffer(voteMatrixSize, sizeof(uint));

            // Initialize vote matrix to zero
            uint[] zeroData = new uint[voteMatrixSize];
            voteMatrix.SetData(zeroData);

            // Create output buffer for final terrain assignments
            ComputeBuffer provinceTerrainTypes = new ComputeBuffer(provinceCount, sizeof(uint));

            try
            {
                // ====================================================================
                // PASS 1: Count Votes
                // ====================================================================

                terrainAnalyzerCompute.SetTexture(countVotesKernel, ProvinceIDTextureID, provinceIDTexture);
                terrainAnalyzerCompute.SetTexture(countVotesKernel, TerrainTextureID, terrainTexture);
                terrainAnalyzerCompute.SetBuffer(countVotesKernel, VoteMatrixID, voteMatrix);
                terrainAnalyzerCompute.SetBuffer(countVotesKernel, ProvinceIDToIndexBufferID, provinceIDToIndexBuffer);
                terrainAnalyzerCompute.SetInt(MapWidthID, mapWidth);
                terrainAnalyzerCompute.SetInt(MapHeightID, mapHeight);
                terrainAnalyzerCompute.SetInt(ProvinceCountID, provinceCount);

                // Calculate thread groups (8x8 threads per group)
                const int THREAD_GROUP_SIZE = 8;
                int threadGroupsX = (mapWidth + THREAD_GROUP_SIZE - 1) / THREAD_GROUP_SIZE;
                int threadGroupsY = (mapHeight + THREAD_GROUP_SIZE - 1) / THREAD_GROUP_SIZE;

                // Dispatch Pass 1
                terrainAnalyzerCompute.Dispatch(countVotesKernel, threadGroupsX, threadGroupsY, 1);

                if (logAnalysis)
                {
                    ArchonLogger.Log($"ProvinceTerrainAnalyzer: Pass 1 dispatched ({threadGroupsX}x{threadGroupsY} thread groups)", "map_rendering");
                }

                // CRITICAL: GPU sync between passes (see unity-compute-shader-coordination.md)
                // Pass 2 reads from VoteMatrix that Pass 1 writes to
                var syncRequest = AsyncGPUReadback.Request(voteMatrix);
                syncRequest.WaitForCompletion();

                if (logAnalysis)
                {
                    ArchonLogger.Log("ProvinceTerrainAnalyzer: GPU sync complete after Pass 1", "map_rendering");
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
                    // Log first few results for verification
                    int sampleCount = Mathf.Min(10, provinceCount);
                    System.Text.StringBuilder sb = new System.Text.StringBuilder();
                    sb.Append("ProvinceTerrainAnalyzer: Sample results - ");
                    for (int i = 1; i < sampleCount; i++) // Skip province 0 (ocean)
                    {
                        sb.Append($"P{i}=T{results[i]} ");
                    }
                    ArchonLogger.Log(sb.ToString(), "map_rendering");
                }

                return results;
            }
            finally
            {
                // Always release GPU buffers
                voteMatrix?.Release();
                provinceTerrainTypes?.Release();
                provinceIDToIndexBuffer?.Release();

                if (logAnalysis)
                {
                    ArchonLogger.Log("ProvinceTerrainAnalyzer: Analysis complete, buffers released", "map_rendering");
                }
            }
        }

        /// <summary>
        /// Analyze terrain and store results in a lookup texture
        /// Creates a 1D texture: [arrayIndex] = terrainTypeIndex
        /// Overload for Texture2D terrain input (uses Texture2D directly - no conversion needed)
        /// </summary>
        public RenderTexture AnalyzeAndCreateLookupTexture(
            RenderTexture provinceIDTexture,
            Texture2D terrainTexture,
            ushort[] provinceIDs)
        {
            // Get terrain assignments - use Texture2D version
            uint[] terrainTypes = AnalyzeProvinceTerrain(provinceIDTexture, terrainTexture, provinceIDs);
            int provinceCount = provinceIDs.Length;
            if (terrainTypes == null)
                return null;

            // Create 1D lookup texture
            var descriptor = new RenderTextureDescriptor(
                provinceCount, 1,
                UnityEngine.Experimental.Rendering.GraphicsFormat.R8_UNorm,
                0
            );
            descriptor.enableRandomWrite = true;
            descriptor.useMipMap = false;
            descriptor.dimension = UnityEngine.Rendering.TextureDimension.Tex2D;

            RenderTexture lookupTexture = new RenderTexture(descriptor);
            lookupTexture.name = "ProvinceTerrainLookup";
            lookupTexture.filterMode = FilterMode.Point;
            lookupTexture.wrapMode = TextureWrapMode.Clamp;
            lookupTexture.Create();

            // Upload data to GPU
            Color[] pixels = new Color[provinceCount];
            for (int i = 0; i < provinceCount; i++)
            {
                float normalizedValue = terrainTypes[i] / 255.0f;
                pixels[i] = new Color(normalizedValue, 0, 0, 1);
            }

            Texture2D tempTexture = new Texture2D(provinceCount, 1, TextureFormat.RGBA32, false);
            tempTexture.SetPixels(pixels);
            tempTexture.Apply();

            // Copy to RenderTexture
            Graphics.Blit(tempTexture, lookupTexture);
            Object.Destroy(tempTexture);

            if (logAnalysis)
            {
                ArchonLogger.Log($"ProvinceTerrainAnalyzer: Created lookup texture ({provinceCount} entries)", "map_rendering");
            }

            return lookupTexture;
        }

        /// <summary>
        /// Analyze terrain from Texture2D
        /// Converts Texture2D to temporary RenderTexture for compute shader access
        /// </summary>
        public uint[] AnalyzeProvinceTerrain(
            RenderTexture provinceIDTexture,
            Texture2D terrainTexture,
            ushort[] provinceIDs)
        {
            if (terrainAnalyzerCompute == null)
            {
                ArchonLogger.LogError("ProvinceTerrainAnalyzer: No compute shader - cannot analyze terrain", "map_rendering");
                return null;
            }

            int mapWidth = provinceIDTexture.width;
            int mapHeight = provinceIDTexture.height;
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

            // Build RGB → Terrain Type lookup from terrain.json5
            var rgbToTerrainType = BuildRGBToTerrainTypeLookup();

            if (rgbToTerrainType == null || rgbToTerrainType.Count == 0)
            {
                ArchonLogger.LogError("ProvinceTerrainAnalyzer: Failed to build RGB→Terrain lookup from terrain.json5", "map_rendering");
                return null;
            }

            // Read terrain.bmp RGB pixels and convert to terrain type indices
            uint[] terrainTypeData = ConvertRGBToTerrainTypes(terrainTexture, rgbToTerrainType, mapWidth, mapHeight);

            if (logAnalysis)
            {
                // Sample terrain type data across the entire map (not just first 10 pixels!)
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.Append("ProvinceTerrainAnalyzer: Terrain type samples (distributed) - ");

                // Sample at different positions across the map
                int sampleCount = 10;
                for (int i = 0; i < sampleCount; i++)
                {
                    // Distribute samples across entire map (0%, 11%, 22%, ..., 99%)
                    int sampleIndex = (terrainTypeData.Length * i) / sampleCount;
                    sb.Append($"[{sampleIndex}]={terrainTypeData[sampleIndex]} ");
                }
                ArchonLogger.Log(sb.ToString(), "map_rendering");

                // Also count unique terrain types to verify variety
                var uniqueTypes = new System.Collections.Generic.HashSet<uint>();
                for (int i = 0; i < terrainTypeData.Length; i++)
                {
                    uniqueTypes.Add(terrainTypeData[i]);
                }
                ArchonLogger.Log($"ProvinceTerrainAnalyzer: Found {uniqueTypes.Count} unique terrain types in {terrainTypeData.Length} pixels", "map_rendering");
            }

            // Upload terrain data via ComputeBuffer (follows NormalMapGenerator pattern)
            // Use sizeof(uint) for stride (must be multiple of 4)
            ComputeBuffer terrainDataBuffer = new ComputeBuffer(terrainTypeData.Length, sizeof(uint));
            terrainDataBuffer.SetData(terrainTypeData);

            // Create vote matrix buffer
            int voteMatrixSize = provinceCount * 256;
            ComputeBuffer voteMatrix = new ComputeBuffer(voteMatrixSize, sizeof(uint));
            uint[] zeroData = new uint[voteMatrixSize];
            voteMatrix.SetData(zeroData);

            // Create output buffer
            ComputeBuffer provinceTerrainTypes = new ComputeBuffer(provinceCount, sizeof(uint));

            // DEBUG: Verify ProvinceIDTexture has data
            if (logAnalysis)
            {
                RenderTexture.active = provinceIDTexture;
                Texture2D tempRead = new Texture2D(mapWidth, mapHeight, TextureFormat.RGBA32, false);
                tempRead.ReadPixels(new Rect(0, 0, mapWidth, mapHeight), 0, 0);
                tempRead.Apply();
                RenderTexture.active = null;

                // Sample a few pixels to verify province IDs are present
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.Append("ProvinceTerrainAnalyzer: ProvinceIDTexture samples - ");
                for (int i = 0; i < 5; i++)
                {
                    int sampleX = (mapWidth * i) / 5;
                    int sampleY = mapHeight / 2;
                    Color pixel = tempRead.GetPixel(sampleX, sampleY);
                    uint r = (uint)(pixel.r * 255.0f + 0.5f);
                    uint g = (uint)(pixel.g * 255.0f + 0.5f);
                    uint provinceID = (g << 8) | r;
                    sb.Append($"[{sampleX},{sampleY}]=P{provinceID} ");
                }
                ArchonLogger.Log(sb.ToString(), "map_rendering");
                Object.Destroy(tempRead);
            }

            try
            {
                // Pass 1: Count Votes - use ComputeBuffer for terrain data
                terrainAnalyzerCompute.SetTexture(countVotesKernel, ProvinceIDTextureID, provinceIDTexture);
                terrainAnalyzerCompute.SetBuffer(countVotesKernel, "TerrainDataBuffer", terrainDataBuffer);  // Use buffer, not texture
                terrainAnalyzerCompute.SetBuffer(countVotesKernel, VoteMatrixID, voteMatrix);
                terrainAnalyzerCompute.SetBuffer(countVotesKernel, ProvinceIDToIndexBufferID, provinceIDToIndexBuffer);
                terrainAnalyzerCompute.SetInt(MapWidthID, mapWidth);
                terrainAnalyzerCompute.SetInt(MapHeightID, mapHeight);
                terrainAnalyzerCompute.SetInt(ProvinceCountID, provinceCount);

                const int THREAD_GROUP_SIZE = 8;
                int threadGroupsX = (mapWidth + THREAD_GROUP_SIZE - 1) / THREAD_GROUP_SIZE;
                int threadGroupsY = (mapHeight + THREAD_GROUP_SIZE - 1) / THREAD_GROUP_SIZE;

                if (logAnalysis)
                {
                    ArchonLogger.Log($"ProvinceTerrainAnalyzer: Dispatching CountVotes - {threadGroupsX}x{threadGroupsY} thread groups, {provinceCount} provinces", "map_rendering");
                    ArchonLogger.Log($"ProvinceTerrainAnalyzer: VoteMatrix size = {voteMatrixSize} (province {provinceCount} * 256 terrain types)", "map_rendering");
                }

                terrainAnalyzerCompute.Dispatch(countVotesKernel, threadGroupsX, threadGroupsY, 1);

                // GPU sync
                var syncRequest = AsyncGPUReadback.Request(voteMatrix);
                syncRequest.WaitForCompletion();

                // Pass 2: Determine Winner
                terrainAnalyzerCompute.SetBuffer(determineWinnerKernel, VoteMatrixID, voteMatrix);
                terrainAnalyzerCompute.SetBuffer(determineWinnerKernel, ProvinceTerrainTypesID, provinceTerrainTypes);
                terrainAnalyzerCompute.SetInt(ProvinceCountID, provinceCount);

                int threadGroupsForProvinces = (provinceCount + 255) / 256;
                terrainAnalyzerCompute.Dispatch(determineWinnerKernel, threadGroupsForProvinces, 1, 1);

                // Read results
                uint[] results = new uint[provinceCount];
                provinceTerrainTypes.GetData(results);

                if (logAnalysis)
                {
                    // Sample results distributed across province range
                    System.Text.StringBuilder sb = new System.Text.StringBuilder();
                    sb.Append("ProvinceTerrainAnalyzer: Sample results (distributed) - ");
                    int sampleCount = 10;
                    for (int i = 0; i < sampleCount; i++)
                    {
                        int provinceID = (provinceCount * i) / sampleCount;
                        if (provinceID >= 1 && provinceID < provinceCount)
                        {
                            sb.Append($"P{provinceID}=T{results[provinceID]} ");
                        }
                    }
                    ArchonLogger.Log(sb.ToString(), "map_rendering");

                    // Count unique terrain type assignments
                    var uniqueAssignments = new System.Collections.Generic.HashSet<uint>();
                    for (int i = 1; i < results.Length; i++)
                    {
                        uniqueAssignments.Add(results[i]);
                    }
                    ArchonLogger.Log($"ProvinceTerrainAnalyzer: {uniqueAssignments.Count} unique terrain types assigned across {provinceCount} provinces", "map_rendering");

                    // DEBUG: Check entire VoteMatrix to see if ANY votes were counted
                    uint[] allVotes = new uint[voteMatrixSize];
                    voteMatrix.GetData(allVotes);

                    uint totalVotes = 0;
                    int nonZeroEntries = 0;
                    for (int i = 0; i < allVotes.Length; i++)
                    {
                        if (allVotes[i] > 0)
                        {
                            totalVotes += allVotes[i];
                            nonZeroEntries++;
                        }
                    }
                    ArchonLogger.Log($"ProvinceTerrainAnalyzer: VoteMatrix stats - {totalVotes} total votes across {nonZeroEntries} non-zero entries (expected ~{mapWidth * mapHeight} pixel votes)", "map_rendering");

                    // Sample a few provinces to see their vote distributions
                    // Include some provinces that should be mountains (e.g., Alps)
                    var sampleProvinceIDs = new List<ushort> { 1, 2, 3, 100, 110, 112, 113, 1276, 1314, 4333 };  // 1276 is ocean, 1314 is inland_ocean, 4333 is sea

                    foreach (ushort provinceID in sampleProvinceIDs)
                    {
                        // Convert province ID to array index using lookup
                        if (provinceID >= provinceIDToIndex.Length)
                            continue;

                        uint arrayIndex = provinceIDToIndex[provinceID];
                        if (arrayIndex == 0 && provinceID != 0)
                        {
                            // This province ID doesn't exist in the dataset
                            continue;
                        }

                        sb.Clear();
                        sb.Append($"ProvinceTerrainAnalyzer: Province {provinceID} (index {arrayIndex}) votes - ");
                        bool hasVotes = false;
                        uint totalVotesForProvince = 0;

                        for (int t = 0; t < 256; t++)
                        {
                            uint votes = allVotes[arrayIndex * 256 + t];
                            if (votes > 0)
                            {
                                sb.Append($"T{t}:{votes} ");
                                hasVotes = true;
                                totalVotesForProvince += votes;
                            }
                        }

                        if (!hasVotes)
                        {
                            sb.Append("(no votes)");
                        }
                        else
                        {
                            sb.Append($"| Total: {totalVotesForProvince} | Winner: T{results[arrayIndex]}");
                        }

                        ArchonLogger.Log(sb.ToString(), "map_rendering");
                    }
                }

                // Apply terrain overrides from terrain.json5 (DISABLED - incompatible with terrain_rgb.json5 system)
                // EU4 uses terrain_override arrays to assign specific provinces different terrain
                // This happens AFTER the bitmap majority vote
                // TODO: Fix terrain overrides to work with terrain_rgb.json5 index system
                // ApplyTerrainOverrides(results);

                return results;
            }
            finally
            {
                terrainDataBuffer?.Release();
                voteMatrix?.Release();
                provinceTerrainTypes?.Release();
                provinceIDToIndexBuffer?.Release();

                if (logAnalysis)
                {
                    ArchonLogger.Log("ProvinceTerrainAnalyzer: Analysis complete, buffers released", "map_rendering");
                }
            }
        }

        /// <summary>
        /// Apply terrain overrides from terrain.json5
        /// EU4 uses terrain_override arrays to force specific provinces to specific terrain types
        /// regardless of what the terrain.bmp shows
        /// </summary>
        private void ApplyTerrainOverrides(uint[] terrainAssignments)
        {
            try
            {
                // Load terrain.json5
                string terrainJsonPath = System.IO.Path.Combine(UnityEngine.Application.dataPath, "Data", "map", "terrain.json5");

                if (!System.IO.File.Exists(terrainJsonPath))
                {
                    ArchonLogger.LogWarning($"ProvinceTerrainAnalyzer: terrain.json5 not found at {terrainJsonPath}", "map_rendering");
                    return;
                }

                JObject terrainData = Json5Loader.LoadJson5File(terrainJsonPath);

                // Build category name → terrain index mapping from "terrain" section
                // terrain.json5 has two sections:
                // - "terrain": maps terrain names to indices (e.g., mountain: { color: [6] })
                // - "categories": defines terrain properties and overrides
                var categoryToIndex = new Dictionary<string, uint>();

                JObject terrainSection = Json5Loader.GetObject(terrainData, "terrain");
                if (terrainSection != null)
                {
                    foreach (var property in terrainSection.Properties())
                    {
                        string terrainName = property.Name;
                        if (property.Value is JObject terrainObj)
                        {
                            var colorArray = Json5Loader.GetIntArray(terrainObj, "color");
                            if (colorArray.Count > 0)
                            {
                                categoryToIndex[terrainName] = (uint)colorArray[0];
                            }
                        }
                    }
                }

                if (logAnalysis)
                {
                    ArchonLogger.Log($"ProvinceTerrainAnalyzer: Loaded {categoryToIndex.Count} terrain type mappings", "map_rendering");
                }

                // Parse "categories" section for terrain_override arrays
                JObject categoriesSection = Json5Loader.GetObject(terrainData, "categories");
                if (categoriesSection == null)
                {
                    ArchonLogger.LogWarning("ProvinceTerrainAnalyzer: No 'categories' section in terrain.json5", "map_rendering");
                    return;
                }

                int overridesApplied = 0;

                foreach (var categoryProperty in categoriesSection.Properties())
                {
                    string categoryName = categoryProperty.Name;

                    if (categoryProperty.Value is JObject categoryObj)
                    {
                        // Get terrain_override array
                        var overrideProvinces = Json5Loader.GetIntArray(categoryObj, "terrain_override");

                        if (overrideProvinces.Count > 0)
                        {
                            // Find the terrain index for this category
                            // Look for matching entry in categoryToIndex
                            uint terrainIndex = 0;
                            if (categoryToIndex.TryGetValue(categoryName, out terrainIndex))
                            {
                                // Apply overrides for all provinces in the list
                                foreach (int provinceID in overrideProvinces)
                                {
                                    if (provinceID > 0 && provinceID < terrainAssignments.Length)
                                    {
                                        terrainAssignments[provinceID] = terrainIndex;
                                        overridesApplied++;
                                    }
                                }
                            }
                            else if (logAnalysis)
                            {
                                ArchonLogger.LogWarning($"ProvinceTerrainAnalyzer: No terrain index mapping for category '{categoryName}'", "map_rendering");
                            }
                        }
                    }
                }

                if (logAnalysis)
                {
                    ArchonLogger.Log($"ProvinceTerrainAnalyzer: Applied {overridesApplied} terrain overrides from terrain.json5", "map_rendering");
                }
            }
            catch (System.Exception e)
            {
                ArchonLogger.LogError($"ProvinceTerrainAnalyzer: Failed to load terrain overrides: {e.Message}", "map_rendering");
            }
        }

        /// <summary>
        /// Read raw palette indices from an 8-bit indexed BMP file
        /// Bypasses Unity's texture import which converts palette indices to RGB
        /// </summary>
        private uint[] ReadBmpPaletteIndices(string bmpPath, int expectedWidth, int expectedHeight)
        {
            try
            {
                if (!System.IO.File.Exists(bmpPath))
                {
                    ArchonLogger.LogError($"ProvinceTerrainAnalyzer: BMP file not found: {bmpPath}", "map_rendering");
                    return null;
                }

                using (System.IO.FileStream fs = new System.IO.FileStream(bmpPath, System.IO.FileMode.Open, System.IO.FileAccess.Read))
                using (System.IO.BinaryReader reader = new System.IO.BinaryReader(fs))
                {
                    // Read BMP header
                    ushort signature = reader.ReadUInt16();
                    if (signature != 0x4D42) // "BM" in little-endian
                    {
                        ArchonLogger.LogError("ProvinceTerrainAnalyzer: Not a valid BMP file (wrong signature)", "map_rendering");
                        return null;
                    }

                    reader.ReadUInt32(); // File size
                    reader.ReadUInt32(); // Reserved
                    uint dataOffset = reader.ReadUInt32(); // Offset to pixel data

                    // Read DIB header
                    uint headerSize = reader.ReadUInt32();
                    int width = reader.ReadInt32();
                    int height = reader.ReadInt32();
                    reader.ReadUInt16(); // Planes
                    ushort bitsPerPixel = reader.ReadUInt16();

                    if (bitsPerPixel != 8)
                    {
                        ArchonLogger.LogError($"ProvinceTerrainAnalyzer: BMP must be 8-bit indexed (got {bitsPerPixel}-bit)", "map_rendering");
                        return null;
                    }

                    if (logAnalysis)
                    {
                        ArchonLogger.Log($"ProvinceTerrainAnalyzer: Reading 8-bit BMP - {width}x{height}, data offset: {dataOffset}", "map_rendering");
                    }

                    // Read palette (256 entries, 4 bytes each: B, G, R, Reserved)
                    // Palette starts after DIB header
                    fs.Seek(14 + headerSize, System.IO.SeekOrigin.Begin);

                    var palette = new System.Collections.Generic.List<(byte r, byte g, byte b)>();
                    for (int i = 0; i < 256; i++)
                    {
                        byte b = reader.ReadByte();
                        byte g = reader.ReadByte();
                        byte r = reader.ReadByte();
                        reader.ReadByte(); // Reserved
                        palette.Add((r, g, b));
                    }

                    if (logAnalysis)
                    {
                        System.Text.StringBuilder sb = new System.Text.StringBuilder();
                        sb.AppendLine("ProvinceTerrainAnalyzer: BMP Palette (first 20 entries):");
                        for (int i = 0; i < System.Math.Min(20, palette.Count); i++)
                        {
                            var (r, g, b) = palette[i];
                            sb.AppendLine($"  Index {i}: RGB({r}, {g}, {b})");
                        }
                        ArchonLogger.Log(sb.ToString(), "map_rendering");
                    }

                    // Skip to pixel data
                    fs.Seek(dataOffset, System.IO.SeekOrigin.Begin);

                    // Read pixel data (palette indices)
                    // BMP rows are bottom-up and padded to 4-byte boundaries
                    int rowSize = ((width * bitsPerPixel + 31) / 32) * 4;
                    uint[] indices = new uint[width * height];

                    for (int y = 0; y < height; y++)
                    {
                        // BMP is bottom-up, so read from bottom to top
                        int targetY = height - 1 - y;

                        for (int x = 0; x < width; x++)
                        {
                            byte paletteIndex = reader.ReadByte();
                            indices[targetY * width + x] = paletteIndex;
                        }

                        // Skip row padding
                        int padding = rowSize - width;
                        if (padding > 0)
                        {
                            reader.ReadBytes(padding);
                        }
                    }

                    if (logAnalysis)
                    {
                        ArchonLogger.Log($"ProvinceTerrainAnalyzer: Successfully read {indices.Length} palette indices from BMP", "map_rendering");

                        // Sample some values
                        System.Text.StringBuilder sb = new System.Text.StringBuilder();
                        sb.Append("ProvinceTerrainAnalyzer: BMP palette index samples - ");
                        for (int i = 0; i < 10; i++)
                        {
                            int idx = (indices.Length * i) / 10;
                            sb.Append($"[{idx}]={indices[idx]} ");
                        }
                        ArchonLogger.Log(sb.ToString(), "map_rendering");
                    }

                    return indices;
                }
            }
            catch (System.Exception e)
            {
                ArchonLogger.LogError($"ProvinceTerrainAnalyzer: Failed to read BMP file: {e.Message}", "map_rendering");
                return null;
            }
        }

        /// <summary>
        /// Analyze terrain and return results as array
        /// Returns uint[] where [arrayIndex] = terrainTypeIndex (0-255)
        /// </summary>
        public uint[] AnalyzeAndGetTerrainTypes(
            RenderTexture provinceIDTexture,
            Texture2D terrainTexture,
            ushort[] provinceIDs)
        {
            // Get terrain assignments and return directly (no RenderTexture conversion)
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
        /// Build RGB → Terrain Type lookup from terrain_rgb.json5
        /// Returns dictionary: RGB(r,g,b) → terrain type index
        /// Terrain type index is determined by ORDER in terrain_rgb.json5 (0, 1, 2, 3...)
        /// </summary>
        private Dictionary<(byte r, byte g, byte b), uint> BuildRGBToTerrainTypeLookup()
        {
            var lookup = new Dictionary<(byte r, byte g, byte b), uint>();

            try
            {
                // Load RGB mappings from terrain_rgb.json5
                string terrainRgbPath = System.IO.Path.Combine(UnityEngine.Application.dataPath, "Data", "map", "terrain_rgb.json5");
                if (!System.IO.File.Exists(terrainRgbPath))
                {
                    ArchonLogger.LogError($"ProvinceTerrainAnalyzer: terrain_rgb.json5 not found at {terrainRgbPath}", "map_rendering");
                    return null;
                }

                JObject terrainRgbData = Json5Loader.LoadJson5File(terrainRgbPath);

                if (terrainRgbData == null)
                {
                    ArchonLogger.LogError("ProvinceTerrainAnalyzer: Failed to parse terrain_rgb.json5", "map_rendering");
                    return null;
                }

                // Assign terrain type indices based on ORDER in terrain_rgb.json5
                uint terrainTypeIndex = 0;
                foreach (var terrainProperty in terrainRgbData.Properties())
                {
                    string terrainName = terrainProperty.Name;

                    if (terrainProperty.Value is JObject terrainObj)
                    {
                        var colorArray = Json5Loader.GetIntArray(terrainObj, "color");
                        string typeName = terrainObj["type"]?.ToString();

                        if (colorArray.Count >= 3)
                        {
                            byte r = (byte)colorArray[0];
                            byte g = (byte)colorArray[1];
                            byte b = (byte)colorArray[2];

                            // Only add if RGB doesn't already exist (handle duplicates like savannah/drylands both at 0,0,0)
                            if (!lookup.ContainsKey((r, g, b)))
                            {
                                lookup[(r, g, b)] = terrainTypeIndex;

                                if (logAnalysis)
                                {
                                    ArchonLogger.Log($"ProvinceTerrainAnalyzer: Terrain mapping - RGB({r},{g},{b}) → T{terrainTypeIndex} ({terrainName}, type={typeName})", "map_rendering");
                                }

                                terrainTypeIndex++;
                            }
                            else if (logAnalysis)
                            {
                                ArchonLogger.LogWarning($"ProvinceTerrainAnalyzer: Duplicate RGB({r},{g},{b}) for {terrainName} (already mapped)", "map_rendering");
                            }
                        }
                    }
                }

                ArchonLogger.Log($"ProvinceTerrainAnalyzer: Built RGB→Terrain lookup with {lookup.Count} mappings", "map_rendering");
                return lookup;
            }
            catch (System.Exception e)
            {
                ArchonLogger.LogError($"ProvinceTerrainAnalyzer: Failed to build RGB lookup: {e.Message}\nStack trace: {e.StackTrace}", "map_rendering");
                return null;
            }
        }

        /// <summary>
        /// Convert RGB pixels from terrain texture to terrain type indices
        /// </summary>
        private uint[] ConvertRGBToTerrainTypes(
            Texture2D terrainTexture,
            Dictionary<(byte r, byte g, byte b), uint> rgbToTerrainType,
            int width,
            int height)
        {
            Color[] pixels = terrainTexture.GetPixels();
            uint[] terrainTypes = new uint[pixels.Length];
            int unmappedCount = 0;
            Dictionary<(byte r, byte g, byte b), int> unmappedColors = new Dictionary<(byte r, byte g, byte b), int>();

            for (int i = 0; i < pixels.Length; i++)
            {
                Color pixel = pixels[i];
                byte r = (byte)(pixel.r * 255f + 0.5f);
                byte g = (byte)(pixel.g * 255f + 0.5f);
                byte b = (byte)(pixel.b * 255f + 0.5f);

                if (rgbToTerrainType.TryGetValue((r, g, b), out uint terrainType))
                {
                    terrainTypes[i] = terrainType;
                }
                else
                {
                    // Unmapped color - default to 0
                    terrainTypes[i] = 0;
                    unmappedCount++;

                    // Track unmapped colors
                    if (!unmappedColors.ContainsKey((r, g, b)))
                    {
                        unmappedColors[(r, g, b)] = 0;
                    }
                    unmappedColors[(r, g, b)]++;
                }
            }

            if (unmappedCount > 0)
            {
                ArchonLogger.LogWarning($"ProvinceTerrainAnalyzer: {unmappedCount} pixels had unmapped RGB colors (defaulted to index 0)", "map_rendering");

                // Log the unmapped colors
                ArchonLogger.Log($"ProvinceTerrainAnalyzer: Found {unmappedColors.Count} unique unmapped RGB values:", "map_rendering");
                foreach (var kvp in unmappedColors.OrderByDescending(x => x.Value).Take(10))
                {
                    byte r = kvp.Key.r;
                    byte g = kvp.Key.g;
                    byte b = kvp.Key.b;
                    int count = kvp.Value;
                    ArchonLogger.Log($"  RGB({r},{g},{b}) - {count} pixels", "map_rendering");
                }
            }

            return terrainTypes;
        }
    }
}
