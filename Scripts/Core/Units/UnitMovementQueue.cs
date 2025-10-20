using System;
using System.Collections.Generic;

namespace Core.Units
{
    /// <summary>
    /// Tracks units that are currently in transit between provinces.
    /// Implements EU4-style time-based movement: units take X days to move.
    ///
    /// DESIGN:
    /// - Dictionary tracks moving units: unitID → MovementState
    /// - Each day, decrement daysRemaining for all moving units
    /// - When daysRemaining reaches 0, move unit to destination
    /// - Units can cancel movement mid-transit (return to origin)
    ///
    /// PERFORMANCE:
    /// - Sparse storage: Only tracks moving units (not all units)
    /// - Daily tick processes O(n) where n = units currently moving
    /// - Typically 10-100 units moving at once (not 10k)
    /// </summary>
    public class UnitMovementQueue
    {
        /// <summary>
        /// State for a unit currently in transit
        /// </summary>
        public struct MovementState
        {
            public ushort originProvinceID;      // Where unit started
            public ushort destinationProvinceID; // Where unit is going
            public int daysRemaining;            // Days until arrival
            public int totalDays;                // Original movement time (for progress %)

            public MovementState(ushort origin, ushort destination, int days)
            {
                this.originProvinceID = origin;
                this.destinationProvinceID = destination;
                this.daysRemaining = days;
                this.totalDays = days;
            }

            /// <summary>Get movement progress as 0-1 value</summary>
            public float GetProgress()
            {
                if (totalDays == 0) return 1f;
                return 1f - ((float)daysRemaining / totalDays);
            }
        }

        // === State ===

        private Dictionary<ushort, MovementState> movingUnits;
        private UnitSystem unitSystem;
        private EventBus eventBus;

        // === Initialization ===

        public UnitMovementQueue(UnitSystem unitSystem, EventBus eventBus = null)
        {
            this.unitSystem = unitSystem;
            this.eventBus = eventBus;
            this.movingUnits = new Dictionary<ushort, MovementState>();

            ArchonLogger.Log("[UnitMovementQueue] Initialized");
        }

        // === Movement Control ===

        /// <summary>
        /// Start a unit moving toward a destination
        /// </summary>
        public void StartMovement(ushort unitID, ushort destinationProvinceID, int movementDays)
        {
            if (!unitSystem.HasUnit(unitID))
            {
                ArchonLogger.LogWarning($"[UnitMovementQueue] Cannot start movement for non-existent unit {unitID}");
                return;
            }

            var unit = unitSystem.GetUnit(unitID);

            // Cancel existing movement if any
            if (movingUnits.ContainsKey(unitID))
            {
                ArchonLogger.LogWarning($"[UnitMovementQueue] Unit {unitID} is already moving - cancelling previous movement");
                CancelMovement(unitID);
            }

            // Add to moving units
            var movementState = new MovementState(unit.provinceID, destinationProvinceID, movementDays);
            movingUnits[unitID] = movementState;

            ArchonLogger.Log($"[UnitMovementQueue] Unit {unitID} started moving {unit.provinceID} → {destinationProvinceID} ({movementDays} days)");

            // Emit event (for UI updates)
            if (eventBus != null)
            {
                eventBus.Emit(new UnitMovementStartedEvent
                {
                    UnitID = unitID,
                    OriginProvinceID = unit.provinceID,
                    DestinationProvinceID = destinationProvinceID,
                    MovementDays = movementDays
                });
            }
        }

        /// <summary>
        /// Cancel a unit's movement (unit stays at origin)
        /// </summary>
        public void CancelMovement(ushort unitID)
        {
            if (!movingUnits.TryGetValue(unitID, out var movementState))
            {
                return; // Not moving
            }

            movingUnits.Remove(unitID);

            ArchonLogger.Log($"[UnitMovementQueue] Cancelled movement for unit {unitID}");

            // Emit event
            if (eventBus != null)
            {
                eventBus.Emit(new UnitMovementCancelledEvent
                {
                    UnitID = unitID,
                    OriginProvinceID = movementState.originProvinceID,
                    DestinationProvinceID = movementState.destinationProvinceID
                });
            }
        }

