using System.Threading.Tasks;
using UnityEngine;
using Map.Rendering;
using Map.Loading.Images;
using Core;
using Core.Loaders;
using Utils;
using static Map.Loading.ProvinceMapProcessor;

namespace Map.Loading
{
    /// <summary>
    /// Handles loading of province map data from files or simulation systems.
    /// Plain C# class - dependencies passed via constructor/Initialize.
    /// </summary>
    public class MapDataLoader : System.IDisposable
    {
        private readonly bool logProgress;

        // Dependencies (injected)
        private ProvinceMapProcessor provinceProcessor;
        private BorderComputeDispatcher borderDispatcher;
        private MapTextureManager textureManager;
        private ProvinceTerrainAnalyzer terrainAnalyzer;
        private Map.Rendering.Terrain.TerrainBlendMapGenerator blendMapGenerator;
        private string dataDirectory;

        // Specialized bitmap loaders
        private TerrainImageLoader terrainLoader;
        private HeightmapBitmapLoader heightmapLoader;
        private NormalMapBitmapLoader normalMapLoader;

        // Persistent terrain buffer for rendering
        private ComputeBuffer provinceTerrainBuffer;

        public MapDataLoader(bool logProgress = true)
        {
            this.logProgress = logProgress;
        }

        public void Initialize(
            ProvinceMapProcessor processor,
            BorderComputeDispatcher borders,
            MapTextureManager textures,
            ProvinceTerrainAnalyzer analyzer,
            Map.Rendering.Terrain.TerrainBlendMapGenerator blendGen,
            string dataDir)
        {
            provinceProcessor = processor;
            borderDispatcher = borders;
            textureManager = textures;
            terrainAnalyzer = analyzer;
            blendMapGenerator = blendGen;
            dataDirectory = dataDir;

            // Initialize terrain color mapper
            if (!string.IsNullOrEmpty(dataDirectory))
            {
                Data.TerrainColorMapper.Initialize(dataDirectory);
            }

            // Initialize specialized bitmap loaders
            terrainLoader = new TerrainImageLoader();
            terrainLoader.Initialize(textures, logProgress);

            heightmapLoader = new HeightmapBitmapLoader();
            heightmapLoader.Initialize(textures, logProgress);

            normalMapLoader = new NormalMapBitmapLoader();
            normalMapLoader.Initialize(textures, logProgress);

            if (terrainAnalyzer == null)
                ArchonLogger.LogWarning("MapDataLoader: ProvinceTerrainAnalyzer not provided - terrain analysis disabled", "map_initialization");

            if (blendMapGenerator == null)
                ArchonLogger.LogWarning("MapDataLoader: TerrainBlendMapGenerator not provided - terrain blending disabled", "map_initialization");
        }

        /// <summary>
        /// Load province map data from simulation systems (preferred method).
        /// </summary>
        public async Task<ProvinceMapResult?> LoadFromSimulationAsync(
            SimulationDataReadyEvent simulationData,
            string bitmapPath,
            string csvPath,
            bool useDefinition)
        {
            if (logProgress)
                ArchonLogger.Log("MapDataLoader: Loading province data from simulation...", "map_initialization");

            try
            {
                if (string.IsNullOrEmpty(bitmapPath))
                {
                    ArchonLogger.LogError("MapDataLoader: Province bitmap path not set", "map_initialization");
                    return null;
                }

                string bmpPath = System.IO.Path.GetFullPath(bitmapPath);
                string definitionPath = useDefinition && !string.IsNullOrEmpty(csvPath)
                    ? System.IO.Path.GetFullPath(csvPath)
                    : null;

                if (logProgress)
                    ArchonLogger.Log($"MapDataLoader: Loading bitmap: {bmpPath}", "map_initialization");

                var provinceResult = await provinceProcessor.LoadProvinceMapAsync(bmpPath, definitionPath);

                if (!provinceResult.IsSuccess)
                {
                    ArchonLogger.LogError($"MapDataLoader: Failed to load bitmap: {provinceResult.ErrorMessage}", "map_initialization");
                    return null;
                }

                if (logProgress)
                    ArchonLogger.Log($"MapDataLoader: Loaded {provinceResult.BMPData.Width}x{provinceResult.BMPData.Height} bitmap", "map_initialization");

                // Load terrain, heightmap, generate normal map
                string mapDirectory = System.IO.Path.GetDirectoryName(bmpPath);
                terrainLoader.LoadAndPopulate(mapDirectory);
                heightmapLoader.LoadAndPopulate(mapDirectory);

                // TODO: Normal map generation disabled - not working, saves ~0.9s load time
                // textureManager.GenerateNormalMapFromHeightmap(heightScale: 10.0f, logProgress: logProgress);

                // Generate initial borders
                if (borderDispatcher != null && logProgress)
                    ArchonLogger.Log("MapDataLoader: Border system ready", "map_initialization");

                return provinceResult;
            }
            catch (System.Exception e)
            {
                ArchonLogger.LogError($"MapDataLoader: Exception: {e.Message}\n{e.StackTrace}", "map_initialization");
                return null;
            }
        }

