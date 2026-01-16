using System;
using System.Collections.Generic;
using Unity.Collections;
using Core.Data.SparseData;
using Core.SaveLoad;
using Core.Systems;

namespace Core.Units
{
    /// <summary>
    /// Central manager for all units in the game.
    ///
    /// ARCHITECTURE:
    /// - Hot data: NativeArray of UnitState (8 bytes each)
    /// - Sparse mapping: Province → Unit IDs (scales with actual units, not possible units)
    /// - Cold data: Dictionary for rare data (custom names, history, etc.)
    /// - Movement data: Separate NativeArray for movement points (2 bytes per unit)
    ///
    /// PERFORMANCE:
    /// - 10k units × 8 bytes = 80KB hot data
    /// - 10k units × 2 bytes = 20KB movement data
    /// - Sparse collections scale with usage (not possibility)
    /// - GetUnitsInProvince() is O(m) where m = units in province (typically 1-10)
    ///
    /// PERSISTENCE:
    /// - SaveState/LoadState for all data
    /// - Atomic unit ID assignment (deterministic)
    /// - Command pattern ensures multiplayer safety
    /// </summary>
    public class UnitSystem : IDisposable
    {
        // === Hot Data (Fast Access) ===

        private NativeArray<UnitState> units;
        private int unitCount;
        private int capacity;

        // === Movement Queue (Time-Based Movement) ===

        private UnitMovementQueue movementQueue;

        // === Sparse Mappings (Scale with Usage) ===

        /// <summary>Province → Unit IDs mapping (scales with actual units)</summary>
        private SparseCollectionManager<ushort, ushort> provinceUnits;

        /// <summary>Country → Unit IDs mapping (for "list all my units" queries)</summary>
        private SparseCollectionManager<ushort, ushort> countryUnits;

        // === Cold Data (Rare Access) ===

        private Dictionary<ushort, UnitColdData> unitColdData;

        // === ID Management ===

        private HashSet<ushort> availableIDs;  // Recycled IDs from disbanded units
        private ushort nextID;

        // === Events ===

        private EventBus eventBus;

        // === Initialization ===

        public UnitSystem(int initialCapacity = 10000, EventBus eventBus = null)
        {
            this.capacity = initialCapacity;
            this.unitCount = 0;
            this.eventBus = eventBus;

            // Allocate hot data
            units = new NativeArray<UnitState>(capacity, Allocator.Persistent);

            // Initialize movement queue
            movementQueue = new UnitMovementQueue(this, eventBus);

            // Initialize sparse collections
            provinceUnits = new SparseCollectionManager<ushort, ushort>();
            provinceUnits.Initialize("ProvinceUnits", initialCapacity * 2); // Province can have multiple units

            countryUnits = new SparseCollectionManager<ushort, ushort>();
            countryUnits.Initialize("CountryUnits", initialCapacity * 2); // Country can have multiple units

            // Initialize cold data
            unitColdData = new Dictionary<ushort, UnitColdData>();

            // Initialize ID management
            availableIDs = new HashSet<ushort>();
            nextID = 1;  // Start at 1 (0 reserved for "no unit")

            // Subscribe to daily tick for movement processing
            if (eventBus != null)
            {
                eventBus.Subscribe<DailyTickEvent>(OnDailyTick);
            }

            UnityEngine.Debug.Log($"[UnitSystem] Initialized with capacity {capacity}");
        }

        // === Unit Creation ===

        /// <summary>
        /// Create a new unit with specified troop count (RISK-style).
        /// Returns the assigned unit ID (1-65535).
        /// </summary>
        public ushort CreateUnit(ushort provinceID, ushort countryID, ushort unitTypeID, ushort troopCount = 1)
        {
            // Get next available ID
            ushort unitID = AllocateUnitID();

            if (unitID == 0)
            {
                UnityEngine.Debug.LogError("[UnitSystem] Failed to allocate unit ID - capacity exhausted!");
                return 0;
            }

            // Create unit state
            UnitState unitState = UnitState.Create(provinceID, countryID, unitTypeID, troopCount);
            units[unitID] = unitState;
            unitCount++;

            // Update sparse mappings
            provinceUnits.Add(provinceID, unitID);
            countryUnits.Add(countryID, unitID);

            // Emit event
            if (eventBus != null)
            {
                eventBus.Emit(new UnitCreatedEvent
                {
                    UnitID = unitID,
                    ProvinceID = provinceID,
                    CountryID = countryID,
                    UnitTypeID = unitTypeID,
                    UnitCount = troopCount
                });
            }

            UnityEngine.Debug.Log($"[UnitSystem] Created unit {unitID} (Type={unitTypeID}, Count={troopCount}) in province {provinceID} for country {countryID}");
            return unitID;
        }

