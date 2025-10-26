using UnityEngine;
using Map.Rendering;
using Map.Loading;
using ParadoxParser.Bitmap;
using Core;
using Utils;
using static Map.Loading.ProvinceMapProcessor;

namespace Map.Rendering
{
    /// <summary>
    /// Handles population of map textures from province data
    /// Extracted from MapGenerator to follow single responsibility principle
    /// Manages conversion from bitmap data to GPU textures with simulation integration
    /// Architecture: Uses GPU compute shader for owner texture (NO CPU pixel ops)
    /// </summary>
    public class MapTexturePopulator : MonoBehaviour
    {
        [Header("Configuration")]
        [SerializeField] private bool logPopulationProgress = true;

        [Header("GPU Dispatchers")]
        [SerializeField] private OwnerTextureDispatcher ownerTextureDispatcher;
        [SerializeField] private ComputeShader populateProvinceIDCompute;

        /// <summary>
        /// Populate MapTextureManager textures using data from Core simulation systems
        /// This method integrates bitmap visual data with simulation layer province/country data
        /// </summary>
        public void PopulateWithSimulationData(ProvinceMapResult provinceResult, MapTextureManager textureManager, ProvinceMapping mapping, GameState gameState)
        {
            if (textureManager == null || mapping == null || gameState == null)
            {
                ArchonLogger.LogError("MapTexturePopulator: Cannot populate textures - missing dependencies", "map_rendering");
                return;
            }

            var pixelData = provinceResult.BMPData.GetPixelData();
            int width = provinceResult.BMPData.Width;
            int height = provinceResult.BMPData.Height;

            if (logPopulationProgress)
            {
                ArchonLogger.Log($"MapTexturePopulator: Starting texture population with simulation data for {width}x{height} map", "map_initialization");
            }

            // Get query interfaces for simulation data
            var provinceQueries = gameState.ProvinceQueries;
            var countryQueries = gameState.CountryQueries;

            // BUG FIX: provinceID from mapping is a DEFINITION ID, not runtime ID
            // Must use ProvinceRegistry.ExistsByDefinition() for validation
            var provinceRegistry = gameState.Registries?.Provinces;
            if (provinceRegistry == null)
            {
                ArchonLogger.LogError("MapTexturePopulator: GameState.Registries not set! Cannot validate provinces.", "map_initialization");
                return;
            }

            // Populate province ID, color, and owner textures from bitmap + simulation data
            int processedPixels = 0;
            int validProvinces = 0;

            // Create pixel arrays for batch operations (GPU-friendly)
            Color32[] provinceIDPixels = new Color32[width * height];
            Color32[] provinceColorPixels = new Color32[width * height];

            // Initialize with default (black = no province)
            for (int i = 0; i < provinceIDPixels.Length; i++)
            {
                provinceIDPixels[i] = new Color32(0, 0, 0, 255);
                provinceColorPixels[i] = new Color32(0, 0, 0, 255);
            }

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (pixelData.TryGetPixelRGB(x, y, out byte r, out byte g, out byte b))
                    {
                        processedPixels++;

                        // Create Color32 for province lookup
                        var pixelColor = new Color32(r, g, b, 255);

                        // Find province ID for this color using ProvinceMapping (returns DEFINITION ID)
                        ushort provinceID = mapping.GetProvinceByColor(pixelColor);

                        // Check if province exists by definition ID (from definition.csv/bitmap)
                        if (provinceID > 0 && provinceRegistry.ExistsByDefinition(provinceID))
                        {
                            validProvinces++;

                            int pixelIndex = y * width + x;

                            // Encode province ID into Color32 for batch write
                            provinceIDPixels[pixelIndex] = Province.ProvinceIDEncoder.PackProvinceID(provinceID);

                            // Set province color for visual display (from bitmap)
                            provinceColorPixels[pixelIndex] = pixelColor;

                            // Owner texture populated by GPU compute shader (see below)
                            // Architecture: NO CPU pixel ops for owner texture

                            // Add pixel to province mapping
                            mapping.AddPixelToProvince(provinceID, x, y);
                        }
                    }
                }
            }

            // DEBUG: Check what we wrote to the CPU array
            int testPixelIndex = 711 * width + 2767;
            var testColor = provinceIDPixels[testPixelIndex];
            ushort testProvinceID = Province.ProvinceIDEncoder.UnpackProvinceID(testColor);
            ArchonLogger.Log($"MapTexturePopulator: CPU array at [711, 2767] (index {testPixelIndex}) = province {testProvinceID} (R={testColor.r} G={testColor.g})", "map_initialization");

