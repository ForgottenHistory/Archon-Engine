using UnityEngine;
using Map.Rendering;
using ParadoxParser.Jobs;
using ParadoxParser.Bitmap;
using Core;
using Utils;

namespace Map.Rendering
{
    /// <summary>
    /// Handles population of map textures from province data
    /// Extracted from MapGenerator to follow single responsibility principle
    /// Manages conversion from bitmap data to GPU textures with simulation integration
    /// Architecture: Uses GPU compute shader for owner texture (NO CPU pixel ops)
    /// </summary>
    public class MapTexturePopulator : MonoBehaviour
    {
        [Header("Configuration")]
        [SerializeField] private bool logPopulationProgress = true;

        [Header("GPU Dispatcher")]
        [SerializeField] private OwnerTextureDispatcher ownerTextureDispatcher;

        /// <summary>
        /// Populate MapTextureManager textures using data from Core simulation systems
        /// This method integrates bitmap visual data with simulation layer province/country data
        /// </summary>
        public void PopulateWithSimulationData(ProvinceMapResult provinceResult, MapTextureManager textureManager, ProvinceMapping mapping, GameState gameState)
        {
            if (textureManager == null || mapping == null || gameState == null)
            {
                DominionLogger.LogError("MapTexturePopulator: Cannot populate textures - missing dependencies");
                return;
            }

            var pixelData = provinceResult.BMPData.GetPixelData();
            int width = provinceResult.BMPData.Width;
            int height = provinceResult.BMPData.Height;

            if (logPopulationProgress)
            {
                DominionLogger.LogMapInit($"MapTexturePopulator: Starting texture population with simulation data for {width}x{height} map");
            }

            // Get query interfaces for simulation data
            var provinceQueries = gameState.ProvinceQueries;
            var countryQueries = gameState.CountryQueries;

            // Populate province ID, color, and owner textures from bitmap + simulation data
            int processedPixels = 0;
            int validProvinces = 0;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (BMPParser.TryGetPixelRGB(pixelData, x, y, out byte r, out byte g, out byte b))
                    {
                        processedPixels++;

                        // Create Color32 for province lookup
                        var pixelColor = new Color32(r, g, b, 255);

                        // Find province ID for this color using ProvinceMapping
                        ushort provinceID = mapping.GetProvinceByColor(pixelColor);

                        if (provinceID > 0 && provinceQueries.Exists(provinceID))
                        {
                            validProvinces++;

                            // Set province ID in texture
                            textureManager.SetProvinceID(x, y, provinceID);

                            // Set province color for visual display (from bitmap)
                            textureManager.SetProvinceColor(x, y, pixelColor);

                            // Owner texture populated by GPU compute shader (see below)
                            // Architecture: NO CPU pixel ops for owner texture

                            // Add pixel to province mapping
                            mapping.AddPixelToProvince(provinceID, x, y);
                        }
                    }
                }
            }

            // Apply texture changes for ID and color textures (CPU-written)
            textureManager.ApplyTextureChanges();

            // Populate owner texture using GPU compute shader (architecture compliance: NO CPU pixel ops)
            if (ownerTextureDispatcher == null)
            {
                ownerTextureDispatcher = GetComponent<OwnerTextureDispatcher>();
                if (ownerTextureDispatcher == null)
                {
                    ownerTextureDispatcher = FindFirstObjectByType<OwnerTextureDispatcher>();
                }
            }

            if (ownerTextureDispatcher != null)
            {
                DominionLogger.LogMapInit("MapTexturePopulator: Populating owner texture via GPU compute shader");
                ownerTextureDispatcher.PopulateOwnerTexture(provinceQueries);
            }
            else
            {
                DominionLogger.LogMapInitError("MapTexturePopulator: OwnerTextureDispatcher not found - cannot populate owner texture!");
            }

