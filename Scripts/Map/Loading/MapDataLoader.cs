using System.Threading.Tasks;
using UnityEngine;
using Map.Rendering;
using Map.Loading.Bitmaps;
using Core;
using Utils;
using static Map.Loading.ProvinceMapProcessor;

namespace Map.Loading
{
    /// <summary>
    /// Handles loading of province map data from files or simulation systems
    /// Orchestrates specialized bitmap loaders for terrain, heightmap, and normal map
    /// Follows single responsibility principle by delegating bitmap loading to specialized classes
    /// </summary>
    public class MapDataLoader : MonoBehaviour, IMapDataProvider
    {
        [Header("Configuration")]
        [SerializeField] private bool logLoadingProgress = true;

        // Dependencies
        private ProvinceMapProcessor provinceProcessor;
        private BorderComputeDispatcher borderDispatcher;
        private MapTextureManager textureManager;

        // Specialized bitmap loaders (replaces manual loading code)
        private TerrainBitmapLoader terrainLoader;
        private HeightmapBitmapLoader heightmapLoader;
        private NormalMapBitmapLoader normalMapLoader;

        // Terrain analyzer for hybrid terrain system
        private ProvinceTerrainAnalyzer terrainAnalyzer;

        public void Initialize(ProvinceMapProcessor processor, BorderComputeDispatcher borders, MapTextureManager textures)
        {
            provinceProcessor = processor;
            borderDispatcher = borders;
            textureManager = textures;

            // Initialize specialized bitmap loaders
            terrainLoader = new TerrainBitmapLoader();
            terrainLoader.Initialize(textures, logLoadingProgress);

            heightmapLoader = new HeightmapBitmapLoader();
            heightmapLoader.Initialize(textures, logLoadingProgress);

            normalMapLoader = new NormalMapBitmapLoader();
            normalMapLoader.Initialize(textures, logLoadingProgress);

            // Find terrain analyzer component (should be in scene, assigned via inspector)
            terrainAnalyzer = Object.FindFirstObjectByType<ProvinceTerrainAnalyzer>();
            if (terrainAnalyzer == null)
            {
                ArchonLogger.LogError("MapDataLoader: ProvinceTerrainAnalyzer component not found in scene!", "map_initialization");
            }
        }

        /// <summary>
        /// Load province map data from simulation systems (preferred method)
        /// Follows dual-layer architecture by getting data from Core layer
        /// </summary>
        public async Task<ProvinceMapResult?> LoadFromSimulationAsync(SimulationDataReadyEvent simulationData, string bitmapPath, string csvPath, bool useDefinition)
        {
            if (logLoadingProgress)
            {
                ArchonLogger.Log("MapDataLoader: Getting province data from Core simulation systems...", "map_initialization");
            }

            // Get GameState to access simulation data
            var gameState = Object.FindFirstObjectByType<GameState>();
            if (gameState == null)
            {
                ArchonLogger.LogError("MapDataLoader: Could not find GameState to access simulation data", "map_initialization");
                return null;
            }

            try
            {
                // We still need to load the bitmap for visual rendering, but now we get the
                // simulation data from Core systems rather than parsing it ourselves
                if (string.IsNullOrEmpty(bitmapPath))
                {
                    ArchonLogger.LogError("MapDataLoader: Province bitmap path not set", "map_initialization");
                    return null;
                }

                // Use absolute paths for loading
                string bmpPath = System.IO.Path.GetFullPath(bitmapPath);
                string definitionPath = useDefinition && !string.IsNullOrEmpty(csvPath)
                    ? System.IO.Path.GetFullPath(csvPath)
                    : null;

                if (logLoadingProgress)
                {
                    ArchonLogger.Log($"MapDataLoader: Loading province bitmap for rendering: {bmpPath}", "map_initialization");
                }

                // Load province map for visual data only (Core already has the simulation data)
                var provinceResult = await provinceProcessor.LoadProvinceMapAsync(bmpPath, definitionPath);

                if (!provinceResult.Success)
                {
                    ArchonLogger.LogError($"MapDataLoader: Failed to load province bitmap: {provinceResult.ErrorMessage}", "map_initialization");
                    return null;
                }

                if (logLoadingProgress)
                {
                    ArchonLogger.Log($"MapDataLoader: Successfully loaded province bitmap with {provinceResult.BMPData.Width}x{provinceResult.BMPData.Height} pixels", "map_initialization");
                }

                // Load supplementary bitmaps in parallel using specialized loaders
                await Task.WhenAll(
                    terrainLoader.LoadAndPopulateAsync(bmpPath),
                    heightmapLoader.LoadAndPopulateAsync(bmpPath)
                    // Note: Normal map is now generated from heightmap, not loaded from file
                );

                // NOTE: Terrain analysis moved to after ProvinceIDTexture population
                // See AnalyzeProvinceTerrainAfterMapInit() - called by MapSystemCoordinator

                // Generate normal map from heightmap (GPU compute shader)
                textureManager.GenerateNormalMapFromHeightmap(
                    heightScale: 10.0f,
                    logProgress: logLoadingProgress
                );

                // Generate initial borders
                GenerateBorders();

                return provinceResult;
            }
            catch (System.Exception e)
            {
                ArchonLogger.LogError($"MapDataLoader: Exception during simulation-driven map loading: {e.Message}\n{e.StackTrace}", "map_initialization");
                return null;
            }
        }

