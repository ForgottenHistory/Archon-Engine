using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;
using Core.Systems;
using Map.Rendering.Border;
using ProvinceSystemType = Core.Systems.ProvinceSystem;
using Core.Queries;
using Core.Modding;

namespace Map.Rendering
{
    /// <summary>
    /// Coordinator for border rendering systems.
    /// Delegates to registry-based renderers (IBorderRenderer implementations).
    ///
    /// Available renderers: None, DistanceField, PixelPerfect, MeshGeometry
    /// Rendering mode is set via VisualStyleConfiguration.
    /// </summary>
    public class BorderComputeDispatcher : MonoBehaviour
    {
        // Loaded via ModLoader (passed to BorderShaderManager)
        private ComputeShader borderDetectionCompute;
        private ComputeShader borderCurveRasterizerCompute;
        private ComputeShader borderSDFCompute;

        [Header("Border Settings")]
        [Tooltip("Which borders to show (set via VisualStyles for runtime control)")]
        [SerializeField] private BorderMode borderMode = BorderMode.Dual;
        [SerializeField] private bool autoUpdateBorders = true;

        // Rendering mode is set via VisualStyleConfiguration (single source of truth)
        private BorderRenderingMode renderingMode = BorderRenderingMode.ShaderDistanceField;

        public BorderMode CurrentBorderMode => borderMode;

        [Header("Debug")]
        [SerializeField] private bool logPerformance = false;

        // Thread group sizes (must match compute shader)
        private const int THREAD_GROUP_SIZE = 8;

        // Core references
        private MapTextureManager textureManager;
        private BorderDistanceFieldGenerator distanceFieldGenerator;
        private ProvinceSystemType provinceSystem;
        private CountrySystem countrySystem;
        private bool smoothBordersInitialized = false;

        // Helper classes
        private BorderShaderManager shaderManager;
        private BorderParameterBinder parameterBinder;

        // Pixel-perfect mode parameters (set via VisualStyleConfiguration)
        private int pixelPerfectCountryThickness = 1;
        private int pixelPerfectProvinceThickness = 0;
        private float pixelPerfectAntiAliasing = 0f;

        private BorderDebugUtility debugUtility;

        // Indexed border update (runtime only — avoids full-map dispatch)
        private ComputeShader updateBorderByIndexCompute;
        private int updateBorderByIndexKernel;
        private ComputeBuffer indexedPixelCoordsBuffer;
        private ComputeBuffer indexedPixelOffsetsBuffer;
        private ComputeBuffer indexedPixelCountsBuffer;
        private ComputeBuffer[] indexedChangedBuffers = new ComputeBuffer[2];
        private ComputeBuffer[] indexedDispatchOffsetsBuffers = new ComputeBuffer[2];
        private int indexedActiveBuffer;
        private uint[] indexedChangedData;
        private uint[] indexedDispatchOffsetsData;
        private int indexedChangedCapacity;
        private uint[] cpuPixelCounts; // for dispatch offset calculation
        private bool hasIndexedBorderSupport;
        private const int INDEXED_THREAD_GROUP_SIZE = 64;

        // Border renderers (managed directly, no external registry needed)
        private Dictionary<string, IBorderRenderer> borderRenderers = new Dictionary<string, IBorderRenderer>();
        private IBorderRenderer activeBorderRenderer;

        private bool isInitialized = false;

