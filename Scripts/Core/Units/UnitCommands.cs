using Core.Commands;

namespace Core.Units
{
    /// <summary>
    /// Command to create a new unit.
    ///
    /// VALIDATION:
    /// - Province must be owned by the country
    /// - Country must have sufficient resources (checked by game layer command factory)
    /// - Unit type must be valid
    ///
    /// EXECUTION:
    /// - Deduct resources (handled by game layer wrapper)
    /// - Create unit in UnitSystem
    /// - Emit UnitCreatedEvent
    /// </summary>
    public class CreateUnitCommand : BaseCommand
    {
        private readonly UnitSystem unitSystem;
        private readonly ushort provinceID;
        private readonly ushort countryID;
        private readonly ushort unitTypeID;

        private ushort createdUnitID;  // Stored after execution for undo
        private string lastValidationError = null;

        public CreateUnitCommand(UnitSystem unitSystem, ushort provinceID, ushort countryID, ushort unitTypeID)
        {
            this.unitSystem = unitSystem;
            this.provinceID = provinceID;
            this.countryID = countryID;
            this.unitTypeID = unitTypeID;
            this.createdUnitID = 0;
        }

        public override bool Validate(GameState gameState)
        {
            // Basic validation - game layer handles resource checks
            if (unitSystem == null)
            {
                lastValidationError = "UnitSystem is null";
                ArchonLogger.LogCoreSimulationError($"[CreateUnitCommand] {lastValidationError}");
                return false;
            }

            if (provinceID == 0)
            {
                lastValidationError = "Invalid province ID";
                ArchonLogger.LogCoreSimulationError($"[CreateUnitCommand] {lastValidationError}");
                return false;
            }

            if (countryID == 0)
            {
                lastValidationError = "Invalid country ID";
                ArchonLogger.LogCoreSimulationError($"[CreateUnitCommand] {lastValidationError}");
                return false;
            }

            if (unitTypeID == 0)
            {
                lastValidationError = "Invalid unit type ID";
                ArchonLogger.LogCoreSimulationError($"[CreateUnitCommand] {lastValidationError}");
                return false;
            }

            lastValidationError = null;
            return true;
        }

        public string GetValidationError(GameState gameState)
        {
            return lastValidationError ?? "Create unit failed validation";
        }

        public string GetSuccessMessage(GameState gameState)
        {
            return $"Unit created in Province {provinceID} (Unit ID: {createdUnitID})";
        }

        public override void Execute(GameState gameState)
        {
            createdUnitID = unitSystem.CreateUnit(provinceID, countryID, unitTypeID);

            if (createdUnitID == 0)
            {
                UnityEngine.Debug.LogError($"[CreateUnitCommand] Failed to create unit in province {provinceID}");
            }
        }

        public override void Undo(GameState gameState)
        {
            if (createdUnitID != 0)
            {
                unitSystem.DisbandUnit(createdUnitID, DestructionReason.Disbanded);
                createdUnitID = 0;
            }
        }

        public override void Serialize(System.IO.BinaryWriter writer)
        {
            writer.Write(provinceID);
            writer.Write(countryID);
            writer.Write(unitTypeID);
            writer.Write(createdUnitID);
        }

        public override void Deserialize(System.IO.BinaryReader reader)
        {
            // Note: This command is created with constructor parameters
            // Deserialization would require a parameterless constructor
            // For now, this is a placeholder
            throw new System.NotImplementedException("CreateUnitCommand deserialization not yet implemented");
        }

        public uint GetChecksum()
        {
            unchecked
            {
                uint hash = 17;
                hash = hash * 31 + provinceID;
                hash = hash * 31 + countryID;
                hash = hash * 31 + unitTypeID;
                return hash;
            }
        }

        public void Dispose()
        {
            // No unmanaged resources
        }

        /// <summary>Get the created unit ID (only valid after Execute())</summary>
        public ushort GetCreatedUnitID() => createdUnitID;
    }

    /// <summary>
    /// Command to disband a unit.
    ///
    /// VALIDATION:
    /// - Unit must exist
    /// - Unit must be owned by the executing country (checked by game layer)
    ///
    /// EXECUTION:
    /// - Remove unit from UnitSystem
    /// - Refund partial resources (handled by game layer wrapper)
    /// - Emit UnitDestroyedEvent
    /// </summary>
    public class DisbandUnitCommand : BaseCommand
    {
        private readonly UnitSystem unitSystem;
        private readonly ushort unitID;
        private readonly ushort countryID;  // For validation

