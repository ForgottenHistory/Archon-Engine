using UnityEngine;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Map.Loading;
using Core;
using Core.Modding;
using Utils;
using static Map.Loading.ProvinceMapProcessor;

namespace Map.Rendering
{
    /// <summary>
    /// Handles population of map textures from province data.
    /// Uses GPU compute shader for the pixel loop (color→provinceID mapping).
    /// Falls back to CPU path if compute shader unavailable or BMP format.
    /// Plain C# class - dependencies passed via constructor.
    /// </summary>
    public class MapTexturePopulator
    {
        private readonly bool logProgress;
        private readonly OwnerTextureDispatcher ownerTextureDispatcher;
        private ComputeShader populateProvinceTexturesCompute;
        private ComputeShader populateProvinceIDCompute; // Legacy single-texture shader

        private const int THREAD_GROUP_SIZE = 8;

        public MapTexturePopulator(OwnerTextureDispatcher ownerDispatcher, bool logProgress = true)
        {
            this.ownerTextureDispatcher = ownerDispatcher;
            this.logProgress = logProgress;

            // Load new unified compute shader (populates both ID + color textures from raw pixels)
            populateProvinceTexturesCompute = ModLoader.LoadAssetWithFallback<ComputeShader>(
                "PopulateProvinceTextures",
                "Shaders/PopulateProvinceTextures"
            );

            // Legacy shader as fallback for pre-computed pixel arrays
            populateProvinceIDCompute = ModLoader.LoadAssetWithFallback<ComputeShader>(
                "PopulateProvinceIDTexture",
                "Shaders/PopulateProvinceIDTexture"
            );
        }

        /// <summary>
        /// Populate textures using simulation data.
        /// Attempts GPU compute path first, falls back to CPU if needed.
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

            // Try GPU path first
            bool gpuSuccess = false;
            if (populateProvinceTexturesCompute != null)
            {
                gpuSuccess = TryPopulateGPU(provinceResult, textureManager, mapping, width, height);
            }

            // Fall back to CPU if GPU path not available or failed
            if (!gpuSuccess)
            {
                if (logProgress)
                    ArchonLogger.LogWarning("MapTexturePopulator: GPU path unavailable, falling back to CPU", "map_initialization");
                PopulateCPU(provinceResult, textureManager, mapping, provinceRegistry, width, height);
            }

            // Populate owner texture via GPU (always uses its own compute shader)
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
        }

