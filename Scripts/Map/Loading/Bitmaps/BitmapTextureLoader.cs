using System.Threading.Tasks;
using UnityEngine;
using Map.Rendering;
using Utils;

namespace Map.Loading.Bitmaps
{
    /// <summary>
    /// Abstract base class for loading and populating textures from bitmap files
    /// Provides common infrastructure for terrain, heightmap, and normal map loading
    /// Eliminates code duplication across specialized bitmap loaders
    /// </summary>
    public abstract class BitmapTextureLoader
    {
        protected ParadoxParser.Jobs.JobifiedBMPLoader bmpLoader;
        protected MapTextureManager textureManager;
        protected bool logProgress;

        /// <summary>
        /// Initialize the bitmap loader with required dependencies
        /// </summary>
        public void Initialize(MapTextureManager textures, bool enableLogging = true)
        {
            textureManager = textures;
            logProgress = enableLogging;
            bmpLoader = new ParadoxParser.Jobs.JobifiedBMPLoader();
        }

        /// <summary>
        /// Load bitmap and populate corresponding texture
        /// </summary>
        /// <param name="provincesBmpPath">Path to provinces.bmp (used to derive bitmap path)</param>
        public async Task LoadAndPopulateAsync(string provincesBmpPath)
        {
            // Derive specific bitmap path from provinces.bmp path
            string bitmapPath = provincesBmpPath.Replace("provinces.bmp", GetBitmapFileName());

            // Check if file exists
            if (!System.IO.File.Exists(bitmapPath))
            {
                if (logProgress)
                {
                    ArchonLogger.LogMapInitWarning($"{GetLoaderName()}: Bitmap not found at {bitmapPath}, using defaults");
                }
                return;
            }

            try
            {
                if (logProgress)
                {
                    ArchonLogger.LogMapInit($"{GetLoaderName()}: Loading bitmap: {bitmapPath}");
                }

                // Load bitmap using Burst-compiled loader
                var bitmapResult = await bmpLoader.LoadBMPAsync(bitmapPath);

                if (!bitmapResult.Success)
                {
                    ArchonLogger.LogMapInitWarning($"{GetLoaderName()}: Failed to load bitmap: {bitmapResult.ErrorMessage}");
                    return;
                }

                if (logProgress)
                {
                    ArchonLogger.LogMapInit($"{GetLoaderName()}: Successfully loaded {bitmapResult.Width}x{bitmapResult.Height} bitmap, {bitmapResult.BitsPerPixel} bpp");
                }

                // Populate texture with bitmap data (implemented by derived class)
                PopulateTexture(bitmapResult);

                if (logProgress)
                {
                    ArchonLogger.LogMapInit($"{GetLoaderName()}: Populated texture with bitmap data");
                }

                // Dispose bitmap result to free memory
                bitmapResult.Dispose();

                // Rebind textures to materials
                RebindTexturesToMaterials();
            }
            catch (System.Exception e)
            {
                ArchonLogger.LogMapInitError($"{GetLoaderName()}: Exception loading bitmap: {e.Message}");
            }
        }

        /// <summary>
        /// Populate texture with bitmap data (implemented by derived classes)
        /// </summary>
        protected abstract void PopulateTexture(ParadoxParser.Jobs.BMPLoadResult bitmapData);

        /// <summary>
        /// Get the bitmap filename (e.g., "terrain.bmp", "heightmap.bmp")
        /// </summary>
        protected abstract string GetBitmapFileName();

        /// <summary>
        /// Get the loader name for logging purposes
        /// </summary>
        protected abstract string GetLoaderName();

        /// <summary>
        /// Rebind textures to runtime materials after texture updates
        /// Ensures all material instances have the updated textures
        /// </summary>
        protected void RebindTexturesToMaterials()
        {
            if (textureManager == null) return;

            // Find map renderer to get runtime material instance
            var mapRenderer = Object.FindFirstObjectByType<Rendering.MapRenderer>();
            if (mapRenderer != null)
            {
                var runtimeMaterial = mapRenderer.GetMaterial();
                if (runtimeMaterial != null)
                {
                    textureManager.BindTexturesToMaterial(runtimeMaterial);
                    if (logProgress)
                    {
                        ArchonLogger.LogMapInit($"{GetLoaderName()}: Rebound textures to runtime material {runtimeMaterial.GetInstanceID()}");
                    }
                }
            }

            // Also bind to coordinator material for safety
            var renderingCoordinator = Object.FindFirstObjectByType<Rendering.MapRenderingCoordinator>();
            if (renderingCoordinator != null && renderingCoordinator.MapMaterial != null)
            {
                textureManager.BindTexturesToMaterial(renderingCoordinator.MapMaterial);

                // Rebind map mode textures (CountryColorPalette, etc.)
                var mapModeManager = Object.FindFirstObjectByType<MapModes.MapModeManager>();
                if (mapModeManager != null && mapModeManager.IsInitialized)
                {
                    mapModeManager.RebindTextures();
                    if (logProgress)
                    {
                        ArchonLogger.LogMapInit($"{GetLoaderName()}: Rebound map mode textures after texture update");
                    }
                }
            }
        }

        /// <summary>
        /// Apply texture changes and force GPU sync
        /// Common pattern used by all bitmap loaders
        /// </summary>
        protected void ApplyTextureAndSync(Texture2D texture)
        {
            if (texture == null) return;

            texture.Apply(false);
            GL.Flush(); // Force GPU sync

            if (logProgress)
            {
                ArchonLogger.LogMapInit($"{GetLoaderName()}: Applied texture changes and forced GPU sync on {texture.GetInstanceID()}");
            }
        }
    }
}
