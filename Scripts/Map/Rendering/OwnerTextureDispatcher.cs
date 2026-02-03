using UnityEngine;
using UnityEngine.Rendering;
using Core.Queries;
using Core.Modding;

namespace Map.Rendering
{
    /// <summary>
    /// Manages GPU compute shaders for owner texture population.
    /// Full-map shader (PopulateOwnerTexture) used at load time.
    /// Indexed shader (UpdateOwnerByIndex) used at runtime — dispatches only over
    /// pixels belonging to changed provinces instead of scanning all 97.5M pixels.
    /// </summary>
    public class OwnerTextureDispatcher : MonoBehaviour
    {
        // Full-map compute shader (load time)
        private ComputeShader populateOwnerCompute;
        private int populateOwnersKernel;

        // Indexed compute shader (runtime)
        private ComputeShader updateByIndexCompute;
        private int updateByIndexKernel;

        [Header("Debug")]
        [SerializeField] private bool logPerformance = true;
        [SerializeField] private bool debugWriteProvinceIDs = false;

        private const int THREAD_GROUP_SIZE_FULL = 8;
        private const int THREAD_GROUP_SIZE_INDEXED = 64;

        // Full-map buffers (load time)
        private ComputeBuffer provinceOwnerBuffer;
        private int bufferCapacity;
        private uint[] ownerData;

        // Pixel index buffers (built at load time, used at runtime)
        private ComputeBuffer pixelCoordsBuffer;
        private ComputeBuffer pixelOffsetsBuffer;
        private ComputeBuffer pixelCountsBuffer;
        private bool hasPixelIndex;

        // Per-dispatch buffers — double-buffered to avoid GPU sync stalls.
        // Writing SetData to a buffer the GPU is still reading forces a pipeline flush.
        private ComputeBuffer[] changedProvincesBuffers = new ComputeBuffer[2];
        private ComputeBuffer[] dispatchOffsetsBuffers = new ComputeBuffer[2];
        private int activeBufferIndex;
        private uint[] changedProvincesData;
        private uint[] dispatchOffsetsData;
        private int changedBufferCapacity;

        // References
        private MapTextureManager textureManager;
        private bool isInitialized = false;

        // CPU-side copies of offset/count tables for dispatch calculation
        private uint[] cpuOffsets;
        private uint[] cpuCounts;

        public void Initialize()
        {
            if (isInitialized) return;
            isInitialized = true;
            InitializeKernels();
        }

        private void InitializeKernels()
        {
            populateOwnerCompute = ModLoader.LoadAssetWithFallback<ComputeShader>(
                "PopulateOwnerTexture", "Shaders/PopulateOwnerTexture");

            if (populateOwnerCompute != null)
                populateOwnersKernel = populateOwnerCompute.FindKernel("PopulateOwners");
            else
                ArchonLogger.LogWarning("OwnerTextureDispatcher: PopulateOwnerTexture compute shader not found!", "map_rendering");

            updateByIndexCompute = ModLoader.LoadAssetWithFallback<ComputeShader>(
                "UpdateOwnerByIndex", "Shaders/UpdateOwnerByIndex");

            if (updateByIndexCompute != null)
                updateByIndexKernel = updateByIndexCompute.FindKernel("UpdateOwnerByIndex");
            else
                ArchonLogger.LogWarning("OwnerTextureDispatcher: UpdateOwnerByIndex compute shader not found!", "map_rendering");
        }

        public void SetTextureManager(MapTextureManager manager)
        {
            textureManager = manager;
        }

