using System;
using System.Collections.Generic;
using Core.Data;

namespace Core.Units
{
    /// <summary>
    /// Tracks units that are currently in transit between provinces.
    /// Implements EU4-style time-based movement: units take X days to move.
    ///
    /// DESIGN:
    /// - Dictionary tracks moving units: unitID → MovementState
    /// - Each movement tick (a day by default, see TicksPerDay), decrement ticksRemaining for all moving units
    /// - When ticksRemaining reaches 0, move unit to destination
    /// - Units can cancel movement mid-transit (return to origin)
    ///
    /// PERFORMANCE:
    /// - Sparse storage: Only tracks moving units (not all units)
    /// - Daily tick processes O(n) where n = units currently moving
    /// - Typically 10-100 units moving at once (not 10k)
    /// - ZERO ALLOCATIONS: Pre-allocated buffers for daily tick processing
    /// </summary>
    public class UnitMovementQueue
    {
        /// <summary>
        /// Movement ticks per game day. 1 (default): units step once a day. 24: once an hour, for games
        /// that want finer movement. Set once at startup, before any unit moves.
        /// </summary>
        public int TicksPerDay { get; set; } = 1;

        /// <summary>Whole days left of a movement (rounded up), for display</summary>
        public int DaysRemaining(MovementState state) => (state.ticksRemaining + TicksPerDay - 1) / TicksPerDay;

        /// <summary>
        /// State for a unit currently in transit
        /// </summary>
        public struct MovementState
        {
            public ushort originProvinceID;      // Where unit started
            public ushort destinationProvinceID; // Where unit is going
            public int ticksRemaining;           // Ticks until arrival
            public int totalTicks;               // Original movement time in ticks (for progress %)

            public MovementState(ushort origin, ushort destination, int ticks)
            {
                this.originProvinceID = origin;
                this.destinationProvinceID = destination;
                this.ticksRemaining = ticks;
                this.totalTicks = ticks;
            }

            /// <summary>
            /// Get movement progress as 0-1 value
            /// This is presentation-only (for UI progress bars), doesn't need FixedPoint64 determinism
            /// </summary>
            public float GetProgress()
            {
                if (totalTicks <= 0) return 1f;
                return 1f - ((float)ticksRemaining / totalTicks);
            }
        }

        // === State ===

        private Dictionary<ushort, MovementState> movingUnits;
        private Dictionary<ushort, System.Collections.Generic.Queue<ushort>> unitPaths; // Remaining waypoints for multi-hop paths
        private UnitSystem unitSystem;
        private EventBus eventBus;

        // Pre-allocated buffers for ProcessTick() (zero allocations)
        private List<ushort> arrivedUnitsBuffer;
        private List<KeyValuePair<ushort, MovementState>> updatedStatesBuffer;

        /// <summary>
        /// Optional game policy: return true to stop a unit from entering its destination (e.g. an enemy
        /// holds it). A blocked unit stays at its origin with the movement kept, and is asked again on
        /// every movement tick until the way is clear. Arguments: unit, destination province.
        /// </summary>
        public System.Func<ushort, ushort, bool> ArrivalBlocker { get; set; }

        /// <summary>True when the unit has reached its destination but is held at the origin by ArrivalBlocker.</summary>
        public bool IsBlocked(ushort unitID) => blockedUnits.Contains(unitID);

        /// <summary>
        /// Optional: the movement ticks of one leg, (unit, from, to) -> ticks, for games whose units move at their
        /// own speed over terrain of its own cost. Asked for every leg, the first one of an order and each next
        /// waypoint of a path; a result of 0 or less keeps the ticks the caller gave (or, for later legs, the
        /// previous leg's). Must be deterministic: it runs in the simulation.
        /// </summary>
        public System.Func<ushort, ushort, ushort, int> LegTicks { get; set; }

        private readonly System.Collections.Generic.HashSet<ushort> blockedUnits = new System.Collections.Generic.HashSet<ushort>();

        // === Initialization ===

