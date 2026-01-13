using System;
using System.Collections.Generic;
using System.IO;
using Core;
using Core.Events;
using Core.Loaders;
using Core.Units;
using Newtonsoft.Json.Linq;

namespace StarterKit
{
    /// <summary>
    /// Simple unit type definition. Lighter weight than GAME layer's UnitDefinition.
    /// </summary>
    public class UnitType
    {
        public ushort ID { get; set; }
        public string StringID { get; set; }
        public string Name { get; set; }
        public int Cost { get; set; }
        public int Maintenance { get; set; }
        public int Attack { get; set; }
        public int Defense { get; set; }
    }

    /// <summary>
    /// Simple military unit system. Wraps Core.Units.UnitSystem and provides unit type loading.
    /// </summary>
    public class UnitSystem : IDisposable
    {
        private readonly GameState gameState;
        private readonly PlayerState playerState;
        private readonly bool logProgress;
        private readonly CompositeDisposable subscriptions = new CompositeDisposable();
        private bool isDisposed;

        // Unit type registry (loaded from Template-Data/units/)
        private readonly Dictionary<string, UnitType> unitTypesByString;
        private readonly Dictionary<ushort, UnitType> unitTypesById;
        private ushort nextTypeId = 1;

        // Events
        public event Action<ushort> OnUnitCreated;
        public event Action<ushort> OnUnitDestroyed;
        public event Action<ushort, ushort, ushort> OnUnitMoved; // unitID, fromProvince, toProvince

        public UnitSystem(GameState gameStateRef, PlayerState playerStateRef, bool log = true)
        {
            gameState = gameStateRef;
            playerState = playerStateRef;
            logProgress = log;

            unitTypesByString = new Dictionary<string, UnitType>();
            unitTypesById = new Dictionary<ushort, UnitType>();

            // Subscribe to unit events from Core (tokens auto-disposed on Dispose)
            subscriptions.Add(gameState.EventBus.Subscribe<UnitCreatedEvent>(OnCoreUnitCreated));
            subscriptions.Add(gameState.EventBus.Subscribe<UnitDestroyedEvent>(OnCoreUnitDestroyed));
            subscriptions.Add(gameState.EventBus.Subscribe<UnitMovedEvent>(OnCoreUnitMoved));

            if (logProgress)
            {
                ArchonLogger.Log("UnitSystem: Initialized", "starter_kit");
            }
        }

        /// <summary>
        /// Load unit types from Template-Data/units/ directory.
        /// </summary>
        public void LoadUnitTypes(string unitsPath)
        {
            if (!Directory.Exists(unitsPath))
            {
                ArchonLogger.LogWarning($"UnitSystem: Units directory not found: {unitsPath}", "starter_kit");
                return;
            }

            var files = Directory.GetFiles(unitsPath, "*.json5", SearchOption.TopDirectoryOnly);

            foreach (var file in files)
            {
                try
                {
                    var unitType = LoadUnitTypeFromFile(file);
                    if (unitType != null)
                    {
                        RegisterUnitType(unitType);
                    }
                }
                catch (Exception ex)
                {
                    ArchonLogger.LogError($"UnitSystem: Failed to load {file}: {ex.Message}", "starter_kit");
                }
            }

            if (logProgress)
            {
                ArchonLogger.Log($"UnitSystem: Loaded {unitTypesByString.Count} unit types", "starter_kit");
            }
        }

        private UnitType LoadUnitTypeFromFile(string filePath)
        {
            var json = Json5Loader.LoadJson5File(filePath);

            var unitType = new UnitType
            {
                StringID = json["id"]?.ToString() ?? "",
                Name = json["name"]?.ToString() ?? "",
                Cost = 10,
                Maintenance = 1,
                Attack = 1,
                Defense = 1
            };

            // Parse cost
            var costObj = json["cost"] as JObject;
            if (costObj?["gold"] != null)
            {
                unitType.Cost = costObj["gold"].ToObject<int>();
            }

            // Parse maintenance
            var maintenanceObj = json["maintenance"] as JObject;
            if (maintenanceObj?["gold"] != null)
            {
                unitType.Maintenance = maintenanceObj["gold"].ToObject<int>();
            }

            // Parse stats
            var statsObj = json["stats"] as JObject;
            if (statsObj != null)
            {
                if (statsObj["attack"] != null)
                    unitType.Attack = statsObj["attack"].ToObject<int>();
                if (statsObj["defense"] != null)
                    unitType.Defense = statsObj["defense"].ToObject<int>();
            }

            return unitType;
        }

        private void RegisterUnitType(UnitType unitType)
        {
            unitType.ID = nextTypeId++;
            unitTypesByString[unitType.StringID] = unitType;
            unitTypesById[unitType.ID] = unitType;

            if (logProgress)
            {
                ArchonLogger.Log($"UnitSystem: Registered '{unitType.Name}' (ID={unitType.ID})", "starter_kit");
            }
        }

