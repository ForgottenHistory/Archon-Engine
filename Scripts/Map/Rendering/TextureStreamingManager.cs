using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Map.Rendering
{
    /// <summary>
    /// Manages texture streaming for very large maps (>10k provinces)
    /// Task 1.3: Add texture streaming for very large maps (>10k provinces)
    /// Uses tile-based streaming to handle massive maps efficiently
    /// </summary>
    public class TextureStreamingManager : MonoBehaviour
    {
        [Header("Streaming Settings")]
        [SerializeField] private int tileSize = 512;           // Tile size in pixels
        [SerializeField] private int maxActiveTiles = 16;      // Maximum tiles in memory
        [SerializeField] private float streamingRadius = 2000f; // Distance from camera to stream
        [SerializeField] private bool enableStreaming = true;   // Enable/disable streaming

        [Header("Performance")]
        [SerializeField] private int tilesPerFrame = 2;        // Max tiles to process per frame
        [SerializeField] private float memoryBudgetMB = 200f;  // Memory budget for streaming

        [Header("Debug")]
        [SerializeField] private bool logStreamingEvents = false;
        [SerializeField] private bool showTileDebugInfo = false;

        // Streaming state
        private MapTextureManager textureManager;
        private Camera mainCamera;
        private bool isInitialized = false;
        private bool streamingRequired = false;

        // Tile management
        private NativeHashMap<int2, StreamingTile> activeTiles;
        private NativeList<int2> tilesToLoad;
        private NativeList<int2> tilesToUnload;
        private NativeHashMap<int2, float> tileLastAccessTime;

        // Map dimensions and tile grid
        private int mapWidth, mapHeight;
        private int tilesX, tilesY;
        private int totalTiles;

        /// <summary>
        /// Streaming tile data
        /// </summary>
        private struct StreamingTile
        {
            public int2 tileCoord;
            public bool isLoaded;
            public bool isLoading;
            public float loadTime;
            public int memoryUsage; // In bytes

            public bool IsActive => isLoaded && !isLoading;
        }

        /// <summary>
        /// Initialize streaming system
        /// </summary>
        public void Initialize(MapTextureManager manager, Camera camera = null)
        {
            textureManager = manager;
            mainCamera = camera ?? Camera.main;

            if (textureManager == null)
            {
                ArchonLogger.LogMapRenderingError("MapTextureManager is required for texture streaming");
                return;
            }

            mapWidth = textureManager.MapWidth;
            mapHeight = textureManager.MapHeight;

            // Calculate if streaming is needed
            long totalPixels = (long)mapWidth * mapHeight;
            long estimatedMemoryMB = (totalPixels * 13) / (1024 * 1024); // ~13 bytes per pixel for all textures
            streamingRequired = estimatedMemoryMB > memoryBudgetMB || enableStreaming;

            if (!streamingRequired)
            {
                if (logStreamingEvents)
                    ArchonLogger.LogMapRendering($"Texture streaming disabled - map size {estimatedMemoryMB}MB is within budget");
                return;
            }

            InitializeTileSystem();
            isInitialized = true;

            if (logStreamingEvents)
            {
                ArchonLogger.LogMapRendering($"Texture streaming initialized - Map: {mapWidth}x{mapHeight}, " +
                         $"Tiles: {tilesX}x{tilesY} ({totalTiles} total), " +
                         $"Estimated memory: {estimatedMemoryMB}MB");
            }
        }

        /// <summary>
        /// Initialize tile-based streaming system
        /// </summary>
        private void InitializeTileSystem()
        {
            // Calculate tile grid dimensions
            tilesX = Mathf.CeilToInt((float)mapWidth / tileSize);
            tilesY = Mathf.CeilToInt((float)mapHeight / tileSize);
            totalTiles = tilesX * tilesY;

            // Initialize collections
            activeTiles = new NativeHashMap<int2, StreamingTile>(maxActiveTiles, Allocator.Persistent);
            tilesToLoad = new NativeList<int2>(maxActiveTiles, Allocator.Persistent);
            tilesToUnload = new NativeList<int2>(maxActiveTiles, Allocator.Persistent);
            tileLastAccessTime = new NativeHashMap<int2, float>(maxActiveTiles * 2, Allocator.Persistent);
        }

        void Update()
        {
            if (!isInitialized || !streamingRequired) return;

            UpdateTileStreaming();
        }

        /// <summary>
        /// Update tile streaming based on camera position
        /// </summary>
        private void UpdateTileStreaming()
        {
            if (mainCamera == null) return;

            // Get camera position in texture space
            Vector3 cameraPos = mainCamera.transform.position;
            int2 cameraTexturePos = WorldToCameraTextureCoord(cameraPos);
            int2 cameraTileCoord = TextureCoordToTileCoord(cameraTexturePos);

            // Determine which tiles should be loaded
            DetermineTilesToStream(cameraTileCoord);

            // Process tile loading/unloading
            ProcessTileOperations();
        }

        /// <summary>
        /// Convert world position to texture coordinate
        /// </summary>
        private int2 WorldToCameraTextureCoord(Vector3 worldPos)
        {
            // This is a simplified conversion - adjust based on your coordinate system
            // Assumes map quad is positioned at origin with size matching texture
            float normalizedX = Mathf.Clamp01(worldPos.x / 10f); // Assuming 10 unit map size
            float normalizedZ = Mathf.Clamp01(worldPos.z / 10f);

            return new int2(
                (int)(normalizedX * mapWidth),
                (int)(normalizedZ * mapHeight)
            );
        }

        /// <summary>
        /// Convert texture coordinate to tile coordinate
        /// </summary>
        private int2 TextureCoordToTileCoord(int2 textureCoord)
        {
            return new int2(
                textureCoord.x / tileSize,
                textureCoord.y / tileSize
            );
        }

        /// <summary>
        /// Determine which tiles should be loaded based on camera position
        /// </summary>
        private void DetermineTilesToStream(int2 cameraTileCoord)
        {
            tilesToLoad.Clear();
            tilesToUnload.Clear();

            // Calculate streaming radius in tiles
            int radiusTiles = Mathf.CeilToInt(streamingRadius / tileSize);

            // Mark tiles for loading within radius
            for (int y = cameraTileCoord.y - radiusTiles; y <= cameraTileCoord.y + radiusTiles; y++)
            {
                for (int x = cameraTileCoord.x - radiusTiles; x <= cameraTileCoord.x + radiusTiles; x++)
                {
                    int2 tileCoord = new int2(x, y);

                    // Skip tiles outside map bounds
                    if (x < 0 || x >= tilesX || y < 0 || y >= tilesY) continue;

                    // Calculate distance from camera
                    float distance = math.distance(cameraTileCoord, tileCoord) * tileSize;
                    if (distance > streamingRadius) continue;

                    // Mark for loading if not already loaded
                    if (!activeTiles.ContainsKey(tileCoord))
                    {
                        tilesToLoad.Add(tileCoord);
                    }

                    // Update last access time
                    tileLastAccessTime[tileCoord] = Time.time;
                }
            }

            // Mark tiles for unloading if outside radius or memory pressure
            var tileKeys = activeTiles.GetKeyArray(Allocator.Temp);
            foreach (var tileCoord in tileKeys)
            {
                float distance = math.distance(cameraTileCoord, tileCoord) * tileSize;
                bool outsideRadius = distance > streamingRadius * 1.2f; // Add hysteresis

                if (outsideRadius || activeTiles.Count >= maxActiveTiles)
                {
                    tilesToUnload.Add(tileCoord);
                }
            }
            tileKeys.Dispose();
        }

        /// <summary>
        /// Process tile loading and unloading operations
        /// </summary>
        private void ProcessTileOperations()
        {
            int operationsThisFrame = 0;

            // Unload tiles first to free memory
            for (int i = 0; i < tilesToUnload.Length && operationsThisFrame < tilesPerFrame; i++)
            {
                var tileCoord = tilesToUnload[i];
                UnloadTile(tileCoord);
                operationsThisFrame++;
            }

            // Load new tiles
            for (int i = 0; i < tilesToLoad.Length && operationsThisFrame < tilesPerFrame; i++)
            {
                var tileCoord = tilesToLoad[i];
                if (!activeTiles.ContainsKey(tileCoord))
                {
                    LoadTile(tileCoord);
                    operationsThisFrame++;
                }
            }
        }

        /// <summary>
        /// Load a tile into memory
        /// </summary>
        private void LoadTile(int2 tileCoord)
        {
            var tile = new StreamingTile
            {
                tileCoord = tileCoord,
                isLoaded = false,
                isLoading = true,
                loadTime = Time.time,
                memoryUsage = CalculateTileMemoryUsage()
            };

            activeTiles.TryAdd(tileCoord, tile);

            // Start asynchronous tile loading
            StartCoroutine(LoadTileAsync(tileCoord));

            if (logStreamingEvents)
            {
                ArchonLogger.LogMapRendering($"Started loading tile ({tileCoord.x}, {tileCoord.y})");
            }
        }

        /// <summary>
        /// Unload a tile from memory
        /// </summary>
        private void UnloadTile(int2 tileCoord)
        {
            if (activeTiles.TryGetValue(tileCoord, out var tile))
            {
                // Clear tile texture data (simplified - would need actual texture tile management)
                ClearTileData(tileCoord);

                activeTiles.Remove(tileCoord);
                tileLastAccessTime.Remove(tileCoord);

                if (logStreamingEvents)
                {
                    ArchonLogger.LogMapRendering($"Unloaded tile ({tileCoord.x}, {tileCoord.y})");
                }
            }
        }

        /// <summary>
        /// Asynchronously load tile data
        /// </summary>
        private System.Collections.IEnumerator LoadTileAsync(int2 tileCoord)
        {
            // Simulate async loading - in real implementation this would:
            // 1. Load province ID data for this tile region
            // 2. Update texture manager with tile data
            // 3. Mark tile as loaded

            yield return new WaitForSeconds(0.1f); // Simulate load time

            if (activeTiles.TryGetValue(tileCoord, out var tile))
            {
                tile.isLoading = false;
                tile.isLoaded = true;
                activeTiles[tileCoord] = tile;

                // Load actual tile data
                LoadTileData(tileCoord);

                if (logStreamingEvents)
                {
                    ArchonLogger.LogMapRendering($"Completed loading tile ({tileCoord.x}, {tileCoord.y})");
                }
            }
        }

        /// <summary>
        /// Load actual texture data for a tile
        /// </summary>
        private void LoadTileData(int2 tileCoord)
        {
            // Calculate tile bounds in texture space
            int startX = tileCoord.x * tileSize;
            int startY = tileCoord.y * tileSize;
            int endX = Mathf.Min(startX + tileSize, mapWidth);
            int endY = Mathf.Min(startY + tileSize, mapHeight);

            // In a real implementation, this would:
            // 1. Load province data for this tile region
            // 2. Update texture manager with province IDs and owners for this region
            // 3. Generate borders and effects for this tile

            // For now, just log the tile bounds
            if (logStreamingEvents)
            {
                ArchonLogger.LogMapRendering($"Loading data for tile ({tileCoord.x}, {tileCoord.y}): " +
                         $"region ({startX}, {startY}) to ({endX}, {endY})");
            }
        }

        /// <summary>
        /// Clear texture data for a tile
        /// </summary>
        private void ClearTileData(int2 tileCoord)
        {
            // Calculate tile bounds
            int startX = tileCoord.x * tileSize;
            int startY = tileCoord.y * tileSize;
            int endX = Mathf.Min(startX + tileSize, mapWidth);
            int endY = Mathf.Min(startY + tileSize, mapHeight);

            // TODO: Clear texture data using GPU compute shader instead of CPU
            // Deprecated CPU methods removed - use OwnerTextureDispatcher for GPU-based updates
            // for (int y = startY; y < endY; y += 8)
            // {
            //     for (int x = startX; x < endX; x += 8)
            //     {
            //         textureManager.SetProvinceID(x, y, 0); // DEPRECATED
            //         textureManager.SetProvinceOwner(x, y, 0); // DEPRECATED
            //     }
            // }
        }

        /// <summary>
        /// Calculate memory usage for a single tile
        /// </summary>
        private int CalculateTileMemoryUsage()
        {
            int pixelsPerTile = tileSize * tileSize;
            // Estimate: Province ID (2 bytes) + Owner (2 bytes) + Color (4 bytes) + effects
            return pixelsPerTile * 8;
        }

        /// <summary>
        /// Get streaming statistics
        /// </summary>
        public string GetStreamingStatistics()
        {
            if (!isInitialized) return "Streaming not initialized";
            if (!streamingRequired) return "Streaming not required for this map size";

            int loadedTiles = 0;
            int loadingTiles = 0;

            var tileValues = activeTiles.GetValueArray(Allocator.Temp);
            foreach (var tile in tileValues)
            {
                if (tile.isLoaded) loadedTiles++;
                if (tile.isLoading) loadingTiles++;
            }
            tileValues.Dispose();

            long memoryUsage = loadedTiles * CalculateTileMemoryUsage();

            return $"Tiles: {loadedTiles}/{maxActiveTiles} loaded, {loadingTiles} loading | " +
                   $"Memory: {memoryUsage / 1024f / 1024f:F1}MB | " +
                   $"Grid: {tilesX}x{tilesY}";
        }

        /// <summary>
        /// Check if a texture coordinate is currently loaded
        /// </summary>
        public bool IsTextureCoordLoaded(int2 textureCoord)
        {
            if (!streamingRequired) return true;

            int2 tileCoord = TextureCoordToTileCoord(textureCoord);
            return activeTiles.TryGetValue(tileCoord, out var tile) && tile.IsActive;
        }

        void OnDestroy()
        {
            if (activeTiles.IsCreated) activeTiles.Dispose();
            if (tilesToLoad.IsCreated) tilesToLoad.Dispose();
            if (tilesToUnload.IsCreated) tilesToUnload.Dispose();
            if (tileLastAccessTime.IsCreated) tileLastAccessTime.Dispose();
        }

        #if UNITY_EDITOR
        void OnDrawGizmos()
        {
            if (!showTileDebugInfo || !isInitialized) return;

            // Draw tile grid
            Gizmos.color = Color.cyan;
            for (int x = 0; x <= tilesX; x++)
            {
                Vector3 start = new Vector3(x * tileSize / (float)mapWidth * 10f, 0, 0);
                Vector3 end = new Vector3(x * tileSize / (float)mapWidth * 10f, 0, 10f);
                Gizmos.DrawLine(start, end);
            }

            for (int y = 0; y <= tilesY; y++)
            {
                Vector3 start = new Vector3(0, 0, y * tileSize / (float)mapHeight * 10f);
                Vector3 end = new Vector3(10f, 0, y * tileSize / (float)mapHeight * 10f);
                Gizmos.DrawLine(start, end);
            }

            // Draw loaded tiles
            if (activeTiles.IsCreated)
            {
                var tileKeys = activeTiles.GetKeyArray(Allocator.Temp);
                foreach (var tileCoord in tileKeys)
                {
                    if (activeTiles.TryGetValue(tileCoord, out var tile) && tile.IsActive)
                    {
                        Vector3 center = new Vector3(
                            (tileCoord.x * tileSize + tileSize * 0.5f) / mapWidth * 10f,
                            0.1f,
                            (tileCoord.y * tileSize + tileSize * 0.5f) / mapHeight * 10f
                        );

                        Gizmos.color = Color.green;
                        Gizmos.DrawWireCube(center, new Vector3(tileSize / (float)mapWidth * 10f, 0.2f, tileSize / (float)mapHeight * 10f));
                    }
                }
                tileKeys.Dispose();
            }
        }

        [ContextMenu("Log Streaming Stats")]
        private void EditorLogStats()
        {
            ArchonLogger.LogMapRendering($"Texture Streaming Stats: {GetStreamingStatistics()}");
        }
        #endif
    }
}