        /// <summary>
        /// GPU compute shader path: uploads raw pixel bytes + hash table to GPU,
        /// performs color→provinceID lookup entirely on GPU.
        /// </summary>
        private bool TryPopulateGPU(
            ProvinceMapResult provinceResult,
            MapTextureManager textureManager,
            ProvinceMapping mapping,
            int width, int height)
        {
            float startTime = Time.realtimeSinceStartup;

            // Get raw pixel bytes from the image data (PNG only)
            if (!provinceResult.BMPData.TryGetRawPixelBytes(out var rawBytes, out int bytesPerPixel))
            {
                if (logProgress)
                    ArchonLogger.LogWarning("MapTexturePopulator: Cannot get raw pixel bytes (BMP format not supported for GPU path)", "map_initialization");
                return false;
            }

            if (bytesPerPixel < 3)
            {
                ArchonLogger.LogWarning($"MapTexturePopulator: Unsupported bytes per pixel: {bytesPerPixel}", "map_initialization");
                return false;
            }

            int totalPixels = width * height;

            // Step 1: Single pass — pack GPU uint[] and write color texture RGBA simultaneously
            // This avoids two separate 97.5M pixel loops and eliminates the Color32[] managed allocation
            uint[] packedPixels = new uint[totalPixels];
            var colorTexRaw = textureManager.ProvinceColorTexture.GetRawTextureData<byte>();

            unsafe
            {
                byte* src = (byte*)rawBytes.GetUnsafeReadOnlyPtr();
                byte* colorDst = (byte*)colorTexRaw.GetUnsafePtr();

                for (int i = 0; i < totalPixels; i++)
                {
                    int srcOffset = i * bytesPerPixel;
                    byte r = src[srcOffset];
                    byte g = src[srcOffset + 1];
                    byte b = src[srcOffset + 2];

                    // Pack for GPU compute shader: r | (g << 8) | (b << 16)
                    packedPixels[i] = (uint)r | ((uint)g << 8) | ((uint)b << 16);

                    // Write RGBA32 directly into texture buffer (R, G, B, A)
                    int dstOffset = i * 4;
                    colorDst[dstOffset] = r;
                    colorDst[dstOffset + 1] = g;
                    colorDst[dstOffset + 2] = b;
                    colorDst[dstOffset + 3] = 255;
                }
            }

            textureManager.ProvinceColorTexture.Apply(false);

            float packTime = (Time.realtimeSinceStartup - startTime) * 1000f;

            // Step 2: Build GPU hash table from color→provinceID mapping
            var hashTable = BuildGPUHashTable(provinceResult.ProvinceMappings.ColorToProvinceID, out int hashTableSize);

            float hashBuildTime = (Time.realtimeSinceStartup - startTime) * 1000f - packTime;

            // Step 3: Upload to GPU and dispatch
            ComputeBuffer pixelBuffer = null;
            ComputeBuffer hashTableBuffer = null;

            try
            {
                pixelBuffer = new ComputeBuffer(totalPixels, sizeof(uint));
                pixelBuffer.SetData(packedPixels);

                hashTableBuffer = new ComputeBuffer(hashTableSize, sizeof(uint) * 2); // uint2 per entry
                hashTableBuffer.SetData(hashTable);

                int kernel = populateProvinceTexturesCompute.FindKernel("PopulateProvinceTextures");

                populateProvinceTexturesCompute.SetBuffer(kernel, "RawPixelData", pixelBuffer);
                populateProvinceTexturesCompute.SetBuffer(kernel, "ColorHashTable", hashTableBuffer);
                populateProvinceTexturesCompute.SetInt("HashTableSize", hashTableSize);
                populateProvinceTexturesCompute.SetInt("HashTableMask", hashTableSize - 1);
                populateProvinceTexturesCompute.SetTexture(kernel, "ProvinceIDTexture", textureManager.ProvinceIDTexture);
                populateProvinceTexturesCompute.SetInt("MapWidth", width);
                populateProvinceTexturesCompute.SetInt("MapHeight", height);

                int threadGroupsX = (width + THREAD_GROUP_SIZE - 1) / THREAD_GROUP_SIZE;
                int threadGroupsY = (height + THREAD_GROUP_SIZE - 1) / THREAD_GROUP_SIZE;

                populateProvinceTexturesCompute.Dispatch(kernel, threadGroupsX, threadGroupsY, 1);

                // Sync to ensure ProvinceIDTexture is ready before subsequent compute shaders use it
                var syncRequest = UnityEngine.Rendering.AsyncGPUReadback.Request(textureManager.ProvinceIDTexture);
                syncRequest.WaitForCompletion();

                float totalTime = (Time.realtimeSinceStartup - startTime) * 1000f;
                float gpuTime = totalTime - packTime - hashBuildTime;

                ArchonLogger.Log($"MapTexturePopulator: GPU path complete in {totalTime:F0}ms " +
                    $"(pack+color: {packTime:F0}ms, hash: {hashBuildTime:F0}ms, GPU: {gpuTime:F0}ms, " +
                    $"{totalPixels:N0} pixels, {threadGroupsX}x{threadGroupsY} groups)",
                    "map_initialization");

                return true;
            }
            catch (System.Exception e)
            {
                ArchonLogger.LogError($"MapTexturePopulator: GPU path failed: {e.Message}", "map_initialization");
                return false;
            }
            finally
            {
                pixelBuffer?.Release();
                hashTableBuffer?.Release();
            }
        }