        /// <summary>
        /// Initialize the border system. Called by ArchonEngine during controlled initialization.
        /// </summary>
        public void Initialize()
        {
            if (isInitialized) return;

            // Load compute shaders via ModLoader (mods first, then Resources)
            if (borderDetectionCompute == null)
            {
                borderDetectionCompute = ModLoader.LoadAssetWithFallback<ComputeShader>(
                    "BorderDetection",
                    "Shaders/BorderDetection"
                );
            }
            if (borderCurveRasterizerCompute == null)
            {
                borderCurveRasterizerCompute = ModLoader.LoadAssetWithFallback<ComputeShader>(
                    "BorderCurveRasterizer",
                    "Shaders/BorderCurveRasterizer"
                );
            }
            if (borderSDFCompute == null)
            {
                borderSDFCompute = ModLoader.LoadAssetWithFallback<ComputeShader>(
                    "BorderSDF",
                    "Shaders/BorderSDF"
                );
            }

            // Initialize helper classes
            shaderManager = new BorderShaderManager(logPerformance);
            parameterBinder = new BorderParameterBinder();

            // Debug: Check if compute shaders are assigned
            ArchonLogger.Log($"BorderComputeDispatcher.Initialize: borderDetectionCompute={borderDetectionCompute != null}, borderCurveRasterizerCompute={borderCurveRasterizerCompute != null}, borderSDFCompute={borderSDFCompute != null}", "map_initialization");

            // Initialize shaders
            shaderManager.InitializeKernels(borderDetectionCompute, borderCurveRasterizerCompute, borderSDFCompute);

            // Debug: Check if initialization succeeded
            ArchonLogger.Log($"BorderComputeDispatcher.Initialize: shaderManager.IsInitialized()={shaderManager.IsInitialized()}", "map_initialization");

            // Sync serialized parameters to parameter binder
            SyncParametersToHelper();

            isInitialized = true;
        }

        void Update()
        {
            // Delegate per-frame rendering to the active renderer from registry
            // This supports both built-in and custom renderers that need per-frame updates
            var activeRenderer = GetActiveBorderRenderer();
            if (activeRenderer != null && activeRenderer.RequiresPerFrameUpdate)
            {
                activeRenderer.OnRenderFrame();
            }
        }

        /// <summary>
        /// Sync Unity serialized fields to parameter binder
        /// NOTE: Visual parameters now come from VisualStyleConfiguration
        /// </summary>
        private void SyncParametersToHelper()
        {
            if (parameterBinder == null) return;

            parameterBinder.BorderMode = borderMode;
            parameterBinder.AutoUpdateBorders = autoUpdateBorders;
            parameterBinder.RenderingMode = renderingMode;
        }

        /// <summary>
        /// Set the texture manager reference
        /// </summary>
        public void SetTextureManager(MapTextureManager manager)
        {
            textureManager = manager;

            // Initialize debug utility once texture manager is set
            debugUtility = new BorderDebugUtility(textureManager, logPerformance);
        }

        /// <summary>
        /// Initialize border rendering system with specific mode.
        /// Only creates the renderer needed for the selected mode.
        /// MUST be called after AdjacencySystem, ProvinceSystem, and CountrySystem are ready.
        /// </summary>
        public void InitializeBorders(BorderRenderingMode mode, AdjacencySystem adjacencySystem, ProvinceSystemType provinceSystem, CountrySystem countrySystem, ProvinceMapping provinceMapping, Transform mapPlaneTransform = null)
        {
            if (textureManager == null)
            {
                ArchonLogger.LogError("BorderComputeDispatcher: Cannot initialize borders - texture manager is null", "map_initialization");
                return;
            }

            if (adjacencySystem == null || provinceSystem == null || countrySystem == null || provinceMapping == null)
            {
                ArchonLogger.LogError("BorderComputeDispatcher: Cannot initialize borders - missing dependencies", "map_initialization");
                return;
            }

            float startTime = Time.realtimeSinceStartup;

            renderingMode = mode;
            this.provinceSystem = provinceSystem;
            this.countrySystem = countrySystem;

            // Initialize distance field generator if needed
            if (mode == BorderRenderingMode.ShaderDistanceField)
            {
                if (distanceFieldGenerator == null)
                {
                    distanceFieldGenerator = GetComponent<BorderDistanceFieldGenerator>();
                }
                if (distanceFieldGenerator != null)
                {
                    distanceFieldGenerator.SetTextureManager(textureManager);
                }
            }

            // Create only the renderer we need
            CreateRenderer(mode, adjacencySystem, provinceSystem, countrySystem, provinceMapping, mapPlaneTransform);

            smoothBordersInitialized = true;

            // Bind border textures to material
            var mapPlane = GameObject.Find("MapPlane");
            if (mapPlane != null)
            {
                var meshRenderer = mapPlane.GetComponent<MeshRenderer>();
                if (meshRenderer != null && textureManager?.DynamicTextures != null)
                {
                    textureManager.DynamicTextures.BindToMaterial(meshRenderer.sharedMaterial);
                    meshRenderer.sharedMaterial.SetInt("_BorderRenderingMode", GetShaderModeValue(mode));
                }
            }

            float elapsed = (Time.realtimeSinceStartup - startTime) * 1000f;
            ArchonLogger.Log($"BorderComputeDispatcher: Initialized {mode} renderer in {elapsed:F0}ms", "map_initialization");
        }