        /// <summary>
        /// Load province map data directly from files (legacy/standalone method)
        /// Used when simulation layer is not available
        /// </summary>
        public async Task<ProvinceMapResult?> LoadFromFilesAsync(string bitmapPath, string csvPath, bool useDefinition)
        {
            if (string.IsNullOrEmpty(bitmapPath))
            {
                ArchonLogger.LogError("MapDataLoader: Province bitmap path not set", "map_initialization");
                return null;
            }

            // Use absolute paths for loading
            string bmpPath = System.IO.Path.GetFullPath(bitmapPath);
            string definitionPath = useDefinition && !string.IsNullOrEmpty(csvPath)
                ? System.IO.Path.GetFullPath(csvPath)
                : null;

            if (logLoadingProgress)
            {
                ArchonLogger.Log($"MapDataLoader: Loading province map from: {bmpPath}", "map_initialization");
                if (definitionPath != null)
                {
                    ArchonLogger.Log($"MapDataLoader: Using definition file: {definitionPath}", "map_initialization");
                }
            }

            try
            {
                // Load province map using modular system
                var provinceResult = await provinceProcessor.LoadProvinceMapAsync(bmpPath, definitionPath);

                if (!provinceResult.Success)
                {
                    ArchonLogger.LogError($"MapDataLoader: Failed to load province map: {provinceResult.ErrorMessage}", "map_initialization");
                    return null;
                }

                if (logLoadingProgress)
                {
                    ArchonLogger.Log($"MapDataLoader: Successfully processed province colors from bitmap", "map_initialization");
                    ArchonLogger.Log($"MapDataLoader: Image size: {provinceResult.BMPData.Width}x{provinceResult.BMPData.Height}", "map_initialization");
                    if (provinceResult.HasDefinitions)
                    {
                        ArchonLogger.Log($"MapDataLoader: Loaded {provinceResult.Definitions.AllDefinitions.Length} province definitions", "map_initialization");
                    }
                }

                // Load supplementary bitmaps in parallel using specialized loaders
                await Task.WhenAll(
                    terrainLoader.LoadAndPopulateAsync(bmpPath),
                    heightmapLoader.LoadAndPopulateAsync(bmpPath)
                    // Note: Normal map is now generated from heightmap, not loaded from file
                );

                // Generate normal map from heightmap (GPU compute shader)
                textureManager.GenerateNormalMapFromHeightmap(
                    heightScale: 10.0f,
                    logProgress: logLoadingProgress
                );

                // Generate initial borders
                GenerateBorders();

                return provinceResult;
            }
            catch (System.Exception e)
            {
                ArchonLogger.LogError($"MapDataLoader: Exception during province map loading: {e.Message}\n{e.StackTrace}", "map_initialization");
                return null;
            }
        }

        /// <summary>
        /// Analyze terrain.bmp to create province terrain lookup (hybrid terrain system)
        /// Runs GPU compute shader to determine dominant terrain type per province via majority voting
        /// CRITICAL: Must be called AFTER ProvinceIDTexture is populated!
        /// </summary>
        public void AnalyzeProvinceTerrainAfterMapInit(GameState gameState)
        {
            AnalyzeProvinceTerrain(gameState);
        }

