using UnityEngine;
using UnityEngine.Rendering;

namespace Map.Rendering
{
    /// <summary>
    /// Generates distance field textures for modern Paradox-style smooth anti-aliased borders.
    /// Uses Jump Flooding Algorithm (JFA) for efficient GPU-based distance field generation.
    ///
    /// Algorithm:
    /// 1. Edge Detection: Mark border pixels
    /// 2. Jump Flooding: Propagate closest border distance (log(n) passes)
    /// 3. Finalize: Convert positions to distances
    ///
    /// Result: Silky smooth borders at any zoom level (CK3/Stellaris quality)
    /// </summary>
    public class BorderDistanceFieldGenerator : MonoBehaviour
    {
        [Header("Compute Shader")]
        [SerializeField] private ComputeShader distanceFieldCompute;

        [Header("Distance Field Settings")]
        [SerializeField] private int maxDistanceRadius = 16; // How far to propagate distances (pixels)

        [Header("Debug")]
        [SerializeField] private bool logPerformance = false;

        // Kernel indices
        private int initKernel;
        private int jumpFloodKernel;
        private int finalizeKernel;

        // Thread group size
        private const int THREAD_GROUP_SIZE = 8;

        // Ping-pong buffers for JFA
        private RenderTexture distanceFieldA;
        private RenderTexture distanceFieldB;

        // References
        private MapTextureManager textureManager;

        void Awake()
        {
            InitializeKernels();
        }

        private void InitializeKernels()
        {
            if (distanceFieldCompute == null)
            {
                #if UNITY_EDITOR
                string[] guids = UnityEditor.AssetDatabase.FindAssets("BorderDistanceField t:ComputeShader");
                if (guids.Length > 0)
                {
                    string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
                    distanceFieldCompute = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>(path);
                    ArchonLogger.Log($"BorderDistanceFieldGenerator: Found compute shader at {path}", "map_initialization");
                }
                #endif

                if (distanceFieldCompute == null)
                {
                    ArchonLogger.LogWarning("BorderDistanceFieldGenerator: Compute shader not assigned", "map_rendering");
                    return;
                }
            }

            if (!distanceFieldCompute.HasKernel("InitDistanceField"))
            {
                ArchonLogger.LogError("BorderDistanceFieldGenerator: Compute shader missing InitDistanceField kernel!", "map_initialization");
                return;
            }
            if (!distanceFieldCompute.HasKernel("JumpFloodStep"))
            {
                ArchonLogger.LogError("BorderDistanceFieldGenerator: Compute shader missing JumpFloodStep kernel!", "map_initialization");
                return;
            }
            if (!distanceFieldCompute.HasKernel("FinalizeDistanceField"))
            {
                ArchonLogger.LogError("BorderDistanceFieldGenerator: Compute shader missing FinalizeDistanceField kernel!", "map_initialization");
                return;
            }

            initKernel = distanceFieldCompute.FindKernel("InitDistanceField");
            jumpFloodKernel = distanceFieldCompute.FindKernel("JumpFloodStep");
            finalizeKernel = distanceFieldCompute.FindKernel("FinalizeDistanceField");

            ArchonLogger.Log($"BorderDistanceFieldGenerator: Initialized kernels (init={initKernel}, jumpFlood={jumpFloodKernel}, finalize={finalizeKernel})", "map_initialization");
        }

        public void SetTextureManager(MapTextureManager manager)
        {
            textureManager = manager;
        }

