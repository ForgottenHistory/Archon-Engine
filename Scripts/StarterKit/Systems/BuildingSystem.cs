using System;
using System.Collections.Generic;
using System.IO;
using Core;
using Core.Loaders;
using Newtonsoft.Json.Linq;

namespace StarterKit
{
    /// <summary>
    /// Simple building definition.
    /// </summary>
    public class BuildingType
    {
        public ushort ID { get; set; }
        public string StringID { get; set; }
        public string Name { get; set; }
        public int Cost { get; set; }
        public int GoldOutput { get; set; } // Bonus gold per month
        public int MaxPerProvince { get; set; }
    }

    /// <summary>
    /// Tracks buildings in a province.
    /// </summary>
    public struct ProvinceBuildingData
    {
        public Dictionary<ushort, int> BuildingCounts; // buildingTypeId -> count
    }

    /// <summary>
    /// Simple building system. Allows constructing buildings that provide bonuses.
    /// </summary>
    public class BuildingSystem : IDisposable
    {
        private readonly GameState gameState;
        private readonly PlayerState playerState;
        private readonly EconomySystem economySystem;
        private readonly bool logProgress;
        private bool isDisposed;

        // Building type registry
        private readonly Dictionary<string, BuildingType> buildingTypesByString;
        private readonly Dictionary<ushort, BuildingType> buildingTypesById;
        private ushort nextTypeId = 1;

        // Province buildings (provinceId -> building data)
        private readonly Dictionary<ushort, ProvinceBuildingData> provinceBuildings;

        // Events
        public event Action<ushort, ushort> OnBuildingConstructed; // provinceId, buildingTypeId

        // Public accessor for UI
        public PlayerState PlayerState => playerState;

        public BuildingSystem(GameState gameStateRef, PlayerState playerStateRef, EconomySystem economySystemRef, bool log = true)
        {
            gameState = gameStateRef;
            playerState = playerStateRef;
            economySystem = economySystemRef;
            logProgress = log;

            buildingTypesByString = new Dictionary<string, BuildingType>();
            buildingTypesById = new Dictionary<ushort, BuildingType>();
            provinceBuildings = new Dictionary<ushort, ProvinceBuildingData>();

            if (logProgress)
            {
                ArchonLogger.Log("BuildingSystem: Initialized", "starter_kit");
            }
        }

        /// <summary>
        /// Load building types from Template-Data/buildings/ directory.
        /// </summary>
        public void LoadBuildingTypes(string buildingsPath)
        {
            if (!Directory.Exists(buildingsPath))
            {
                ArchonLogger.LogWarning($"BuildingSystem: Buildings directory not found: {buildingsPath}", "starter_kit");
                return;
            }

            var files = Directory.GetFiles(buildingsPath, "*.json5", SearchOption.TopDirectoryOnly);

            foreach (var file in files)
            {
                try
                {
                    var buildingType = LoadBuildingTypeFromFile(file);
                    if (buildingType != null)
                    {
                        RegisterBuildingType(buildingType);
                    }
                }
                catch (Exception ex)
                {
                    ArchonLogger.LogError($"BuildingSystem: Failed to load {file}: {ex.Message}", "starter_kit");
                }
            }

            if (logProgress)
            {
                ArchonLogger.Log($"BuildingSystem: Loaded {buildingTypesByString.Count} building types", "starter_kit");
            }
        }

        private BuildingType LoadBuildingTypeFromFile(string filePath)
        {
            var jObj = Json5Loader.LoadJson5File(filePath);

            var buildingType = new BuildingType
            {
                StringID = jObj["id"]?.Value<string>() ?? Path.GetFileNameWithoutExtension(filePath),
                Name = jObj["name"]?.Value<string>() ?? "Unknown",
                Cost = jObj["cost"]?["gold"]?.Value<int>() ?? 0,
                GoldOutput = jObj["modifiers"]?["gold_output"]?.Value<int>() ?? 0,
                MaxPerProvince = jObj["max_per_province"]?.Value<int>() ?? 1
            };

            return buildingType;
        }

