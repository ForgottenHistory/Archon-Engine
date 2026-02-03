using UnityEngine;
using Core.Queries;
using Core.Modding;

namespace Map.Rendering
{
    /// <summary>
    /// Manages GPU compute shader for high-performance owner texture population.
    /// Processes entire map in parallel to populate province owner texture from simulation data.
    /// Part of the texture-based map rendering system - dual-layer architecture compliance.
    /// Performance: ~2ms for entire map vs 50+ seconds on CPU
    /// </summary>
    public class OwnerTextureDispatcher : MonoBehaviour
    {
        // Loaded via ModLoader
        private ComputeShader populateOwnerCompute;

        [Header("Debug")]
        [SerializeField] private bool logPerformance = true;
        [SerializeField] private bool debugWriteProvinceIDs = false; // Debug: Write province IDs instead of owner IDs to verify texture reading

        // Kernel index
        private int populateOwnersKernel;

        // Thread group sizes (must match compute shader)
        private const int THREAD_GROUP_SIZE = 8;

        // GPU buffer for province owner data
        private ComputeBuffer provinceOwnerBuffer;
        private int bufferCapacity;

        // Reusable CPU-side buffer for owner data (avoids allocation per update)
        private uint[] ownerData;

        // References
        private MapTextureManager textureManager;
        private bool isInitialized = false;

        /// <summary>
        /// Initialize compute shader kernels. Called by ArchonEngine.
        /// </summary>
        public void Initialize()
        {
            if (isInitialized) return;
            isInitialized = true;
            InitializeKernels();
        }

        /// <summary>
        /// Initialize compute shader kernels
        /// </summary>
        private void InitializeKernels()
        {
            if (populateOwnerCompute == null)
            {
                // Load compute shader - check mods first, then fall back to Resources
                populateOwnerCompute = ModLoader.LoadAssetWithFallback<ComputeShader>(
                    "PopulateOwnerTexture",
                    "Shaders/PopulateOwnerTexture"
                );

                if (populateOwnerCompute == null)
                {
                    ArchonLogger.LogWarning("OwnerTextureDispatcher: Compute shader not found!", "map_rendering");
                    return;
                }
            }

            // Get kernel index
            populateOwnersKernel = populateOwnerCompute.FindKernel("PopulateOwners");

            if (logPerformance)
            {
                ArchonLogger.Log($"OwnerTextureDispatcher: Initialized with kernel index {populateOwnersKernel}", "map_initialization");
            }
        }

        /// <summary>
        /// Set the texture manager reference
        /// </summary>
        public void SetTextureManager(MapTextureManager manager)
        {
            textureManager = manager;
        }

        /// <summary>
        /// Populate owner texture from Core simulation data using GPU compute shader
        /// Architecture: Core ProvinceQueries → GPU buffer → GPU texture
        /// </summary>
        /// <param name="provinceQueries">Read-only access to Core simulation data</param>
        [ContextMenu("Populate Owner Texture")]
        public void PopulateOwnerTexture(ProvinceQueries provinceQueries)
        {
            if (populateOwnerCompute == null)
            {
                ArchonLogger.LogWarning("OwnerTextureDispatcher: Compute shader not loaded. Skipping owner texture population.", "map_rendering");
                return;
            }

            if (textureManager == null)
            {
                textureManager = GetComponent<MapTextureManager>();
                if (textureManager == null)
                {
                    ArchonLogger.LogError("OwnerTextureDispatcher: MapTextureManager not found!", "map_rendering");
                    return;
                }
            }

            if (provinceQueries == null)
            {
                ArchonLogger.LogError("OwnerTextureDispatcher: ProvinceQueries is null!", "map_rendering");
                return;
            }

            // Start performance timing
            float startTime = Time.realtimeSinceStartup;

            // Create/resize buffer if needed (supports up to 65536 provinces)
            const int MAX_PROVINCES = 65536;
            if (provinceOwnerBuffer == null || bufferCapacity != MAX_PROVINCES)
            {
                // Release old buffer
                provinceOwnerBuffer?.Release();

                // Create new buffer with uint data (4 bytes per province)
                bufferCapacity = MAX_PROVINCES;
                provinceOwnerBuffer = new ComputeBuffer(bufferCapacity, sizeof(uint));

                if (logPerformance)
                {
                    ArchonLogger.Log($"OwnerTextureDispatcher: Created GPU buffer for {bufferCapacity} provinces", "map_initialization");
                }
            }

            // Bulk fill owner data — single linear pass over contiguous buffer, no per-province hash lookups
            if (ownerData == null || ownerData.Length != bufferCapacity)
            {
                ownerData = new uint[bufferCapacity];
            }
            System.Array.Clear(ownerData, 0, ownerData.Length);
            provinceQueries.FillOwnerBuffer(ownerData);

            // Upload to GPU
            provinceOwnerBuffer.SetData(ownerData);

            // ProvinceIDTexture is now a RenderTexture, so compute shader can read it directly
            var provinceIDTex = textureManager.ProvinceIDTexture;

            // Set compute shader parameters - direct binding, no temporary copy needed
            populateOwnerCompute.SetTexture(populateOwnersKernel, "ProvinceIDTexture", provinceIDTex);
            populateOwnerCompute.SetBuffer(populateOwnersKernel, "ProvinceOwnerBuffer", provinceOwnerBuffer);
            populateOwnerCompute.SetTexture(populateOwnersKernel, "ProvinceOwnerTexture", textureManager.ProvinceOwnerTexture);

            // Set dimensions
            populateOwnerCompute.SetInt("MapWidth", textureManager.MapWidth);
            populateOwnerCompute.SetInt("MapHeight", textureManager.MapHeight);

            // Set debug mode - write province IDs instead of owner IDs to verify coordinate system
            populateOwnerCompute.SetInt("DebugWriteProvinceIDs", debugWriteProvinceIDs ? 1 : 0);

            if (debugWriteProvinceIDs)
            {
                ArchonLogger.Log("OwnerTextureDispatcher: DEBUG MODE - Writing province IDs instead of owner IDs to ProvinceOwnerTexture", "map_initialization");
            }

            // Calculate thread groups (round up division)
            int threadGroupsX = (textureManager.MapWidth + THREAD_GROUP_SIZE - 1) / THREAD_GROUP_SIZE;
            int threadGroupsY = (textureManager.MapHeight + THREAD_GROUP_SIZE - 1) / THREAD_GROUP_SIZE;

            // Dispatch compute shader - GPU processes all pixels in parallel
            populateOwnerCompute.Dispatch(populateOwnersKernel, threadGroupsX, threadGroupsY, 1);

            // Log performance
            if (logPerformance)
            {
                float elapsedMs = (Time.realtimeSinceStartup - startTime) * 1000f;
                ArchonLogger.Log($"OwnerTextureDispatcher: Owner texture populated in {elapsedMs:F2}ms " +
                    $"({textureManager.MapWidth}x{textureManager.MapHeight} pixels, " +
                    $"{threadGroupsX}x{threadGroupsY} thread groups)", "map_rendering");
            }
        }

        void OnDestroy()
        {
            // Release GPU buffer
            if (provinceOwnerBuffer != null)
            {
                provinceOwnerBuffer.Release();
                provinceOwnerBuffer = null;
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

            // Warm up
            PopulateOwnerTexture(provinceQueries);

            // Measure
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