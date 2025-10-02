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
    /// Integrates texture-based rendering with province data management
    /// Handles synchronization between GPU textures and CPU data structures
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

        private ProvinceDataManager dataManager;
        private ProvinceNeighborDetector.NeighborResult neighborResult;
        private ProvinceMetadataGenerator.MetadataResult metadataResult;
        private bool isInitialized = false;

        public ProvinceDataManager DataManager => dataManager;
        public ProvinceNeighborDetector.NeighborResult NeighborResult => neighborResult;
        public ProvinceMetadataGenerator.MetadataResult MetadataResult => metadataResult;
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
                DominionLogger.LogError("MapTextureManager not found! Please assign or ensure one exists in scene.");
                return false;
            }

            if (mapRenderer == null)
            {
                DominionLogger.LogError("MapRenderer not found! Please assign or ensure one exists in scene.");
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
                DominionLogger.LogWarning("Map data already initialized!");
                return;
            }

            DominionLogger.Log("Initializing map data integration...");

            // Load province bitmap using optimized ProvinceMapProcessor (via compatibility layer)
            var loadResult = ProvinceMapLoader.LoadProvinceMap(
                provinceBitmapPath,
                textureManager
            );

            if (!loadResult.Success)
            {
                DominionLogger.LogError($"Failed to load province bitmap: {loadResult.ErrorMessage}");

                // Create error texture as fallback
                var errorTexture = ProvinceMapLoader.CreateErrorTexture(textureManager.MapWidth, textureManager.MapHeight);
                DominionLogger.LogWarning("Created error texture as fallback");
                return;
            }

            // Resize texture manager if needed
            if (loadResult.Width != textureManager.MapWidth || loadResult.Height != textureManager.MapHeight)
            {
                DominionLogger.Log($"Resizing textures to match bitmap: {loadResult.Width}x{loadResult.Height}");
                textureManager.ResizeTextures(loadResult.Width, loadResult.Height);
            }

            // Convert load result to data manager format
            ConvertLoadResultToDataManager(loadResult);

            // Populate textures with province data
            PopulateTexturesFromLoadResult(loadResult);

            // Detect province neighbors if enabled
            if (detectNeighbors)
            {
                DominionLogger.Log("Detecting province neighbors...");
                neighborResult = ProvinceNeighborDetector.DetectNeighbors(loadResult, includeOceanNeighbors);

                if (neighborResult.Success)
                {
                    ProvinceNeighborDetector.LogNeighborStatistics(neighborResult);

                    // Update province data manager with coastal flags
                    UpdateCoastalFlags();
                }
                else
                {
                    DominionLogger.LogError($"Neighbor detection failed: {neighborResult.ErrorMessage}");
                }
            }

            // Generate province metadata if enabled
            if (generateMetadata)
            {
                DominionLogger.Log("Generating province metadata...");
                metadataResult = ProvinceMetadataGenerator.GenerateMetadata(loadResult, neighborResult, generateConvexHulls);

                if (metadataResult.Success)
                {
                    DominionLogger.Log($"Province metadata generation complete for {metadataResult.ProvinceMetadata.Count} provinces");

                    // Update province data manager with terrain flags
                    UpdateTerrainFlags();
                }
                else
                {
                    DominionLogger.LogError($"Metadata generation failed: {metadataResult.ErrorMessage}");
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
            DominionLogger.Log($"Map data integration complete! {dataManager.ProvinceCount} provinces loaded.");
        }

        /// <summary>
        /// Convert load result to data manager format
        /// </summary>
        private void ConvertLoadResultToDataManager(ProvinceMapLoader.LoadResult loadResult)
        {
            // Group pixels by province ID
            var provincePixelGroups = new Dictionary<ushort, List<int2>>();

            // Initialize groups for all provinces
            foreach (var kvp in loadResult.ColorToID)
            {
                provincePixelGroups[kvp.Value] = new List<int2>();
            }

            // Group pixels by province
            for (int i = 0; i < loadResult.ProvincePixels.Length; i++)
            {
                var pixel = loadResult.ProvincePixels[i];
                if (pixel.ProvinceID > 0) // Skip ocean (ID 0)
                {
                    provincePixelGroups[pixel.ProvinceID].Add(pixel.Position);
                }
            }

            // Add provinces to data manager
            foreach (var kvp in provincePixelGroups)
            {
                ushort provinceID = kvp.Key;
                var pixelList = kvp.Value;

                if (pixelList.Count == 0) continue;

                // Convert to native array
                var pixels = new NativeArray<int2>(pixelList.Count, Allocator.Temp);
                for (int i = 0; i < pixelList.Count; i++)
                {
                    pixels[i] = pixelList[i];
                }

                // Find the color for this province ID
                Color32 provinceColor = Color.black;
                foreach (var colorKvp in loadResult.ColorToID)
                {
                    if (colorKvp.Value == provinceID)
                    {
                        provinceColor = colorKvp.Key;
                        break;
                    }
                }

                // Add to data manager
                dataManager.AddProvince(provinceID, provinceColor, pixels);

                pixels.Dispose();
            }
        }

        /// <summary>
        /// Populate textures from load result data
        /// </summary>
        private void PopulateTexturesFromLoadResult(ProvinceMapLoader.LoadResult loadResult)
        {
            // Populate all textures with province data
            for (int i = 0; i < loadResult.ProvincePixels.Length; i++)
            {
                var pixel = loadResult.ProvincePixels[i];
                int x = pixel.Position.x;
                int y = pixel.Position.y;

                // Set province ID
                textureManager.SetProvinceID(x, y, pixel.ProvinceID);

                // Set initial display color (same as identifier color)
                textureManager.SetProvinceColor(x, y, pixel.Color);

                // No owner initially
                textureManager.SetProvinceOwner(x, y, 0);
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
                DominionLogger.LogError("Map data not initialized!");
                return;
            }

            // Update data manager
            dataManager.SetProvinceOwner(provinceID, ownerCountryID);

            // Sync with textures if auto-sync enabled
            if (autoSyncChanges)
            {
                SyncProvinceOwnerToTexture(provinceID);
            }
        }

        /// <summary>
        /// Update province display color and sync with textures
        /// </summary>
        public void SetProvinceDisplayColor(ushort provinceID, Color32 newColor)
        {
            if (!isInitialized)
            {
                DominionLogger.LogError("Map data not initialized!");
                return;
            }

            // Update data manager
            dataManager.SetProvinceDisplayColor(provinceID, newColor);

            // Sync with textures if auto-sync enabled
            if (autoSyncChanges)
            {
                SyncProvinceColorToTexture(provinceID);
            }
        }

        /// <summary>
        /// Update multiple province colors at once (batch operation)
        /// </summary>
        public void SetProvinceDisplayColors(NativeHashMap<ushort, Color32> provinceColors)
        {
            if (!isInitialized)
            {
                DominionLogger.LogError("Map data not initialized!");
                return;
            }

            // Update all provinces in data manager
            foreach (var kvp in provinceColors)
            {
                dataManager.SetProvinceDisplayColor(kvp.Key, kvp.Value);
            }

            // Batch sync with textures
            if (autoSyncChanges)
            {
                SyncMultipleProvinceColors(provinceColors);
            }
        }

        /// <summary>
        /// Sync province owner from data manager to texture
        /// </summary>
        private void SyncProvinceOwnerToTexture(ushort provinceID)
        {
            var provinceData = dataManager.GetProvinceByID(provinceID);
            if (provinceData.id == 0) return; // Province not found

            // Get province bounds for efficient update
            int minX = Mathf.FloorToInt(provinceData.boundsMin.x);
            int maxX = Mathf.CeilToInt(provinceData.boundsMax.x);
            int minY = Mathf.FloorToInt(provinceData.boundsMin.y);
            int maxY = Mathf.CeilToInt(provinceData.boundsMax.y);

            // Update texture in bounding rectangle
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    // Check if this pixel belongs to our province by sampling ID texture
                    // This is more efficient than storing all pixel coordinates
                    ushort pixelProvinceID = textureManager.GetProvinceID(x, y);

                    if (pixelProvinceID == provinceID)
                    {
                        textureManager.SetProvinceOwner(x, y, provinceData.ownerCountryID);
                    }
                }
            }

            textureManager.ApplyTextureChanges();
        }

        /// <summary>
        /// Sync province color from data manager to texture
        /// </summary>
        private void SyncProvinceColorToTexture(ushort provinceID)
        {
            var provinceData = dataManager.GetProvinceByID(provinceID);
            if (provinceData.id == 0) return; // Province not found

            // Get province bounds for efficient update
            int minX = Mathf.FloorToInt(provinceData.boundsMin.x);
            int maxX = Mathf.CeilToInt(provinceData.boundsMax.x);
            int minY = Mathf.FloorToInt(provinceData.boundsMin.y);
            int maxY = Mathf.CeilToInt(provinceData.boundsMax.y);

            // Update texture in bounding rectangle
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    // Check if this pixel belongs to our province
                    ushort pixelProvinceID = textureManager.GetProvinceID(x, y);

                    if (pixelProvinceID == provinceID)
                    {
                        textureManager.SetProvinceColor(x, y, provinceData.displayColor);
                    }
                }
            }

            textureManager.ApplyTextureChanges();
        }

        /// <summary>
        /// Efficiently sync multiple province colors at once
        /// </summary>
        private void SyncMultipleProvinceColors(NativeHashMap<ushort, Color32> provinceColors)
        {
            // Get full texture dimensions for batch update
            int width = textureManager.MapWidth;
            int height = textureManager.MapHeight;

            // Process entire texture once for efficiency
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    // Get province ID at this pixel
                    ushort provinceID = textureManager.GetProvinceID(x, y);

                    // Update if this province is in our update set
                    if (provinceColors.TryGetValue(provinceID, out Color32 newColor))
                    {
                        textureManager.SetProvinceColor(x, y, newColor);
                    }
                }
            }

            textureManager.ApplyTextureChanges();
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
        /// Force full sync of all data to textures
        /// </summary>
        [ContextMenu("Force Full Sync")]
        public void ForceFullSync()
        {
            if (!isInitialized) return;

            DominionLogger.Log("Performing full data-to-texture sync...");

            var allProvinces = dataManager.GetAllProvinces();
            var colorUpdates = new NativeHashMap<ushort, Color32>(allProvinces.Length, Allocator.Temp);

            foreach (var province in allProvinces)
            {
                colorUpdates.TryAdd(province.id, province.displayColor);
            }

            SyncMultipleProvinceColors(colorUpdates);
            colorUpdates.Dispose();

            DominionLogger.Log("Full sync complete!");
        }

        /// <summary>
        /// Update coastal flags in data manager from neighbor detection
        /// </summary>
        private void UpdateCoastalFlags()
        {
            if (!neighborResult.Success) return;

            foreach (var coastalID in neighborResult.CoastalProvinces)
            {
                dataManager.AddProvinceFlag(coastalID, ProvinceFlags.IsCoastal);
            }

            DominionLogger.Log($"Updated coastal flags for {neighborResult.CoastalProvinces.Count} provinces");
        }

        /// <summary>
        /// Update terrain flags in data manager from metadata generation
        /// </summary>
        private void UpdateTerrainFlags()
        {
            if (!metadataResult.Success) return;

            foreach (var kvp in metadataResult.ProvinceMetadata)
            {
                ushort provinceID = kvp.Key;
                var metadata = kvp.Value;

                // Set impassable flag for mountains, lakes, etc.
                if (metadata.TerrainType == ProvinceMetadataGenerator.TerrainType.Impassable ||
                    metadata.TerrainType == ProvinceMetadataGenerator.TerrainType.Lake ||
                    metadata.TerrainType == ProvinceMetadataGenerator.TerrainType.Mountains)
                {
                    dataManager.AddProvinceFlag(provinceID, ProvinceFlags.IsImpassable);
                }

                // Set trade center flag for large, compact provinces
                if (ProvinceMetadataGenerator.IsSuitableForLargeLabel(metadata, 1000f))
                {
                    dataManager.AddProvinceFlag(provinceID, ProvinceFlags.IsTradeCenter);
                }
            }

            DominionLogger.Log($"Updated terrain flags for {metadataResult.ProvinceMetadata.Count} provinces");
        }

        /// <summary>
        /// Get neighbors of a specific province
        /// </summary>
        /// <param name="provinceID">Province to get neighbors for</param>
        /// <param name="allocator">Memory allocator for result</param>
        /// <returns>Array of neighbor province IDs</returns>
        public NativeArray<ushort> GetProvinceNeighbors(ushort provinceID, Allocator allocator)
        {
            if (!isInitialized || !neighborResult.Success)
            {
                return new NativeArray<ushort>(0, allocator);
            }

            return ProvinceNeighborDetector.GetNeighbors(neighborResult.ProvinceNeighbors, provinceID, allocator);
        }

        /// <summary>
        /// Check if two provinces are neighbors
        /// </summary>
        public bool AreProvincesNeighbors(ushort provinceID1, ushort provinceID2)
        {
            if (!isInitialized || !neighborResult.Success)
                return false;

            return ProvinceNeighborDetector.AreNeighbors(neighborResult.ProvinceNeighbors, provinceID1, provinceID2);
        }

        /// <summary>
        /// Get neighbor count for a province
        /// </summary>
        public int GetProvinceNeighborCount(ushort provinceID)
        {
            if (!isInitialized || !neighborResult.Success)
                return 0;

            return ProvinceNeighborDetector.GetNeighborCount(neighborResult.ProvinceNeighbors, provinceID);
        }

        /// <summary>
        /// Check if province is coastal
        /// </summary>
        public bool IsProvinceCoastal(ushort provinceID)
        {
            if (!isInitialized || !neighborResult.Success)
                return false;

            return neighborResult.CoastalProvinces.Contains(provinceID);
        }

        /// <summary>
        /// Get province bounding box
        /// </summary>
        public bool TryGetProvinceBounds(ushort provinceID, out ProvinceNeighborDetector.ProvinceBounds bounds)
        {
            bounds = default;

            if (!isInitialized || !neighborResult.Success)
                return false;

            return neighborResult.ProvinceBounds.TryGetValue(provinceID, out bounds);
        }

        /// <summary>
        /// Get province metadata
        /// </summary>
        public bool TryGetProvinceMetadata(ushort provinceID, out ProvinceMetadataGenerator.ProvinceMetadata metadata)
        {
            metadata = default;

            if (!isInitialized || !metadataResult.Success)
                return false;

            return metadataResult.ProvinceMetadata.TryGetValue(provinceID, out metadata);
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

            if (neighborResult.Success)
            {
                neighborResult.Dispose();
            }

            if (metadataResult.Success)
            {
                metadataResult.Dispose();
            }
        }

#if UNITY_EDITOR
        [ContextMenu("Log Integration Statistics")]
        private void LogStatistics()
        {
            if (!isInitialized)
            {
                DominionLogger.Log("Map data integration not initialized.");
                return;
            }

            var (totalBytes, provinceBytes, lookupBytes) = dataManager.GetMemoryUsage();

            DominionLogger.Log($"Map Data Integration Statistics:\n" +
                     $"Initialized: {isInitialized}\n" +
                     $"Provinces: {dataManager.ProvinceCount}\n" +
                     $"Texture Size: {textureManager.MapWidth}x{textureManager.MapHeight}\n" +
                     $"Memory Usage: {totalBytes / 1024f:F1} KB\n" +
                     $"Auto Sync: {autoSyncChanges}");
        }
#endif
    }
}