        /// <summary>
        /// Set the per-province pixel index built at load time.
        /// Enables targeted runtime updates via UpdateOwnerByIndex compute shader.
        /// </summary>
        public void SetPixelIndex(uint[] pixelCoords, uint[] offsets, uint[] counts)
        {
            // Release old buffers
            pixelCoordsBuffer?.Release();
            pixelOffsetsBuffer?.Release();
            pixelCountsBuffer?.Release();

            if (pixelCoords.Length == 0)
            {
                hasPixelIndex = false;
                return;
            }

            pixelCoordsBuffer = new ComputeBuffer(pixelCoords.Length, sizeof(uint));
            pixelCoordsBuffer.SetData(pixelCoords);

            pixelOffsetsBuffer = new ComputeBuffer(offsets.Length, sizeof(uint));
            pixelOffsetsBuffer.SetData(offsets);

            pixelCountsBuffer = new ComputeBuffer(counts.Length, sizeof(uint));
            pixelCountsBuffer.SetData(counts);

            // Keep CPU copies for dispatch offset calculation
            cpuOffsets = offsets;
            cpuCounts = counts;
            hasPixelIndex = true;

            ArchonLogger.Log($"OwnerTextureDispatcher: Pixel index set ({pixelCoords.Length:N0} pixel entries)", "map_initialization");
        }

        /// <summary>
        /// Full populate: builds entire owner buffer from simulation data.
        /// Used at load time only.
        /// </summary>
        [ContextMenu("Populate Owner Texture")]
        public void PopulateOwnerTexture(ProvinceQueries provinceQueries)
        {
            if (!ValidateDependencies(provinceQueries))
                return;

            EnsureOwnerBufferCreated();

            System.Array.Clear(ownerData, 0, ownerData.Length);
            provinceQueries.FillOwnerBuffer(ownerData);

            provinceOwnerBuffer.SetData(ownerData);

            populateOwnerCompute.SetTexture(populateOwnersKernel, "ProvinceIDTexture", textureManager.ProvinceIDTexture);
            populateOwnerCompute.SetBuffer(populateOwnersKernel, "ProvinceOwnerBuffer", provinceOwnerBuffer);
            populateOwnerCompute.SetTexture(populateOwnersKernel, "ProvinceOwnerTexture", textureManager.ProvinceOwnerTexture);
            populateOwnerCompute.SetInt("MapWidth", textureManager.MapWidth);
            populateOwnerCompute.SetInt("MapHeight", textureManager.MapHeight);
            populateOwnerCompute.SetInt("DebugWriteProvinceIDs", debugWriteProvinceIDs ? 1 : 0);

            int threadGroupsX = (textureManager.MapWidth + THREAD_GROUP_SIZE_FULL - 1) / THREAD_GROUP_SIZE_FULL;
            int threadGroupsY = (textureManager.MapHeight + THREAD_GROUP_SIZE_FULL - 1) / THREAD_GROUP_SIZE_FULL;
            populateOwnerCompute.Dispatch(populateOwnersKernel, threadGroupsX, threadGroupsY, 1);
        }