        /// <summary>
        /// Generate distance field for borders (main entry point)
        /// Runs dual-channel JFA: once for country borders (R), once for province borders (G)
        /// </summary>
        public void GenerateDistanceField()
        {
            if (distanceFieldCompute == null)
            {
                ArchonLogger.LogError("BorderDistanceFieldGenerator: Compute shader is null!", "map_rendering");
                return;
            }

            if (textureManager == null)
            {
                ArchonLogger.LogError("BorderDistanceFieldGenerator: TextureManager is null!", "map_rendering");
                return;
            }

            ArchonLogger.Log("BorderDistanceFieldGenerator: Starting dual-channel distance field generation", "map_rendering");
            float startTime = Time.realtimeSinceStartup;

            // Create ping-pong buffers if needed
            EnsureBuffersCreated();

            // PASS 1: Country borders (R channel)
            ArchonLogger.Log("BorderDistanceFieldGenerator: Pass 1 - Country borders", "map_rendering");
            RunInitPass(borderType: 0); // 0 = country borders
            RunJumpFloodingPasses();
            RunFinalizePass(outputChannel: 0); // Write to R channel

            // PASS 2: Province borders (G channel)
            ArchonLogger.Log("BorderDistanceFieldGenerator: Pass 2 - Province borders", "map_rendering");
            RunInitPass(borderType: 1); // 1 = province borders
            RunJumpFloodingPasses();
            RunFinalizePass(outputChannel: 1); // Write to G channel

            // Cleanup temporary buffers
            ReleaseBuffers();

            float elapsed = (Time.realtimeSinceStartup - startTime) * 1000f;
            ArchonLogger.Log($"BorderDistanceFieldGenerator: Dual-channel distance field generation complete in {elapsed:F2}ms", "map_rendering");
        }

        private void EnsureBuffersCreated()
        {
            int width = textureManager.MapWidth;
            int height = textureManager.MapHeight;

            ArchonLogger.Log($"BorderDistanceFieldGenerator: Creating buffers for {width}x{height} map", "map_rendering");

            // RG32F format to store 2D positions (x, y) of closest border pixel
            // CRITICAL: Use explicit GraphicsFormat to avoid TYPELESS issues
            if (distanceFieldA == null || distanceFieldA.width != width)
            {
                distanceFieldA?.Release();

                var descriptor = new RenderTextureDescriptor(
                    width,
                    height,
                    UnityEngine.Experimental.Rendering.GraphicsFormat.R32G32_SFloat, // 2x 32-bit float for (x,y) positions
                    0 // No depth buffer
                );
                descriptor.enableRandomWrite = true; // Required for compute shader UAV
                descriptor.useMipMap = false;
                descriptor.autoGenerateMips = false;

                distanceFieldA = new RenderTexture(descriptor);
                distanceFieldA.name = "BorderDistanceField_A";
                distanceFieldA.filterMode = FilterMode.Point; // No filtering for position data
                distanceFieldA.wrapMode = TextureWrapMode.Clamp;
                distanceFieldA.Create();
                ArchonLogger.Log("BorderDistanceFieldGenerator: Created DistanceFieldA", "map_rendering");
            }

            if (distanceFieldB == null || distanceFieldB.width != width)
            {
                distanceFieldB?.Release();

                var descriptor = new RenderTextureDescriptor(
                    width,
                    height,
                    UnityEngine.Experimental.Rendering.GraphicsFormat.R32G32_SFloat,
                    0
                );
                descriptor.enableRandomWrite = true;
                descriptor.useMipMap = false;
                descriptor.autoGenerateMips = false;

                distanceFieldB = new RenderTexture(descriptor);
                distanceFieldB.name = "BorderDistanceField_B";
                distanceFieldB.filterMode = FilterMode.Point;
                distanceFieldB.wrapMode = TextureWrapMode.Clamp;
                distanceFieldB.Create();
                ArchonLogger.Log("BorderDistanceFieldGenerator: Created DistanceFieldB", "map_rendering");
            }
        }

