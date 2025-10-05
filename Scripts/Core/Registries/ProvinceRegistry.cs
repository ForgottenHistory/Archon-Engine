using System.Collections.Generic;
using System.Linq;
using Core.Data;
using Utils;

namespace Core.Registries
{
    /// <summary>
    /// Specialized registry for provinces with dense ID mapping
    /// Converts sparse definition IDs (1, 2, 5, 100...) to dense runtime IDs (1, 2, 3, 4...)
    /// Following data-linking-architecture.md specifications
    /// </summary>
    public class ProvinceRegistry
    {
        private readonly Dictionary<int, ushort> definitionToRuntime = new();
        private readonly List<ProvinceData> provinces = new();

        public string TypeName => "Province";
        public int Count => provinces.Count - 1; // Exclude index 0

        public ProvinceRegistry()
        {
            // Reserve index 0 for "none/invalid"
            provinces.Add(null);

            ArchonLogger.LogDataLinking("ProvinceRegistry initialized");
        }

        /// <summary>
        /// Register a province with its definition ID from files
        /// Returns dense runtime ID for efficient array access
        /// </summary>
        public ushort Register(int definitionId, ProvinceData province)
        {
            if (province == null)
                throw new System.ArgumentNullException(nameof(province));

            if (definitionToRuntime.ContainsKey(definitionId))
                throw new System.InvalidOperationException($"Duplicate province definition ID: {definitionId}");

            if (provinces.Count >= ushort.MaxValue)
                throw new System.InvalidOperationException($"ProvinceRegistry exceeded maximum capacity of {ushort.MaxValue}");

            ushort runtimeId = (ushort)provinces.Count;
            provinces.Add(province);
            definitionToRuntime[definitionId] = runtimeId;

            // Set IDs in the province data
            province.RuntimeId = runtimeId;
            province.DefinitionId = definitionId;

            ArchonLogger.LogDataLinking($"Registered province {definitionId} with runtime ID {runtimeId}");
            return runtimeId;
        }

        /// <summary>
        /// Get province by runtime ID (O(1) array access)
        /// Primary access method for simulation systems
        /// </summary>
        public ProvinceData GetByRuntime(ushort runtimeId)
        {
            if (runtimeId >= provinces.Count)
                return null;

            return provinces[runtimeId];
        }

        /// <summary>
        /// Get province by definition ID from files (O(1) hash lookup)
        /// Use during loading phase when working with file data
        /// </summary>
        public ProvinceData GetByDefinition(int definitionId)
        {
            if (definitionToRuntime.TryGetValue(definitionId, out ushort runtimeId))
                return provinces[runtimeId];

            return null;
        }

        /// <summary>
        /// Get runtime ID from definition ID
        /// </summary>
        public ushort GetRuntimeId(int definitionId)
        {
            return definitionToRuntime.TryGetValue(definitionId, out ushort runtimeId) ? runtimeId : (ushort)0;
        }

        /// <summary>
        /// Get definition ID from runtime ID
        /// </summary>
        public int GetDefinitionId(ushort runtimeId)
        {
            if (runtimeId >= provinces.Count)
                return 0;

            var province = provinces[runtimeId];
            return province?.DefinitionId ?? 0;
        }

        /// <summary>
        /// Try get province by definition ID
        /// </summary>
        public bool TryGetByDefinition(int definitionId, out ProvinceData province)
        {
            province = null;

            if (definitionToRuntime.TryGetValue(definitionId, out ushort runtimeId))
            {
                province = provinces[runtimeId];
                return true;
            }

            return false;
        }

        /// <summary>
        /// Try get runtime ID by definition ID
        /// </summary>
        public bool TryGetRuntimeId(int definitionId, out ushort runtimeId)
        {
            return definitionToRuntime.TryGetValue(definitionId, out runtimeId);
        }

        /// <summary>
        /// Check if province exists by definition ID
        /// </summary>
        public bool ExistsByDefinition(int definitionId)
        {
            return definitionToRuntime.ContainsKey(definitionId);
        }

        /// <summary>
        /// Check if province exists by runtime ID
        /// </summary>
        public bool ExistsByRuntime(ushort runtimeId)
        {
            return runtimeId > 0 && runtimeId < provinces.Count && provinces[runtimeId] != null;
        }

        /// <summary>
        /// Get all provinces
        /// </summary>
        public IEnumerable<ProvinceData> GetAll()
        {
            return provinces.Skip(1).Where(province => province != null);
        }

        /// <summary>
        /// Get all valid runtime IDs
        /// </summary>
        public IEnumerable<ushort> GetAllRuntimeIds()
        {
            for (ushort i = 1; i < provinces.Count; i++)
            {
                if (provinces[i] != null)
                    yield return i;
            }
        }

        /// <summary>
        /// Get all definition IDs
        /// </summary>
        public IEnumerable<int> GetAllDefinitionIds()
        {
            return definitionToRuntime.Keys;
        }

        /// <summary>
        /// Get diagnostic information
        /// </summary>
        public string GetDiagnostics()
        {
            var validProvinces = GetAll().Count();
            return $"ProvinceRegistry: {validProvinces} provinces, {definitionToRuntime.Count} definition mappings, capacity {provinces.Count - 1}";
        }
    }

    /// <summary>
    /// Core province data structure with resolved references
    /// Contains both runtime simulation data and cold metadata
    /// </summary>
    public class ProvinceData
    {
        // Core identification
        public ushort RuntimeId { get; set; }     // Dense array index for simulation
        public int DefinitionId { get; set; }     // Original ID from files

        // Basic info
        public string Name { get; set; }

        // Hot simulation data (matches ProvinceState)
        public ushort OwnerId { get; set; }
        public ushort ControllerId { get; set; }
        public byte Development { get; set; }
        public byte Terrain { get; set; }
        public byte Flags { get; set; }

        // Cold reference data (resolved from strings)
        public ushort CultureId { get; set; }
        public ushort ReligionId { get; set; }
        public ushort TradeGoodId { get; set; }

        // Development components
        public byte BaseTax { get; set; }
        public byte BaseProduction { get; set; }
        public byte BaseManpower { get; set; }
        public byte CenterOfTrade { get; set; }

        // Buildings (resolved from string list)
        public ushort[] Buildings { get; set; } = new ushort[0];

        // Geography
        public bool IsCoastal { get; set; }
        public List<ushort> NeighborProvinces { get; set; } = new();

        // Visual data (for map rendering)
        public UnityEngine.Color32 MapColor { get; set; }
    }
}