        /// <summary>
        /// Create and initialize only the specific renderer needed.
        /// </summary>
        private void CreateRenderer(BorderRenderingMode mode, AdjacencySystem adjacencySystem, ProvinceSystemType provinceSystem,
            CountrySystem countrySystem, ProvinceMapping provinceMapping, Transform mapPlaneTransform)
        {
            string rendererId = MapRenderingModeToId(mode);
            if (borderRenderers.ContainsKey(rendererId)) return;

            var context = new BorderRendererContext
            {
                AdjacencySystem = adjacencySystem,
                ProvinceSystem = provinceSystem,
                CountrySystem = countrySystem,
                ProvinceMapping = provinceMapping,
                MapPlaneTransform = mapPlaneTransform,
                BorderDetectionCompute = borderDetectionCompute,
                BorderSDFCompute = borderSDFCompute
            };

            IBorderRenderer renderer = mode switch
            {
                BorderRenderingMode.None => new NoneBorderRenderer(),
                BorderRenderingMode.ShaderDistanceField => new DistanceFieldBorderRenderer(distanceFieldGenerator),
                BorderRenderingMode.ShaderPixelPerfect => new PixelPerfectBorderRenderer(borderDetectionCompute),
                BorderRenderingMode.MeshGeometry => new MeshGeometryBorderRenderer(),
                _ => new NoneBorderRenderer()
            };

            renderer.Initialize(textureManager, context);
            borderRenderers[rendererId] = renderer;
            activeBorderRenderer = renderer;

            ArchonLogger.Log($"BorderComputeDispatcher: Created {rendererId} renderer", "map_initialization");
        }

        /// <summary>
        /// Get the currently active border renderer.
        /// </summary>
        public IBorderRenderer GetActiveBorderRenderer()
        {
            if (activeBorderRenderer != null)
                return activeBorderRenderer;

            string rendererId = MapRenderingModeToId(renderingMode);
            borderRenderers.TryGetValue(rendererId, out var renderer);
            return renderer;
        }

        /// <summary>
        /// Map BorderRenderingMode enum to renderer ID string.
        /// </summary>
        private static string MapRenderingModeToId(BorderRenderingMode mode)
        {
            return mode switch
            {
                BorderRenderingMode.None => "None",
                BorderRenderingMode.ShaderDistanceField => "DistanceField",
                BorderRenderingMode.ShaderPixelPerfect => "PixelPerfect",
                BorderRenderingMode.MeshGeometry => "MeshGeometry",
                _ => "DistanceField"
            };
        }

        /// <summary>
        /// Set the active border renderer by ID.
        /// </summary>
        public void SetActiveBorderRenderer(string rendererId, ProvinceQueries provinceQueries = null)
        {
            if (!borderRenderers.TryGetValue(rendererId, out var renderer))
            {
                ArchonLogger.LogWarning($"BorderComputeDispatcher: Renderer '{rendererId}' not found", "map_rendering");
                return;
            }

            activeBorderRenderer = renderer;
            renderer.GenerateBorders(new BorderGenerationParams { Mode = borderMode, ProvinceQueries = provinceQueries });

            ArchonLogger.Log($"BorderComputeDispatcher: Set active renderer to '{rendererId}'", "map_rendering");
        }