        /// <summary>
        /// Load province map data directly from files (legacy method).
        /// </summary>
        public async Task<ProvinceMapResult?> LoadFromFilesAsync(string bitmapPath, string csvPath, bool useDefinition)
        {
            if (string.IsNullOrEmpty(bitmapPath))
            {
                ArchonLogger.LogError("MapDataLoader: Province bitmap path not set", "map_initialization");
                return null;
            }

            string bmpPath = System.IO.Path.GetFullPath(bitmapPath);
            string definitionPath = useDefinition && !string.IsNullOrEmpty(csvPath)
                ? System.IO.Path.GetFullPath(csvPath)
                : null;

            if (logProgress)
            {
                ArchonLogger.Log($"MapDataLoader: Loading from: {bmpPath}", "map_initialization");
                if (definitionPath != null)
                    ArchonLogger.Log($"MapDataLoader: Definition: {definitionPath}", "map_initialization");
            }

            try
            {
                var provinceResult = await provinceProcessor.LoadProvinceMapAsync(bmpPath, definitionPath);

                if (!provinceResult.IsSuccess)
                {
                    ArchonLogger.LogError($"MapDataLoader: Failed: {provinceResult.ErrorMessage}", "map_initialization");
                    return null;
                }

                if (logProgress)
                {
                    ArchonLogger.Log($"MapDataLoader: Loaded {provinceResult.BMPData.Width}x{provinceResult.BMPData.Height}", "map_initialization");
                    if (provinceResult.HasDefinitions)
                        ArchonLogger.Log($"MapDataLoader: {provinceResult.Definitions.AllDefinitions.Length} definitions", "map_initialization");
                }

                string mapDirectory = System.IO.Path.GetDirectoryName(bmpPath);
                terrainLoader.LoadAndPopulate(mapDirectory);
                heightmapLoader.LoadAndPopulate(mapDirectory);

                // TODO: Normal map generation disabled - not working, saves ~0.9s load time
                // textureManager.GenerateNormalMapFromHeightmap(heightScale: 10.0f, logProgress: logProgress);

                return provinceResult;
            }
            catch (System.Exception e)
            {
                ArchonLogger.LogError($"MapDataLoader: Exception: {e.Message}\n{e.StackTrace}", "map_initialization");
                return null;
            }
        }

