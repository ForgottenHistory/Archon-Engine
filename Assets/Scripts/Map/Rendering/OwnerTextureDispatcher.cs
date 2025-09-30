using UnityEngine;
using Unity.Collections;
using Core.Queries;

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
        [Header("Compute Shader")]
        [SerializeField] private ComputeShader populateOwnerCompute;

        [Header("Debug")]
        [SerializeField] private bool logPerformance = true;

        // Kernel index
        private int populateOwnersKernel;

        // Thread group sizes (must match compute shader)
        private const int THREAD_GROUP_SIZE = 8;

        // GPU buffer for province owner data
        private ComputeBuffer provinceOwnerBuffer;
        private int bufferCapacity;

        // References
        private MapTextureManager textureManager;

        void Awake()
        {
            InitializeKernels();
        }

        /// <summary>
        /// Initialize compute shader kernels
        /// </summary>
        private void InitializeKernels()
        {
            if (populateOwnerCompute == null)
            {
                // Try to find the compute shader in the project
                #if UNITY_EDITOR
                string[] guids = UnityEditor.AssetDatabase.FindAssets("PopulateOwnerTexture t:ComputeShader");
                if (guids.Length > 0)
                {
                    string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
                    populateOwnerCompute = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>(path);
                    DominionLogger.Log($"OwnerTextureDispatcher: Found compute shader at {path}");
                }
                #endif

                if (populateOwnerCompute == null)
                {
                    DominionLogger.LogWarning("OwnerTextureDispatcher: Compute shader not assigned. Owner texture population will not work.");
                    return;
                }
            }

            // Get kernel index
            populateOwnersKernel = populateOwnerCompute.FindKernel("PopulateOwners");

            if (logPerformance)
            {
                DominionLogger.Log($"OwnerTextureDispatcher: Initialized with kernel index {populateOwnersKernel}");
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
            DominionLogger.Log("OwnerTextureDispatcher: PopulateOwnerTexture() called");

            if (populateOwnerCompute == null)
            {
                DominionLogger.LogWarning("OwnerTextureDispatcher: Compute shader not loaded. Skipping owner texture population.");
                return;
            }

            if (textureManager == null)
            {
                textureManager = GetComponent<MapTextureManager>();
                if (textureManager == null)
                {
                    DominionLogger.LogError("OwnerTextureDispatcher: MapTextureManager not found!");
                    return;
                }
            }

            if (provinceQueries == null)
            {
                DominionLogger.LogError("OwnerTextureDispatcher: ProvinceQueries is null!");
                return;
            }

            // Start performance timing
            float startTime = Time.realtimeSinceStartup;

            // Get all province IDs and their owners from Core simulation
            using var allProvinces = provinceQueries.GetAllProvinceIds(Allocator.Temp);
            int provinceCount = allProvinces.Length;

            if (provinceCount == 0)
            {
                DominionLogger.LogWarning("OwnerTextureDispatcher: No provinces available from ProvinceQueries");
                return;
            }

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
                    DominionLogger.Log($"OwnerTextureDispatcher: Created GPU buffer for {bufferCapacity} provinces");
                }
            }

            // Populate buffer with owner data from Core simulation
            uint[] ownerData = new uint[bufferCapacity];

            // Initialize all to 0 (unowned)
            for (int i = 0; i < bufferCapacity; i++)
            {
                ownerData[i] = 0;
            }

            // Fill with actual owner data
            int populatedCount = 0;
            int nonZeroOwners = 0;
            for (int i = 0; i < allProvinces.Length; i++)
            {
                ushort provinceId = allProvinces[i];

                // Bounds check
                if (provinceId >= bufferCapacity)
                {
                    if (populatedCount < 5)
                    {
                        DominionLogger.LogWarning($"OwnerTextureDispatcher: Province ID {provinceId} exceeds buffer capacity {bufferCapacity}");
                    }
                    continue;
                }

                // Get owner from Core simulation
                ushort ownerId = provinceQueries.GetOwner(provinceId);
                ownerData[provinceId] = ownerId;
                populatedCount++;

                if (ownerId != 0)
                {
                    nonZeroOwners++;
                    // Log first few non-zero owners
                    if (nonZeroOwners <= 10)
                    {
                        DominionLogger.Log($"OwnerTextureDispatcher: Province {provinceId} → Owner {ownerId}");
                    }
                }
            }

            DominionLogger.Log($"OwnerTextureDispatcher: Populated {populatedCount} provinces, {nonZeroOwners} have non-zero owners");

            // Upload to GPU
            provinceOwnerBuffer.SetData(ownerData);

            // Set compute shader parameters
            populateOwnerCompute.SetTexture(populateOwnersKernel, "ProvinceIDTexture", textureManager.ProvinceIDTexture);
            populateOwnerCompute.SetBuffer(populateOwnersKernel, "ProvinceOwnerBuffer", provinceOwnerBuffer);
            populateOwnerCompute.SetTexture(populateOwnersKernel, "ProvinceOwnerTexture", textureManager.ProvinceOwnerTexture);

            // Set dimensions
            populateOwnerCompute.SetInt("MapWidth", textureManager.MapWidth);
            populateOwnerCompute.SetInt("MapHeight", textureManager.MapHeight);

            // Calculate thread groups (round up division)
            int threadGroupsX = (textureManager.MapWidth + THREAD_GROUP_SIZE - 1) / THREAD_GROUP_SIZE;
            int threadGroupsY = (textureManager.MapHeight + THREAD_GROUP_SIZE - 1) / THREAD_GROUP_SIZE;

            // Dispatch compute shader - GPU processes all pixels in parallel
            populateOwnerCompute.Dispatch(populateOwnersKernel, threadGroupsX, threadGroupsY, 1);

            // Log performance
            if (logPerformance)
            {
                float elapsedMs = (Time.realtimeSinceStartup - startTime) * 1000f;
                DominionLogger.Log($"OwnerTextureDispatcher: Owner texture populated in {elapsedMs:F2}ms " +
                    $"({populatedCount} provinces, {textureManager.MapWidth}x{textureManager.MapHeight} pixels, " +
                    $"{threadGroupsX}x{threadGroupsY} thread groups)");
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
                DominionLogger.LogError("Cannot benchmark without texture manager");
                return;
            }

            var gameState = FindFirstObjectByType<global::Core.GameState>();
            if (gameState == null || gameState.ProvinceQueries == null)
            {
                DominionLogger.LogError("Cannot benchmark without GameState and ProvinceQueries");
                return;
            }

            DominionLogger.Log("=== Owner Texture Population Benchmark ===");
            DominionLogger.Log($"Map Size: {textureManager.MapWidth}x{textureManager.MapHeight}");

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
            DominionLogger.Log($"Average: {avgMs:F2}ms per population ({iterations} iterations)");
            DominionLogger.Log("=== Benchmark Complete ===");
        }
#endif
    }
}