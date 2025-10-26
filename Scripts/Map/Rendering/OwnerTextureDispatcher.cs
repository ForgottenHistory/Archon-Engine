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
        [SerializeField] private bool debugWriteProvinceIDs = false; // Debug: Write province IDs instead of owner IDs to verify texture reading

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
                    ArchonLogger.Log($"OwnerTextureDispatcher: Found compute shader at {path}", "map_initialization");
                }
                #endif

                if (populateOwnerCompute == null)
                {
                    ArchonLogger.LogWarning("OwnerTextureDispatcher: Compute shader not assigned. Owner texture population will not work.", "map_rendering");
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
            ArchonLogger.Log("OwnerTextureDispatcher: PopulateOwnerTexture() called", "map_initialization");

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

            // Get all province IDs and their owners from Core simulation
            using var allProvinces = provinceQueries.GetAllProvinceIds(Allocator.Temp);
            int provinceCount = allProvinces.Length;

            if (provinceCount == 0)
            {
                ArchonLogger.LogWarning("OwnerTextureDispatcher: No provinces available from ProvinceQueries", "map_rendering");
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
                    ArchonLogger.Log($"OwnerTextureDispatcher: Created GPU buffer for {bufferCapacity} provinces", "map_initialization");
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
                        ArchonLogger.LogWarning($"OwnerTextureDispatcher: Province ID {provinceId} exceeds buffer capacity {bufferCapacity}", "map_rendering");
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
                        ArchonLogger.Log($"OwnerTextureDispatcher: Province {provinceId} → Owner {ownerId}", "map_initialization");
                    }
                }
            }

            ArchonLogger.Log($"OwnerTextureDispatcher: Populated {populatedCount} provinces, {nonZeroOwners} have non-zero owners", "map_initialization");

            // DEBUG: Check specific test provinces before uploading to GPU
            ArchonLogger.Log($"OwnerTextureDispatcher: Buffer at index 2751 (Castile) = {ownerData[2751]} (expected 151)", "map_initialization");
            ArchonLogger.Log($"OwnerTextureDispatcher: Buffer at index 817 (Inca) = {ownerData[817]} (expected 731)", "map_initialization");

            // Upload to GPU
            provinceOwnerBuffer.SetData(ownerData);

            // ProvinceIDTexture is now a RenderTexture, so compute shader can read it directly
            var provinceIDTex = textureManager.ProvinceIDTexture;

            // Set compute shader parameters - direct binding, no temporary copy needed
            populateOwnerCompute.SetTexture(populateOwnersKernel, "ProvinceIDTexture", provinceIDTex);
            populateOwnerCompute.SetBuffer(populateOwnersKernel, "ProvinceOwnerBuffer", provinceOwnerBuffer);
            populateOwnerCompute.SetTexture(populateOwnersKernel, "ProvinceOwnerTexture", textureManager.ProvinceOwnerTexture);

            // DEBUG: Verify texture binding
            ArchonLogger.Log($"OwnerTextureDispatcher: Bound ProvinceIDTexture ({provinceIDTex?.GetInstanceID()}, {provinceIDTex?.format}) directly to compute shader", "map_initialization");
            ArchonLogger.Log($"OwnerTextureDispatcher: Compute shader will write to ProvinceOwnerTexture instance {textureManager.ProvinceOwnerTexture?.GetInstanceID()}", "map_initialization");

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

            // DEBUG: Verify compute shader wrote data - sample a known province pixel
            // Castile province 2751 should have owner 151
            // We know from logs that province 2751 is at approx pixel (2767, 711) based on previous debugging
            // But we don't want to loop - just sample ONE specific pixel
            RenderTexture.active = textureManager.ProvinceOwnerTexture;
            Texture2D singlePixel = new Texture2D(1, 1, TextureFormat.RFloat, false);
            singlePixel.ReadPixels(new Rect(2767, 711, 1, 1), 0, 0);
            singlePixel.Apply();
            RenderTexture.active = null;

            float ownerRawFloat = singlePixel.GetPixel(0, 0).r;
            // ProvinceOwnerTexture is R32_SFloat storing raw float values (151.0, not normalized)
            // No multiplication needed - just cast to uint
            uint decodedValue = (uint)(ownerRawFloat + 0.5f);
            Object.Destroy(singlePixel);

            if (debugWriteProvinceIDs)
            {
                ArchonLogger.Log($"OwnerTextureDispatcher: DEBUG - ProvinceOwnerTexture at pixel (2767,711) contains province ID {decodedValue} (expected 2751 for Castile if coordinates match)", "map_initialization");
            }
            else
            {
                ArchonLogger.Log($"OwnerTextureDispatcher: ProvinceOwnerTexture at pixel (2767,711) contains owner ID {decodedValue} (expected 151 for Castile)", "map_initialization");
            }

            // Log performance
            if (logPerformance)
            {
                float elapsedMs = (Time.realtimeSinceStartup - startTime) * 1000f;
                ArchonLogger.Log($"OwnerTextureDispatcher: Owner texture populated in {elapsedMs:F2}ms " +
                    $"({populatedCount} provinces, {textureManager.MapWidth}x{textureManager.MapHeight} pixels, " +
                    $"{threadGroupsX}x{threadGroupsY} thread groups)", "map_initialization");
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