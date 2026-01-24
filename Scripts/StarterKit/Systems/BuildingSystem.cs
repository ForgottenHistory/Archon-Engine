using System;
using System.Collections.Generic;
using System.IO;
using Core;
using Core.Data;
using Core.Loaders;
using Core.Modifiers;
using Newtonsoft.Json.Linq;

namespace StarterKit
{
    /// <summary>
    /// Building modifier definition (loaded from JSON5).
    /// </summary>
    public struct BuildingModifier
    {
        public ModifierType Type;
        public FixedPoint64 Value;
        public bool IsMultiplicative;
        public bool IsCountryWide;
    }

    /// <summary>
    /// Simple building definition.
    /// </summary>
    public class BuildingType
    {
        public ushort ID { get; set; }
        public string StringID { get; set; }
        public string Name { get; set; }
        public int Cost { get; set; }
        public int MaxPerProvince { get; set; }

        // Modifiers applied when building is constructed
        public List<BuildingModifier> Modifiers { get; set; } = new List<BuildingModifier>();
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
    /// Uses ModifierSystem for province-local and country-wide effects.
    /// </summary>
    public class BuildingSystem : IDisposable
    {
        #region Fields & Properties

        private readonly GameState gameState;
        private readonly PlayerState playerState;
        private readonly EconomySystem economySystem;
        private readonly ModifierSystem modifierSystem;
        private readonly bool logProgress;
        private bool isDisposed;

        // Building type registry
        private readonly Dictionary<string, BuildingType> buildingTypesByString;
        private readonly Dictionary<ushort, BuildingType> buildingTypesById;
        private ushort nextTypeId = 1;

        // Province buildings (provinceId -> building data)
        private readonly Dictionary<ushort, ProvinceBuildingData> provinceBuildings;

        // Public accessor for UI
        public PlayerState PlayerState => playerState;

        #endregion

        #region Constructor & Disposal

        public BuildingSystem(GameState gameStateRef, PlayerState playerStateRef, EconomySystem economySystemRef, ModifierSystem modifierSystemRef, bool log = true)
        {
            gameState = gameStateRef;
            playerState = playerStateRef;
            economySystem = economySystemRef;
            modifierSystem = modifierSystemRef;
            logProgress = log;

            buildingTypesByString = new Dictionary<string, BuildingType>();
            buildingTypesById = new Dictionary<ushort, BuildingType>();
            provinceBuildings = new Dictionary<ushort, ProvinceBuildingData>();

            if (logProgress)
            {
                ArchonLogger.Log("BuildingSystem: Initialized", "starter_kit");
            }
        }

        #endregion

        #region Building Type Loading

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
                MaxPerProvince = jObj["max_per_province"]?.Value<int>() ?? 1
            };

            // Parse modifiers from JSON5
            var modifiersObj = jObj["modifiers"] as JObject;
            if (modifiersObj != null)
            {
                foreach (var prop in modifiersObj.Properties())
                {
                    string key = prop.Name;
                    double value = prop.Value.Value<double>();

                    // Convert string key to ModifierType
                    var modType = ModifierTypeHelper.FromKey(key);
                    if (modType == null)
                    {
                        ArchonLogger.LogWarning($"BuildingSystem: Unknown modifier key '{key}' in {buildingType.StringID}", "starter_kit");
                        continue;
                    }

                    buildingType.Modifiers.Add(new BuildingModifier
                    {
                        Type = modType.Value,
                        Value = FixedPoint64.FromDouble(value),
                        IsMultiplicative = ModifierTypeHelper.IsMultiplicative(modType.Value),
                        IsCountryWide = ModifierTypeHelper.IsCountryWide(modType.Value)
                    });
                }
            }

            return buildingType;
        }

        private void RegisterBuildingType(BuildingType buildingType)
        {
            buildingType.ID = nextTypeId++;
            buildingTypesByString[buildingType.StringID] = buildingType;
            buildingTypesById[buildingType.ID] = buildingType;

            if (logProgress)
            {
                string modifierInfo = buildingType.Modifiers.Count > 0
                    ? $", modifiers: {buildingType.Modifiers.Count}"
                    : "";
                ArchonLogger.Log($"BuildingSystem: Registered '{buildingType.Name}' (ID={buildingType.ID}, cost={buildingType.Cost}{modifierInfo})", "starter_kit");
            }
        }

        #endregion

        #region Building Type Queries

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

        #endregion

        #region Construction

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
        /// Applies modifiers via ModifierSystem.
        /// </summary>
        public bool Construct(ushort provinceId, string buildingTypeId)
        {
            if (!CanConstruct(provinceId, buildingTypeId, out var reason))
            {
                ArchonLogger.LogWarning($"BuildingSystem: Cannot construct {buildingTypeId} in province {provinceId}: {reason}", "starter_kit");
                return false;
            }

            var buildingType = GetBuildingType(buildingTypeId);
            ushort ownerId = gameState.ProvinceQueries.GetOwner(provinceId);

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

            // Apply modifiers via ModifierSystem
            ApplyBuildingModifiers(provinceId, ownerId, buildingType);

            if (logProgress)
            {
                ArchonLogger.Log($"BuildingSystem: Constructed {buildingType.Name} in province {provinceId}", "starter_kit");
            }

            // Emit event via EventBus (Pattern 3)
            EmitBuildingConstructed(provinceId, buildingType.ID, ownerId);
            return true;
        }