        /// <summary>
        /// Process daily tick - advance all movements
        /// Called by TimeManager on daily tick
        /// </summary>
        public void ProcessDailyTick()
        {
            if (movingUnits.Count == 0)
                return;

            // Collect units that arrive this tick and updated states (can't modify dict during iteration)
            List<ushort> arrivedUnits = new List<ushort>();
            List<KeyValuePair<ushort, MovementState>> updatedStates = new List<KeyValuePair<ushort, MovementState>>();

            // Decrement days remaining for all moving units
            foreach (var kvp in movingUnits)
            {
                ushort unitID = kvp.Key;
                MovementState state = kvp.Value;

                state.daysRemaining--;

                // Check if unit arrived
                if (state.daysRemaining <= 0)
                {
                    arrivedUnits.Add(unitID);
                }
                else
                {
                    // Collect updated state to apply after iteration
                    updatedStates.Add(new KeyValuePair<ushort, MovementState>(unitID, state));
                }
            }

            // Apply updated states
            foreach (var kvp in updatedStates)
            {
                movingUnits[kvp.Key] = kvp.Value;
            }

            // Process arrivals
            foreach (ushort unitID in arrivedUnits)
            {
                CompleteMovement(unitID);
            }
        }

        /// <summary>
        /// Complete a unit's movement (teleport to destination)
        /// </summary>
        private void CompleteMovement(ushort unitID)
        {
            if (!movingUnits.TryGetValue(unitID, out var movementState))
            {
                return;
            }

            // Remove from queue
            movingUnits.Remove(unitID);

            // Teleport unit to destination
            unitSystem.MoveUnit(unitID, movementState.destinationProvinceID);

            ArchonLogger.Log($"[UnitMovementQueue] Unit {unitID} arrived at province {movementState.destinationProvinceID}");

            // Emit event (for UI updates) - Note: UnitMovedEvent already emitted by UnitSystem.MoveUnit()
            if (eventBus != null)
            {
                eventBus.Emit(new UnitMovementCompletedEvent
                {
                    UnitID = unitID,
                    OriginProvinceID = movementState.originProvinceID,
                    DestinationProvinceID = movementState.destinationProvinceID
                });
            }
        }

        // === Queries ===

        /// <summary>Check if a unit is currently moving</summary>
        public bool IsUnitMoving(ushort unitID)
        {
            return movingUnits.ContainsKey(unitID);
        }

        /// <summary>Get movement state for a unit (returns false if not moving)</summary>
        public bool TryGetMovementState(ushort unitID, out MovementState state)
        {
            return movingUnits.TryGetValue(unitID, out state);
        }

        /// <summary>Get count of units currently moving</summary>
        public int GetMovingUnitCount() => movingUnits.Count;

        /// <summary>Get all moving unit IDs</summary>
        public IEnumerable<ushort> GetMovingUnits() => movingUnits.Keys;

        // === Save/Load ===

        public void SaveState(System.IO.BinaryWriter writer)
        {
            writer.Write(movingUnits.Count);

            foreach (var kvp in movingUnits)
            {
                writer.Write(kvp.Key); // unitID
                writer.Write(kvp.Value.originProvinceID);
                writer.Write(kvp.Value.destinationProvinceID);
                writer.Write(kvp.Value.daysRemaining);
                writer.Write(kvp.Value.totalDays);
            }

            ArchonLogger.Log($"[UnitMovementQueue] Saved {movingUnits.Count} moving units");
        }

        public void LoadState(System.IO.BinaryReader reader)
        {
            movingUnits.Clear();

            int count = reader.ReadInt32();

            for (int i = 0; i < count; i++)
            {
                ushort unitID = reader.ReadUInt16();
                ushort origin = reader.ReadUInt16();
                ushort destination = reader.ReadUInt16();
                int daysRemaining = reader.ReadInt32();
                int totalDays = reader.ReadInt32();

                var state = new MovementState(origin, destination, totalDays);
                state.daysRemaining = daysRemaining; // Restore progress

                movingUnits[unitID] = state;
            }

            ArchonLogger.Log($"[UnitMovementQueue] Loaded {movingUnits.Count} moving units");
        }
    }

    // === Events ===

    public struct UnitMovementStartedEvent : IGameEvent
    {
        public float TimeStamp { get; set; }
        public ushort UnitID;
        public ushort OriginProvinceID;
        public ushort DestinationProvinceID;
        public int MovementDays;
    }

    public struct UnitMovementCompletedEvent : IGameEvent
    {
        public float TimeStamp { get; set; }
        public ushort UnitID;
        public ushort OriginProvinceID;
        public ushort DestinationProvinceID;
    }

    public struct UnitMovementCancelledEvent : IGameEvent
    {
        public float TimeStamp { get; set; }
        public ushort UnitID;
        public ushort OriginProvinceID;
        public ushort DestinationProvinceID;
    }
}
