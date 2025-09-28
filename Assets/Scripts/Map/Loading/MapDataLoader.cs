using System.Threading.Tasks;
using UnityEngine;
using ParadoxParser.Jobs;
using Map.Rendering;
using Core;
using Utils;

namespace Map.Loading
{
    /// <summary>
    /// Handles loading of province map data from files or simulation systems
    /// Extracted from MapGenerator to follow single responsibility principle
    /// Manages async data loading and conversion to presentation layer format
    /// </summary>
    public class MapDataLoader : MonoBehaviour, IMapDataProvider
    {
        [Header("Configuration")]
        [SerializeField] private bool logLoadingProgress = true;

        // Dependencies
        private ProvinceMapProcessor provinceProcessor;
        private BorderComputeDispatcher borderDispatcher;
        private MapTextureManager textureManager;

        public void Initialize(ProvinceMapProcessor processor, BorderComputeDispatcher borders, MapTextureManager textures)
        {
            provinceProcessor = processor;
            borderDispatcher = borders;
            textureManager = textures;
        }

        /// <summary>
        /// Load province map data from simulation systems (preferred method)
        /// Follows dual-layer architecture by getting data from Core layer
        /// </summary>
        public async Task<ProvinceMapResult?> LoadFromSimulationAsync(SimulationDataReadyEvent simulationData, string bitmapPath, string csvPath, bool useDefinition)
        {
            if (logLoadingProgress)
            {
                DominionLogger.Log("MapDataLoader: Getting province data from Core simulation systems...");
            }

            // Get GameState to access simulation data
            var gameState = Object.FindFirstObjectByType<GameState>();
            if (gameState == null)
            {
                DominionLogger.LogError("MapDataLoader: Could not find GameState to access simulation data");
                return null;
            }

            try
            {
                // We still need to load the bitmap for visual rendering, but now we get the
                // simulation data from Core systems rather than parsing it ourselves
                if (string.IsNullOrEmpty(bitmapPath))
                {
                    DominionLogger.LogError("MapDataLoader: Province bitmap path not set");
                    return null;
                }

                // Use absolute paths for loading
                string bmpPath = System.IO.Path.GetFullPath(bitmapPath);
                string definitionPath = useDefinition && !string.IsNullOrEmpty(csvPath)
                    ? System.IO.Path.GetFullPath(csvPath)
                    : null;

                if (logLoadingProgress)
                {
                    DominionLogger.Log($"MapDataLoader: Loading province bitmap for rendering: {bmpPath}");
                }

                // Load province map for visual data only (Core already has the simulation data)
                var provinceResult = await provinceProcessor.LoadProvinceMapAsync(bmpPath, definitionPath);

                if (!provinceResult.Success)
                {
                    DominionLogger.LogError($"MapDataLoader: Failed to load province bitmap: {provinceResult.ErrorMessage}");
                    return null;
                }

                if (logLoadingProgress)
                {
                    DominionLogger.Log($"MapDataLoader: Successfully loaded province bitmap with {provinceResult.BMPData.Width}x{provinceResult.BMPData.Height} pixels");
                }

                // Generate initial borders
                GenerateBorders();

                return provinceResult;
            }
            catch (System.Exception e)
            {
                DominionLogger.LogError($"MapDataLoader: Exception during simulation-driven map loading: {e.Message}\n{e.StackTrace}");
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
                DominionLogger.LogError("MapDataLoader: Province bitmap path not set");
                return null;
            }

            // Use absolute paths for loading
            string bmpPath = System.IO.Path.GetFullPath(bitmapPath);
            string definitionPath = useDefinition && !string.IsNullOrEmpty(csvPath)
                ? System.IO.Path.GetFullPath(csvPath)
                : null;

            if (logLoadingProgress)
            {
                DominionLogger.Log($"MapDataLoader: Loading province map from: {bmpPath}");
                if (definitionPath != null)
                {
                    DominionLogger.Log($"MapDataLoader: Using definition file: {definitionPath}");
                }
            }

            try
            {
                // Load province map using modular system
                var provinceResult = await provinceProcessor.LoadProvinceMapAsync(bmpPath, definitionPath);

                if (!provinceResult.Success)
                {
                    DominionLogger.LogError($"MapDataLoader: Failed to load province map: {provinceResult.ErrorMessage}");
                    return null;
                }

                if (logLoadingProgress)
                {
                    DominionLogger.Log($"MapDataLoader: Successfully processed province colors from bitmap");
                    DominionLogger.Log($"MapDataLoader: Image size: {provinceResult.BMPData.Width}x{provinceResult.BMPData.Height}");
                    if (provinceResult.HasDefinitions)
                    {
                        DominionLogger.Log($"MapDataLoader: Loaded {provinceResult.Definitions.AllDefinitions.Length} province definitions");
                    }
                }

                // Generate initial borders
                GenerateBorders();

                return provinceResult;
            }
            catch (System.Exception e)
            {
                DominionLogger.LogError($"MapDataLoader: Exception during province map loading: {e.Message}\n{e.StackTrace}");
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
                borderDispatcher.ClearBorders();
                borderDispatcher.SetBorderMode(BorderComputeDispatcher.BorderMode.Country);
                borderDispatcher.DetectBorders();

                if (logLoadingProgress)
                {
                    DominionLogger.Log("MapDataLoader: Generated province borders using GPU compute shader");
                }
            }
        }
    }
}