        /// <summary>
        /// Check if a building can be constructed in a province by a specific country.
        /// Used by both player and AI.
        /// </summary>
        public bool CanConstructForCountry(ushort provinceId, string buildingTypeId, ushort countryId, out string reason)
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
            ushort ownerId = gameState.ProvinceQueries.GetOwner(provinceId);
            if (ownerId != countryId)
            {
                reason = $"Province not owned by country {countryId}";
                return false;
            }

            // Check gold
            int currentGold = economySystem.GetCountryGoldInt(countryId);
            if (currentGold < buildingType.Cost)
            {
                reason = $"Not enough gold (need {buildingType.Cost}, have {currentGold})";
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
        /// Construct a building in a province for a specific country.
        /// Used by both player and AI via ConstructBuildingCommand.
        /// </summary>
        public bool ConstructForCountry(ushort provinceId, string buildingTypeId, ushort countryId)
        {
            if (!CanConstructForCountry(provinceId, buildingTypeId, countryId, out var reason))
            {
                ArchonLogger.LogWarning($"BuildingSystem: Cannot construct {buildingTypeId} in province {provinceId} for country {countryId}: {reason}", "starter_kit");
                return false;
            }

            var buildingType = GetBuildingType(buildingTypeId);

            // Deduct gold from the country
            economySystem.RemoveGoldFromCountry(countryId, buildingType.Cost);

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

            // Apply modifiers via ModifierSystem
            ApplyBuildingModifiers(provinceId, countryId, buildingType);

            if (logProgress)
            {
                ArchonLogger.Log($"BuildingSystem: Country {countryId} constructed {buildingType.Name} in province {provinceId}", "starter_kit");
            }

            // Emit event via EventBus (Pattern 3)
            EmitBuildingConstructed(provinceId, buildingType.ID, countryId);
            return true;
        }

        /// <summary>
        /// DEPRECATED: Use ConstructForCountry instead.
        /// Construct a building for AI (no gold cost, no ownership check).
        /// </summary>
        [System.Obsolete("Use ConstructForCountry with proper command flow instead")]
        public bool ConstructForAI(ushort provinceId, string buildingTypeId)
        {
            var buildingType = GetBuildingType(buildingTypeId);
            if (buildingType == null)
                return false;

            // Check max per province
            int currentCount = GetBuildingCount(provinceId, buildingType.ID);
            if (currentCount >= buildingType.MaxPerProvince)
                return false;

            ushort ownerId = gameState.ProvinceQueries.GetOwner(provinceId);

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

            // Apply modifiers via ModifierSystem
            ApplyBuildingModifiers(provinceId, ownerId, buildingType);

            // Emit event via EventBus (Pattern 3)
            EmitBuildingConstructed(provinceId, buildingType.ID, ownerId);
            return true;
        }

        private void EmitBuildingConstructed(ushort provinceId, ushort buildingTypeId, ushort countryId)
        {
            gameState.EventBus.Emit(new BuildingConstructedEvent
            {
                ProvinceId = provinceId,
                BuildingTypeId = buildingTypeId,
                CountryId = countryId
            });
        }

        #endregion

        #region Province Building Queries

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
        /// Apply building modifiers to ModifierSystem.
        /// Local modifiers go to province scope, country modifiers go to country scope.
        /// </summary>
        private void ApplyBuildingModifiers(ushort provinceId, ushort ownerId, BuildingType buildingType)
        {
            if (modifierSystem == null || buildingType.Modifiers.Count == 0)
                return;

            foreach (var modifier in buildingType.Modifiers)
            {
                var source = ModifierSource.CreatePermanent(
                    type: ModifierSource.SourceType.Building,
                    sourceId: (uint)buildingType.ID,
                    modifierTypeId: (ushort)modifier.Type,
                    value: modifier.Value,
                    isMultiplicative: modifier.IsMultiplicative
                );

                if (modifier.IsCountryWide)
                {
                    // Country-wide modifier (affects all provinces of owner)
                    if (ownerId > 0)
                    {
                        modifierSystem.AddCountryModifier(ownerId, source);
                        if (logProgress)
                        {
                            ArchonLogger.Log($"BuildingSystem: Applied country modifier {modifier.Type} = {modifier.Value.ToFloat():F2} from {buildingType.Name} to country {ownerId}", "starter_kit");
                        }
                    }
                }
                else
                {
                    // Province-local modifier
                    modifierSystem.AddProvinceModifier(provinceId, source);
                    if (logProgress)
                    {
                        ArchonLogger.Log($"BuildingSystem: Applied province modifier {modifier.Type} = {modifier.Value.ToFloat():F2} from {buildingType.Name} to province {provinceId}", "starter_kit");
                    }
                }
            }
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

        #endregion

        #region Disposal

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

        #endregion

        #region Serialization

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

        #endregion
    }
}