        /// <summary>
        /// Convert BorderRenderingMode enum to shader integer value
        /// Shader values: 0=None, 1=DistanceField, 2=PixelPerfect, 3=MeshGeometry
        /// </summary>
        private int GetShaderModeValue(BorderRenderingMode mode)
        {
            switch (mode)
            {
                case BorderRenderingMode.None: return 0;
                case BorderRenderingMode.ShaderDistanceField: return 1;
                case BorderRenderingMode.ShaderPixelPerfect: return 2;
                case BorderRenderingMode.MeshGeometry: return 3;
                default: return 0;
            }
        }

        /// <summary>
        /// Set border rendering mode and regenerate borders.
        /// </summary>
        public void SetBorderRenderingMode(BorderRenderingMode mode)
        {
            if (renderingMode == mode)
                return;

            renderingMode = mode;
            parameterBinder.RenderingMode = mode;

            if (!smoothBordersInitialized)
                return;

            // Update shader mode on material
            var mapPlane = GameObject.Find("MapPlane");
            if (mapPlane != null)
            {
                var meshRenderer = mapPlane.GetComponent<MeshRenderer>();
                if (meshRenderer != null)
                {
                    int shaderMode = GetShaderModeValue(mode);
                    meshRenderer.sharedMaterial.SetInt("_BorderRenderingMode", shaderMode);
                }
            }

            // Get renderer and generate borders
            string rendererId = MapRenderingModeToId(mode);
            if (!borderRenderers.TryGetValue(rendererId, out var renderer))
            {
                ArchonLogger.LogError($"BorderComputeDispatcher: Renderer '{rendererId}' not found", "map_rendering");
                return;
            }

            activeBorderRenderer = renderer;
            renderer.GenerateBorders(new BorderGenerationParams { Mode = borderMode });

            ArchonLogger.Log($"BorderComputeDispatcher: Switched to {mode} ({rendererId})", "map_rendering");
        }

        /// <summary>
        /// Generate pixel-perfect borders using DetectDualBorders kernel
        /// Writes to PixelPerfectBorderTexture (R=country, G=province)
        /// </summary>
        public void GeneratePixelPerfectBorders()
        {
            if (!shaderManager.IsInitialized())
            {
                ArchonLogger.LogWarning("BorderComputeDispatcher: Cannot generate pixel-perfect borders - shaders not initialized", "map_rendering");
                return;
            }

            if (textureManager == null || textureManager.PixelPerfectBorderTexture == null)
            {
                ArchonLogger.LogWarning("BorderComputeDispatcher: Cannot generate pixel-perfect borders - texture not available", "map_rendering");
                return;
            }

            var shader = shaderManager.BorderDetectionShader;
            int kernel = shaderManager.DetectDualBordersKernel;

            // Set map dimensions (required by compute shader)
            shader.SetInt("MapWidth", textureManager.MapWidth);
            shader.SetInt("MapHeight", textureManager.MapHeight);

            // Set border thickness parameters from serialized fields
            shader.SetInt("CountryBorderThickness", pixelPerfectCountryThickness);
            shader.SetInt("ProvinceBorderThickness", pixelPerfectProvinceThickness);
            shader.SetFloat("BorderAntiAliasing", pixelPerfectAntiAliasing);

            // Bind textures
            shader.SetTexture(kernel, "ProvinceIDTexture", textureManager.ProvinceIDTexture);
            shader.SetTexture(kernel, "ProvinceOwnerTexture", textureManager.ProvinceOwnerTexture);
            shader.SetTexture(kernel, "DualBorderTexture", textureManager.PixelPerfectBorderTexture);

            // Dispatch compute shader
            int threadGroupsX = Mathf.CeilToInt(textureManager.MapWidth / (float)THREAD_GROUP_SIZE);
            int threadGroupsY = Mathf.CeilToInt(textureManager.MapHeight / (float)THREAD_GROUP_SIZE);
            shader.Dispatch(kernel, threadGroupsX, threadGroupsY, 1);

            ArchonLogger.Log("BorderComputeDispatcher: Generated pixel-perfect borders", "map_rendering");
        }

