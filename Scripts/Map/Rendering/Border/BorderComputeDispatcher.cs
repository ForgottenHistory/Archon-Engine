using UnityEngine;
using UnityEngine.Rendering;
using Core.Systems;
using Map.Rendering.Border;
using ProvinceSystemType = Core.Systems.ProvinceSystem;
using Core.Queries;
using Core.Modding;

namespace Map.Rendering
{
    /// <summary>
    /// Coordinator for border rendering systems
    /// Orchestrates multiple rendering modes: distance field, mesh geometry, and pixel-perfect
    ///
    /// REFACTORED: Now uses specialized helper classes for single responsibility
    /// - BorderShaderManager: Compute shader loading and kernel management
    /// - BorderParameterBinder: Rendering parameters and shader binding
    /// - BorderStyleUpdater: Border style classification and updates
    /// - BorderDebugUtility: Debug utilities and benchmarking
    ///
    /// Architecture: Facade pattern - provides unified interface to border rendering subsystems
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

        [Header("Rendering Mode")]
        [Tooltip("How borders are rendered (set via VisualStyles for runtime control)")]
        [SerializeField] private BorderRenderingMode renderingMode = BorderRenderingMode.ShaderDistanceField;

        // NOTE: Visual parameters (colors, widths, distance field settings) are now
        // controlled via VisualStyleConfiguration - the single source of truth for visuals.
        // BorderComputeDispatcher only handles the technical rendering mode selection
        // and compute shader orchestration.

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

        // Smooth curve border system components
        private BorderCurveExtractor curveExtractor;
        private BorderCurveCache curveCache;
        private BorderMeshGenerator meshGenerator;
        private BorderMeshRenderer meshRenderer;
        private bool smoothBordersInitialized = false;

        // Helper classes (extracted for single responsibility)
        private BorderShaderManager shaderManager;
        private BorderParameterBinder parameterBinder;
        private BorderStyleUpdater styleUpdater;

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

        // Pluggable renderer support
        private bool renderersRegistered = false;
        private IBorderRenderer activeBorderRenderer;

