using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using System.Collections.Generic;
using Map.Rendering;
using Map.Province;
using Map.Loading;

namespace Map.Integration
{
    /// <summary>
    /// Coordinates province data management by delegating to specialized components
    /// Orchestrates ProvinceDataConverter, ProvinceTextureSynchronizer, and ProvinceMetadataManager
    /// Refactored to follow single responsibility principle
    /// </summary>
    public class MapDataIntegrator : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField] private MapTextureManager textureManager;
        [SerializeField] private MapRenderer mapRenderer;

        [Header("Settings")]
        [SerializeField] private bool autoSyncChanges = true;
        [SerializeField] private string provinceBitmapPath = "Assets/Map/provinces.bmp";
        [SerializeField] private bool detectNeighbors = true;
        [SerializeField] private bool includeOceanNeighbors = true;
        [SerializeField] private bool generateMetadata = true;
        [SerializeField] private bool generateConvexHulls = true;

        // Core data manager
        private ProvinceDataManager dataManager;

        // Specialized components (extracted for single responsibility)
        private ProvinceTextureSynchronizer textureSynchronizer;
        private ProvinceMetadataManager metadataManager;

        private bool isInitialized = false;

        // Public accessors
        public ProvinceDataManager DataManager => dataManager;
        public ProvinceNeighborDetector.NeighborResult NeighborResult => metadataManager?.NeighborResult ?? default;
        public ProvinceMetadataGenerator.MetadataResult MetadataResult => metadataManager?.MetadataResult ?? default;
        public bool IsInitialized => isInitialized;

        void Awake()
        {
            // Initialize data manager
            dataManager = new ProvinceDataManager();

            // Find dependencies if not assigned
            if (textureManager == null)
                textureManager = FindFirstObjectByType<MapTextureManager>();

            if (mapRenderer == null)
                mapRenderer = FindFirstObjectByType<MapRenderer>();

            // Initialize specialized components
            textureSynchronizer = new ProvinceTextureSynchronizer(dataManager, textureManager);
            metadataManager = new ProvinceMetadataManager(dataManager);
        }

        void Start()
        {
            if (ValidateDependencies())
            {
                InitializeMapData();
            }
        }

        /// <summary>
        /// Validate that all required dependencies are available
        /// </summary>
        private bool ValidateDependencies()
        {
            if (textureManager == null)
            {
                ArchonLogger.LogError("MapTextureManager not found! Please assign or ensure one exists in scene.", "map_rendering");
                return false;
            }

            if (mapRenderer == null)
            {
                ArchonLogger.LogError("MapRenderer not found! Please assign or ensure one exists in scene.", "map_rendering");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Initialize map data by loading province bitmap and setting up data structures
        /// </summary>
        public void InitializeMapData()
        {
            if (isInitialized)
            {
                ArchonLogger.LogWarning("Map data already initialized!", "map_rendering");
                return;
            }

            ArchonLogger.Log("Initializing map data integration...", "map_rendering");

            // Load province bitmap using optimized ProvinceMapProcessor (via compatibility layer)
            var loadResult = ProvinceMapLoader.LoadProvinceMap(
                provinceBitmapPath,
                textureManager
            );

            if (!loadResult.Success)
            {
                ArchonLogger.LogError($"Failed to load province bitmap: {loadResult.ErrorMessage}", "map_rendering");

                // Create error texture as fallback
                var errorTexture = ProvinceMapLoader.CreateErrorTexture(textureManager.MapWidth, textureManager.MapHeight);
                ArchonLogger.LogWarning("Created error texture as fallback", "map_rendering");
                return;
            }

            // Resize texture manager if needed
            if (loadResult.Width != textureManager.MapWidth || loadResult.Height != textureManager.MapHeight)
            {
                ArchonLogger.Log($"Resizing textures to match bitmap: {loadResult.Width}x{loadResult.Height}", "map_rendering");
                textureManager.ResizeTextures(loadResult.Width, loadResult.Height);
            }

            // Convert load result to data manager format using extracted component
            ProvinceDataConverter.ConvertLoadResult(loadResult, dataManager);

            // Populate textures with province data
            PopulateTexturesFromLoadResult(loadResult);

            // Detect province neighbors if enabled
            if (detectNeighbors)
            {
                ArchonLogger.Log("Detecting province neighbors...", "map_rendering");
                var neighborResult = ProvinceNeighborDetector.DetectNeighbors(loadResult, includeOceanNeighbors);

                if (neighborResult.Success)
                {
                    ProvinceNeighborDetector.LogNeighborStatistics(neighborResult);

                    // Update metadata manager with neighbor results (automatically updates coastal flags)
                    metadataManager.SetNeighborResult(neighborResult);
                }
                else
                {
                    ArchonLogger.LogError($"Neighbor detection failed: {neighborResult.ErrorMessage}", "map_rendering");
                }
            }

            // Generate province metadata if enabled
            if (generateMetadata)
            {
                ArchonLogger.Log("Generating province metadata...", "map_rendering");
                var metadataResult = ProvinceMetadataGenerator.GenerateMetadata(loadResult, metadataManager.NeighborResult, generateConvexHulls);

                if (metadataResult.Success)
                {
                    ArchonLogger.Log($"Province metadata generation complete for {metadataResult.ProvinceMetadata.Count} provinces", "map_rendering");

                    // Update metadata manager with metadata results (automatically updates terrain flags)
                    metadataManager.SetMetadataResult(metadataResult);
                }
                else
                {
                    ArchonLogger.LogError($"Metadata generation failed: {metadataResult.ErrorMessage}", "map_rendering");
                }
            }

            // Bind textures to map renderer material
            if (mapRenderer.GetMaterial() != null)
            {
                textureManager.BindTexturesToMaterial(mapRenderer.GetMaterial());
            }

            // Clean up load result
            loadResult.Dispose();

            isInitialized = true;
            ArchonLogger.Log($"Map data integration complete! {dataManager.ProvinceCount} provinces loaded.", "map_rendering");
        }


        /// <summary>
        /// Populate textures from load result data
        /// </summary>
        private void PopulateTexturesFromLoadResult(ProvinceMapLoader.LoadResult loadResult)
        {
            // TODO: Populate textures using GPU compute shader instead of CPU
            // Deprecated CPU methods removed - use ProvinceMapProcessor and OwnerTextureDispatcher
            // for (int i = 0; i < loadResult.ProvincePixels.Length; i++)
            // {
            //     var pixel = loadResult.ProvincePixels[i];
            //     int x = pixel.Position.x;
            //     int y = pixel.Position.y;
            //
            //     // textureManager.SetProvinceID(x, y, pixel.ProvinceID); // DEPRECATED
            //     textureManager.SetProvinceColor(x, y, pixel.Color);
            //     // textureManager.SetProvinceOwner(x, y, 0); // DEPRECATED
            // }

            // Set province colors only (IDs and owners handled by GPU)
            for (int i = 0; i < loadResult.ProvincePixels.Length; i++)
            {
                var pixel = loadResult.ProvincePixels[i];
                textureManager.SetProvinceColor(pixel.Position.x, pixel.Position.y, pixel.Color);
            }

            // Apply all texture changes
            textureManager.ApplyTextureChanges();
        }

        /// <summary>
        /// Update province owner and sync with textures
        /// </summary>
        public void SetProvinceOwner(ushort provinceID, ushort ownerCountryID)
        {
            if (!isInitialized)
            {
                ArchonLogger.LogError("Map data not initialized!", "map_rendering");
                return;
            }

            // Update data manager
            dataManager.SetProvinceOwner(provinceID, ownerCountryID);

            // Sync with textures if auto-sync enabled (delegate to texture synchronizer)
            if (autoSyncChanges)
            {
                textureSynchronizer.SyncProvinceOwner(provinceID);
            }
        }

        /// <summary>
        /// Update province display color and sync with textures
        /// </summary>
        public void SetProvinceDisplayColor(ushort provinceID, Color32 newColor)
        {
            if (!isInitialized)
            {
                ArchonLogger.LogError("Map data not initialized!", "map_rendering");
                return;
            }

            // Update data manager
            dataManager.SetProvinceDisplayColor(provinceID, newColor);

            // Sync with textures if auto-sync enabled (delegate to texture synchronizer)
            if (autoSyncChanges)
            {
                textureSynchronizer.SyncProvinceColor(provinceID);
            }
        }

        /// <summary>
        /// Update multiple province colors at once (batch operation)
        /// </summary>
        public void SetProvinceDisplayColors(NativeHashMap<ushort, Color32> provinceColors)
        {
            if (!isInitialized)
            {
                ArchonLogger.LogError("Map data not initialized!", "map_rendering");
                return;
            }

            // Update all provinces in data manager
            foreach (var kvp in provinceColors)
            {
                dataManager.SetProvinceDisplayColor(kvp.Key, kvp.Value);
            }

            // Batch sync with textures (delegate to texture synchronizer)
            if (autoSyncChanges)
            {
                foreach (var kvp in provinceColors)
                {
                    textureSynchronizer.SyncProvinceColor(kvp.Key);
                }
            }
        }


        /// <summary>
        /// Get province at world position (for mouse interaction)
        /// </summary>
        public ushort GetProvinceAtWorldPosition(Vector3 worldPosition)
        {
            if (!isInitialized) return 0;

            // Convert world position to texture coordinates
            // This assumes the map quad is at origin with size matching texture dimensions
            int textureX = Mathf.RoundToInt(worldPosition.x * textureManager.MapWidth / 10f); // Assuming 10 unit wide quad
            int textureY = Mathf.RoundToInt(worldPosition.z * textureManager.MapHeight / 10f);

            // Clamp to texture bounds
            textureX = Mathf.Clamp(textureX, 0, textureManager.MapWidth - 1);
            textureY = Mathf.Clamp(textureY, 0, textureManager.MapHeight - 1);

            // Sample province ID texture
            return textureManager.GetProvinceID(textureX, textureY);
        }

        /// <summary>
        /// Force full sync of all data to textures (delegate to texture synchronizer)
        /// </summary>
        [ContextMenu("Force Full Sync")]
        public void ForceFullSync()
        {
            if (!isInitialized) return;

            ArchonLogger.Log("Performing full data-to-texture sync...", "map_rendering");
            textureSynchronizer.SyncAllProvinces(syncOwner: false, syncColor: true, syncDevelopment: false);
            ArchonLogger.Log("Full sync complete!", "map_rendering");
        }

        /// <summary>
        /// Get neighbors of a specific province (delegate to metadata manager)
        /// </summary>
        public NativeArray<ushort> GetProvinceNeighbors(ushort provinceID, Allocator allocator)
        {
            if (!isInitialized) return new NativeArray<ushort>(0, allocator);

            var neighbors = metadataManager.GetNeighbors(provinceID);
            var neighborArray = new NativeArray<ushort>(neighbors.Count, allocator);

            int index = 0;
            foreach (var neighbor in neighbors)
            {
                neighborArray[index++] = neighbor;
            }

            return neighborArray;
        }

        /// <summary>
        /// Check if two provinces are neighbors (delegate to metadata manager)
        /// </summary>
        public bool AreProvincesNeighbors(ushort provinceID1, ushort provinceID2)
        {
            if (!isInitialized) return false;
            return metadataManager.AreNeighbors(provinceID1, provinceID2);
        }

        /// <summary>
        /// Get neighbor count for a province (delegate to metadata manager)
        /// </summary>
        public int GetProvinceNeighborCount(ushort provinceID)
        {
            if (!isInitialized) return 0;
            return metadataManager.GetNeighbors(provinceID).Count;
        }

        /// <summary>
        /// Check if province is coastal (delegate to data manager)
        /// </summary>
        public bool IsProvinceCoastal(ushort provinceID)
        {
            if (!isInitialized) return false;

            var data = dataManager.GetProvinceByID(provinceID);
            if (data.id != 0)
            {
                return (data.flags & ProvinceFlags.IsCoastal) != 0;
            }

            return false;
        }

        /// <summary>
        /// Get province bounding box (delegate to metadata manager)
        /// </summary>
        public bool TryGetProvinceBounds(ushort provinceID, out ProvinceNeighborDetector.ProvinceBounds bounds)
        {
            bounds = default;
            if (!isInitialized) return false;

            var (min, max) = metadataManager.GetProvinceBounds(provinceID);
            if (min == Vector2.zero && max == Vector2.zero) return false;

            bounds = new ProvinceNeighborDetector.ProvinceBounds
            {
                ProvinceID = provinceID,
                Min = new int2((int)min.x, (int)min.y),
                Max = new int2((int)max.x, (int)max.y)
            };

            return true;
        }

        /// <summary>
        /// Get province metadata (delegate to metadata manager)
        /// </summary>
        public bool TryGetProvinceMetadata(ushort provinceID, out ProvinceMetadataGenerator.ProvinceMetadata metadata)
        {
            metadata = default;
            if (!isInitialized) return false;

            metadata = metadataManager.GetMetadata(provinceID);
            return metadata.PixelCount > 0; // Check if valid metadata
        }

        /// <summary>
        /// Get optimal label position for province
        /// </summary>
        public float2 GetOptimalLabelPosition(ushort provinceID)
        {
            if (TryGetProvinceMetadata(provinceID, out var metadata))
            {
                return ProvinceMetadataGenerator.GetOptimalLabelPosition(metadata);
            }

            // Fallback to basic province data if available
            var provinceData = dataManager.GetProvinceByID(provinceID);
            if (provinceData.id != 0)
            {
                return provinceData.centerPoint;
            }

            return float2.zero;
        }

        /// <summary>
        /// Get province terrain type
        /// </summary>
        public ProvinceMetadataGenerator.TerrainType GetProvinceTerrainType(ushort provinceID)
        {
            if (TryGetProvinceMetadata(provinceID, out var metadata))
            {
                return metadata.TerrainType;
            }

            return ProvinceMetadataGenerator.TerrainType.Plains;
        }

        /// <summary>
        /// Check if province is suitable for large labels
        /// </summary>
        public bool IsProvinceSuitableForLargeLabel(ushort provinceID)
        {
            if (TryGetProvinceMetadata(provinceID, out var metadata))
            {
                return ProvinceMetadataGenerator.IsSuitableForLargeLabel(metadata);
            }

            return false;
        }

        /// <summary>
        /// Get province pixel count
        /// </summary>
        public int GetProvincePixelCount(ushort provinceID)
        {
            if (TryGetProvinceMetadata(provinceID, out var metadata))
            {
                return metadata.PixelCount;
            }

            return 0;
        }

        void OnDestroy()
        {
            if (dataManager != null)
            {
                dataManager.Dispose();
            }

            // Dispose neighbor and metadata results through metadata manager accessors
            if (metadataManager?.NeighborResult.Success == true)
            {
                metadataManager.NeighborResult.Dispose();
            }

            if (metadataManager?.MetadataResult.Success == true)
            {
                metadataManager.MetadataResult.Dispose();
            }
        }

#if UNITY_EDITOR
        [ContextMenu("Log Integration Statistics")]
        private void LogStatistics()
        {
            if (!isInitialized)
            {
                ArchonLogger.Log("Map data integration not initialized.", "map_rendering");
                return;
            }

            var (totalBytes, provinceBytes, lookupBytes) = dataManager.GetMemoryUsage();

            ArchonLogger.Log($"Map Data Integration Statistics:\n" +
                     $"Initialized: {isInitialized}\n" +
                     $"Provinces: {dataManager.ProvinceCount}\n" +
                     $"Texture Size: {textureManager.MapWidth}x{textureManager.MapHeight}\n" +
                     $"Memory Usage: {totalBytes / 1024f:F1} KB\n" +
                     $"Auto Sync: {autoSyncChanges}", "map_rendering");
        }
#endif
    }
}