        /// <summary>
        /// Set pixel index for indexed border updates. Called once at load time.
        /// Shares the same pixel index data as OwnerTextureDispatcher.
        /// </summary>
        public void SetPixelIndex(uint[] pixelCoords, uint[] offsets, uint[] counts)
        {
            indexedPixelCoordsBuffer?.Release();
            indexedPixelOffsetsBuffer?.Release();
            indexedPixelCountsBuffer?.Release();

            if (pixelCoords.Length == 0)
            {
                hasIndexedBorderSupport = false;
                return;
            }

            updateBorderByIndexCompute = ModLoader.LoadAssetWithFallback<ComputeShader>(
                "UpdateBorderByIndex", "Shaders/UpdateBorderByIndex");

            if (updateBorderByIndexCompute == null)
            {
                ArchonLogger.LogWarning("BorderComputeDispatcher: UpdateBorderByIndex compute shader not found", "map_rendering");
                hasIndexedBorderSupport = false;
                return;
            }

            updateBorderByIndexKernel = updateBorderByIndexCompute.FindKernel("UpdateBorderByIndex");

            indexedPixelCoordsBuffer = new ComputeBuffer(pixelCoords.Length, sizeof(uint));
            indexedPixelCoordsBuffer.SetData(pixelCoords);

            indexedPixelOffsetsBuffer = new ComputeBuffer(offsets.Length, sizeof(uint));
            indexedPixelOffsetsBuffer.SetData(offsets);

            indexedPixelCountsBuffer = new ComputeBuffer(counts.Length, sizeof(uint));
            indexedPixelCountsBuffer.SetData(counts);

            cpuPixelCounts = counts;
            hasIndexedBorderSupport = true;

            ArchonLogger.Log($"BorderComputeDispatcher: Indexed border support initialized ({pixelCoords.Length:N0} pixel entries)", "map_initialization");
        }

        /// <summary>
        /// Regenerate borders using the active renderer.
        /// </summary>
        public void DetectBorders()
        {
            var renderer = GetActiveBorderRenderer();
            if (renderer != null)
            {
                renderer.GenerateBorders(new BorderGenerationParams { Mode = borderMode });
            }
        }