        // Context for renderer initialization
        private BorderRendererContext rendererContext;

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
            // Render mesh-based borders every frame if enabled
            if (renderingMode == BorderRenderingMode.MeshGeometry && meshRenderer != null)
            {
                meshRenderer.RenderBorders();
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
        /// Initialize smooth curve-based border rendering system
        /// MUST be called after AdjacencySystem, ProvinceSystem, and CountrySystem are ready
        /// </summary>
        public void InitializeSmoothBorders(AdjacencySystem adjacencySystem, ProvinceSystemType provinceSystem, CountrySystem countrySystem, ProvinceMapping provinceMapping, Transform mapPlaneTransform = null)
        {
            if (textureManager == null)
            {
                ArchonLogger.LogError("BorderComputeDispatcher: Cannot initialize smooth borders - texture manager is null", "map_initialization");
                return;
            }

            if (adjacencySystem == null || provinceSystem == null || countrySystem == null || provinceMapping == null)
            {
                ArchonLogger.LogError("BorderComputeDispatcher: Cannot initialize smooth borders - missing dependencies", "map_initialization");
                return;
            }

            if (shaderManager.BorderCurveRasterizerShader == null)
            {
                ArchonLogger.LogWarning("BorderComputeDispatcher: Border curve rasterizer compute shader not assigned - smooth borders disabled", "map_initialization");
                return;
            }

            float startTime = Time.realtimeSinceStartup;

            // Store systems for border style updates
            this.provinceSystem = provinceSystem;
            this.countrySystem = countrySystem;

            // OPTIMIZATION: Only extract curves for MeshGeometry mode
            // ShaderDistanceField and ShaderPixelPerfect modes work directly from textures (GPU)
            if (renderingMode == BorderRenderingMode.MeshGeometry)
            {
                // Initialize curve extractor with province pixel lists
                curveExtractor = new BorderCurveExtractor(textureManager, adjacencySystem, provinceSystem, provinceMapping);

                // Extract all border curves (CPU intensive, done once at startup)
                ArchonLogger.Log("BorderComputeDispatcher: Extracting border curves for mesh rendering...", "map_initialization");
                var borderCurves = curveExtractor.ExtractAllBorders();

                // Initialize curve cache to store extracted curves
                curveCache = new BorderCurveCache();
                curveCache.Initialize(borderCurves);

                // Initialize style updater
                styleUpdater = new BorderStyleUpdater(curveCache, provinceSystem, countrySystem);

                // Update all border styles (classify as country vs province borders)
                styleUpdater.UpdateAllBorderStyles();

                // Initialize mesh generator and renderer
                meshGenerator = new BorderMeshGenerator(1.0f, textureManager.MapWidth, textureManager.MapHeight);
                meshRenderer = new BorderMeshRenderer(mapPlaneTransform);
            }
            else
            {
                ArchonLogger.Log("BorderComputeDispatcher: Skipping curve extraction (not needed for shader-based rendering)", "map_initialization");
            }

            // Initialize distance field generator (for ShaderDistanceField mode)
            // Note: BorderDistanceFieldGenerator is a MonoBehaviour, should already exist on GameObject
            if (distanceFieldGenerator == null)
            {
                distanceFieldGenerator = GetComponent<BorderDistanceFieldGenerator>();
                if (distanceFieldGenerator == null)
                {
                    ArchonLogger.LogWarning("BorderComputeDispatcher: BorderDistanceFieldGenerator component not found", "map_initialization");
                }
            }

            // Pass texture manager to distance field generator
            if (distanceFieldGenerator != null)
            {
                distanceFieldGenerator.SetTextureManager(textureManager);
            }

            smoothBordersInitialized = true;

            float elapsed = (Time.realtimeSinceStartup - startTime) * 1000f;
            ArchonLogger.Log($"BorderComputeDispatcher: Smooth borders initialized in {elapsed:F0}ms", "map_initialization");

            // Generate borders based on current rendering mode
            switch (renderingMode)
            {
                case BorderRenderingMode.ShaderDistanceField:
                    if (distanceFieldGenerator != null)
                        distanceFieldGenerator.GenerateDistanceField();
                    break;

                case BorderRenderingMode.MeshGeometry:
                    meshGenerator.GenerateBorderMeshes(curveCache);
                    meshRenderer.SetMeshes(meshGenerator.GetProvinceBorderMeshes(), meshGenerator.GetCountryBorderMeshes());
                    break;

                case BorderRenderingMode.ShaderPixelPerfect:
                    GeneratePixelPerfectBorders();
                    break;
            }

            // Bind the correct border textures to the material based on rendering mode
            Material mapMaterial = null;
            var mapPlane = GameObject.Find("MapPlane");
            if (mapPlane != null)
            {
                var renderer = mapPlane.GetComponent<MeshRenderer>();
                if (renderer != null)
                {
                    mapMaterial = renderer.sharedMaterial;
                }
            }

            if (mapMaterial != null && textureManager?.DynamicTextures != null)
            {
                // Bind both border textures - shader selects based on _BorderRenderingMode
                textureManager.DynamicTextures.BindToMaterial(mapMaterial);

                // Set shader mode: 0=None, 1=DistanceField, 2=PixelPerfect, 3=MeshGeometry
                int shaderMode = GetShaderModeValue(renderingMode);
                mapMaterial.SetInt("_BorderRenderingMode", shaderMode);

                ArchonLogger.Log($"BorderComputeDispatcher: Bound border textures, mode={renderingMode} (shader={shaderMode})", "map_initialization");
            }

            // Register ENGINE default renderers with registry
            RegisterDefaultRenderers(adjacencySystem, provinceSystem, countrySystem, provinceMapping, mapPlaneTransform);
        }

        /// <summary>
        /// Register ENGINE's default border renderers with MapRendererRegistry.
        /// Called during smooth border initialization.
        /// GAME layer can register additional custom renderers via MapRendererRegistry.Instance.RegisterBorderRenderer().
        /// </summary>
        private void RegisterDefaultRenderers(AdjacencySystem adjacencySystem, ProvinceSystemType provinceSystem,
            CountrySystem countrySystem, ProvinceMapping provinceMapping, Transform mapPlaneTransform)
        {
            if (renderersRegistered) return;

            var registry = MapRendererRegistry.Instance;
            if (registry == null)
            {
                ArchonLogger.LogWarning("BorderComputeDispatcher: MapRendererRegistry not found, cannot register renderers", "map_initialization");
                return;
            }

            // Build context for renderer initialization
            rendererContext = new BorderRendererContext
            {
                AdjacencySystem = adjacencySystem,
                ProvinceSystem = provinceSystem,
                CountrySystem = countrySystem,
                ProvinceMapping = provinceMapping,
                MapPlaneTransform = mapPlaneTransform,
                BorderDetectionCompute = borderDetectionCompute,
                BorderSDFCompute = borderSDFCompute
            };

            // Register None renderer
            var noneRenderer = new NoneBorderRenderer();
            noneRenderer.Initialize(textureManager, rendererContext);
            registry.RegisterBorderRenderer(noneRenderer);

            // Register Distance Field renderer
            var distanceFieldRenderer = new DistanceFieldBorderRenderer(distanceFieldGenerator);
            distanceFieldRenderer.Initialize(textureManager, rendererContext);
            registry.RegisterBorderRenderer(distanceFieldRenderer);

            // Register Pixel Perfect renderer
            var pixelPerfectRenderer = new PixelPerfectBorderRenderer(borderDetectionCompute);
            pixelPerfectRenderer.Initialize(textureManager, rendererContext);
            registry.RegisterBorderRenderer(pixelPerfectRenderer);

            // Register Mesh Geometry renderer
            var meshGeometryRenderer = new MeshGeometryBorderRenderer();
            meshGeometryRenderer.Initialize(textureManager, rendererContext);
            registry.RegisterBorderRenderer(meshGeometryRenderer);

            // Set default based on current mode
            string defaultId = MapRendererRegistry.MapBorderModeToRendererId(renderingMode);
            registry.SetDefaultBorderRenderer(defaultId);

            // Initialize the registry
            registry.Initialize();

            renderersRegistered = true;
            ArchonLogger.Log($"BorderComputeDispatcher: Registered {4} ENGINE default border renderers", "map_initialization");
        }

        /// <summary>
        /// Get the currently active border renderer from registry.
        /// </summary>
        public IBorderRenderer GetActiveBorderRenderer()
        {
            if (activeBorderRenderer != null)
                return activeBorderRenderer;

            var registry = MapRendererRegistry.Instance;
            if (registry == null)
                return null;

            string rendererId = MapRendererRegistry.MapBorderModeToRendererId(renderingMode);
            return registry.GetBorderRenderer(rendererId);
        }

        /// <summary>
        /// Set the active border renderer by ID.
        /// Used by VisualStyleManager to switch renderers.
        /// </summary>
        public void SetActiveBorderRenderer(string rendererId, ProvinceQueries provinceQueries = null)
        {
            var registry = MapRendererRegistry.Instance;
            if (registry == null)
            {
                ArchonLogger.LogWarning("BorderComputeDispatcher: Cannot set active renderer - registry not found", "map_rendering");
                return;
            }

            var renderer = registry.GetBorderRenderer(rendererId);
            if (renderer == null)
            {
                ArchonLogger.LogWarning($"BorderComputeDispatcher: Renderer '{rendererId}' not found in registry", "map_rendering");
                return;
            }

            activeBorderRenderer = renderer;

            // Generate borders with the new renderer
            var parameters = new BorderGenerationParams
            {
                Mode = borderMode,
                ForceRegenerate = false,
                ProvinceQueries = provinceQueries
            };
            renderer.GenerateBorders(parameters);

            ArchonLogger.Log($"BorderComputeDispatcher: Set active border renderer to '{rendererId}'", "map_rendering");
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
        /// Set border rendering mode and regenerate borders
        /// </summary>
        public void SetBorderRenderingMode(BorderRenderingMode mode)
        {
            if (renderingMode == mode)
                return;

            renderingMode = mode;
            parameterBinder.RenderingMode = mode;

            if (!smoothBordersInitialized)
                return;

            // Find the map plane's material
            Material mapMaterial = null;
            var mapPlane = GameObject.Find("MapPlane");
            if (mapPlane != null)
            {
                var renderer = mapPlane.GetComponent<MeshRenderer>();
                if (renderer != null)
                {
                    mapMaterial = renderer.sharedMaterial;
                }
            }

            // Update shader mode value
            if (mapMaterial != null)
            {
                int shaderMode = GetShaderModeValue(mode);
                mapMaterial.SetInt("_BorderRenderingMode", shaderMode);
            }

            // Regenerate borders for new mode
            switch (mode)
            {
                case BorderRenderingMode.ShaderDistanceField:
                    if (distanceFieldGenerator != null)
                        distanceFieldGenerator.GenerateDistanceField();
                    break;

                case BorderRenderingMode.MeshGeometry:
                    if (meshGenerator != null && meshRenderer != null)
                    {
                        meshGenerator.GenerateBorderMeshes(curveCache);
                        meshRenderer.SetMeshes(meshGenerator.GetProvinceBorderMeshes(), meshGenerator.GetCountryBorderMeshes());
                    }
                    break;

                case BorderRenderingMode.ShaderPixelPerfect:
                    GeneratePixelPerfectBorders();
                    break;
            }

            ArchonLogger.Log($"BorderComputeDispatcher: Switched to {mode} rendering mode", "map_rendering");
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
        /// Detect and update borders (called per-frame or on-demand).
        /// Full-map dispatch — use DetectBordersIndexed for runtime incremental updates.
        /// </summary>
        public void DetectBorders()
        {
            // Only generate distance field if using ShaderDistanceField mode
            if (renderingMode == BorderRenderingMode.ShaderDistanceField)
            {
                if (distanceFieldGenerator != null)
                    distanceFieldGenerator.GenerateDistanceField();
            }
            else if (renderingMode == BorderRenderingMode.ShaderPixelPerfect)
            {
                GeneratePixelPerfectBorders();
            }
            // MeshGeometry mode doesn't need per-frame updates
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

            // Regenerate borders if in pixel-perfect mode
            if (renderingMode == BorderRenderingMode.ShaderPixelPerfect && autoUpdateBorders)
            {
                GeneratePixelPerfectBorders();
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
            if (curveCache != null)
            {
                curveCache.Clear();
            }

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