            // Also check what's at the Y-flipped location (what GPU might have after Graphics.Blit)
            int flippedPixelIndex = (height - 1 - 711) * width + 2767; // Y-flipped location
            var flippedColor = provinceIDPixels[flippedPixelIndex];
            ushort flippedProvinceID = Province.ProvinceIDEncoder.UnpackProvinceID(flippedColor);
            ArchonLogger.Log($"MapTexturePopulator: CPU array at Y-flipped [{height-1-711}, 2767] (index {flippedPixelIndex}) = province {flippedProvinceID} (R={flippedColor.r} G={flippedColor.g})", "map_initialization");

            // Batch-write to GPU using RenderTexture (architecture: GPU-native textures)
            PopulateProvinceIDTextureGPU(textureManager, width, height, provinceIDPixels);

            // DEBUG: Verify ProvinceIDTexture was populated correctly after blit
            ushort verifyProvinceID = textureManager.GetProvinceID(2767, 711);
            ArchonLogger.Log($"MapTexturePopulator: ProvinceIDTexture at pixel (2767,711) contains province ID {verifyProvinceID} AFTER blit (expected 2751 for Castile)", "map_initialization");

            PopulateProvinceColorTextureGPU(textureManager, width, height, provinceColorPixels);

            // Populate owner texture using GPU compute shader (architecture compliance: NO CPU pixel ops)
            if (ownerTextureDispatcher == null)
            {
                ownerTextureDispatcher = GetComponent<OwnerTextureDispatcher>();
                if (ownerTextureDispatcher == null)
                {
                    ownerTextureDispatcher = FindFirstObjectByType<OwnerTextureDispatcher>();
                }
            }

            if (ownerTextureDispatcher != null)
            {
                ArchonLogger.Log("MapTexturePopulator: Populating owner texture via GPU compute shader", "map_initialization");
                ownerTextureDispatcher.PopulateOwnerTexture(provinceQueries);

                // CRITICAL: Force GPU synchronization after owner texture population
                // OwnerTextureDispatcher.Dispatch() is async - fragment shader may try to read before GPU finishes writing
                // This forces CPU to wait for GPU completion before rendering
                var ownerSyncRequest = UnityEngine.Rendering.AsyncGPUReadback.Request(textureManager.ProvinceOwnerTexture);
                ownerSyncRequest.WaitForCompletion();

                if (logPopulationProgress)
                {
                    ArchonLogger.Log("MapTexturePopulator: Forced GPU sync on ProvinceOwnerTexture", "map_initialization");
                }
            }
            else
            {
                ArchonLogger.LogError("MapTexturePopulator: OwnerTextureDispatcher not found - cannot populate owner texture!", "map_initialization");
            }