        /// <summary>
        /// Indexed border update: queues compute shader dispatch into a CommandBuffer
        /// for only pixels of changed provinces + neighbors. Non-blocking.
        /// Falls back to full DetectBorders if indexed support not available.
        /// </summary>
        public void DetectBordersIndexed(ushort[] changedProvinces, CommandBuffer cmd)
        {
            if (!hasIndexedBorderSupport || renderingMode != BorderRenderingMode.ShaderPixelPerfect)
            {
                DetectBorders();
                return;
            }

            if (changedProvinces == null || changedProvinces.Length == 0) return;

            int numChanged = changedProvinces.Length;
            EnsureIndexedChangedBuffers(numChanged);

            uint totalPixels = 0;
            for (int i = 0; i < numChanged; i++)
            {
                ushort pid = changedProvinces[i];
                indexedChangedData[i] = (uint)pid;
                indexedDispatchOffsetsData[i] = totalPixels;
                totalPixels += (pid < cpuPixelCounts.Length) ? cpuPixelCounts[pid] : 0;
            }

            if (totalPixels == 0) return;

            indexedActiveBuffer = 1 - indexedActiveBuffer;
            var changedBuf = indexedChangedBuffers[indexedActiveBuffer];
            var offsetsBuf = indexedDispatchOffsetsBuffers[indexedActiveBuffer];

            changedBuf.SetData(indexedChangedData, 0, 0, numChanged);
            offsetsBuf.SetData(indexedDispatchOffsetsData, 0, 0, numChanged);

            cmd.SetComputeTextureParam(updateBorderByIndexCompute, updateBorderByIndexKernel, "ProvinceIDTexture", textureManager.ProvinceIDTexture);
            cmd.SetComputeTextureParam(updateBorderByIndexCompute, updateBorderByIndexKernel, "ProvinceOwnerTexture", textureManager.ProvinceOwnerTexture);
            cmd.SetComputeTextureParam(updateBorderByIndexCompute, updateBorderByIndexKernel, "DualBorderTexture", textureManager.PixelPerfectBorderTexture);
            cmd.SetComputeBufferParam(updateBorderByIndexCompute, updateBorderByIndexKernel, "PixelCoords", indexedPixelCoordsBuffer);
            cmd.SetComputeBufferParam(updateBorderByIndexCompute, updateBorderByIndexKernel, "ProvincePixelOffsets", indexedPixelOffsetsBuffer);
            cmd.SetComputeBufferParam(updateBorderByIndexCompute, updateBorderByIndexKernel, "ProvincePixelCounts", indexedPixelCountsBuffer);
            cmd.SetComputeBufferParam(updateBorderByIndexCompute, updateBorderByIndexKernel, "ChangedProvinces", changedBuf);
            cmd.SetComputeBufferParam(updateBorderByIndexCompute, updateBorderByIndexKernel, "DispatchOffsets", offsetsBuf);
            cmd.SetComputeIntParam(updateBorderByIndexCompute, "NumChangedProvinces", numChanged);
            cmd.SetComputeIntParam(updateBorderByIndexCompute, "MapWidth", textureManager.MapWidth);
            cmd.SetComputeIntParam(updateBorderByIndexCompute, "MapHeight", textureManager.MapHeight);
            cmd.SetComputeIntParam(updateBorderByIndexCompute, "CountryBorderThickness", pixelPerfectCountryThickness);
            cmd.SetComputeIntParam(updateBorderByIndexCompute, "ProvinceBorderThickness", pixelPerfectProvinceThickness);
            cmd.SetComputeFloatParam(updateBorderByIndexCompute, "BorderAntiAliasing", pixelPerfectAntiAliasing);

            int threadGroups = ((int)totalPixels + INDEXED_THREAD_GROUP_SIZE - 1) / INDEXED_THREAD_GROUP_SIZE;
            cmd.DispatchCompute(updateBorderByIndexCompute, updateBorderByIndexKernel, threadGroups, 1, 1);
        }

        private void EnsureIndexedChangedBuffers(int needed)
        {
            if (indexedChangedCapacity >= needed) return;

            for (int i = 0; i < 2; i++)
            {
                indexedChangedBuffers[i]?.Release();
                indexedDispatchOffsetsBuffers[i]?.Release();
            }

            indexedChangedCapacity = Mathf.Max(needed, 64);
            for (int i = 0; i < 2; i++)
            {
                indexedChangedBuffers[i] = new ComputeBuffer(indexedChangedCapacity, sizeof(uint));
                indexedDispatchOffsetsBuffers[i] = new ComputeBuffer(indexedChangedCapacity, sizeof(uint));
            }
            indexedChangedData = new uint[indexedChangedCapacity];
            indexedDispatchOffsetsData = new uint[indexedChangedCapacity];
        }

        /// <summary>
        /// Update borders when provinces change
        /// </summary>
        public void UpdateBorders()
        {
            if (autoUpdateBorders)
            {
                DetectBorders();
            }
        }

        /// <summary>
        /// Set border mode
        /// </summary>
        public void SetBorderMode(BorderMode mode)
        {
            borderMode = mode;
            parameterBinder.BorderMode = mode;

            if (autoUpdateBorders)
            {
                DetectBorders();
            }
        }