        /// <summary>
        /// Create a unit with custom stats (for loading saves, reinforcements, etc.)
        /// </summary>
        public ushort CreateUnitWithStats(ushort provinceID, ushort countryID, ushort unitTypeID, ushort unitCount)
        {
            ushort unitID = AllocateUnitID();
            if (unitID == 0) return 0;

            UnitState unitState = UnitState.CreateWithStats(provinceID, countryID, unitTypeID, unitCount);
            units[unitID] = unitState;
            this.unitCount++;

            provinceUnits.Add(provinceID, unitID);
            countryUnits.Add(countryID, unitID);

            // Don't emit event (used for loading saves)

            return unitID;
        }

        // === Unit Destruction ===

        /// <summary>
        /// Disband a unit and recycle its ID.
        /// </summary>
        public void DisbandUnit(ushort unitID, DestructionReason reason = DestructionReason.Disbanded)
        {
            if (unitID == 0 || unitID >= capacity)
            {
                UnityEngine.Debug.LogWarning($"[UnitSystem] Invalid unit ID {unitID}");
                return;
            }

            UnitState unit = units[unitID];

            // Remove from sparse mappings
            provinceUnits.Remove(unit.provinceID, unitID);
            countryUnits.Remove(unit.countryID, unitID);

            // Remove cold data if exists
            unitColdData.Remove(unitID);

            // Clear unit state
            units[unitID] = default;
            unitCount--;

            // Recycle ID
            availableIDs.Add(unitID);

            // Emit event
            if (eventBus != null)
            {
                eventBus.Emit(new UnitDestroyedEvent
                {
                    UnitID = unitID,
                    ProvinceID = unit.provinceID,
                    CountryID = unit.countryID,
                    UnitTypeID = unit.unitTypeID,
                    Reason = reason
                });
            }

            UnityEngine.Debug.Log($"[UnitSystem] Disbanded unit {unitID} (Reason={reason})");
        }

        // === Queries ===

        /// <summary>Get unit state by ID</summary>
        public UnitState GetUnit(ushort unitID)
        {
            if (unitID == 0 || unitID >= capacity)
                return default;
            return units[unitID];
        }

        /// <summary>Does this unit exist?</summary>
        public bool HasUnit(ushort unitID)
        {
            if (unitID == 0 || unitID >= capacity)
                return false;
            return units[unitID].unitCount > 0;  // unitCount=0 means destroyed/empty slot
        }

        /// <summary>Get all unit IDs in a province (O(m) where m = units in province)</summary>
        public List<ushort> GetUnitsInProvince(ushort provinceID)
        {
            var result = new List<ushort>();
            provinceUnits.ProcessValues(provinceID, (unitID) => result.Add(unitID));
            return result;
        }

        /// <summary>Get count of units in a province</summary>
        public int GetUnitCountInProvince(ushort provinceID)
        {
            return provinceUnits.GetCount(provinceID);
        }

        /// <summary>Get all unit IDs owned by a country</summary>
        public List<ushort> GetCountryUnits(ushort countryID)
        {
            var result = new List<ushort>();
            countryUnits.ProcessValues(countryID, (unitID) => result.Add(unitID));
            return result;
        }

        /// <summary>Get count of units owned by a country</summary>
        public int GetCountryUnitCount(ushort countryID)
        {
            return countryUnits.GetCount(countryID);
        }

        /// <summary>Get total unit count in the game</summary>
        public int GetUnitCount() => unitCount;

        // === Movement Queue Access ===

        /// <summary>Get the movement queue for time-based movement</summary>
        public UnitMovementQueue MovementQueue => movementQueue;

        // === Modification ===