        private void RegisterBuildingType(BuildingType buildingType)
        {
            buildingType.ID = nextTypeId++;
            buildingTypesByString[buildingType.StringID] = buildingType;
            buildingTypesById[buildingType.ID] = buildingType;

            if (logProgress)
            {
                ArchonLogger.Log($"BuildingSystem: Registered '{buildingType.Name}' (ID={buildingType.ID}, cost={buildingType.Cost}, gold_output={buildingType.GoldOutput})", "starter_kit");
            }
        }

        /// <summary>
        /// Get building type by string ID.
        /// </summary>
        public BuildingType GetBuildingType(string stringId)
        {
            return buildingTypesByString.TryGetValue(stringId, out var type) ? type : null;
        }

        /// <summary>
        /// Get building type by numeric ID.
        /// </summary>
        public BuildingType GetBuildingType(ushort typeId)
        {
            return buildingTypesById.TryGetValue(typeId, out var type) ? type : null;
        }

        /// <summary>
        /// Get all registered building types.
        /// </summary>
        public IEnumerable<BuildingType> GetAllBuildingTypes()
        {
            return buildingTypesByString.Values;
        }

        /// <summary>
        /// Check if a building can be constructed in a province.
        /// </summary>
        public bool CanConstruct(ushort provinceId, string buildingTypeId, out string reason)
        {
            reason = null;

            // Get building type
            var buildingType = GetBuildingType(buildingTypeId);
            if (buildingType == null)
            {
                reason = "Unknown building type";
                return false;
            }

            // Check ownership
            if (!IsProvinceOwnedByPlayer(provinceId))
            {
                reason = "Province not owned by player";
                return false;
            }

            // Check gold
            if (economySystem.Gold < buildingType.Cost)
            {
                reason = $"Not enough gold (need {buildingType.Cost}, have {economySystem.Gold})";
                return false;
            }

            // Check max per province
            int currentCount = GetBuildingCount(provinceId, buildingType.ID);
            if (currentCount >= buildingType.MaxPerProvince)
            {
                reason = $"Max {buildingType.MaxPerProvince} per province";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Construct a building in a province (instant construction).
        /// </summary>
        public bool Construct(ushort provinceId, string buildingTypeId)
        {
            if (!CanConstruct(provinceId, buildingTypeId, out var reason))
            {
                ArchonLogger.LogWarning($"BuildingSystem: Cannot construct {buildingTypeId} in province {provinceId}: {reason}", "starter_kit");
                return false;
            }

            var buildingType = GetBuildingType(buildingTypeId);

            // Deduct gold
            economySystem.RemoveGold(buildingType.Cost);

            // Add building to province
            if (!provinceBuildings.TryGetValue(provinceId, out var data))
            {
                data = new ProvinceBuildingData { BuildingCounts = new Dictionary<ushort, int>() };
                provinceBuildings[provinceId] = data;
            }

            if (!data.BuildingCounts.ContainsKey(buildingType.ID))
            {
                data.BuildingCounts[buildingType.ID] = 0;
            }
            data.BuildingCounts[buildingType.ID]++;

            if (logProgress)
            {
                ArchonLogger.Log($"BuildingSystem: Constructed {buildingType.Name} in province {provinceId}", "starter_kit");
            }

            OnBuildingConstructed?.Invoke(provinceId, buildingType.ID);
            return true;
        }

        /// <summary>
        /// Construct a building for AI (no gold cost, no ownership check).
        /// </summary>
        public bool ConstructForAI(ushort provinceId, string buildingTypeId)
        {
            var buildingType = GetBuildingType(buildingTypeId);
            if (buildingType == null)
                return false;

            // Check max per province
            int currentCount = GetBuildingCount(provinceId, buildingType.ID);
            if (currentCount >= buildingType.MaxPerProvince)
                return false;

            // Add building to province (no gold cost for AI)
            if (!provinceBuildings.TryGetValue(provinceId, out var data))
            {
                data = new ProvinceBuildingData { BuildingCounts = new Dictionary<ushort, int>() };
                provinceBuildings[provinceId] = data;
            }

            if (!data.BuildingCounts.ContainsKey(buildingType.ID))
            {
                data.BuildingCounts[buildingType.ID] = 0;
            }
            data.BuildingCounts[buildingType.ID]++;

            OnBuildingConstructed?.Invoke(provinceId, buildingType.ID);
            return true;
        }

        /// <summary>
        /// Get the count of a specific building type in a province.
        /// </summary>
        public int GetBuildingCount(ushort provinceId, ushort buildingTypeId)
        {
            if (!provinceBuildings.TryGetValue(provinceId, out var data))
                return 0;

            return data.BuildingCounts.TryGetValue(buildingTypeId, out var count) ? count : 0;
        }

        /// <summary>
        /// Get total building count in a province.
        /// </summary>
        public int GetTotalBuildingCount(ushort provinceId)
        {
            if (!provinceBuildings.TryGetValue(provinceId, out var data))
                return 0;

            int total = 0;
            foreach (var count in data.BuildingCounts.Values)
                total += count;

            return total;
        }

        /// <summary>
        /// Get the total gold output bonus from buildings in a province.
        /// </summary>
        public int GetProvinceGoldBonus(ushort provinceId)
        {
            if (!provinceBuildings.TryGetValue(provinceId, out var data))
                return 0;

            int bonus = 0;
            foreach (var kvp in data.BuildingCounts)
            {
                var buildingType = GetBuildingType(kvp.Key);
                if (buildingType != null)
                {
                    bonus += buildingType.GoldOutput * kvp.Value;
                }
            }

            return bonus;
        }

        /// <summary>
        /// Get all buildings in a province (buildingTypeId -> count).
        /// </summary>
        public Dictionary<ushort, int> GetProvinceBuildings(ushort provinceId)
        {
            if (!provinceBuildings.TryGetValue(provinceId, out var data))
                return new Dictionary<ushort, int>();

            return new Dictionary<ushort, int>(data.BuildingCounts);
        }

        /// <summary>
        /// Check if a province is owned by the player.
        /// </summary>
        public bool IsProvinceOwnedByPlayer(ushort provinceId)
        {
            if (!playerState.HasPlayerCountry || provinceId == 0)
                return false;

            ushort ownerID = gameState.ProvinceQueries.GetOwner(provinceId);
            return ownerID == playerState.PlayerCountryId;
        }

        public void Dispose()
        {
            if (isDisposed) return;
            isDisposed = true;

            buildingTypesByString.Clear();
            buildingTypesById.Clear();
            provinceBuildings.Clear();

            if (logProgress)
            {
                ArchonLogger.Log("BuildingSystem: Disposed", "starter_kit");
            }
        }

        // ====================================================================
        // SERIALIZATION
        // ====================================================================

        /// <summary>
        /// Serialize building state to byte array
        /// </summary>
        public byte[] Serialize()
        {
            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms))
            {
                // Write province buildings
                writer.Write(provinceBuildings.Count);
                foreach (var kvp in provinceBuildings)
                {
                    writer.Write(kvp.Key); // provinceId
                    var buildingCounts = kvp.Value.BuildingCounts;
                    writer.Write(buildingCounts?.Count ?? 0);
                    if (buildingCounts != null)
                    {
                        foreach (var building in buildingCounts)
                        {
                            writer.Write(building.Key);   // buildingTypeId
                            writer.Write(building.Value); // count
                        }
                    }
                }
                return ms.ToArray();
            }
        }

        /// <summary>
        /// Deserialize building state from byte array
        /// </summary>
        public void Deserialize(byte[] data)
        {
            if (data == null || data.Length == 0) return;

            using (var ms = new MemoryStream(data))
            using (var reader = new BinaryReader(ms))
            {
                provinceBuildings.Clear();
                int provinceCount = reader.ReadInt32();
                for (int i = 0; i < provinceCount; i++)
                {
                    ushort provinceId = reader.ReadUInt16();
                    int buildingCount = reader.ReadInt32();
                    var buildingCounts = new Dictionary<ushort, int>();
                    for (int j = 0; j < buildingCount; j++)
                    {
                        ushort buildingTypeId = reader.ReadUInt16();
                        int count = reader.ReadInt32();
                        buildingCounts[buildingTypeId] = count;
                    }
                    provinceBuildings[provinceId] = new ProvinceBuildingData
                    {
                        BuildingCounts = buildingCounts
                    };
                }

                if (logProgress)
                {
                    ArchonLogger.Log($"BuildingSystem: Loaded buildings for {provinceCount} provinces", "starter_kit");
                }
            }
        }
    }
}