        /// <summary>
        /// Incremental update: queues compute shader dispatch into a CommandBuffer
        /// for only pixels of changed provinces. Non-blocking — no GPU sync stall.
        /// Falls back to full-map dispatch if pixel index not available.
        /// </summary>
        public void UpdateOwnerTexture(ProvinceQueries provinceQueries, ushort[] changedProvinces, CommandBuffer cmd)
        {
            if (!ValidateDependencies(provinceQueries))
                return;

            if (!hasPixelIndex || updateByIndexCompute == null)
            {
                // Fallback: full map dispatch via command buffer
                EnsureOwnerBufferCreated();
                for (int i = 0; i < changedProvinces.Length; i++)
                {
                    ushort pid = changedProvinces[i];
                    if (pid < ownerData.Length)
                    {
                        ownerData[pid] = provinceQueries.GetOwner(pid);
                        provinceOwnerBuffer.SetData(ownerData, pid, pid, 1);
                    }
                }
                cmd.SetComputeTextureParam(populateOwnerCompute, populateOwnersKernel, "ProvinceIDTexture", textureManager.ProvinceIDTexture);
                cmd.SetComputeBufferParam(populateOwnerCompute, populateOwnersKernel, "ProvinceOwnerBuffer", provinceOwnerBuffer);
                cmd.SetComputeTextureParam(populateOwnerCompute, populateOwnersKernel, "ProvinceOwnerTexture", textureManager.ProvinceOwnerTexture);
                cmd.SetComputeIntParam(populateOwnerCompute, "MapWidth", textureManager.MapWidth);
                cmd.SetComputeIntParam(populateOwnerCompute, "MapHeight", textureManager.MapHeight);
                cmd.SetComputeIntParam(populateOwnerCompute, "DebugWriteProvinceIDs", 0);
                int tgx = (textureManager.MapWidth + THREAD_GROUP_SIZE_FULL - 1) / THREAD_GROUP_SIZE_FULL;
                int tgy = (textureManager.MapHeight + THREAD_GROUP_SIZE_FULL - 1) / THREAD_GROUP_SIZE_FULL;
                cmd.DispatchCompute(populateOwnerCompute, populateOwnersKernel, tgx, tgy, 1);
                return;
            }

            int numChanged = changedProvinces.Length;
            if (numChanged == 0) return;

            // Build per-dispatch data: packed province+owner, and cumulative pixel offsets
            EnsureChangedBuffers(numChanged);

            uint totalPixels = 0;
            for (int i = 0; i < numChanged; i++)
            {
                ushort pid = changedProvinces[i];
                uint newOwner = provinceQueries.GetOwner(pid);
                changedProvincesData[i] = (uint)pid | (newOwner << 16);
                dispatchOffsetsData[i] = totalPixels;
                totalPixels += (pid < cpuCounts.Length) ? cpuCounts[pid] : 0;
            }

            if (totalPixels == 0) return;

            // Swap to the other buffer set so we don't write to buffers the GPU is still reading
            activeBufferIndex = 1 - activeBufferIndex;
            var changedBuf = changedProvincesBuffers[activeBufferIndex];
            var offsetsBuf = dispatchOffsetsBuffers[activeBufferIndex];

            changedBuf.SetData(changedProvincesData, 0, 0, numChanged);
            offsetsBuf.SetData(dispatchOffsetsData, 0, 0, numChanged);

            // Queue dispatch into command buffer — non-blocking
            cmd.SetComputeTextureParam(updateByIndexCompute, updateByIndexKernel, "ProvinceOwnerTexture", textureManager.ProvinceOwnerTexture);
            cmd.SetComputeBufferParam(updateByIndexCompute, updateByIndexKernel, "PixelCoords", pixelCoordsBuffer);
            cmd.SetComputeBufferParam(updateByIndexCompute, updateByIndexKernel, "ProvincePixelOffsets", pixelOffsetsBuffer);
            cmd.SetComputeBufferParam(updateByIndexCompute, updateByIndexKernel, "ProvincePixelCounts", pixelCountsBuffer);
            cmd.SetComputeBufferParam(updateByIndexCompute, updateByIndexKernel, "ChangedProvinces", changedBuf);
            cmd.SetComputeBufferParam(updateByIndexCompute, updateByIndexKernel, "DispatchOffsets", offsetsBuf);
            cmd.SetComputeIntParam(updateByIndexCompute, "NumChangedProvinces", numChanged);

            int threadGroups = ((int)totalPixels + THREAD_GROUP_SIZE_INDEXED - 1) / THREAD_GROUP_SIZE_INDEXED;
            cmd.DispatchCompute(updateByIndexCompute, updateByIndexKernel, threadGroups, 1, 1);

            // Also update CPU-side owner data for consistency (used by full populate if called later)
            EnsureOwnerBufferCreated();
            for (int i = 0; i < numChanged; i++)
            {
                ushort pid = changedProvinces[i];
                if (pid < ownerData.Length)
                    ownerData[pid] = provinceQueries.GetOwner(pid);
            }
        }