        private UnitState savedUnitState;  // For undo
        private bool wasExecuted;
        private string lastValidationError = null;

        public DisbandUnitCommand(UnitSystem unitSystem, ushort unitID, ushort countryID)
        {
            this.unitSystem = unitSystem;
            this.unitID = unitID;
            this.countryID = countryID;
            this.wasExecuted = false;
        }

        public override bool Validate(GameState gameState)
        {
            if (unitSystem == null)
            {
                lastValidationError = "UnitSystem is null";
                ArchonLogger.LogCoreSimulationError($"[DisbandUnitCommand] {lastValidationError}");
                return false;
            }

            if (!unitSystem.HasUnit(unitID))
            {
                lastValidationError = $"Unit {unitID} does not exist";
                ArchonLogger.LogCoreSimulationError($"[DisbandUnitCommand] {lastValidationError}");
                return false;
            }

            // Verify ownership
            UnitState unit = unitSystem.GetUnit(unitID);
            if (unit.countryID != countryID)
            {
                lastValidationError = $"Unit {unitID} is owned by Country {unit.countryID}, not Country {countryID}";
                ArchonLogger.LogCoreSimulationError($"[DisbandUnitCommand] {lastValidationError}");
                return false;
            }

            lastValidationError = null;
            return true;
        }

        public string GetValidationError(GameState gameState)
        {
            return lastValidationError ?? "Disband unit failed validation";
        }

        public string GetSuccessMessage(GameState gameState)
        {
            return $"Unit {unitID} disbanded";
        }

        public override void Execute(GameState gameState)
        {
            // Save state for undo
            savedUnitState = unitSystem.GetUnit(unitID);
            wasExecuted = true;

            // Disband unit
            unitSystem.DisbandUnit(unitID, DestructionReason.Disbanded);
        }

        public override void Undo(GameState gameState)
        {
            if (!wasExecuted)
                return;

            // Recreate unit with saved state
            ushort recreatedID = unitSystem.CreateUnitWithStats(
                savedUnitState.provinceID,
                savedUnitState.countryID,
                savedUnitState.unitTypeID,
                savedUnitState.strength,
                savedUnitState.morale
            );

            if (recreatedID != unitID)
            {
                UnityEngine.Debug.LogWarning($"[DisbandUnitCommand] Undo created unit with different ID: {recreatedID} (expected {unitID})");
            }

            wasExecuted = false;
        }

        public override void Serialize(System.IO.BinaryWriter writer)
        {
            writer.Write(unitID);
            writer.Write(countryID);
            writer.Write(wasExecuted);
            if (wasExecuted)
            {
                byte[] stateBytes = savedUnitState.ToBytes();
                writer.Write(stateBytes);
            }
        }

        public override void Deserialize(System.IO.BinaryReader reader)
        {
            throw new System.NotImplementedException("DisbandUnitCommand deserialization not yet implemented");
        }

        public uint GetChecksum()
        {
            unchecked
            {
                uint hash = 19;
                hash = hash * 31 + unitID;
                hash = hash * 31 + countryID;
                return hash;
            }
        }

        public void Dispose()
        {
            // No unmanaged resources
        }
    }

    /// <summary>
    /// Command to move a unit to a new province using time-based movement (EU4-style).
    /// Supports pathfinding for multi-province journeys.
    ///
    /// VALIDATION:
    /// - Unit must exist
    /// - Unit must be owned by the executing country
    /// - Pathfinding system must be initialized
    /// - Unit must not already be moving (warning, but allowed)
    ///
    /// EXECUTION:
    /// - Calculate path using PathfindingSystem
    /// - Add unit to movement queue with full path
    /// - Unit will automatically hop through waypoints
    /// - Emit UnitMovementStartedEvent
    /// </summary>
    public class MoveUnitCommand : BaseCommand
    {
        private readonly UnitSystem unitSystem;
        private readonly Core.Systems.PathfindingSystem pathfindingSystem;
        private readonly ushort unitID;
        private readonly ushort targetProvinceID;
        private readonly ushort countryID;  // For validation
        private readonly int movementDays;  // How many days per province hop

        private ushort oldProvinceID;  // For undo
        private bool wasMoving;  // Was unit already moving before this command?
        private string lastValidationError = null;

        public MoveUnitCommand(UnitSystem unitSystem, Core.Systems.PathfindingSystem pathfindingSystem, ushort unitID, ushort targetProvinceID, ushort countryID, int movementDays = 2)
        {
            this.unitSystem = unitSystem;
            this.pathfindingSystem = pathfindingSystem;
            this.unitID = unitID;
            this.targetProvinceID = targetProvinceID;
            this.countryID = countryID;
            this.movementDays = movementDays;
            this.wasMoving = false;
        }