            if (logPopulationProgress)
            {
                DominionLogger.LogMapInit($"MapTexturePopulator: Populated texture manager with {width}x{height} province data from simulation layer");
                DominionLogger.LogMapInit($"MapTexturePopulator: Processed {processedPixels} pixels, {validProvinces} valid province pixels");
            }
        }

        /// <summary>
        /// Populate MapTextureManager textures from province processing result
        /// Legacy method for standalone operation without simulation integration
        /// </summary>
        public void PopulateFromProvinceResult(ProvinceMapResult provinceResult, MapTextureManager textureManager, ProvinceMapping mapping)
        {
            if (textureManager == null || mapping == null)
            {
                DominionLogger.LogError("MapTexturePopulator: Cannot populate textures - missing dependencies");
                return;
            }

            var pixelData = provinceResult.BMPData.GetPixelData();
            int width = provinceResult.BMPData.Width;
            int height = provinceResult.BMPData.Height;

            if (logPopulationProgress)
            {
                DominionLogger.LogMapInit($"MapTexturePopulator: Starting texture population from province result for {width}x{height} map");
            }

            // Populate province ID and color textures from BMP data
            int processedPixels = 0;
            int validProvinces = 0;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (BMPParser.TryGetPixelRGB(pixelData, x, y, out byte r, out byte g, out byte b))
                    {
                        processedPixels++;

                        // Create Color32 for province lookup
                        var pixelColor = new Color32(r, g, b, 255);

                        // Find province ID for this color using ProvinceMapping's public method
                        ushort provinceID = mapping.GetProvinceByColor(pixelColor);

                        if (provinceID > 0) // Valid province ID
                        {
                            validProvinces++;

                            // Set province ID in texture
                            textureManager.SetProvinceID(x, y, provinceID);

                            // Set province color for visual display
                            textureManager.SetProvinceColor(x, y, pixelColor);

                            // Add pixel to province mapping
                            mapping.AddPixelToProvince(provinceID, x, y);
                        }
                    }
                }
            }

            // Apply all texture changes
            textureManager.ApplyTextureChanges();

            if (logPopulationProgress)
            {
                DominionLogger.LogMapInit($"MapTexturePopulator: Populated texture manager with {width}x{height} province data");
                DominionLogger.LogMapInit($"MapTexturePopulator: Processed {processedPixels} pixels, {validProvinces} valid province pixels");
            }
        }

        /// <summary>
        /// Update texture manager with live simulation data changes
        /// Optimized method for runtime updates without full repopulation
        /// Architecture: Uses GPU compute shader for owner texture updates (NO CPU pixel ops)
        /// </summary>
        public void UpdateSimulationData(MapTextureManager textureManager, ProvinceMapping mapping, GameState gameState, ushort[] changedProvinces)
        {
            if (textureManager == null || mapping == null || gameState == null || changedProvinces == null)
            {
                DominionLogger.LogError("MapTexturePopulator: Cannot update simulation data - missing dependencies");
                return;
            }

            var provinceQueries = gameState.ProvinceQueries;

            // Architecture: Use GPU compute shader for owner texture population
            // NO CPU pixel-by-pixel operations (removed legacy SetProvinceOwner loop)
            if (ownerTextureDispatcher == null)
            {
                ownerTextureDispatcher = GetComponent<OwnerTextureDispatcher>();
                if (ownerTextureDispatcher == null)
                {
                    ownerTextureDispatcher = FindFirstObjectByType<OwnerTextureDispatcher>();
                }
            }

            if (ownerTextureDispatcher != null)
            {
                DominionLogger.LogMapInit($"MapTexturePopulator: Updating owner texture for {changedProvinces.Length} changed provinces via GPU compute shader");
                ownerTextureDispatcher.PopulateOwnerTexture(provinceQueries);
            }
            else
            {
                DominionLogger.LogError("MapTexturePopulator: OwnerTextureDispatcher not found - cannot update owner texture!");
            }
        }
    }
}