        private void RunInitPass(int borderType)
        {
            string borderTypeName = borderType == 0 ? "country" : "province";
            ArchonLogger.Log($"BorderDistanceFieldGenerator: Starting init pass for {borderTypeName} borders", "map_rendering");

            // DEBUG: Verify input textures before binding
            if (borderType == 0) // Country borders pass
            {
                ArchonLogger.Log($"BorderDistanceFieldGenerator: Input textures - ProvinceIDTexture={textureManager.ProvinceIDTexture?.GetInstanceID()}, ProvinceOwnerTexture={textureManager.ProvinceOwnerTexture?.GetInstanceID()}", "map_rendering");

                // Sample owner texture to verify it has data
                RenderTexture.active = textureManager.ProvinceOwnerTexture;
                Texture2D ownerSample = new Texture2D(1, 1, TextureFormat.RFloat, false);
                ownerSample.ReadPixels(new Rect(2767, 711, 1, 1), 0, 0);
                ownerSample.Apply();
                RenderTexture.active = null;

                float ownerRaw = ownerSample.GetPixel(0, 0).r;
                uint ownerId = (uint)(ownerRaw + 0.5f);
                ArchonLogger.Log($"BorderDistanceFieldGenerator: ProvinceOwnerTexture at (2767,711) contains owner ID {ownerId} (expected 151 for Castile)", "map_rendering");
                UnityEngine.Object.Destroy(ownerSample);
            }

            // Set input textures
            distanceFieldCompute.SetTexture(initKernel, "ProvinceIDTexture", textureManager.ProvinceIDTexture);
            distanceFieldCompute.SetTexture(initKernel, "ProvinceOwnerTexture", textureManager.ProvinceOwnerTexture);
            distanceFieldCompute.SetTexture(initKernel, "DistanceFieldA", distanceFieldA);

            // Set border type (0 = country, 1 = province)
            distanceFieldCompute.SetInt("BorderType", borderType);

            // Set dimensions
            distanceFieldCompute.SetInt("MapWidth", textureManager.MapWidth);
            distanceFieldCompute.SetInt("MapHeight", textureManager.MapHeight);

            // Dispatch
            int threadGroupsX = (textureManager.MapWidth + THREAD_GROUP_SIZE - 1) / THREAD_GROUP_SIZE;
            int threadGroupsY = (textureManager.MapHeight + THREAD_GROUP_SIZE - 1) / THREAD_GROUP_SIZE;

            ArchonLogger.Log($"BorderDistanceFieldGenerator: Dispatching init pass ({threadGroupsX}x{threadGroupsY} thread groups)", "map_rendering");
            distanceFieldCompute.Dispatch(initKernel, threadGroupsX, threadGroupsY, 1);
            ArchonLogger.Log($"BorderDistanceFieldGenerator: Init pass complete for {borderTypeName} borders", "map_rendering");
        }

        private void RunJumpFloodingPasses()
        {
            int maxDimension = Mathf.Max(textureManager.MapWidth, textureManager.MapHeight);

            // Calculate number of passes needed: log2(maxDimension)
            int numPasses = Mathf.CeilToInt(Mathf.Log(maxDimension, 2));

            // Start with largest step size and halve each iteration
            int stepSize = Mathf.NextPowerOfTwo(maxDimension) / 2;

            ArchonLogger.Log($"BorderDistanceFieldGenerator: Starting {numPasses} JFA passes (maxDim={maxDimension}, startStep={stepSize})", "map_rendering");

            bool useBufferA = true; // Ping-pong flag

            for (int pass = 0; pass < numPasses; pass++)
            {
                // Set step size
                distanceFieldCompute.SetInt("StepSize", stepSize);

                // Set input/output buffers (ping-pong)
                RenderTexture inputBuffer = useBufferA ? distanceFieldA : distanceFieldB;
                RenderTexture outputBuffer = useBufferA ? distanceFieldB : distanceFieldA;

                distanceFieldCompute.SetTexture(jumpFloodKernel, "DistanceFieldA", inputBuffer);
                distanceFieldCompute.SetTexture(jumpFloodKernel, "DistanceFieldB", outputBuffer);

                // Set dimensions
                distanceFieldCompute.SetInt("MapWidth", textureManager.MapWidth);
                distanceFieldCompute.SetInt("MapHeight", textureManager.MapHeight);

                // Dispatch
                int threadGroupsX = (textureManager.MapWidth + THREAD_GROUP_SIZE - 1) / THREAD_GROUP_SIZE;
                int threadGroupsY = (textureManager.MapHeight + THREAD_GROUP_SIZE - 1) / THREAD_GROUP_SIZE;

                ArchonLogger.Log($"BorderDistanceFieldGenerator: JFA pass {pass+1}/{numPasses} (stepSize={stepSize})", "map_rendering");
                distanceFieldCompute.Dispatch(jumpFloodKernel, threadGroupsX, threadGroupsY, 1);

                // Halve step size for next pass
                stepSize = Mathf.Max(1, stepSize / 2);

                // Flip buffers
                useBufferA = !useBufferA;
            }

            ArchonLogger.Log($"BorderDistanceFieldGenerator: All JFA passes complete (useBufferA={useBufferA})", "map_rendering");

            // After all passes, result is in the "output" buffer from last iteration
            // Copy it back to DistanceFieldA for finalize pass
            if (!useBufferA)
            {
                ArchonLogger.Log("BorderDistanceFieldGenerator: Blitting result from B to A", "map_rendering");
                Graphics.Blit(distanceFieldB, distanceFieldA);
            }
        }