        /// <summary>
        /// Analyze terrain after map init (GPU compute shader).
        /// Must be called AFTER ProvinceIDTexture is populated.
        /// </summary>
        public void AnalyzeProvinceTerrainAfterMapInit(GameState gameState, System.Collections.Generic.Dictionary<ushort, string> provinceTerrainOverrides = null)
        {
            if (terrainAnalyzer == null)
            {
                ArchonLogger.LogWarning("MapDataLoader: Terrain analyzer not available", "map_rendering");
                return;
            }

            if (textureManager == null)
            {
                ArchonLogger.LogError("MapDataLoader: TextureManager not available", "map_rendering");
                return;
            }

            var provinceIDTexture = textureManager.ProvinceIDTexture;
            var terrainTypeTexture = textureManager.TerrainTypeTexture;
            int provinceCount = gameState.Provinces.ProvinceCount;

            if (provinceIDTexture == null)
            {
                ArchonLogger.LogError("MapDataLoader: ProvinceID texture not available", "map_rendering");
                return;
            }

            using (var provinceIDsNative = gameState.Provinces.GetAllProvinceIds(Unity.Collections.Allocator.Temp))
            {
                ushort[] provinceIDs = provinceIDsNative.ToArray();

                uint[] terrainTypes;

                if (terrainTypeTexture != null)
                {
                    // GPU auto-assign from terrain.png
                    terrainTypes = terrainAnalyzer.AnalyzeAndGetTerrainTypes(
                        provinceIDTexture,
                        terrainTypeTexture,
                        provinceIDs
                    );

                    if (terrainTypes == null)
                    {
                        ArchonLogger.LogWarning("MapDataLoader: GPU terrain analysis failed, using defaults", "map_rendering");
                        terrainTypes = new uint[provinceIDs.Length];
                    }
                }
                else
                {
                    // No terrain.png — assign from default.json5 water lists + default land terrain
                    terrainTypes = new uint[provinceIDs.Length];

                    // Find terrain indices for ocean and default land type
                    var terrainRegistry = gameState.Registries.Terrains;
                    uint oceanIndex = 0;
                    uint landIndex = 0;
                    if (terrainRegistry.TryGet("ocean", out var oceanData))
                        oceanIndex = oceanData.TerrainId;
                    if (terrainRegistry.TryGet("grasslands", out var grassData))
                        landIndex = grassData.TerrainId;

                    // Load water province lists from default.json5
                    var mapConfig = MapConfigLoader.Load(dataDirectory);

                    // Build provinceID lookup
                    var pidSet = new System.Collections.Generic.HashSet<ushort>();
                    for (int i = 0; i < provinceIDs.Length; i++)
                        pidSet.Add(provinceIDs[i]);

                    for (int i = 0; i < provinceIDs.Length; i++)
                    {
                        ushort pid = provinceIDs[i];
                        if (mapConfig.HasValue && mapConfig.Value.IsWater(pid))
                            terrainTypes[i] = oceanIndex;
                        else
                            terrainTypes[i] = landIndex;
                    }

                    if (logProgress)
                    {
                        int waterCount = 0;
                        for (int i = 0; i < terrainTypes.Length; i++)
                            if (terrainTypes[i] == oceanIndex) waterCount++;
                        ArchonLogger.Log($"MapDataLoader: No terrain texture — assigned from default.json5 ({waterCount} water, {terrainTypes.Length - waterCount} land)", "map_rendering");
                    }
                }

                // Apply province-level terrain overrides (highest priority)
                // Province file > terrain.json5 > auto-assign
                if (provinceTerrainOverrides != null && provinceTerrainOverrides.Count > 0)
                {
                    // Build category name → terrain index mapping
                    var terrainRegistry = gameState.Registries.Terrains;
                    var provinceIDToIndex = new System.Collections.Generic.Dictionary<ushort, int>();
                    for (int i = 0; i < provinceIDs.Length; i++)
                        provinceIDToIndex[provinceIDs[i]] = i;

                    int provinceOverridesApplied = 0;
                    foreach (var kvp in provinceTerrainOverrides)
                    {
                        ushort pid = kvp.Key;
                        string terrainName = kvp.Value;

                        if (provinceIDToIndex.TryGetValue(pid, out int arrayIndex))
                        {
                            if (terrainRegistry.TryGet(terrainName, out var terrainData))
                            {
                                terrainTypes[arrayIndex] = terrainData.TerrainId;
                                provinceOverridesApplied++;
                            }
                            else if (logProgress)
                            {
                                ArchonLogger.LogWarning($"MapDataLoader: Province {pid} terrain override '{terrainName}' not found in registry", "map_rendering");
                            }
                        }
                    }

                    if (logProgress && provinceOverridesApplied > 0)
                        ArchonLogger.Log($"MapDataLoader: Applied {provinceOverridesApplied} province-level terrain overrides", "map_rendering");
                }

                // Store terrain types into ProvinceState
                for (int i = 1; i < terrainTypes.Length; i++)
                {
                    ushort provinceID = provinceIDs[i];
                    gameState.Provinces.SetProvinceTerrain(provinceID, (ushort)terrainTypes[i]);
                }

                if (logProgress)
                    ArchonLogger.Log($"MapDataLoader: Stored {terrainTypes.Length - 1} terrain types", "map_rendering");

                // Create ComputeBuffer indexed by province ID
                uint[] terrainByProvinceID = new uint[65536];
                for (int i = 1; i < terrainTypes.Length; i++)
                {
                    ushort provinceID = provinceIDs[i];
                    terrainByProvinceID[provinceID] = terrainTypes[i];
                }

                provinceTerrainBuffer?.Dispose();
                provinceTerrainBuffer = new ComputeBuffer(65536, sizeof(uint));
                provinceTerrainBuffer.SetData(terrainByProvinceID);

                // GPU sync
                RenderTexture tempRT = RenderTexture.GetTemporary(1, 1, 0);
                var syncRequest = UnityEngine.Rendering.AsyncGPUReadback.Request(tempRT);
                syncRequest.WaitForCompletion();
                RenderTexture.ReleaseTemporary(tempRT);

                if (logProgress)
                    ArchonLogger.Log($"MapDataLoader: Terrain analysis complete ({provinceCount} provinces)", "map_rendering");

                // Generate terrain blend maps
                if (blendMapGenerator != null)
                {
                    var (detailIndex, detailMask) = blendMapGenerator.Generate(
                        provinceIDTexture,
                        provinceTerrainBuffer,
                        textureManager.MapWidth,
                        textureManager.MapHeight
                    );

                    if (detailIndex != null && detailMask != null)
                    {
                        textureManager.SetTerrainBlendMaps(detailIndex, detailMask);

                        if (logProgress)
                            ArchonLogger.Log("MapDataLoader: Terrain blend maps generated", "map_rendering");
                    }
                }
            }
        }