        public UnitMovementQueue(UnitSystem unitSystem, EventBus eventBus = null)
        {
            this.unitSystem = unitSystem;
            this.eventBus = eventBus;
            this.movingUnits = new Dictionary<ushort, MovementState>();
            this.unitPaths = new Dictionary<ushort, System.Collections.Generic.Queue<ushort>>();

            // Pre-allocate buffers (worst-case: all moving units arrive same day)
            this.arrivedUnitsBuffer = new List<ushort>(100);
            this.updatedStatesBuffer = new List<KeyValuePair<ushort, MovementState>>(100);

            ArchonLogger.Log("[UnitMovementQueue] Initialized (zero-allocation mode)", "core_simulation");
        }

        // === Movement Control ===

        /// <summary>
        /// Start a unit moving toward a destination
        /// Supports multi-hop paths for pathfinding
        /// </summary>
        /// <param name="unitID">Unit to move</param>
        /// <param name="destinationProvinceID">Next waypoint</param>
        /// <param name="movementDays">Days to reach next waypoint</param>
        /// <param name="fullPath">Optional: Full path including all waypoints (for multi-hop movement)</param>
        public void StartMovement(ushort unitID, ushort destinationProvinceID, int movementDays, System.Collections.Generic.List<ushort> fullPath = null)
            => StartMovementTicks(unitID, destinationProvinceID, movementDays * TicksPerDay, fullPath);