        /// <summary>
        /// Get unit type by string ID (e.g., "infantry").
        /// </summary>
        public UnitType GetUnitType(string stringId)
        {
            return unitTypesByString.TryGetValue(stringId, out var unitType) ? unitType : null;
        }

        /// <summary>
        /// Get unit type by numeric ID.
        /// </summary>
        public UnitType GetUnitType(ushort typeId)
        {
            return unitTypesById.TryGetValue(typeId, out var unitType) ? unitType : null;
        }

        /// <summary>
        /// Get all registered unit types.
        /// </summary>
        public IEnumerable<UnitType> GetAllUnitTypes()
        {
            return unitTypesByString.Values;
        }

        /// <summary>
        /// Create a unit at specified province for player's country.
        /// Returns unit ID, or 0 if failed.
        /// </summary>
        public ushort CreateUnit(ushort provinceId, string unitTypeStringId)
        {
            if (!playerState.HasPlayerCountry)
            {
                ArchonLogger.LogWarning("UnitSystem: Cannot create unit - no player country", "starter_kit");
                return 0;
            }

            var unitType = GetUnitType(unitTypeStringId);
            if (unitType == null)
            {
                ArchonLogger.LogWarning($"UnitSystem: Unknown unit type '{unitTypeStringId}'", "starter_kit");
                return 0;
            }

            return CreateUnit(provinceId, unitType.ID);
        }

        /// <summary>
        /// Create a unit at specified province for player's country.
        /// Returns unit ID, or 0 if failed.
        /// </summary>
        public ushort CreateUnit(ushort provinceId, ushort unitTypeId)
        {
            if (!playerState.HasPlayerCountry)
            {
                ArchonLogger.LogWarning("UnitSystem: Cannot create unit - no player country", "starter_kit");
                return 0;
            }

            var unitSystem = gameState.Units;
            if (unitSystem == null)
            {
                ArchonLogger.LogError("UnitSystem: UnitSystem not available", "starter_kit");
                return 0;
            }

            ushort unitId = unitSystem.CreateUnit(provinceId, playerState.PlayerCountryId, unitTypeId);

            if (logProgress && unitId != 0)
            {
                var unitType = GetUnitType(unitTypeId);
                string typeName = unitType?.Name ?? $"Type{unitTypeId}";
                ArchonLogger.Log($"UnitSystem: Created {typeName} (ID={unitId}) in province {provinceId}", "starter_kit");
            }

            return unitId;
        }

        /// <summary>
        /// Get all units in a province.
        /// </summary>
        public List<ushort> GetUnitsInProvince(ushort provinceId)
        {
            var unitSystem = gameState.Units;
            return unitSystem?.GetUnitsInProvince(provinceId) ?? new List<ushort>();
        }

        /// <summary>
        /// Get unit count in a province.
        /// </summary>
        public int GetUnitCountInProvince(ushort provinceId)
        {
            var unitSystem = gameState.Units;
            return unitSystem?.GetUnitCountInProvince(provinceId) ?? 0;
        }

        /// <summary>
        /// Get all units owned by player.
        /// </summary>
        public List<ushort> GetPlayerUnits()
        {
            if (!playerState.HasPlayerCountry)
                return new List<ushort>();

            var unitSystem = gameState.Units;
            return unitSystem?.GetCountryUnits(playerState.PlayerCountryId) ?? new List<ushort>();
        }

        /// <summary>
        /// Get unit state by ID.
        /// </summary>
        public UnitState GetUnit(ushort unitId)
        {
            var unitSystem = gameState.Units;
            return unitSystem?.GetUnit(unitId) ?? default;
        }

        /// <summary>
        /// Move a unit to a new province.
        /// </summary>
        public void MoveUnit(ushort unitId, ushort newProvinceId)
        {
            var unitSystem = gameState.Units;
            unitSystem?.MoveUnit(unitId, newProvinceId);
        }

        /// <summary>
        /// Disband a unit.
        /// </summary>
        public void DisbandUnit(ushort unitId)
        {
            var unitSystem = gameState.Units;
            unitSystem?.DisbandUnit(unitId, DestructionReason.Disbanded);
        }

        // Core event handlers
        private void OnCoreUnitCreated(UnitCreatedEvent evt)
        {
            OnUnitCreated?.Invoke(evt.UnitID);
        }

        private void OnCoreUnitDestroyed(UnitDestroyedEvent evt)
        {
            OnUnitDestroyed?.Invoke(evt.UnitID);
        }

        private void OnCoreUnitMoved(UnitMovedEvent evt)
        {
            OnUnitMoved?.Invoke(evt.UnitID, evt.OldProvinceID, evt.NewProvinceID);
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

            subscriptions.Dispose();
            isDisposed = true;
        }
    }
}
