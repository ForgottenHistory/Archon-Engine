using UnityEngine;
using System.Collections.Generic;
using Map.Province;
using Utils;

namespace Map.Integration
{
    /// <summary>
    /// Manages province metadata including neighbors, terrain flags, and coastal status
    /// Provides query methods for province spatial relationships and properties
    /// Extracted from MapDataIntegrator for single responsibility
    /// </summary>
    public class ProvinceMetadataManager
    {
        private readonly ProvinceDataManager dataManager;
        private ProvinceNeighborDetector.NeighborResult neighborResult;
        private ProvinceMetadataGenerator.MetadataResult metadataResult;

        public ProvinceNeighborDetector.NeighborResult NeighborResult => neighborResult;
        public ProvinceMetadataGenerator.MetadataResult MetadataResult => metadataResult;

        public ProvinceMetadataManager(ProvinceDataManager dataManager)
        {
            this.dataManager = dataManager;
        }

        /// <summary>
        /// Store neighbor detection results
        /// </summary>
        public void SetNeighborResult(ProvinceNeighborDetector.NeighborResult result)
        {
            neighborResult = result;

            if (result.Success)
            {
                UpdateCoastalFlags();
            }
        }

        /// <summary>
        /// Store metadata generation results
        /// </summary>
        public void SetMetadataResult(ProvinceMetadataGenerator.MetadataResult result)
        {
            metadataResult = result;

            if (result.Success)
            {
                UpdateTerrainFlags();
            }
        }

        /// <summary>
        /// Update coastal flags based on neighbor detection results
        /// </summary>
        private void UpdateCoastalFlags()
        {
            if (!neighborResult.Success)
                return;

            foreach (var kvp in neighborResult.ProvinceNeighbors)
            {
                ushort provinceID = kvp.Key;
                var neighborData = kvp.Value;

                // Check if neighbors list contains ocean (province 0)
                bool hasOceanNeighbor = false;
                if (neighborData.Neighbors.IsCreated)
                {
                    for (int i = 0; i < neighborData.Neighbors.Length; i++)
                    {
                        if (neighborData.Neighbors[i] == 0)
                        {
                            hasOceanNeighbor = true;
                            break;
                        }
                    }
                }

                // Add coastal flag if has ocean neighbor
                if (hasOceanNeighbor)
                {
                    dataManager.AddProvinceFlag(provinceID, ProvinceFlags.IsCoastal);
                }
            }

            ArchonLogger.Log("ProvinceMetadataManager: Updated coastal flags based on neighbor detection");
        }

        /// <summary>
        /// Update terrain flags based on metadata generation results
        /// </summary>
        private void UpdateTerrainFlags()
        {
            if (!metadataResult.Success)
                return;

            foreach (var kvp in metadataResult.ProvinceMetadata)
            {
                ushort provinceID = kvp.Key;
                var metadata = kvp.Value;

                // Set impassable flag for mountains, lakes, etc.
                // Note: ProvinceData doesn't have terrainType field, only flags
                if (metadata.TerrainType == ProvinceMetadataGenerator.TerrainType.Impassable ||
                    metadata.TerrainType == ProvinceMetadataGenerator.TerrainType.Mountains ||
                    metadata.TerrainType == ProvinceMetadataGenerator.TerrainType.Lake)
                {
                    dataManager.AddProvinceFlag(provinceID, ProvinceFlags.IsImpassable);
                }
            }

            ArchonLogger.Log("ProvinceMetadataManager: Updated terrain flags based on metadata generation");
        }

        /// <summary>
        /// Get neighbors for a specific province
        /// </summary>
        public HashSet<ushort> GetNeighbors(ushort provinceID)
        {
            if (!neighborResult.Success)
            {
                ArchonLogger.LogWarning("ProvinceMetadataManager: Neighbor data not available");
                return new HashSet<ushort>();
            }

            if (neighborResult.ProvinceNeighbors.TryGetValue(provinceID, out var neighborData))
            {
                var neighbors = new HashSet<ushort>();
                if (neighborData.Neighbors.IsCreated)
                {
                    for (int i = 0; i < neighborData.Neighbors.Length; i++)
                    {
                        neighbors.Add(neighborData.Neighbors[i]);
                    }
                }
                return neighbors; // Return copy to prevent modification
            }

            return new HashSet<ushort>();
        }

        /// <summary>
        /// Check if two provinces are neighbors
        /// </summary>
        public bool AreNeighbors(ushort provinceID1, ushort provinceID2)
        {
            var neighbors = GetNeighbors(provinceID1);
            return neighbors.Contains(provinceID2);
        }

        /// <summary>
        /// Get metadata for a specific province
        /// </summary>
        public ProvinceMetadataGenerator.ProvinceMetadata GetMetadata(ushort provinceID)
        {
            if (!metadataResult.Success)
            {
                ArchonLogger.LogWarning("ProvinceMetadataManager: Metadata not available");
                return default;
            }

            if (metadataResult.ProvinceMetadata.TryGetValue(provinceID, out var metadata))
            {
                return metadata;
            }

            return default;
        }

        /// <summary>
        /// Get all coastal provinces
        /// </summary>
        public List<ushort> GetCoastalProvinces()
        {
            var coastalProvinces = new List<ushort>();
            var provinces = dataManager.GetAllProvinces();

            foreach (var province in provinces)
            {
                if ((province.flags & ProvinceFlags.IsCoastal) != 0)
                {
                    coastalProvinces.Add(province.id);
                }
            }

            return coastalProvinces;
        }

        /// <summary>
        /// Get all provinces of a specific terrain type
        /// </summary>
        public List<ushort> GetProvincesByTerrain(ProvinceMetadataGenerator.TerrainType terrainType)
        {
            var provinces = new List<ushort>();

            // Query metadata result instead of ProvinceData (which doesn't have terrainType field)
            if (!metadataResult.Success)
            {
                ArchonLogger.LogWarning("ProvinceMetadataManager: Cannot query by terrain - metadata not available");
                return provinces;
            }

            foreach (var kvp in metadataResult.ProvinceMetadata)
            {
                if (kvp.Value.TerrainType == terrainType)
                {
                    provinces.Add(kvp.Key);
                }
            }

            return provinces;
        }

        /// <summary>
        /// Get province center point
        /// </summary>
        public Vector2 GetProvinceCenter(ushort provinceID)
        {
            var data = dataManager.GetProvinceByID(provinceID);
            if (data.id != 0)
            {
                return data.centerPoint;
            }

            return Vector2.zero;
        }

        /// <summary>
        /// Get province bounds
        /// </summary>
        public (Vector2 min, Vector2 max) GetProvinceBounds(ushort provinceID)
        {
            var data = dataManager.GetProvinceByID(provinceID);
            if (data.id != 0)
            {
                return (data.boundsMin, data.boundsMax);
            }

            return (Vector2.zero, Vector2.zero);
        }

        /// <summary>
        /// Calculate distance between two provinces (center to center)
        /// </summary>
        public float GetProvinceDistance(ushort provinceID1, ushort provinceID2)
        {
            Vector2 center1 = GetProvinceCenter(provinceID1);
            Vector2 center2 = GetProvinceCenter(provinceID2);

            return Vector2.Distance(center1, center2);
        }
    }
}