        /// <summary>
        /// Internal terrain analysis implementation
        /// </summary>
        private void AnalyzeProvinceTerrain(GameState gameState)
        {
            if (terrainAnalyzer == null)
            {
                ArchonLogger.LogError("MapDataLoader: ProvinceTerrainAnalyzer not initialized!", "map_rendering");
                return;
            }

            if (textureManager == null)
            {
                ArchonLogger.LogError("MapDataLoader: TextureManager not available for terrain analysis!", "map_rendering");
                return;
            }

            // Get required textures
            var provinceIDTexture = textureManager.ProvinceIDTexture;
            var terrainTexture = textureManager.ProvinceTerrainTexture;  // Use terrain COLOR texture (RGBA32 with real palette RGB), not terrain TYPE texture
            int provinceCount = gameState.Provinces.ProvinceCount;

            if (provinceIDTexture == null || terrainTexture == null)
            {
                ArchonLogger.LogError("MapDataLoader: Required textures not available for terrain analysis!", "map_rendering");
                return;
            }

            // Get province IDs for lookup buffer
            using (var provinceIDsNative = gameState.Provinces.GetAllProvinceIds(Unity.Collections.Allocator.Temp))
            {
                ushort[] provinceIDs = provinceIDsNative.ToArray();

                // Analyze terrain and get results directly as uint[] array
                // Avoids Graphics.Blit corruption (unpredictable Y-flipping)
                uint[] terrainTypes = terrainAnalyzer.AnalyzeAndGetTerrainTypes(
                    provinceIDTexture,
                    terrainTexture,
                    provinceIDs
                );

                if (terrainTypes != null)
                {

                    // Debug: Log what terrain types are actually in the buffer
                    if (logLoadingProgress)
                    {
                        System.Text.StringBuilder sb = new System.Text.StringBuilder();
                        sb.Append("MapDataLoader: Terrain buffer samples - ");
                        for (int i = 1; i <= Mathf.Min(20, provinceCount); i++)
                        {
                            sb.Append($"P{i}=T{terrainTypes[i]} ");
                        }
                        ArchonLogger.Log(sb.ToString(), "map_rendering");

                        // Count how many provinces have each terrain type
                        var counts = new System.Collections.Generic.Dictionary<uint, int>();
                        for (int i = 1; i < terrainTypes.Length; i++)
                        {
                            uint t = terrainTypes[i];
                            if (!counts.ContainsKey(t)) counts[t] = 0;
                            counts[t]++;
                        }
                        sb.Clear();
                        sb.Append("MapDataLoader: Terrain type distribution - ");
                        foreach (var kvp in counts)
                        {
                            sb.Append($"T{kvp.Key}:{kvp.Value} ");
                        }
                        ArchonLogger.Log(sb.ToString(), "map_rendering");
                    }

                    // Store terrain types into ProvinceState (simulation layer)
                    // CRITICAL: UI reads terrain from ProvinceState, not GPU buffer
                    // Use provinceIDs[i] as the province ID, not the array index i
                    for (int i = 1; i < terrainTypes.Length; i++)
                    {
                        ushort provinceID = provinceIDs[i];
                        gameState.Provinces.SetProvinceTerrain(provinceID, (ushort)terrainTypes[i]);
                    }

                    if (logLoadingProgress)
                    {
                        ArchonLogger.Log($"MapDataLoader: Stored {terrainTypes.Length - 1} terrain types into ProvinceState", "map_rendering");
                    }

                    // Create ComputeBuffer indexed by province ID (not array index)
                    // Must be 65536 entries so shader can use _ProvinceTerrainBuffer[provinceID]
                    uint[] terrainByProvinceID = new uint[65536];
                    for (int i = 1; i < terrainTypes.Length; i++)
                    {
                        ushort provinceID = provinceIDs[i];
                        terrainByProvinceID[provinceID] = terrainTypes[i];
                    }

                    ComputeBuffer terrainBuffer = new ComputeBuffer(65536, sizeof(uint));
                    terrainBuffer.SetData(terrainByProvinceID);

                    // Bind to material (fragment shaders can use StructuredBuffer)
                    var meshRenderer = Object.FindFirstObjectByType<MeshRenderer>();
                    if (meshRenderer != null && meshRenderer.material != null)
                    {
                        meshRenderer.material.SetBuffer("_ProvinceTerrainBuffer", terrainBuffer);
                        if (logLoadingProgress)
                        {
                            ArchonLogger.Log($"MapDataLoader: Bound province terrain buffer to material ({provinceCount} entries)", "map_rendering");
                        }
                    }

                    if (logLoadingProgress)
                    {
                        ArchonLogger.Log($"MapDataLoader: Province terrain analysis complete ({provinceCount} provinces)", "map_rendering");
                    }

                    // NOTE: terrainBuffer not released - needs to persist for rendering
                    // Should be managed by a persistent component, not leaked here
                    // TODO: Store buffer reference for cleanup
                }
                else
                {
                    ArchonLogger.LogError("MapDataLoader: Terrain analysis failed!", "map_rendering");
                }
            } // End using provinceIDsNative
        }

        /// <summary>
        /// Generate initial province borders using GPU compute shader
        /// </summary>
        private void GenerateBorders()
        {
            if (borderDispatcher != null)
            {
                // Note: Border mode and generation is controlled by GAME layer (VisualStyleManager)
                // ENGINE only initializes the border system, GAME decides what borders to show
                if (logLoadingProgress)
                {
                    ArchonLogger.Log("MapDataLoader: Border system ready (mode will be set by GAME layer)", "map_initialization");
                }
            }
            else
            {
                ArchonLogger.LogError("MapDataLoader: BorderComputeDispatcher is NULL - borders will not be generated!", "map_initialization");
            }
        }
    }
}