            if (logPopulationProgress)
            {
                ArchonLogger.Log($"MapTexturePopulator: Populated texture manager with {width}x{height} province data from simulation layer", "map_initialization");
                ArchonLogger.Log($"MapTexturePopulator: Processed {processedPixels} pixels, {validProvinces} valid province pixels", "map_initialization");
            }
        }

        /// <summary>
        /// Populate MapTextureManager textures from province processing result
        /// Legacy method for standalone operation without simulation integration
        /// </summary>
        public void PopulateFromProvinceResult(ProvinceMapResult provinceResult, MapTextureManager textureManager, ProvinceMapping mapping)
        {
            if (textureManager == null || mapping == null)
            {
                ArchonLogger.LogError("MapTexturePopulator: Cannot populate textures - missing dependencies", "map_rendering");
                return;
            }

            var pixelData = provinceResult.BMPData.GetPixelData();
            int width = provinceResult.BMPData.Width;
            int height = provinceResult.BMPData.Height;

            if (logPopulationProgress)
            {
                ArchonLogger.Log($"MapTexturePopulator: Starting texture population from province result for {width}x{height} map", "map_initialization");
            }

            // Populate province ID and color textures from BMP data
            int processedPixels = 0;
            int validProvinces = 0;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (pixelData.TryGetPixelRGB(x, y, out byte r, out byte g, out byte b))
                    {
                        processedPixels++;

                        // Create Color32 for province lookup
                        var pixelColor = new Color32(r, g, b, 255);

                        // Find province ID for this color using ProvinceMapping's public method
                        ushort provinceID = mapping.GetProvinceByColor(pixelColor);

                        if (provinceID > 0) // Valid province ID
                        {
                            validProvinces++;

                            // TODO: Set province ID using GPU compute shader instead of CPU
                            // Deprecated CPU method removed - use ProvinceMapProcessor compute shader
                            // textureManager.SetProvinceID(x, y, provinceID); // DEPRECATED

                            // Set province color for visual display
                            textureManager.SetProvinceColor(x, y, pixelColor);

                            // Add pixel to province mapping
                            mapping.AddPixelToProvince(provinceID, x, y);
                        }
                    }
                }
            }

            // Apply all texture changes
            textureManager.ApplyTextureChanges();

            if (logPopulationProgress)
            {
                ArchonLogger.Log($"MapTexturePopulator: Populated texture manager with {width}x{height} province data", "map_initialization");
                ArchonLogger.Log($"MapTexturePopulator: Processed {processedPixels} pixels, {validProvinces} valid province pixels", "map_initialization");
            }
        }

        /// <summary>
        /// Update texture manager with live simulation data changes
        /// Optimized method for runtime updates without full repopulation
        /// Architecture: Uses GPU compute shader for owner texture updates (NO CPU pixel ops)
        /// </summary>
        public void UpdateSimulationData(MapTextureManager textureManager, ProvinceMapping mapping, GameState gameState, ushort[] changedProvinces)
        {
            if (textureManager == null || mapping == null || gameState == null || changedProvinces == null)
            {
                ArchonLogger.LogError("MapTexturePopulator: Cannot update simulation data - missing dependencies", "map_rendering");
                return;
            }

            var provinceQueries = gameState.ProvinceQueries;

            // Architecture: Use GPU compute shader for owner texture population
            // NO CPU pixel-by-pixel operations (removed legacy SetProvinceOwner loop)
            if (ownerTextureDispatcher == null)
            {
                ownerTextureDispatcher = GetComponent<OwnerTextureDispatcher>();
                if (ownerTextureDispatcher == null)
                {
                    ownerTextureDispatcher = FindFirstObjectByType<OwnerTextureDispatcher>();
                }
            }

            if (ownerTextureDispatcher != null)
            {
                ArchonLogger.Log($"MapTexturePopulator: Updating owner texture for {changedProvinces.Length} changed provinces via GPU compute shader", "map_initialization");
                ownerTextureDispatcher.PopulateOwnerTexture(provinceQueries);
            }
            else
            {
                ArchonLogger.LogError("MapTexturePopulator: OwnerTextureDispatcher not found - cannot update owner texture!", "map_rendering");
            }
        }

        /// <summary>
        /// Populate ProvinceIDTexture RenderTexture using compute shader
        /// Architecture: CPU data → GPU buffer → compute shader → RenderTexture
        /// This ensures same coordinate system as OwnerTextureDispatcher compute shader
        /// </summary>
        private void PopulateProvinceIDTextureGPU(MapTextureManager textureManager, int width, int height, Color32[] pixels)
        {
            // Load compute shader if not assigned
            if (populateProvinceIDCompute == null)
            {
                #if UNITY_EDITOR
                string[] guids = UnityEditor.AssetDatabase.FindAssets("PopulateProvinceIDTexture t:ComputeShader");
                if (guids.Length > 0)
                {
                    string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
                    populateProvinceIDCompute = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>(path);
                }
                #endif

                if (populateProvinceIDCompute == null)
                {
                    ArchonLogger.LogError("MapTexturePopulator: PopulateProvinceIDTexture compute shader not found! Falling back to Graphics.Blit", "map_rendering");
                    PopulateProvinceIDTextureGPU_Fallback(textureManager, width, height, pixels);
                    return;
                }
            }

            // Convert Color32[] to packed uint[] for GPU buffer
            uint[] packedPixels = new uint[pixels.Length];
            for (int i = 0; i < pixels.Length; i++)
            {
                Color32 c = pixels[i];
                packedPixels[i] = ((uint)c.a << 24) | ((uint)c.r << 16) | ((uint)c.g << 8) | (uint)c.b;
            }

            // DEBUG: Verify what we're sending to GPU for test pixel
            int testIndex = 711 * width + 2767;
            uint testPacked = packedPixels[testIndex];
            byte testR = (byte)((testPacked >> 16) & 0xFF);
            byte testG = (byte)((testPacked >> 8) & 0xFF);
            ushort testProvinceFromBuffer = (ushort)((testG << 8) | testR);
            ArchonLogger.Log($"MapTexturePopulator: GPU buffer[{testIndex}] (x=2767,y=711) = packed 0x{testPacked:X8}, R={testR} G={testG}, province={testProvinceFromBuffer}", "map_initialization");

            // Create GPU buffer
            ComputeBuffer pixelBuffer = new ComputeBuffer(packedPixels.Length, sizeof(uint));
            pixelBuffer.SetData(packedPixels);

            // Setup compute shader
            int kernel = populateProvinceIDCompute.FindKernel("PopulateProvinceIDs");
            populateProvinceIDCompute.SetBuffer(kernel, "ProvinceIDPixelData", pixelBuffer);
            populateProvinceIDCompute.SetTexture(kernel, "ProvinceIDTexture", textureManager.ProvinceIDTexture);
            populateProvinceIDCompute.SetInt("MapWidth", width);
            populateProvinceIDCompute.SetInt("MapHeight", height);

            // Dispatch compute shader (same thread group size as OwnerTextureDispatcher)
            const int THREAD_GROUP_SIZE = 8;
            int threadGroupsX = (width + THREAD_GROUP_SIZE - 1) / THREAD_GROUP_SIZE;
            int threadGroupsY = (height + THREAD_GROUP_SIZE - 1) / THREAD_GROUP_SIZE;
            populateProvinceIDCompute.Dispatch(kernel, threadGroupsX, threadGroupsY, 1);

            // CRITICAL: Force GPU synchronization before subsequent shaders read ProvinceIDTexture
            // Dispatch() is async - GPU may not have finished writing when PopulateOwnerTexture runs
            // This forces CPU to wait for GPU completion
            var asyncRead = UnityEngine.Rendering.AsyncGPUReadback.Request(textureManager.ProvinceIDTexture);
            asyncRead.WaitForCompletion();

            // Clean up
            pixelBuffer.Release();

            // DEBUG: Verify what the compute shader actually wrote
            RenderTexture.active = textureManager.ProvinceIDTexture;
            Texture2D debugPixel = new Texture2D(1, 1, TextureFormat.ARGB32, false);
            debugPixel.ReadPixels(new Rect(2767, 711, 1, 1), 0, 0);
            debugPixel.Apply();
            RenderTexture.active = null;

            Color32 debugColor = debugPixel.GetPixel(0, 0);
            ushort debugProvinceID = Province.ProvinceIDEncoder.UnpackProvinceID(debugColor);
            Object.Destroy(debugPixel);

            ArchonLogger.Log($"MapTexturePopulator: VERIFY - After compute shader, ProvinceIDTexture(2767,711) = province {debugProvinceID} (R={debugColor.r} G={debugColor.g} - expected 2751)", "map_initialization");

            if (logPopulationProgress)
            {
                ArchonLogger.Log($"MapTexturePopulator: Populated ProvinceIDTexture via compute shader (eliminates Graphics.Blit coordinate issues)", "map_initialization");
                ArchonLogger.Log($"MapTexturePopulator: Wrote to ProvinceIDTexture instance {textureManager.ProvinceIDTexture.GetInstanceID()}", "map_initialization");
            }
        }

        /// <summary>
        /// Fallback method using Graphics.Blit (has coordinate system issues)
        /// </summary>
        private void PopulateProvinceIDTextureGPU_Fallback(MapTextureManager textureManager, int width, int height, Color32[] pixels)
        {
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

        /// <summary>
        /// Populate ProvinceColorTexture using batch operations
        /// </summary>
        private void PopulateProvinceColorTextureGPU(MapTextureManager textureManager, int width, int height, Color32[] pixels)
        {
            // ProvinceColorTexture is still a Texture2D, so we can use SetPixels32 directly
            textureManager.ProvinceColorTexture.SetPixels32(pixels);
            textureManager.ProvinceColorTexture.Apply(false);

            if (logPopulationProgress)
            {
                ArchonLogger.Log($"MapTexturePopulator: Populated ProvinceColorTexture via batch SetPixels32", "map_initialization");
            }
        }
    }
}