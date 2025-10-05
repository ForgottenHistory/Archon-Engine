using UnityEngine;
using Map.Province;
using Map.Rendering;
using Utils;

namespace Map.Integration
{
    /// <summary>
    /// Handles synchronization between CPU province data and GPU textures
    /// Provides generic sync patterns to eliminate code duplication
    /// Extracted from MapDataIntegrator for single responsibility
    /// </summary>
    public class ProvinceTextureSynchronizer
    {
        private readonly ProvinceDataManager dataManager;
        private readonly MapTextureManager textureManager;

        public ProvinceTextureSynchronizer(ProvinceDataManager dataManager, MapTextureManager textureManager)
        {
            this.dataManager = dataManager;
            this.textureManager = textureManager;
        }

        /// <summary>
        /// Sync province owner from CPU data to GPU texture
        /// Updates owner texture for all pixels belonging to the province
        /// NOTE: Currently uses CPU path with TODO to use GPU compute shader (OwnerTextureDispatcher)
        /// </summary>
        public void SyncProvinceOwner(ushort provinceID)
        {
            var provinceData = dataManager.GetProvinceByID(provinceID);
            if (provinceData.id == 0)
            {
                ArchonLogger.LogWarning($"ProvinceTextureSynchronizer: Cannot sync owner - province {provinceID} not found");
                return;
            }

            // TODO: Use GPU compute shader (OwnerTextureDispatcher) instead of CPU path
            // For now, the CPU path is deprecated and commented out in MapDataIntegrator
            // GPU-based updates should be triggered through OwnerTextureDispatcher

            ArchonLogger.LogWarning($"ProvinceTextureSynchronizer: Owner sync for province {provinceID} requires GPU compute shader implementation");
        }

        /// <summary>
        /// Sync province color from CPU data to GPU texture
        /// Updates color texture for all pixels belonging to the province
        /// </summary>
        public void SyncProvinceColor(ushort provinceID)
        {
            var provinceData = dataManager.GetProvinceByID(provinceID);
            if (provinceData.id == 0)
            {
                ArchonLogger.LogWarning($"ProvinceTextureSynchronizer: Cannot sync color - province {provinceID} not found");
                return;
            }

            // Get province bounds to optimize iteration
            Vector2 min = provinceData.boundsMin;
            Vector2 max = provinceData.boundsMax;

            int minX = Mathf.FloorToInt(min.x);
            int maxX = Mathf.CeilToInt(max.x);
            int minY = Mathf.FloorToInt(min.y);
            int maxY = Mathf.CeilToInt(max.y);

            // Update color for all pixels in bounding box (filtered by province ID)
            int updatedPixels = 0;
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    // Check if this pixel belongs to our province
                    ushort pixelProvinceID = textureManager.GetProvinceID(x, y);

                    if (pixelProvinceID == provinceID)
                    {
                        textureManager.SetProvinceColor(x, y, provinceData.displayColor);
                        updatedPixels++;
                    }
                }
            }

            // Apply texture changes
            textureManager.ApplyTextureChanges();

            ArchonLogger.Log($"ProvinceTextureSynchronizer: Synced color for province {provinceID} ({updatedPixels} pixels updated)");
        }

        /// <summary>
        /// Sync province development value to texture
        /// Updates development texture for visualization
        /// </summary>
        public void SyncProvinceDevelopment(ushort provinceID)
        {
            // TODO: Development field doesn't exist in ProvinceData
            // This would need to be stored separately if needed
            ArchonLogger.LogWarning($"ProvinceTextureSynchronizer: Development sync not implemented - no development field in ProvinceData");
            return;

            /* Original implementation - development field doesn't exist
            var provinceData = dataManager.GetProvinceByID(provinceID);
            if (provinceData.id == 0)
            {
                ArchonLogger.LogWarning($"ProvinceTextureSynchronizer: Cannot sync development - province {provinceID} not found");
                return;
            }

            // Convert development value to color (heatmap visualization)
            Color32 developmentColor = ConvertDevelopmentToColor(provinceData.development);

            // Get province bounds
            Vector2 min = provinceData.boundsMin;
            Vector2 max = provinceData.boundsMax;

            int minX = Mathf.FloorToInt(min.x);
            int maxX = Mathf.CeilToInt(max.x);
            int minY = Mathf.FloorToInt(min.y);
            int maxY = Mathf.CeilToInt(max.y);

            // Update development color for all pixels
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    ushort pixelProvinceID = textureManager.GetProvinceID(x, y);

                    if (pixelProvinceID == provinceID)
                    {
                        textureManager.SetProvinceDevelopment(x, y, developmentColor);
                    }
                }
            }

            textureManager.ApplyTextureChanges();
            */
        }

        /// <summary>
        /// Convert development value (0-100) to heatmap color
        /// </summary>
        private Color32 ConvertDevelopmentToColor(byte development)
        {
            // Simple heatmap: green (low) -> yellow (mid) -> red (high)
            float normalized = development / 100f;

            if (normalized < 0.5f)
            {
                // Green to yellow
                byte intensity = (byte)(normalized * 2f * 255f);
                return new Color32(intensity, 255, 0, 255);
            }
            else
            {
                // Yellow to red
                byte intensity = (byte)((1f - normalized) * 2f * 255f);
                return new Color32(255, intensity, 0, 255);
            }
        }

        /// <summary>
        /// Batch sync all provinces - useful after bulk updates
        /// NOTE: Expensive operation, use sparingly
        /// </summary>
        public void SyncAllProvinces(bool syncOwner = false, bool syncColor = true, bool syncDevelopment = false)
        {
            var provinces = dataManager.GetAllProvinces();
            int provinceCount = provinces.Length;

            ArchonLogger.Log($"ProvinceTextureSynchronizer: Starting batch sync of {provinceCount} provinces");

            foreach (var province in provinces)
            {
                if (syncOwner) SyncProvinceOwner(province.id);
                if (syncColor) SyncProvinceColor(province.id);
                if (syncDevelopment) SyncProvinceDevelopment(province.id);
            }

            ArchonLogger.Log($"ProvinceTextureSynchronizer: Batch sync complete");
        }
    }
}
