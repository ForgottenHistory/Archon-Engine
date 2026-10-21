using System.Threading.Tasks;
using UnityEngine;
using Map.Rendering;
using Utils;

namespace Map.Loading.Images
{
    /// <summary>
    /// Abstract base class for loading and populating textures from bitmap files
    /// Provides common infrastructure for terrain, heightmap, and normal map loading
    /// Eliminates code duplication across specialized bitmap loaders
    /// </summary>
    public abstract class BitmapTextureLoader
    {
        protected JobifiedBMPLoader bmpLoader;
        protected MapTextureManager textureManager;
        protected bool logProgress;

        /// <summary>
        /// Initialize the bitmap loader with required dependencies
        /// </summary>
        public void Initialize(MapTextureManager textures, bool enableLogging = true)
        {
            textureManager = textures;
            logProgress = enableLogging;
            bmpLoader = new JobifiedBMPLoader();
        }

        /// <summary>
        /// Load bitmap and populate corresponding texture
        /// </summary>
        /// <param name="provincesPath">Path to provinces.bmp or provinces.png (used to derive bitmap path)</param>
        public async Task LoadAndPopulateAsync(string provincesPath)
        {
            // Derive specific bitmap path from provinces path (handles both .bmp and .png)
            string bitmapPath = provincesPath
                .Replace("provinces.bmp", GetBitmapFileName())
                .Replace("provinces.png", GetBitmapFileName());

            // Check if file exists
            if (!System.IO.File.Exists(bitmapPath))
            {
                if (logProgress)
                {
                    ArchonLogger.LogWarning($"{GetLoaderName()}: Bitmap not found at {bitmapPath}, using defaults", "map_initialization");
                }
                return;
            }

            try
            {
                if (logProgress)
                {
                    ArchonLogger.Log($"{GetLoaderName()}: Loading bitmap: {bitmapPath}", "map_initialization");
                }

                // Load bitmap using Burst-compiled loader
                var bitmapResult = await bmpLoader.LoadBMPAsync(bitmapPath);

                if (!bitmapResult.IsSuccess)
                {
                    ArchonLogger.LogWarning($"{GetLoaderName()}: Failed to load bitmap: {bitmapResult.ErrorMessage}", "map_initialization");
                    return;
                }

                if (logProgress)
                {
                    ArchonLogger.Log($"{GetLoaderName()}: Successfully loaded {bitmapResult.Width}x{bitmapResult.Height} bitmap, {bitmapResult.BitsPerPixel} bpp", "map_initialization");
                }

                // Populate texture with bitmap data (implemented by derived class)
                PopulateTexture(bitmapResult);

                if (logProgress)
                {
                    ArchonLogger.Log($"{GetLoaderName()}: Populated texture with bitmap data", "map_initialization");
                }

                // Dispose bitmap result to free memory
                bitmapResult.Dispose();

                // Republish textures (texture objects may have been replaced)
                RebindTextures();
            }
            catch (System.Exception e)
            {
                ArchonLogger.LogError($"{GetLoaderName()}: Exception loading bitmap: {e.Message}", "map_initialization");
            }
        }

        /// <summary>
        /// Populate texture with bitmap data (implemented by derived classes)
        /// </summary>
        protected abstract void PopulateTexture(BMPLoadResult bitmapData);

        /// <summary>
        /// Get the bitmap filename (e.g., "terrain.bmp", "heightmap.bmp")
        /// </summary>
        protected abstract string GetBitmapFileName();

        /// <summary>
        /// Get the loader name for logging purposes
        /// </summary>
        protected abstract string GetLoaderName();

        /// <summary>
        /// Republish map textures as shader globals after texture updates
        /// </summary>
        protected void RebindTextures()
        {
            if (textureManager == null) return;

            textureManager.BindGlobalTextures();

            // Map mode textures (CountryColorPalette, etc.)
            var mapModeManager = Object.FindFirstObjectByType<MapModes.MapModeManager>();
            if (mapModeManager != null && mapModeManager.IsInitialized)
            {
                mapModeManager.RebindTextures();
            }

            if (logProgress)
            {
                ArchonLogger.Log($"{GetLoaderName()}: Rebound textures as shader globals", "map_initialization");
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
                ArchonLogger.Log($"{GetLoaderName()}: Applied texture changes and forced GPU sync on {texture.GetInstanceID()}", "map_initialization");
            }
        }
    }
}
