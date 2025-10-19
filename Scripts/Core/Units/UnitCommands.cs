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
                UnityEngine.Debug.LogError("[CreateUnitCommand] UnitSystem is null");
                return false;
            }

            if (provinceID == 0)
            {
                UnityEngine.Debug.LogError("[CreateUnitCommand] Invalid province ID");
                return false;
            }

            if (countryID == 0)
            {
                UnityEngine.Debug.LogError("[CreateUnitCommand] Invalid country ID");
                return false;
            }

            if (unitTypeID == 0)
            {
                UnityEngine.Debug.LogError("[CreateUnitCommand] Invalid unit type ID");
                return false;
            }

            return true;
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
                UnityEngine.Debug.LogError("[DisbandUnitCommand] UnitSystem is null");
                return false;
            }

            if (!unitSystem.HasUnit(unitID))
            {
                UnityEngine.Debug.LogError($"[DisbandUnitCommand] Unit {unitID} does not exist");
                return false;
            }

            // Verify ownership
            UnitState unit = unitSystem.GetUnit(unitID);
            if (unit.countryID != countryID)
            {
                UnityEngine.Debug.LogError($"[DisbandUnitCommand] Unit {unitID} is owned by country {unit.countryID}, not {countryID}");
                return false;
            }

            return true;
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
    /// Command to move a unit to a new province.
    /// NOTE: Phase 1 just updates location. Phase 2 will add pathfinding, movement time, etc.
    ///
    /// VALIDATION:
    /// - Unit must exist
    /// - Unit must be owned by the executing country
    /// - Target province must be valid
    /// - Phase 2: Check adjacency, movement points, etc.
    ///
    /// EXECUTION:
    /// - Update unit's provinceID
    /// - Update sparse mappings (province → units)
    /// - Emit UnitMovedEvent
    /// </summary>
    public class MoveUnitCommand : BaseCommand
    {
        private readonly UnitSystem unitSystem;
        private readonly ushort unitID;
        private readonly ushort targetProvinceID;
        private readonly ushort countryID;  // For validation

        private ushort oldProvinceID;  // For undo

        public MoveUnitCommand(UnitSystem unitSystem, ushort unitID, ushort targetProvinceID, ushort countryID)
        {
            this.unitSystem = unitSystem;
            this.unitID = unitID;
            this.targetProvinceID = targetProvinceID;
            this.countryID = countryID;
        }

        public override bool Validate(GameState gameState)
        {
            if (unitSystem == null)
            {
                UnityEngine.Debug.LogError("[MoveUnitCommand] UnitSystem is null");
                return false;
            }

            if (!unitSystem.HasUnit(unitID))
            {
                UnityEngine.Debug.LogError($"[MoveUnitCommand] Unit {unitID} does not exist");
                return false;
            }

            UnitState unit = unitSystem.GetUnit(unitID);

            // Verify ownership
            if (unit.countryID != countryID)
            {
                UnityEngine.Debug.LogError($"[MoveUnitCommand] Unit {unitID} is owned by country {unit.countryID}, not {countryID}");
                return false;
            }

            // Phase 2 TODO: Check adjacency, movement points, etc.

            return true;
        }

        public override void Execute(GameState gameState)
        {
            UnitState unit = unitSystem.GetUnit(unitID);
            oldProvinceID = unit.provinceID;

            // Move unit
            unitSystem.MoveUnit(unitID, targetProvinceID);
        }

        public override void Undo(GameState gameState)
        {
            // Move back to old province
            unitSystem.MoveUnit(unitID, oldProvinceID);
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
