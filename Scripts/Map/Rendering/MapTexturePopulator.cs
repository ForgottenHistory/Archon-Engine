using UnityEngine;
using Map.Loading;
using Core;
using Core.Modding;
using Utils;
using static Map.Loading.ProvinceMapProcessor;

namespace Map.Rendering
{
    /// <summary>
    /// Handles population of map textures from province data.
    /// Plain C# class - dependencies passed via constructor.
    /// </summary>
    public class MapTexturePopulator
    {
        private readonly bool logProgress;
        private readonly OwnerTextureDispatcher ownerTextureDispatcher;
        private ComputeShader populateProvinceIDCompute;

        public MapTexturePopulator(OwnerTextureDispatcher ownerDispatcher, bool logProgress = true)
        {
            this.ownerTextureDispatcher = ownerDispatcher;
            this.logProgress = logProgress;

            // Load compute shader - check mods first, then fall back to Resources
            populateProvinceIDCompute = ModLoader.LoadAssetWithFallback<ComputeShader>(
                "PopulateProvinceIDTexture",
                "Shaders/PopulateProvinceIDTexture"
            );
        }

        /// <summary>
        /// Populate textures using simulation data.
        /// </summary>
        public void PopulateWithSimulationData(
            ProvinceMapResult provinceResult,
            MapTextureManager textureManager,
            ProvinceMapping mapping,
            GameState gameState)
        {
            if (textureManager == null || mapping == null || gameState == null)
            {
                ArchonLogger.LogError("MapTexturePopulator: Missing dependencies", "map_rendering");
                return;
            }

            var pixelData = provinceResult.BMPData.GetPixelData();
            int width = provinceResult.BMPData.Width;
            int height = provinceResult.BMPData.Height;

            if (logProgress)
                ArchonLogger.Log($"MapTexturePopulator: Populating {width}x{height} textures", "map_initialization");

            var provinceRegistry = gameState.Registries?.Provinces;
            if (provinceRegistry == null)
            {
                ArchonLogger.LogError("MapTexturePopulator: GameState.Registries not set!", "map_initialization");
                return;
            }

            // Create pixel arrays for batch operations
            Color32[] provinceIDPixels = new Color32[width * height];
            Color32[] provinceColorPixels = new Color32[width * height];

            // Initialize with default
            for (int i = 0; i < provinceIDPixels.Length; i++)
            {
                provinceIDPixels[i] = new Color32(0, 0, 0, 255);
                provinceColorPixels[i] = new Color32(0, 0, 0, 255);
            }

            int processedPixels = 0;
            int validProvinces = 0;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (pixelData.TryGetPixelRGB(x, y, out byte r, out byte g, out byte b))
                    {
                        processedPixels++;
                        var pixelColor = new Color32(r, g, b, 255);
                        ushort provinceID = mapping.GetProvinceByColor(pixelColor);

                        if (provinceID > 0 && provinceRegistry.ExistsByDefinition(provinceID))
                        {
                            validProvinces++;
                            int pixelIndex = y * width + x;

                            provinceIDPixels[pixelIndex] = Province.ProvinceIDEncoder.PackProvinceID(provinceID);
                            provinceColorPixels[pixelIndex] = pixelColor;
                            mapping.AddPixelToProvince(provinceID, x, y);
                        }
                    }
                }
            }

            // Write to GPU
            PopulateProvinceIDTextureGPU(textureManager, width, height, provinceIDPixels);
            PopulateProvinceColorTextureGPU(textureManager, width, height, provinceColorPixels);

            // Populate owner texture via GPU
            if (ownerTextureDispatcher != null)
            {
                ownerTextureDispatcher.PopulateOwnerTexture(gameState.ProvinceQueries);

                var ownerSyncRequest = UnityEngine.Rendering.AsyncGPUReadback.Request(textureManager.ProvinceOwnerTexture);
                ownerSyncRequest.WaitForCompletion();
            }
            else
            {
                ArchonLogger.LogError("MapTexturePopulator: OwnerTextureDispatcher not available!", "map_initialization");
            }