        /// <summary>Set unit count (RISK-style troop count)</summary>
        public void SetUnitCount(ushort unitID, ushort count)
        {
            if (!HasUnit(unitID)) return;

            ushort oldCount = units[unitID].unitCount;
            UnitState unit = units[unitID];
            unit.unitCount = count;
            units[unitID] = unit;

            // Emit event
            if (eventBus != null && oldCount != count)
            {
                eventBus.Emit(new UnitCountChangedEvent
                {
                    UnitID = unitID,
                    OldCount = oldCount,
                    NewCount = count
                });
            }

            // Auto-disband if count reaches 0
            if (count == 0)
            {
                DisbandUnit(unitID, DestructionReason.Combat);
            }
        }

        /// <summary>Add troops to unit (reinforcement)</summary>
        public void AddTroops(ushort unitID, ushort amount)
        {
            if (!HasUnit(unitID)) return;
            ushort currentCount = units[unitID].unitCount;
            ushort newCount = (ushort)System.Math.Min(currentCount + amount, ushort.MaxValue);
            SetUnitCount(unitID, newCount);
        }

        /// <summary>Remove troops from unit (combat losses)</summary>
        public void RemoveTroops(ushort unitID, ushort amount)
        {
            if (!HasUnit(unitID)) return;
            ushort currentCount = units[unitID].unitCount;
            ushort newCount = amount >= currentCount ? (ushort)0 : (ushort)(currentCount - amount);
            SetUnitCount(unitID, newCount);
        }

        /// <summary>
        /// Move unit to a new province.
        /// Updates sparse mappings automatically.
        /// </summary>
        public void MoveUnit(ushort unitID, ushort newProvinceID)
        {
            if (!HasUnit(unitID)) return;

            UnitState unit = units[unitID];
            ushort oldProvinceID = unit.provinceID;

            if (oldProvinceID == newProvinceID)
                return;  // Already there

            // Update sparse mapping
            provinceUnits.Remove(oldProvinceID, unitID);
            provinceUnits.Add(newProvinceID, unitID);

            // Update unit state
            unit.provinceID = newProvinceID;
            units[unitID] = unit;

            // Update cold data (track provinces marched)
            if (unitColdData.TryGetValue(unitID, out var coldData))
            {
                coldData.ProvincesMarched++;
            }

            // Emit event
            if (eventBus != null)
            {
                eventBus.Emit(new UnitMovedEvent
                {
                    UnitID = unitID,
                    OldProvinceID = oldProvinceID,
                    NewProvinceID = newProvinceID
                });
            }
        }

        // === Cold Data Access ===

        /// <summary>Get or create cold data for a unit</summary>
        public UnitColdData GetColdData(ushort unitID)
        {
            if (!unitColdData.TryGetValue(unitID, out var coldData))
            {
                coldData = new UnitColdData();
                unitColdData[unitID] = coldData;
            }
            return coldData;
        }

        /// <summary>Check if unit has cold data</summary>
        public bool HasColdData(ushort unitID)
        {
            return unitColdData.ContainsKey(unitID);
        }

        // === ID Management ===

        private ushort AllocateUnitID()
        {
            // Reuse recycled ID if available
            if (availableIDs.Count > 0)
            {
                var enumerator = availableIDs.GetEnumerator();
                enumerator.MoveNext();
                ushort recycledID = enumerator.Current;
                availableIDs.Remove(recycledID);
                return recycledID;
            }

            // Allocate new ID
            if (nextID >= capacity)
            {
                UnityEngine.Debug.LogError($"[UnitSystem] Capacity exhausted! Max units: {capacity}");
                return 0;
            }

            return nextID++;
        }

        private void RestoreUnitCount(int count)
        {
            unitCount = count;
        }

        // === Save/Load ===

        public void SaveState(System.IO.BinaryWriter writer)
        {
            writer.Write(capacity);
            writer.Write(unitCount);
            writer.Write(nextID);

            // Write available IDs
            writer.Write(availableIDs.Count);
            foreach (var id in availableIDs)
            {
                writer.Write(id);
            }

            // Write unit states (only active units to save space)
            writer.Write(unitCount);
            for (ushort i = 1; i < nextID; i++)
            {
                if (HasUnit(i))
                {
                    writer.Write(i);  // Unit ID
                    byte[] stateBytes = units[i].ToBytes();
                    writer.Write(stateBytes);
                }
            }

            // Write cold data (only for units that have it)
            writer.Write(unitColdData.Count);
            foreach (var kvp in unitColdData)
            {
                writer.Write(kvp.Key);  // Unit ID
                SaveUnitColdData(writer, kvp.Value);
            }

            // Write movement queue
            movementQueue.SaveState(writer);

            UnityEngine.Debug.Log($"[UnitSystem] Saved {unitCount} units");
        }