        /// <summary>
        /// Update terrain type for a single province at runtime and regenerate blend maps.
        /// Used by editor tools to get visual feedback when painting terrain.
        /// </summary>
        public void UpdateProvinceTerrain(ushort provinceId, uint terrainType)
        {
            if (provinceTerrainBuffer == null) return;

            // Update the single entry in the compute buffer
            uint[] data = new uint[] { terrainType };
            provinceTerrainBuffer.SetData(data, 0, provinceId, 1);
        }

        /// <summary>
        /// Regenerate terrain blend maps from the current province terrain buffer.
        /// Call after one or more UpdateProvinceTerrain calls to refresh visuals.
        /// </summary>
        public void RegenerateBlendMaps()
        {
            if (provinceTerrainBuffer == null || blendMapGenerator == null || textureManager == null)
                return;

            var provinceIDTexture = textureManager.ProvinceIDTexture;
            if (provinceIDTexture == null) return;

            var (detailIndex, detailMask) = blendMapGenerator.Generate(
                provinceIDTexture,
                provinceTerrainBuffer,
                textureManager.MapWidth,
                textureManager.MapHeight
            );

            if (detailIndex != null && detailMask != null)
            {
                textureManager.SetTerrainBlendMaps(detailIndex, detailMask);
            }
        }

        /// <summary>
        /// Get the province terrain buffer for material binding.
        /// </summary>
        public ComputeBuffer GetProvinceTerrainBuffer() => provinceTerrainBuffer;

        public void Dispose()
        {
            provinceTerrainBuffer?.Dispose();
            provinceTerrainBuffer = null;
        }
    }
}
