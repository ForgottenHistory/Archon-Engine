using System.Threading.Tasks;
using UnityEngine;
using ParadoxParser.Jobs;
using Map.Rendering;
using Core;
using Utils;
using System.Collections.Generic;

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

        // Terrain loading
        private ParadoxParser.Jobs.JobifiedBMPLoader terrainLoader;

        public void Initialize(ProvinceMapProcessor processor, BorderComputeDispatcher borders, MapTextureManager textures)
        {
            provinceProcessor = processor;
            borderDispatcher = borders;
            textureManager = textures;
            terrainLoader = new ParadoxParser.Jobs.JobifiedBMPLoader();
        }

        /// <summary>
        /// Load province map data from simulation systems (preferred method)
        /// Follows dual-layer architecture by getting data from Core layer
        /// </summary>
        public async Task<ProvinceMapResult?> LoadFromSimulationAsync(SimulationDataReadyEvent simulationData, string bitmapPath, string csvPath, bool useDefinition)
        {
            if (logLoadingProgress)
            {
                DominionLogger.LogMapInit("MapDataLoader: Getting province data from Core simulation systems...");
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
                    DominionLogger.LogMapInit($"MapDataLoader: Loading province bitmap for rendering: {bmpPath}");
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
                    DominionLogger.LogMapInit($"MapDataLoader: Successfully loaded province bitmap with {provinceResult.BMPData.Width}x{provinceResult.BMPData.Height} pixels");
                }

                // Load terrain bitmap for terrain colors
                await LoadTerrainBitmapAsync(bmpPath);

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
                DominionLogger.LogMapInit($"MapDataLoader: Loading province map from: {bmpPath}");
                if (definitionPath != null)
                {
                    DominionLogger.LogMapInit($"MapDataLoader: Using definition file: {definitionPath}");
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
                    DominionLogger.LogMapInit($"MapDataLoader: Successfully processed province colors from bitmap");
                    DominionLogger.LogMapInit($"MapDataLoader: Image size: {provinceResult.BMPData.Width}x{provinceResult.BMPData.Height}");
                    if (provinceResult.HasDefinitions)
                    {
                        DominionLogger.LogMapInit($"MapDataLoader: Loaded {provinceResult.Definitions.AllDefinitions.Length} province definitions");
                    }
                }

                // Load terrain bitmap for terrain colors
                await LoadTerrainBitmapAsync(bmpPath);

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
                    DominionLogger.LogMapInit("MapDataLoader: Generated province borders using GPU compute shader");
                }
            }
        }

        /// <summary>
        /// Load terrain bitmap and populate terrain texture with colors from terrain.bmp
        /// </summary>
        private async Task LoadTerrainBitmapAsync(string provincesBmpPath)
        {
            // Derive terrain.bmp path from provinces.bmp path
            string terrainBmpPath = provincesBmpPath.Replace("provinces.bmp", "terrain.bmp");

            if (!System.IO.File.Exists(terrainBmpPath))
            {
                if (logLoadingProgress)
                {
                    DominionLogger.LogWarning($"MapDataLoader: Terrain bitmap not found at {terrainBmpPath}, using default terrain colors");
                }
                return;
            }

            try
            {
                if (logLoadingProgress)
                {
                    DominionLogger.LogMapInit($"MapDataLoader: Loading terrain bitmap: {terrainBmpPath}");
                }

                // Load terrain bitmap
                var terrainResult = await terrainLoader.LoadBMPAsync(terrainBmpPath);

                if (!terrainResult.Success)
                {
                    DominionLogger.LogWarning($"MapDataLoader: Failed to load terrain bitmap: {terrainResult.ErrorMessage}");
                    return;
                }

                if (logLoadingProgress)
                {
                    DominionLogger.LogMapInit($"MapDataLoader: Successfully loaded terrain bitmap with {terrainResult.Width}x{terrainResult.Height} pixels");
                }

                // Populate terrain texture with bitmap colors
                PopulateTerrainTexture(terrainResult);

                if (logLoadingProgress)
                {
                    DominionLogger.LogMapInit("MapDataLoader: Populated terrain texture with colors from terrain.bmp");
                }

                // Dispose terrain result to free Persistent allocations
                terrainResult.Dispose();
            }
            catch (System.Exception e)
            {
                DominionLogger.LogError($"MapDataLoader: Exception loading terrain bitmap: {e.Message}");
            }
        }

        /// <summary>
        /// Populate terrain texture with colors from loaded terrain bitmap data
        /// </summary>
        private void PopulateTerrainTexture(ParadoxParser.Jobs.BMPLoadResult terrainData)
        {
            if (textureManager == null || textureManager.ProvinceTerrainTexture == null)
            {
                DominionLogger.LogError("MapDataLoader: Cannot populate terrain texture - texture manager or terrain texture not available");
                return;
            }

            var terrainTexture = textureManager.ProvinceTerrainTexture;

            // DEBUG: Log texture instance details
            if (logLoadingProgress)
            {
                DominionLogger.LogMapInit($"MapDataLoader: Populating terrain texture instance {terrainTexture.GetInstanceID()} ({terrainTexture.name}) size {terrainTexture.width}x{terrainTexture.height}");
            }
            int width = terrainTexture.width;
            int height = terrainTexture.height;

            // Create pixel array for terrain texture
            var pixels = new UnityEngine.Color32[width * height];

            // Get the pixel data from the terrain bitmap
            var pixelData = terrainData.GetPixelData();

            // Debug: Log terrain bitmap format information
            if (logLoadingProgress)
            {
                DominionLogger.LogMapInit($"MapDataLoader: Terrain bitmap format - Width: {terrainData.Width}, Height: {terrainData.Height}, BitsPerPixel: {terrainData.BitsPerPixel}");
            }

            int successfulReads = 0;
            int failedReads = 0;

            // Handle indexed color format (8-bit) vs direct RGB
            if (terrainData.BitsPerPixel == 8)
            {
                // 8-bit indexed color - mapping based on terrain.txt definitions
                var terrainColorMap = new Dictionary<byte, UnityEngine.Color32>
                {
                    [0] = new UnityEngine.Color32(50, 180, 50, 255),    // grasslands
                    [1] = new UnityEngine.Color32(160, 140, 120, 255),  // hills
                    [2] = new UnityEngine.Color32(120, 120, 120, 255),  // desert_mountain (mountain)
                    [3] = new UnityEngine.Color32(255, 230, 180, 255),  // desert
                    [4] = new UnityEngine.Color32(80, 160, 80, 255),    // plains (grasslands type)
                    [5] = new UnityEngine.Color32(70, 150, 70, 255),    // terrain_5 (grasslands type)
                    [6] = new UnityEngine.Color32(100, 100, 100, 255),  // mountain
                    [7] = new UnityEngine.Color32(200, 180, 140, 255),  // desert_mountain_low (desert type)
                    [8] = new UnityEngine.Color32(140, 120, 100, 255),  // terrain_8 (hills type)
                    [9] = new UnityEngine.Color32(60, 100, 60, 255),    // marsh
                    [10] = new UnityEngine.Color32(139, 125, 107, 255), // terrain_10 (farmlands)
                    [11] = new UnityEngine.Color32(149, 135, 117, 255), // terrain_11 (farmlands)
                    [12] = new UnityEngine.Color32(0, 120, 0, 255),     // forest_12
                    [13] = new UnityEngine.Color32(10, 130, 10, 255),   // forest_13
                    [14] = new UnityEngine.Color32(20, 140, 20, 255),   // forest_14
                    [15] = new UnityEngine.Color32(0, 100, 200, 255),   // ocean
                    [16] = new UnityEngine.Color32(200, 200, 255, 255), // snow (mountain type)
                    [17] = new UnityEngine.Color32(20, 120, 220, 255),  // inland_ocean_17
                    [19] = new UnityEngine.Color32(220, 200, 160, 255), // coastal_desert_18 (index 19 in file)
                    [20] = new UnityEngine.Color32(180, 160, 100, 255), // savannah
                    [21] = new UnityEngine.Color32(159, 145, 127, 255), // terrain_21 (farmlands)
                    [22] = new UnityEngine.Color32(200, 180, 120, 255), // drylands
                    [23] = new UnityEngine.Color32(140, 130, 110, 255), // highlands
                    [24] = new UnityEngine.Color32(160, 150, 130, 255), // dry_highlands
                    [35] = new UnityEngine.Color32(100, 180, 255, 255), // coastline
                    [254] = new UnityEngine.Color32(0, 80, 0, 255),     // jungle
                    [255] = new UnityEngine.Color32(40, 100, 40, 255),  // woods
                };

                // Copy terrain colors from indexed bitmap data
                for (int y = 0; y < height && y < terrainData.Height; y++)
                {
                    for (int x = 0; x < width && x < terrainData.Width; x++)
                    {
                        int textureIndex = y * width + x;

                        // For indexed color, read the palette index
                        if (ParadoxParser.Bitmap.BMPParser.TryGetPixelRGB(pixelData, x, y, out byte index, out byte _, out byte __))
                        {
                            // Use the index to look up terrain color, or default to plains if not found
                            if (terrainColorMap.TryGetValue(index, out UnityEngine.Color32 terrainColor))
                            {
                                pixels[textureIndex] = terrainColor;
                            }
                            else
                            {
                                // Default to plains color for unknown indices
                                pixels[textureIndex] = new UnityEngine.Color32(139, 125, 107, 255);
                                if (logLoadingProgress && successfulReads < 10) // Log first few unknown indices
                                {
                                    DominionLogger.Log($"MapDataLoader: Unknown terrain index {index} at ({x},{y})");
                                }
                            }
                            successfulReads++;
                        }
                        else
                        {
                            // Fallback to default land color if pixel reading fails
                            pixels[textureIndex] = new UnityEngine.Color32(139, 125, 107, 255);
                            failedReads++;
                        }
                    }
                }
            }
            else
            {
                // Direct RGB format (24-bit or 32-bit)
                for (int y = 0; y < height && y < terrainData.Height; y++)
                {
                    for (int x = 0; x < width && x < terrainData.Width; x++)
                    {
                        int textureIndex = y * width + x;

                        // Sample terrain color from bitmap data using the correct BMPParser API
                        if (ParadoxParser.Bitmap.BMPParser.TryGetPixelRGB(pixelData, x, y, out byte r, out byte g, out byte b))
                        {
                            pixels[textureIndex] = new UnityEngine.Color32(r, g, b, 255);
                            successfulReads++;
                        }
                        else
                        {
                            // Fallback to default land color if pixel reading fails
                            pixels[textureIndex] = new UnityEngine.Color32(139, 125, 107, 255);
                            failedReads++;
                        }
                    }
                }
            }

            // Debug: Log read statistics and terrain index distribution
            if (logLoadingProgress)
            {
                DominionLogger.LogMapInit($"MapDataLoader: Terrain bitmap read stats - Successful: {successfulReads}, Failed: {failedReads}");

                // Sample some terrain indices to see what we're actually getting
                if (terrainData.BitsPerPixel == 8)
                {
                    var indexCounts = new Dictionary<byte, int>();
                    int sampleCount = 0;
                    const int maxSamples = 1000; // Sample every 11534th pixel

                    for (int y = 0; y < terrainData.Height && sampleCount < maxSamples; y += terrainData.Height / 20)
                    {
                        for (int x = 0; x < terrainData.Width && sampleCount < maxSamples; x += terrainData.Width / 50)
                        {
                            if (ParadoxParser.Bitmap.BMPParser.TryGetPixelRGB(pixelData, x, y, out byte index, out byte _, out byte __))
                            {
                                if (!indexCounts.ContainsKey(index)) indexCounts[index] = 0;
                                indexCounts[index]++;
                                sampleCount++;
                            }
                        }
                    }

                    DominionLogger.LogMapInit($"MapDataLoader: Terrain indices found in samples: {string.Join(", ", indexCounts.Keys)}");
                }
            }

            // Apply terrain colors to texture
            if (logLoadingProgress)
            {
                DominionLogger.LogMapInit($"MapDataLoader: About to apply {pixels.Length} pixels to terrain texture instance {terrainTexture.GetInstanceID()}");
            }

            terrainTexture.SetPixels32(pixels);
            terrainTexture.Apply(false);

            // CRITICAL: Force GPU sync to ensure texture upload completes before shader access
            GL.Flush();

            if (logLoadingProgress)
            {
                DominionLogger.LogMapInit($"MapDataLoader: Applied terrain pixels, called Apply(), and forced GPU sync on texture instance {terrainTexture.GetInstanceID()}");
            }

            // Debug: Log some sample terrain colors to verify they're not all black
            if (logLoadingProgress)
            {
                Color32 sample1 = pixels[width * height / 4]; // Sample from 1/4 through
                Color32 sample2 = pixels[width * height / 2]; // Sample from middle
                Color32 sample3 = pixels[width * height * 3 / 4]; // Sample from 3/4 through

                DominionLogger.LogMapInit($"MapDataLoader: Terrain texture samples - [{sample1.r},{sample1.g},{sample1.b}] [{sample2.r},{sample2.g},{sample2.b}] [{sample3.r},{sample3.g},{sample3.b}]");
            }

            // DEBUG: Test sampling the populated texture from C# to verify it has colors
            if (logLoadingProgress)
            {
                // Sample a few specific pixels using GetPixel to verify the texture actually has our colors
                Color32 sample1 = terrainTexture.GetPixel(100, 100);
                Color32 sample2 = terrainTexture.GetPixel(1000, 500);
                Color32 sample3 = terrainTexture.GetPixel(2000, 1000);

                DominionLogger.LogMapInit($"MapDataLoader: C# GetPixel samples - [{sample1.r},{sample1.g},{sample1.b}] [{sample2.r},{sample2.g},{sample2.b}] [{sample3.r},{sample3.g},{sample3.b}]");

                // Also check if the texture is actually readable
                try
                {
                    var testRead = terrainTexture.GetPixels32();
                    DominionLogger.LogMapInit($"MapDataLoader: Texture is readable, got {testRead.Length} pixels");
                }
                catch (System.Exception e)
                {
                    DominionLogger.LogError($"MapDataLoader: Texture is NOT readable: {e.Message}");
                }
            }

            // CRITICAL: Rebind all textures to the ACTUAL RUNTIME material instance, not the original
            // Unity creates a material instance when you assign to meshRenderer.material
            if (textureManager != null)
            {
                // Find the map mesh renderer to get the actual runtime material instance
                var mapRenderer = Object.FindFirstObjectByType<Map.Rendering.MapRenderer>();
                if (mapRenderer != null)
                {
                    var runtimeMaterial = mapRenderer.GetMaterial();
                    if (runtimeMaterial != null)
                    {
                        textureManager.BindTexturesToMaterial(runtimeMaterial);
                        if (logLoadingProgress)
                        {
                            DominionLogger.Log($"MapDataLoader: Rebound textures to RUNTIME material instance {runtimeMaterial.GetInstanceID()}");
                        }
                    }
                }

                // Also bind to the coordinator's material for safety
                var mapRenderingCoordinator = Object.FindFirstObjectByType<Map.Rendering.MapRenderingCoordinator>();
                if (mapRenderingCoordinator != null && mapRenderingCoordinator.MapMaterial != null)
                {
                    textureManager.BindTexturesToMaterial(mapRenderingCoordinator.MapMaterial);
                    if (logLoadingProgress)
                    {
                        DominionLogger.Log($"MapDataLoader: Also bound textures to coordinator material {mapRenderingCoordinator.MapMaterial.GetInstanceID()}");
                    }

                    // CRITICAL FIX: Also rebind MapModeDataTextures so CountryColorPalette binding isn't lost!
                    // MapTextureManager.BindTexturesToMaterial() doesn't bind map mode textures (CountryColorPalette, etc.)
                    // So we need to also rebind those after rebinding base textures
                    var mapModeManager = Object.FindFirstObjectByType<Map.MapModes.MapModeManager>();
                    if (mapModeManager != null && mapModeManager.IsInitialized)
                    {
                        mapModeManager.RebindTextures();
                        if (logLoadingProgress)
                        {
                            DominionLogger.Log($"MapDataLoader: Rebound MapModeManager textures (CountryColorPalette, etc.) after base texture rebind");
                        }
                    }
                }
            }
        }
    }
}