            if (logProgress)
            {
                ArchonLogger.Log($"MapTexturePopulator: Processed {processedPixels} pixels, {validProvinces} valid", "map_initialization");
            }
        }

        /// <summary>
        /// Update owner texture for changed provinces (runtime updates).
        /// </summary>
        public void UpdateSimulationData(
            MapTextureManager textureManager,
            ProvinceMapping mapping,
            GameState gameState,
            ushort[] changedProvinces)
        {
            if (textureManager == null || mapping == null || gameState == null || changedProvinces == null)
            {
                ArchonLogger.LogError("MapTexturePopulator: Missing dependencies for update", "map_rendering");
                return;
            }

            if (ownerTextureDispatcher != null)
            {
                ownerTextureDispatcher.PopulateOwnerTexture(gameState.ProvinceQueries);

                var ownerSyncRequest = UnityEngine.Rendering.AsyncGPUReadback.Request(textureManager.ProvinceOwnerTexture);
                ownerSyncRequest.WaitForCompletion();
            }
        }

        private void PopulateProvinceIDTextureGPU(MapTextureManager textureManager, int width, int height, Color32[] pixels)
        {
            if (populateProvinceIDCompute == null)
            {
                PopulateProvinceIDTextureGPU_Fallback(textureManager, width, height, pixels);
                return;
            }

            // Convert Color32[] to packed uint[] for GPU buffer
            uint[] packedPixels = new uint[pixels.Length];
            for (int i = 0; i < pixels.Length; i++)
            {
                Color32 c = pixels[i];
                packedPixels[i] = ((uint)c.a << 24) | ((uint)c.r << 16) | ((uint)c.g << 8) | (uint)c.b;
            }

            ComputeBuffer pixelBuffer = new ComputeBuffer(packedPixels.Length, sizeof(uint));
            pixelBuffer.SetData(packedPixels);

            int kernel = populateProvinceIDCompute.FindKernel("PopulateProvinceIDs");
            populateProvinceIDCompute.SetBuffer(kernel, "ProvinceIDPixelData", pixelBuffer);
            populateProvinceIDCompute.SetTexture(kernel, "ProvinceIDTexture", textureManager.ProvinceIDTexture);
            populateProvinceIDCompute.SetInt("MapWidth", width);
            populateProvinceIDCompute.SetInt("MapHeight", height);

            const int THREAD_GROUP_SIZE = 8;
            int threadGroupsX = (width + THREAD_GROUP_SIZE - 1) / THREAD_GROUP_SIZE;
            int threadGroupsY = (height + THREAD_GROUP_SIZE - 1) / THREAD_GROUP_SIZE;
            populateProvinceIDCompute.Dispatch(kernel, threadGroupsX, threadGroupsY, 1);

            var asyncRead = UnityEngine.Rendering.AsyncGPUReadback.Request(textureManager.ProvinceIDTexture);
            asyncRead.WaitForCompletion();

            pixelBuffer.Release();

            if (logProgress)
                ArchonLogger.Log("MapTexturePopulator: ProvinceIDTexture populated via compute shader", "map_initialization");
        }

        private void PopulateProvinceIDTextureGPU_Fallback(MapTextureManager textureManager, int width, int height, Color32[] pixels)
        {
            if (logProgress)
                ArchonLogger.LogWarning("MapTexturePopulator: Using Graphics.Blit fallback (compute shader not available)", "map_initialization");

            Texture2D tempTex = new Texture2D(width, height, TextureFormat.ARGB32, false);
            tempTex.filterMode = FilterMode.Point;
            tempTex.wrapMode = TextureWrapMode.Clamp;
            tempTex.SetPixels32(pixels);
            tempTex.Apply(false);
            Graphics.Blit(tempTex, textureManager.ProvinceIDTexture);
            GL.Flush();
            GL.InvalidateState();
            Object.Destroy(tempTex);
        }

        private void PopulateProvinceColorTextureGPU(MapTextureManager textureManager, int width, int height, Color32[] pixels)
        {
            int textureSize = textureManager.ProvinceColorTexture.width * textureManager.ProvinceColorTexture.height;
            int arraySize = pixels.Length;

            if (arraySize != textureSize)
            {
                ArchonLogger.LogError($"MapTexturePopulator: Size mismatch - Array: {arraySize}, Texture: {textureSize}", "map_initialization");
                return;
            }

            textureManager.ProvinceColorTexture.SetPixels32(pixels);
            textureManager.ProvinceColorTexture.Apply(false);

            if (logProgress)
                ArchonLogger.Log("MapTexturePopulator: ProvinceColorTexture populated", "map_initialization");
        }
    }
}