        /// <summary>
        /// Set pixel-perfect mode parameters (called from VisualStyleManager)
        /// </summary>
        /// <param name="countryThickness">Country border thickness in pixels (0 = 1px thin)</param>
        /// <param name="provinceThickness">Province border thickness in pixels (0 = 1px thin)</param>
        /// <param name="antiAliasing">Anti-aliasing gradient width (0 = sharp, 1-2 = smooth)</param>
        public void SetPixelPerfectParameters(int countryThickness, int provinceThickness, float antiAliasing)
        {
            pixelPerfectCountryThickness = Mathf.Clamp(countryThickness, 0, 5);
            pixelPerfectProvinceThickness = Mathf.Clamp(provinceThickness, 0, 5);
            pixelPerfectAntiAliasing = Mathf.Clamp(antiAliasing, 0f, 2f);

            // Pass parameters to the renderer if it's a PixelPerfectBorderRenderer
            if (activeBorderRenderer is PixelPerfectBorderRenderer pixelPerfectRenderer)
            {
                pixelPerfectRenderer.SetParameters(pixelPerfectCountryThickness, pixelPerfectProvinceThickness, pixelPerfectAntiAliasing);
            }

            // Regenerate borders if in pixel-perfect mode
            if (renderingMode == BorderRenderingMode.ShaderPixelPerfect && autoUpdateBorders)
            {
                DetectBorders();
            }
        }

        /// <summary>
        /// Clear all borders
        /// </summary>
        public void ClearBorders()
        {
            debugUtility?.ClearBorders();
        }

        /// <summary>
        /// Fill borders with white for debugging
        /// </summary>
        public void DebugFillBordersWhite()
        {
            debugUtility?.FillBordersWhite();
        }

        /// <summary>
        /// Toggle border mode for testing
        /// </summary>
        [ContextMenu("Toggle Border Mode")]
        public void ToggleBorderMode()
        {
            borderMode = (BorderMode)(((int)borderMode + 1) % 5);
            SyncParametersToHelper();
            DetectBorders();
            ArchonLogger.Log($"BorderComputeDispatcher: Toggled to border mode: {borderMode}", "map_rendering");
        }

        /// <summary>
        /// Force border mode to province for testing
        /// </summary>
        [ContextMenu("Force Province Mode")]
        public void ForceProvinceMode()
        {
            SetBorderMode(BorderMode.Province);
            ArchonLogger.Log($"BorderComputeDispatcher: Forced border mode to {borderMode}", "map_rendering");
        }

        /// <summary>
        /// Generate debug texture showing curve points
        /// </summary>
        public Texture2D GenerateCurveDebugTexture()
        {
            return debugUtility?.GenerateCurveDebugTexture();
        }

#if UNITY_EDITOR
        /// <summary>
        /// Save curve points debug visualization
        /// </summary>
        [ContextMenu("DEBUG: Save Curve Points Visualization")]
        private void DebugSaveCurvePoints()
        {
            debugUtility?.SaveCurvePointsDebug();
        }

        /// <summary>
        /// Benchmark border detection performance
        /// </summary>
        [ContextMenu("Benchmark Border Detection")]
        private void BenchmarkBorderDetection()
        {
            debugUtility?.BenchmarkBorderDetection((mode) =>
            {
                borderMode = mode;
                SyncParametersToHelper();
                DetectBorders();
            });
        }
#endif

        /// <summary>
        /// Clean up GPU resources
        /// </summary>
        void OnDestroy()
        {
            // Dispose all renderers
            foreach (var renderer in borderRenderers.Values)
            {
                renderer?.Dispose();
            }
            borderRenderers.Clear();

            // Release indexed border update buffers
            indexedPixelCoordsBuffer?.Release();
            indexedPixelOffsetsBuffer?.Release();
            indexedPixelCountsBuffer?.Release();
            for (int i = 0; i < 2; i++)
            {
                indexedChangedBuffers[i]?.Release();
                indexedDispatchOffsetsBuffers[i]?.Release();
            }
        }
    }
}