        /// <summary>
        /// Build an open-addressing hash table from NativeHashMap for GPU upload.
        /// Each entry is uint2: x = packed RGB key, y = province ID.
        /// Empty slots have key = 0xFFFFFFFF.
        /// Table size is power of 2 with ~50% load factor.
        /// </summary>
        private static uint[] BuildGPUHashTable(NativeHashMap<int, int> colorToProvinceID, out int tableSize)
        {
            // Size table to ~2x entry count, minimum 256, power of 2
            int entryCount = colorToProvinceID.Count;
            tableSize = 256;
            while (tableSize < entryCount * 2)
                tableSize *= 2;

            uint mask = (uint)(tableSize - 1);

            // Each entry is 2 uints (key, value), flattened into a single array
            uint[] table = new uint[tableSize * 2];

            // Initialize all keys to empty sentinel
            for (int i = 0; i < tableSize; i++)
            {
                table[i * 2] = 0xFFFFFFFF;     // key = empty
                table[i * 2 + 1] = 0;           // value = 0
            }

            // Insert entries using same hash function as compute shader
            var enumerator = colorToProvinceID.GetEnumerator();
            while (enumerator.MoveNext())
            {
                uint key = (uint)enumerator.Current.Key;
                uint value = (uint)enumerator.Current.Value;

                uint hash = HashRGB(key) & mask;

                // Linear probing
                for (int i = 0; i < tableSize; i++)
                {
                    uint slot = (hash + (uint)i) & mask;
                    if (table[slot * 2] == 0xFFFFFFFF)
                    {
                        table[slot * 2] = key;
                        table[slot * 2 + 1] = value;
                        break;
                    }
                }
            }

            return table;
        }

        /// <summary>
        /// Must match the hash function in PopulateProvinceTextures.compute
        /// </summary>
        private static uint HashRGB(uint key)
        {
            key ^= key >> 16;
            key *= 0x45d9f3b;
            key ^= key >> 16;
            return key;
        }

        /// <summary>
        /// CPU fallback path: iterates all pixels sequentially.
        /// Used when GPU compute shader unavailable or BMP format.
        /// </summary>
        private void PopulateCPU(
            ProvinceMapResult provinceResult,
            MapTextureManager textureManager,
            ProvinceMapping mapping,
            global::Core.Registries.ProvinceRegistry provinceRegistry,
            int width, int height)
        {
            float startTime = Time.realtimeSinceStartup;

            var pixelData = provinceResult.BMPData.GetPixelData();

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
                        }
                    }
                }
            }

            // Write to GPU using legacy compute shader
            PopulateProvinceIDTextureGPU(textureManager, width, height, provinceIDPixels);
            PopulateProvinceColorTextureGPU(textureManager, width, height, provinceColorPixels);

            float elapsedMs = (Time.realtimeSinceStartup - startTime) * 1000f;

            if (logProgress)
            {
                ArchonLogger.Log($"MapTexturePopulator: CPU path complete in {elapsedMs:F1}ms " +
                    $"({processedPixels} pixels, {validProvinces} valid)", "map_initialization");
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

        #region Legacy GPU upload (pre-computed pixel arrays)

        private void PopulateProvinceIDTextureGPU(MapTextureManager textureManager, int width, int height, Color32[] pixels)
        {
            if (populateProvinceIDCompute == null)
            {
                PopulateProvinceIDTextureGPU_Fallback(textureManager, width, height, pixels);
                return;
            }

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

            int threadGroupsX = (width + THREAD_GROUP_SIZE - 1) / THREAD_GROUP_SIZE;
            int threadGroupsY = (height + THREAD_GROUP_SIZE - 1) / THREAD_GROUP_SIZE;
            populateProvinceIDCompute.Dispatch(kernel, threadGroupsX, threadGroupsY, 1);

            var asyncRead = UnityEngine.Rendering.AsyncGPUReadback.Request(textureManager.ProvinceIDTexture);
            asyncRead.WaitForCompletion();

            pixelBuffer.Release();
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

        #endregion
    }
}