        private void RunFinalizePass(int outputChannel)
        {
            string channelName = outputChannel == 0 ? "R (country)" : "G (province)";
            ArchonLogger.Log($"BorderDistanceFieldGenerator: Starting finalize pass (output to {channelName} channel)", "map_rendering");

            // Set input buffer
            distanceFieldCompute.SetTexture(finalizeKernel, "DistanceFieldA", distanceFieldA);

            // Set output texture (BorderTexture in MapTextureManager)
            distanceFieldCompute.SetTexture(finalizeKernel, "BorderDistanceTexture", textureManager.BorderTexture);

            // Set output channel (0 = R, 1 = G)
            distanceFieldCompute.SetInt("OutputChannel", outputChannel);

            // Set dimensions
            distanceFieldCompute.SetInt("MapWidth", textureManager.MapWidth);
            distanceFieldCompute.SetInt("MapHeight", textureManager.MapHeight);

            // Dispatch
            int threadGroupsX = (textureManager.MapWidth + THREAD_GROUP_SIZE - 1) / THREAD_GROUP_SIZE;
            int threadGroupsY = (textureManager.MapHeight + THREAD_GROUP_SIZE - 1) / THREAD_GROUP_SIZE;

            ArchonLogger.Log($"BorderDistanceFieldGenerator: Dispatching finalize pass ({threadGroupsX}x{threadGroupsY} thread groups)", "map_rendering");
            distanceFieldCompute.Dispatch(finalizeKernel, threadGroupsX, threadGroupsY, 1);
            ArchonLogger.Log($"BorderDistanceFieldGenerator: Finalize pass complete for {channelName} channel)", "map_rendering");

            // DEBUG: Sample border texture to verify distance values are being written
            if (outputChannel == 0) // After country borders pass
            {
                // Known test coordinates: Castile (2751) at pixel (2767, 711)
                // Should have a country border nearby
                RenderTexture.active = textureManager.BorderTexture;
                Texture2D samplePixel = new Texture2D(1, 1, TextureFormat.RGFloat, false);
                samplePixel.ReadPixels(new Rect(2767, 711, 1, 1), 0, 0);
                samplePixel.Apply();
                RenderTexture.active = null;

                Color pixel = samplePixel.GetPixel(0, 0);
                float countryDist = pixel.r; // Normalized distance [0,1]
                float provinceDist = pixel.g;

                ArchonLogger.Log($"BorderDistanceFieldGenerator: BorderTexture at (2767,711) - Country dist={countryDist:F4}, Province dist={provinceDist:F4} (expect <1.0 if near border)", "map_rendering");
                UnityEngine.Object.Destroy(samplePixel);
            }
        }

        private void ReleaseBuffers()
        {
            ArchonLogger.Log("BorderDistanceFieldGenerator: Releasing temporary buffers", "map_rendering");
            distanceFieldA?.Release();
            distanceFieldB?.Release();
            distanceFieldA = null;
            distanceFieldB = null;
        }

        void OnDestroy()
        {
            ReleaseBuffers();
        }

        [ContextMenu("Test Distance Field Generation")]
        public void TestGeneration()
        {
            if (textureManager == null)
            {
                textureManager = GetComponent<MapTextureManager>();
            }

            GenerateDistanceField();
            ArchonLogger.Log("BorderDistanceFieldGenerator: Test generation complete", "map_rendering");
        }
    }
}