        private bool ValidateDependencies(ProvinceQueries provinceQueries)
        {
            if (populateOwnerCompute == null)
            {
                ArchonLogger.LogWarning("OwnerTextureDispatcher: Compute shader not loaded.", "map_rendering");
                return false;
            }

            if (textureManager == null)
            {
                textureManager = GetComponent<MapTextureManager>();
                if (textureManager == null)
                {
                    ArchonLogger.LogError("OwnerTextureDispatcher: MapTextureManager not found!", "map_rendering");
                    return false;
                }
            }

            if (provinceQueries == null)
            {
                ArchonLogger.LogError("OwnerTextureDispatcher: ProvinceQueries is null!", "map_rendering");
                return false;
            }

            return true;
        }

        private void EnsureOwnerBufferCreated()
        {
            const int MAX_PROVINCES = 65536;
            if (provinceOwnerBuffer == null || bufferCapacity != MAX_PROVINCES)
            {
                provinceOwnerBuffer?.Release();
                bufferCapacity = MAX_PROVINCES;
                provinceOwnerBuffer = new ComputeBuffer(bufferCapacity, sizeof(uint));
            }

            if (ownerData == null || ownerData.Length != bufferCapacity)
            {
                ownerData = new uint[bufferCapacity];
            }
        }

        private void EnsureChangedBuffers(int needed)
        {
            if (changedBufferCapacity >= needed) return;

            for (int i = 0; i < 2; i++)
            {
                changedProvincesBuffers[i]?.Release();
                dispatchOffsetsBuffers[i]?.Release();
            }

            // Round up to avoid frequent resizing
            changedBufferCapacity = Mathf.Max(needed, 64);
            for (int i = 0; i < 2; i++)
            {
                changedProvincesBuffers[i] = new ComputeBuffer(changedBufferCapacity, sizeof(uint));
                dispatchOffsetsBuffers[i] = new ComputeBuffer(changedBufferCapacity, sizeof(uint));
            }
            changedProvincesData = new uint[changedBufferCapacity];
            dispatchOffsetsData = new uint[changedBufferCapacity];
        }

        void OnDestroy()
        {
            provinceOwnerBuffer?.Release();
            pixelCoordsBuffer?.Release();
            pixelOffsetsBuffer?.Release();
            pixelCountsBuffer?.Release();
            for (int i = 0; i < 2; i++)
            {
                changedProvincesBuffers[i]?.Release();
                dispatchOffsetsBuffers[i]?.Release();
            }
        }

#if UNITY_EDITOR
        [ContextMenu("Benchmark Owner Texture Population")]
        private void BenchmarkOwnerTexturePopulation()
        {
            if (textureManager == null)
            {
                ArchonLogger.LogError("Cannot benchmark without texture manager", "map_rendering");
                return;
            }

            var gameState = FindFirstObjectByType<global::Core.GameState>();
            if (gameState == null || gameState.ProvinceQueries == null)
            {
                ArchonLogger.LogError("Cannot benchmark without GameState and ProvinceQueries", "map_rendering");
                return;
            }

            ArchonLogger.Log("=== Owner Texture Population Benchmark ===", "map_rendering");
            ArchonLogger.Log($"Map Size: {textureManager.MapWidth}x{textureManager.MapHeight}", "map_rendering");

            var provinceQueries = gameState.ProvinceQueries;
            PopulateOwnerTexture(provinceQueries);

            float totalTime = 0;
            int iterations = 10;

            for (int i = 0; i < iterations; i++)
            {
                float start = Time.realtimeSinceStartup;
                PopulateOwnerTexture(provinceQueries);
                totalTime += (Time.realtimeSinceStartup - start);
            }

            float avgMs = (totalTime / iterations) * 1000f;
            ArchonLogger.Log($"Average: {avgMs:F2}ms per population ({iterations} iterations)", "map_rendering");
            ArchonLogger.Log("=== Benchmark Complete ===", "map_rendering");
        }
#endif
    }
}