        /// <summary>Like StartMovement, with the time to the next waypoint in movement ticks</summary>
        public void StartMovementTicks(ushort unitID, ushort destinationProvinceID, int movementTicks, System.Collections.Generic.List<ushort> fullPath = null)
        {
            int movementDays = (movementTicks + TicksPerDay - 1) / TicksPerDay;
            if (!unitSystem.HasUnit(unitID))
            {
                ArchonLogger.LogWarning($"[UnitMovementQueue] Cannot start movement for non-existent unit {unitID}", "core_simulation");
                return;
            }

            var unit = unitSystem.GetUnit(unitID);
            if (LegTicks != null)
            {
                int leg = LegTicks(unitID, unit.provinceID, destinationProvinceID);
                if (leg > 0)
                {
                    movementTicks = leg;
                    movementDays = (movementTicks + TicksPerDay - 1) / TicksPerDay;
                }
            }

            // Cancel existing movement if any
            if (movingUnits.ContainsKey(unitID))
            {
                ArchonLogger.LogWarning($"[UnitMovementQueue] Unit {unitID} is already moving - cancelling previous movement", "core_simulation");
                CancelMovement(unitID);
            }

            // Store path if provided (for multi-hop movement)
            if (fullPath != null && fullPath.Count > 2)
            {
                // Store remaining waypoints (excluding current province and next hop)
                var pathQueue = new System.Collections.Generic.Queue<ushort>();
                for (int i = 2; i < fullPath.Count; i++) // Start at index 2 (skip current and next)
                {
                    pathQueue.Enqueue(fullPath[i]);
                }
                unitPaths[unitID] = pathQueue;

                if (LogMoves) ArchonLogger.Log($"[UnitMovementQueue] Unit {unitID} multi-hop path: {fullPath.Count} provinces total, {pathQueue.Count} waypoints remaining", "core_simulation");
            }
            // Note: We don't clear the path here if fullPath is null, because:
            // - If this is a continuation of multi-hop journey (from CompleteMovement), we want to keep the path
            // - If this is a new movement over an existing one, CancelMovement() already cleared the path
            // So there's no need to clear here!

            // Add to moving units
            var movementState = new MovementState(unit.provinceID, destinationProvinceID, movementTicks);
            movingUnits[unitID] = movementState;

            if (LogMoves) ArchonLogger.Log($"[UnitMovementQueue] Unit {unitID} started moving {unit.provinceID} → {destinationProvinceID} ({movementTicks} ticks)", "core_simulation");

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
            blockedUnits.Remove(unitID);

            // Clear any remaining path
            if (unitPaths.ContainsKey(unitID))
            {
                unitPaths.Remove(unitID);
            }

            if (LogMoves) ArchonLogger.Log($"[UnitMovementQueue] Cancelled movement for unit {unitID}", "core_simulation");

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
        /// Process a movement tick - advance all movements
        /// Called by UnitSystem once per TicksPerDay step (daily or hourly)
        /// ZERO ALLOCATIONS: Uses pre-allocated buffers
        /// </summary>
        public void ProcessTick()
        {
            if (movingUnits.Count == 0)
                return;

            // CLEAR pre-allocated buffers (zero allocations!)
            arrivedUnitsBuffer.Clear();
            updatedStatesBuffer.Clear();

            // Decrement days remaining for all moving units
            foreach (var kvp in movingUnits)
            {
                ushort unitID = kvp.Key;
                MovementState state = kvp.Value;

                state.ticksRemaining--;

                // Check if unit arrived
                if (state.ticksRemaining <= 0)
                {
                    arrivedUnitsBuffer.Add(unitID);
                }
                else
                {
                    // Collect updated state to apply after iteration
                    updatedStatesBuffer.Add(new KeyValuePair<ushort, MovementState>(unitID, state));
                }
            }

            // Apply updated states
            foreach (var kvp in updatedStatesBuffer)
            {
                movingUnits[kvp.Key] = kvp.Value;
            }

            // Process arrivals
            foreach (ushort unitID in arrivedUnitsBuffer)
            {
                CompleteMovement(unitID);
            }
        }

        /// <summary>
        /// Complete a unit's movement (teleport to destination)
        /// Checks for multi-hop paths and automatically continues to next waypoint
        /// </summary>
        private void CompleteMovement(ushort unitID)
        {
            if (!movingUnits.TryGetValue(unitID, out var movementState))
            {
                return;
            }

            // Remove from queue
            if (ArrivalBlocker != null && ArrivalBlocker(unitID, movementState.destinationProvinceID))
            {
                blockedUnits.Add(unitID);
                movementState.ticksRemaining = 0;
                movingUnits[unitID] = movementState;
                return;
            }
            blockedUnits.Remove(unitID);

            movingUnits.Remove(unitID);

            // Teleport unit to destination
            unitSystem.MoveUnit(unitID, movementState.destinationProvinceID);

            if (LogMoves) ArchonLogger.Log($"[UnitMovementQueue] Unit {unitID} arrived at province {movementState.destinationProvinceID}", "core_simulation");

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

            // Check if unit has more waypoints to visit (multi-hop pathfinding)
            if (unitPaths.TryGetValue(unitID, out var pathQueue) && pathQueue.Count > 0)
            {
                ushort nextWaypoint = pathQueue.Dequeue();
                if (LogMoves) ArchonLogger.Log($"[UnitMovementQueue] Unit {unitID} continuing journey to {nextWaypoint} ({pathQueue.Count} waypoints remaining)", "core_simulation");

                // Start movement to next waypoint: LegTicks decides its time, else it takes as long as the one
                // just finished
                StartMovementTicks(unitID, nextWaypoint, movementState.totalTicks);

                // If no more waypoints after this, remove path
                if (pathQueue.Count == 0)
                {
                    unitPaths.Remove(unitID);
                    if (LogMoves) ArchonLogger.Log($"[UnitMovementQueue] Unit {unitID} completed multi-hop journey", "core_simulation");
                }
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

        // Per-unit movement lines: every order, hop and arrival, so only at Debug level (a war moves hundreds of
        // units an hour, and each console line costs a Debug.Log)
        static bool LogMoves => GameSettings.Instance?.ShouldLog(LogLevel.Debug) ?? false;

        /// <summary>Get all moving unit IDs</summary>
        public IEnumerable<ushort> GetMovingUnits() => movingUnits.Keys;

        /// <summary>
        /// Final province of a unit's current march (the last waypoint, else the current hop's destination);
        /// 0 when it isn't moving. No allocation, unlike GetFullPath: for per-tick queries over many units.
        /// </summary>
        public ushort GetFinalDestination(ushort unitID)
        {
            if (!movingUnits.TryGetValue(unitID, out var movementState))
                return 0;
            ushort last = movementState.destinationProvinceID;
            if (unitPaths.TryGetValue(unitID, out var pathQueue))
                foreach (ushort waypoint in pathQueue)
                    last = waypoint;
            return last;
        }

        /// <summary>
        /// Get full movement path for visualization (current position + all waypoints)
        /// Returns empty list if unit is not moving
        /// </summary>
        public System.Collections.Generic.List<ushort> GetFullPath(ushort unitID)
        {
            var path = new System.Collections.Generic.List<ushort>();

            if (!unitSystem.HasUnit(unitID))
                return path;

            var unit = unitSystem.GetUnit(unitID);

            // Add current position (start of arrow)
            path.Add(unit.provinceID);

            // Add current destination if moving
            if (movingUnits.TryGetValue(unitID, out var movementState))
            {
                path.Add(movementState.destinationProvinceID);
            }

            // Add remaining waypoints
            if (unitPaths.TryGetValue(unitID, out var pathQueue))
            {
                path.AddRange(pathQueue);
            }

            return path;
        }

        /// <summary>Get remaining path for a unit (for UI visualization)</summary>
        public System.Collections.Generic.List<ushort> GetRemainingPath(ushort unitID)
        {
            var path = new System.Collections.Generic.List<ushort>();

            // Add current destination if moving
            if (movingUnits.TryGetValue(unitID, out var movementState))
            {
                path.Add(movementState.destinationProvinceID);
            }

            // Add remaining waypoints
            if (unitPaths.TryGetValue(unitID, out var pathQueue))
            {
                path.AddRange(pathQueue);
            }

            return path;
        }

        // === Save/Load ===

        public void SaveState(System.IO.BinaryWriter writer)
        {
            writer.Write(movingUnits.Count);

            foreach (var kvp in movingUnits)
            {
                writer.Write(kvp.Key); // unitID
                writer.Write(kvp.Value.originProvinceID);
                writer.Write(kvp.Value.destinationProvinceID);
                writer.Write(kvp.Value.ticksRemaining);
                writer.Write(kvp.Value.totalTicks);
            }

            // Save paths for multi-hop movement
            writer.Write(unitPaths.Count);
            foreach (var kvp in unitPaths)
            {
                writer.Write(kvp.Key); // unitID
                writer.Write(kvp.Value.Count); // path length
                foreach (ushort provinceID in kvp.Value)
                {
                    writer.Write(provinceID);
                }
            }

            ArchonLogger.Log($"[UnitMovementQueue] Saved {movingUnits.Count} moving units, {unitPaths.Count} multi-hop paths", "core_simulation");
        }

        public void LoadState(System.IO.BinaryReader reader)
        {
            movingUnits.Clear();
            unitPaths.Clear();

            int count = reader.ReadInt32();

            for (int i = 0; i < count; i++)
            {
                ushort unitID = reader.ReadUInt16();
                ushort origin = reader.ReadUInt16();
                ushort destination = reader.ReadUInt16();
                int ticksRemaining = reader.ReadInt32();
                int totalTicks = reader.ReadInt32();

                var state = new MovementState(origin, destination, totalTicks);
                state.ticksRemaining = ticksRemaining; // Restore progress

                movingUnits[unitID] = state;
            }

            // Load paths for multi-hop movement
            int pathCount = reader.ReadInt32();
            for (int i = 0; i < pathCount; i++)
            {
                ushort unitID = reader.ReadUInt16();
                int pathLength = reader.ReadInt32();

                var pathQueue = new System.Collections.Generic.Queue<ushort>();
                for (int j = 0; j < pathLength; j++)
                {
                    pathQueue.Enqueue(reader.ReadUInt16());
                }

                unitPaths[unitID] = pathQueue;
            }

            ArchonLogger.Log($"[UnitMovementQueue] Loaded {movingUnits.Count} moving units, {unitPaths.Count} multi-hop paths", "core_simulation");
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
