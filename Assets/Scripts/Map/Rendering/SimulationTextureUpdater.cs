using Unity.Collections;
using UnityEngine;
using Map.Simulation;
using Core.Systems;

namespace Map.Rendering
{
    /// <summary>
    /// Updates GPU textures from simulation state data
    /// Task 1.3: Implement texture update system from simulation state
    /// Bridges the dual-layer architecture: CPU simulation → GPU presentation
    /// </summary>
    public class SimulationTextureUpdater : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private MapTextureManager textureManager;

        [Header("Update Settings")]
        [SerializeField] private bool autoUpdate = true;
        [SerializeField] private float updateInterval = 0.1f; // Update every 100ms
        [SerializeField] private bool logUpdates = false;

        // Current state
        private ProvinceSimulation currentSimulation;
        private SimulationMapLoader.SimulationMapData currentMapData;
        private bool isInitialized = false;
        private float lastUpdateTime;

        // Update tracking
        private uint lastSimulationVersion;
        private bool fullUpdateRequired = true;

        /// <summary>
        /// Initialize with simulation data from SimulationMapLoader
        /// </summary>
        public void Initialize(ProvinceSimulation simulation, SimulationMapLoader.SimulationMapData mapData)
        {
            if (textureManager == null)
            {
                DominionLogger.LogError("MapTextureManager reference is required");
                return;
            }

            currentSimulation = simulation;
            currentMapData = mapData;
            lastSimulationVersion = 0;
            fullUpdateRequired = true;
            isInitialized = true;

            // Ensure texture dimensions match map data
            if (textureManager.MapWidth != mapData.Width || textureManager.MapHeight != mapData.Height)
            {
                textureManager.ResizeTextures(mapData.Width, mapData.Height);
                DominionLogger.Log($"Resized textures to match map: {mapData.Width}x{mapData.Height}");
            }

            // Perform initial full update
            PerformFullTextureUpdate();

            if (logUpdates)
            {
                DominionLogger.Log($"SimulationTextureUpdater initialized with {simulation.ProvinceCount} provinces");
            }
        }

        void Update()
        {
            if (!isInitialized || !autoUpdate || currentSimulation == null) return;

            // Check if it's time for an update
            if (Time.time - lastUpdateTime < updateInterval) return;

            // Check if simulation state has changed
            if (currentSimulation.StateVersion != lastSimulationVersion || fullUpdateRequired)
            {
                if (currentSimulation.IsDirty || fullUpdateRequired)
                {
                    UpdateTexturesFromSimulation();
                }
            }

            lastUpdateTime = Time.time;
        }

        /// <summary>
        /// Force immediate update of all textures from simulation
        /// </summary>
        public void ForceUpdate()
        {
            if (!isInitialized) return;

            fullUpdateRequired = true;
            UpdateTexturesFromSimulation();
        }

        /// <summary>
        /// Update textures based on current simulation state
        /// </summary>
        private void UpdateTexturesFromSimulation()
        {
            if (currentSimulation == null || textureManager == null) return;

            if (fullUpdateRequired)
            {
                PerformFullTextureUpdate();
                fullUpdateRequired = false;
            }
            else if (currentSimulation.IsDirty)
            {
                PerformIncrementalUpdate();
            }

            // Update tracking variables
            lastSimulationVersion = currentSimulation.StateVersion;
        }

        /// <summary>
        /// Perform full update of all textures (expensive - use sparingly)
        /// </summary>
        private void PerformFullTextureUpdate()
        {
            if (logUpdates)
                DominionLogger.Log("Performing full texture update");

            var startTime = Time.realtimeSinceStartup;

            // Update province ID texture from map data
            UpdateProvinceIDTexture();

            // Update owner texture from simulation state
            UpdateOwnerTextureFromSimulation();

            // Apply all changes
            textureManager.ApplyTextureChanges();

            var updateTime = (Time.realtimeSinceStartup - startTime) * 1000f;
            if (logUpdates)
                DominionLogger.Log($"Full texture update completed in {updateTime:F2}ms");
        }

        /// <summary>
        /// Perform incremental update of only changed provinces (efficient)
        /// </summary>
        private void PerformIncrementalUpdate()
        {
            if (logUpdates)
                DominionLogger.Log("Performing incremental texture update");

            var startTime = Time.realtimeSinceStartup;
            var dirtyIndices = currentSimulation.GetDirtyIndices();

            if (dirtyIndices.Count == 0) return;

            // Update only dirty provinces
            UpdateDirtyProvincesInTextures(dirtyIndices);

            // Apply changes and clear dirty flags
            textureManager.ApplyTextureChanges();
            currentSimulation.ClearDirtyFlags();

            var updateTime = (Time.realtimeSinceStartup - startTime) * 1000f;
            if (logUpdates)
                DominionLogger.Log($"Incremental update of {dirtyIndices.Count} provinces completed in {updateTime:F2}ms");
        }

        /// <summary>
        /// Update province ID texture using map data
        /// This is typically done once during initialization
        /// </summary>
        private void UpdateProvinceIDTexture()
        {
            if (!currentMapData.IsValid) return;

            // Update province IDs based on bounds data
            for (int i = 0; i < currentMapData.ProvinceBounds.Length; i++)
            {
                var bounds = currentMapData.ProvinceBounds[i];
                if (bounds.ProvinceID == 0) continue; // Skip ocean

                // Fill province bounds with province ID
                FillProvinceRegion(bounds);
            }
        }

