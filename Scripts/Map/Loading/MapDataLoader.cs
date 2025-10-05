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
        }

        /// <summary>
        /// Load province map data from simulation systems (preferred method)
        /// Follows dual-layer architecture by getting data from Core layer
        /// </summary>
        public async Task<ProvinceMapResult?> LoadFromSimulationAsync(SimulationDataReadyEvent simulationData, string bitmapPath, string csvPath, bool useDefinition)
        {
            if (logLoadingProgress)
            {
                ArchonLogger.LogMapInit("MapDataLoader: Getting province data from Core simulation systems...");
            }

            // Get GameState to access simulation data
            var gameState = Object.FindFirstObjectByType<GameState>();
            if (gameState == null)
            {
                ArchonLogger.LogError("MapDataLoader: Could not find GameState to access simulation data");
                return null;
            }

            try
            {
                // We still need to load the bitmap for visual rendering, but now we get the
                // simulation data from Core systems rather than parsing it ourselves
                if (string.IsNullOrEmpty(bitmapPath))
                {
                    ArchonLogger.LogError("MapDataLoader: Province bitmap path not set");
                    return null;
                }

                // Use absolute paths for loading
                string bmpPath = System.IO.Path.GetFullPath(bitmapPath);
                string definitionPath = useDefinition && !string.IsNullOrEmpty(csvPath)
                    ? System.IO.Path.GetFullPath(csvPath)
                    : null;

                if (logLoadingProgress)
                {
                    ArchonLogger.LogMapInit($"MapDataLoader: Loading province bitmap for rendering: {bmpPath}");
                }

                // Load province map for visual data only (Core already has the simulation data)
                var provinceResult = await provinceProcessor.LoadProvinceMapAsync(bmpPath, definitionPath);

                if (!provinceResult.Success)
                {
                    ArchonLogger.LogError($"MapDataLoader: Failed to load province bitmap: {provinceResult.ErrorMessage}");
                    return null;
                }

                if (logLoadingProgress)
                {
                    ArchonLogger.LogMapInit($"MapDataLoader: Successfully loaded province bitmap with {provinceResult.BMPData.Width}x{provinceResult.BMPData.Height} pixels");
                }

                // Load supplementary bitmaps in parallel using specialized loaders
                await Task.WhenAll(
                    terrainLoader.LoadAndPopulateAsync(bmpPath),
                    heightmapLoader.LoadAndPopulateAsync(bmpPath),
                    normalMapLoader.LoadAndPopulateAsync(bmpPath)
                );

                // Generate initial borders
                GenerateBorders();

                return provinceResult;
            }
            catch (System.Exception e)
            {
                ArchonLogger.LogError($"MapDataLoader: Exception during simulation-driven map loading: {e.Message}\n{e.StackTrace}");
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
                ArchonLogger.LogError("MapDataLoader: Province bitmap path not set");
                return null;
            }

            // Use absolute paths for loading
            string bmpPath = System.IO.Path.GetFullPath(bitmapPath);
            string definitionPath = useDefinition && !string.IsNullOrEmpty(csvPath)
                ? System.IO.Path.GetFullPath(csvPath)
                : null;

            if (logLoadingProgress)
            {
                ArchonLogger.LogMapInit($"MapDataLoader: Loading province map from: {bmpPath}");
                if (definitionPath != null)
                {
                    ArchonLogger.LogMapInit($"MapDataLoader: Using definition file: {definitionPath}");
                }
            }

            try
            {
                // Load province map using modular system
                var provinceResult = await provinceProcessor.LoadProvinceMapAsync(bmpPath, definitionPath);

                if (!provinceResult.Success)
                {
                    ArchonLogger.LogError($"MapDataLoader: Failed to load province map: {provinceResult.ErrorMessage}");
                    return null;
                }

                if (logLoadingProgress)
                {
                    ArchonLogger.LogMapInit($"MapDataLoader: Successfully processed province colors from bitmap");
                    ArchonLogger.LogMapInit($"MapDataLoader: Image size: {provinceResult.BMPData.Width}x{provinceResult.BMPData.Height}");
                    if (provinceResult.HasDefinitions)
                    {
                        ArchonLogger.LogMapInit($"MapDataLoader: Loaded {provinceResult.Definitions.AllDefinitions.Length} province definitions");
                    }
                }

                // Load supplementary bitmaps in parallel using specialized loaders
                await Task.WhenAll(
                    terrainLoader.LoadAndPopulateAsync(bmpPath),
                    heightmapLoader.LoadAndPopulateAsync(bmpPath),
                    normalMapLoader.LoadAndPopulateAsync(bmpPath)
                );

                // Generate initial borders
                GenerateBorders();

                return provinceResult;
            }
            catch (System.Exception e)
            {
                ArchonLogger.LogError($"MapDataLoader: Exception during province map loading: {e.Message}\n{e.StackTrace}");
                return null;
            }
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
                    ArchonLogger.LogMapInit("MapDataLoader: Border system ready (mode will be set by GAME layer)");
                }
            }
            else
            {
                ArchonLogger.LogMapInitError("MapDataLoader: BorderComputeDispatcher is NULL - borders will not be generated!");
            }
        }
    }
}