        public void LoadState(System.IO.BinaryReader reader)
        {
            Clear();

            capacity = reader.ReadInt32();
            int savedUnitCount = reader.ReadInt32();
            nextID = reader.ReadUInt16();

            // Reallocate if needed
            if (units.Length < capacity)
            {
                units.Dispose();
                units = new NativeArray<UnitState>(capacity, Allocator.Persistent);
            }

            // Read available IDs
            int availableIDCount = reader.ReadInt32();
            availableIDs.Clear();
            for (int i = 0; i < availableIDCount; i++)
            {
                availableIDs.Add(reader.ReadUInt16());
            }

            // Read unit states
            int unitsToRead = reader.ReadInt32();
            for (int i = 0; i < unitsToRead; i++)
            {
                ushort unitID = reader.ReadUInt16();
                byte[] stateBytes = reader.ReadBytes(8);
                UnitState state = UnitState.FromBytes(stateBytes);

                units[unitID] = state;

                // Rebuild sparse mappings
                provinceUnits.Add(state.provinceID, unitID);
                countryUnits.Add(state.countryID, unitID);
            }

            // Read cold data
            int coldDataCount = reader.ReadInt32();
            unitColdData.Clear();
            for (int i = 0; i < coldDataCount; i++)
            {
                ushort unitID = reader.ReadUInt16();
                UnitColdData coldData = LoadUnitColdData(reader);
                unitColdData[unitID] = coldData;
            }

            // Read movement queue
            movementQueue.LoadState(reader);

            // Restore internal state
            RestoreUnitCount(savedUnitCount);

            UnityEngine.Debug.Log($"[UnitSystem] Loaded {unitCount} units");
        }

        private void SaveUnitColdData(System.IO.BinaryWriter writer, UnitColdData coldData)
        {
            SerializationHelper.WriteString(writer, coldData.CustomName ?? "");
            writer.Write(coldData.CreationTick);
            writer.Write(coldData.TotalKills);
            writer.Write(coldData.BattlesCount);
            writer.Write(coldData.ProvincesMarched);

            // Write recent combat history
            writer.Write(coldData.RecentCombatHistory.Count);
            foreach (var provinceID in coldData.RecentCombatHistory)
            {
                writer.Write(provinceID);
            }
        }

        private UnitColdData LoadUnitColdData(System.IO.BinaryReader reader)
        {
            var coldData = new UnitColdData();
            coldData.CustomName = SerializationHelper.ReadString(reader);
            if (string.IsNullOrEmpty(coldData.CustomName))
                coldData.CustomName = null;

            coldData.CreationTick = reader.ReadUInt64();
            coldData.TotalKills = reader.ReadInt32();
            coldData.BattlesCount = reader.ReadInt32();
            coldData.ProvincesMarched = reader.ReadInt32();

            // Read recent combat history
            int historyCount = reader.ReadInt32();
            coldData.RecentCombatHistory.Clear();
            for (int i = 0; i < historyCount; i++)
            {
                coldData.RecentCombatHistory.Add(reader.ReadUInt16());
            }

            return coldData;
        }

        private void Clear()
        {
            // Clear sparse collections
            provinceUnits.Clear();
            countryUnits.Clear();

            // Clear cold data
            unitColdData.Clear();

            // Clear ID tracking
            availableIDs.Clear();

            // Reset counter
            unitCount = 0;
        }

        // === Daily Tick Processing ===

        private void OnDailyTick(DailyTickEvent evt)
        {
            movementQueue?.ProcessDailyTick();
        }

        // === Disposal ===

        public void Dispose()
        {
            // Unsubscribe from events
            if (eventBus != null)
            {
                eventBus.Unsubscribe<DailyTickEvent>(OnDailyTick);
            }

            if (units.IsCreated)
            {
                units.Dispose();
            }

            provinceUnits?.Dispose();
            countryUnits?.Dispose();
        }
    }
}