        /// <summary>
        /// Fill a province region with its ID in the province ID texture
        /// </summary>
        private void FillProvinceRegion(SimulationMapLoader.ProvinceBounds bounds)
        {
            // For now, use a simple approach: set center pixel and bounding box
            // TODO: In Phase 2, this should use the actual pixel data from SimulationMapLoader

            // Set center pixel
            textureManager.SetProvinceID(bounds.CenterX, bounds.CenterY, bounds.ProvinceID);

            // Set a few pixels around center for visibility (temporary implementation)
            for (int y = bounds.MinY; y <= bounds.MaxY && y < textureManager.MapHeight; y += 4)
            {
                for (int x = bounds.MinX; x <= bounds.MaxX && x < textureManager.MapWidth; x += 4)
                {
                    textureManager.SetProvinceID(x, y, bounds.ProvinceID);
                }
            }
        }

        /// <summary>
        /// Update owner texture from current simulation state
        /// </summary>
        private void UpdateOwnerTextureFromSimulation()
        {
            var allProvinces = currentSimulation.GetAllProvinces();

            for (int i = 0; i < allProvinces.Length; i++)
            {
                var state = allProvinces[i];

                // Find province bounds for this province
                var bounds = FindProvinceBounds(GetProvinceIDAtIndex(i));
                if (bounds.ProvinceID == 0) continue;

                // Update owner for this province's pixels
                UpdateOwnerForProvince(bounds, state.ownerID);
            }
        }

        /// <summary>
        /// Get province ID at specific simulation array index
        /// </summary>
        private ushort GetProvinceIDAtIndex(int index)
        {
            // This is a simplified approach - in a real implementation,
            // we'd need to maintain an index→ID mapping
            // For now, scan the bounds array
            if (index < currentMapData.ProvinceBounds.Length)
            {
                return currentMapData.ProvinceBounds[index].ProvinceID;
            }
            return 0;
        }

        /// <summary>
        /// Update only dirty provinces in textures
        /// </summary>
        private void UpdateDirtyProvincesInTextures(System.Collections.Generic.IReadOnlyCollection<int> dirtyIndices)
        {
            var allProvinces = currentSimulation.GetAllProvinces();

            foreach (int index in dirtyIndices)
            {
                if (index >= allProvinces.Length) continue;

                var state = allProvinces[index];
                var provinceID = GetProvinceIDAtIndex(index);
                var bounds = FindProvinceBounds(provinceID);

                if (bounds.ProvinceID == 0) continue;

                // Update owner texture for this province
                UpdateOwnerForProvince(bounds, state.ownerID);
            }
        }

        /// <summary>
        /// Update owner texture for a specific province
        /// </summary>
        private void UpdateOwnerForProvince(SimulationMapLoader.ProvinceBounds bounds, ushort ownerID)
        {
            // Update owner for province's pixels
            // For now, use simple approach with bounds
            for (int y = bounds.MinY; y <= bounds.MaxY && y < textureManager.MapHeight; y += 2)
            {
                for (int x = bounds.MinX; x <= bounds.MaxX && x < textureManager.MapWidth; x += 2)
                {
                    // Check if this pixel belongs to our province
                    ushort pixelProvinceID = textureManager.GetProvinceID(x, y);
                    if (pixelProvinceID == bounds.ProvinceID)
                    {
                        textureManager.SetProvinceOwner(x, y, ownerID);
                    }
                }
            }
        }

        /// <summary>
        /// Find province bounds by province ID
        /// </summary>
        private SimulationMapLoader.ProvinceBounds FindProvinceBounds(ushort provinceID)
        {
            for (int i = 0; i < currentMapData.ProvinceBounds.Length; i++)
            {
                if (currentMapData.ProvinceBounds[i].ProvinceID == provinceID)
                {
                    return currentMapData.ProvinceBounds[i];
                }
            }
            return default; // Returns struct with ProvinceID = 0
        }

        /// <summary>
        /// Update color palette from country colors
        /// </summary>
        /// <param name="countryColors">Dictionary of country ID → color</param>
        public void UpdateColorPalette(System.Collections.Generic.Dictionary<ushort, Color32> countryColors)
        {
            if (textureManager == null) return;

            foreach (var kvp in countryColors)
            {
                if (kvp.Key <= 255) // Palette only supports 256 entries
                {
                    textureManager.SetPaletteColor((byte)kvp.Key, kvp.Value);
                }
            }

            textureManager.ApplyPaletteChanges();
        }

        /// <summary>
        /// Get current update statistics
        /// </summary>
        public string GetUpdateStatistics()
        {
            if (!isInitialized) return "Not initialized";

            return $"Simulation v{lastSimulationVersion}, " +
                   $"Provinces: {currentSimulation?.ProvinceCount ?? 0}, " +
                   $"Dirty: {currentSimulation?.IsDirty ?? false}, " +
                   $"Last Update: {Time.time - lastUpdateTime:F1}s ago";
        }

        #if UNITY_EDITOR
        [ContextMenu("Force Update")]
        private void EditorForceUpdate()
        {
            ForceUpdate();
        }

        [ContextMenu("Log Statistics")]
        private void EditorLogStatistics()
        {
            DominionLogger.Log($"SimulationTextureUpdater Stats: {GetUpdateStatistics()}");
        }
        #endif
    }
}