        public override bool Validate(GameState gameState)
        {
            if (unitSystem == null)
            {
                lastValidationError = "UnitSystem is null";
                ArchonLogger.LogCoreSimulationError($"[MoveUnitCommand] {lastValidationError}");
                return false;
            }

            if (pathfindingSystem == null || !pathfindingSystem.IsInitialized)
            {
                lastValidationError = "PathfindingSystem not initialized";
                ArchonLogger.LogCoreSimulationError($"[MoveUnitCommand] {lastValidationError}");
                return false;
            }

            if (!unitSystem.HasUnit(unitID))
            {
                lastValidationError = $"Unit {unitID} does not exist";
                ArchonLogger.LogCoreSimulationError($"[MoveUnitCommand] {lastValidationError}");
                return false;
            }

            UnitState unit = unitSystem.GetUnit(unitID);

            // Verify ownership
            if (unit.countryID != countryID)
            {
                lastValidationError = $"Unit {unitID} is owned by Country {unit.countryID}, not Country {countryID}";
                ArchonLogger.LogCoreSimulationError($"[MoveUnitCommand] {lastValidationError}");
                return false;
            }

            // Check if unit is already moving
            if (unitSystem.MovementQueue.IsUnitMoving(unitID))
            {
                ArchonLogger.LogCoreSimulationWarning($"[MoveUnitCommand] Unit {unitID} is already moving - will cancel previous movement");
                // Allow command to proceed - it will cancel the old movement
            }

            lastValidationError = null;
            return true;
        }

        public string GetValidationError(GameState gameState)
        {
            return lastValidationError ?? "Move unit failed validation";
        }

        public string GetSuccessMessage(GameState gameState)
        {
            return $"Unit {unitID} moving to Province {targetProvinceID}";
        }

        public override void Execute(GameState gameState)
        {
            UnitState unit = unitSystem.GetUnit(unitID);
            oldProvinceID = unit.provinceID;
            wasMoving = unitSystem.MovementQueue.IsUnitMoving(unitID);

            // Calculate path using pathfinding (with unit owner and type for movement validation)
            var path = pathfindingSystem.FindPath(unit.provinceID, targetProvinceID, unit.countryID, unit.unitTypeID);

            if (path == null || path.Count == 0)
            {
                ArchonLogger.LogCoreSimulationWarning($"[MoveUnitCommand] No path found from province {unit.provinceID} to {targetProvinceID}");
                return; // Cannot move - no path exists
            }

            if (path.Count == 1)
            {
                // Already at destination
                ArchonLogger.LogCoreSimulationWarning($"[MoveUnitCommand] Unit {unitID} is already at destination province {targetProvinceID}");
                return;
            }

            // Calculate total journey time
            int totalHops = path.Count - 1;
            int totalDays = totalHops * movementDays;

            ArchonLogger.LogCoreSimulation($"[MoveUnitCommand] Unit {unitID} pathfinding {unit.provinceID} → {targetProvinceID}: {path.Count} provinces, {totalDays} days total");

            // Start time-based movement with full path (EU4-style with pathfinding)
            if (path.Count == 2)
            {
                // Adjacent provinces - simple single-hop movement
                unitSystem.MovementQueue.StartMovement(unitID, path[1], movementDays);
            }
            else
            {
                // Multi-hop journey - pass full path to movement queue
                unitSystem.MovementQueue.StartMovement(unitID, path[1], movementDays, path);
            }
        }

        public override void Undo(GameState gameState)
        {
            // Cancel the movement
            unitSystem.MovementQueue.CancelMovement(unitID);

            // If unit wasn't moving before, it stays at origin
            // If it was moving before, it's now stationary (we don't restore old movement)
        }

        public override void Serialize(System.IO.BinaryWriter writer)
        {
            writer.Write(unitID);
            writer.Write(targetProvinceID);
            writer.Write(countryID);
            writer.Write(oldProvinceID);
        }

        public override void Deserialize(System.IO.BinaryReader reader)
        {
            throw new System.NotImplementedException("MoveUnitCommand deserialization not yet implemented");
        }

        public uint GetChecksum()
        {
            unchecked
            {
                uint hash = 23;
                hash = hash * 31 + unitID;
                hash = hash * 31 + targetProvinceID;
                hash = hash * 31 + countryID;
                return hash;
            }
        }

        public void Dispose()
        {
            // No unmanaged resources
        }
    }
}
