using System.Collections.Generic;
using System.IO;
using Core;
using Core.Commands;
using Core.Data.Ids;
using StarterKit.Validation;

namespace StarterKit.Commands
{
    /// <summary>
    /// StarterKit command: Queue unit movement along a path.
    /// Uses time-based EU4-style movement (units take X days per province).
    /// For multiplayer sync - all clients queue the same movement orders.
    /// Note: Uses BaseCommand instead of SimpleCommand for custom List serialization.
    /// </summary>
    [Command("queue_movement",
        Description = "Queue unit movement along a path",
        Examples = new[] { "queue_movement 1 5,6,7,10" })]
    public class QueueUnitMovementCommand : BaseCommand
    {
        public ushort UnitId { get; set; }

        /// <summary>
        /// Full path including start province and all waypoints.
        /// Index 0 = current province, Index 1 = first destination, etc.
        /// </summary>
        public List<ushort> Path { get; set; }

        /// <summary>
        /// Days to move between each province (unit speed).
        /// </summary>
        public int MovementDays { get; set; } = 2;

        /// <summary>
        /// Country that owns the unit (for validation).
        /// </summary>
        public ushort CountryId { get; set; }

        private string validationError;

        public override bool Validate(GameState gameState)
        {
            if (Path == null || Path.Count < 2)
            {
                validationError = "Invalid path (need at least 2 provinces)";
                return false;
            }

            var units = Initializer.Instance?.UnitSystem;
            if (units == null)
            {
                validationError = "UnitSystem not available";
                return false;
            }

            // Check unit exists
            var unit = units.GetUnit(UnitId);
            if (unit.unitCount == 0)
            {
                validationError = $"Unit {UnitId} does not exist";
                return false;
            }

            // Check unit belongs to the specified country
            if (unit.countryID != CountryId)
            {
                validationError = $"Unit {UnitId} does not belong to country {CountryId}";
                return false;
            }

            // Check unit is at the start of the path
            if (unit.provinceID != Path[0])
            {
                validationError = $"Unit {UnitId} is not at path start (province {Path[0]})";
                return false;
            }

            return true;
        }

        public override void Execute(GameState gameState)
        {
            var movementQueue = gameState.Units?.MovementQueue;
            if (movementQueue == null)
            {
                ArchonLogger.LogError("QueueUnitMovementCommand: MovementQueue not available", "starter_kit");
                return;
            }

            // Queue movement with full path
            ushort firstDestination = Path[1];
            movementQueue.StartMovement(UnitId, firstDestination, MovementDays, Path);

            LogExecution($"Queued movement for unit {UnitId} along path ({Path.Count} provinces, {MovementDays} days per hop)");
        }

        public override void Undo(GameState gameState)
        {
            // Cancel the movement
            var movementQueue = gameState.Units?.MovementQueue;
            movementQueue?.CancelMovement(UnitId);

            LogExecution($"Cancelled movement for unit {UnitId}");
        }

        public override void Serialize(BinaryWriter writer)
        {
            writer.Write(UnitId);
            writer.Write(MovementDays);
            writer.Write(CountryId);

            // Write path as count + elements
            writer.Write((ushort)(Path?.Count ?? 0));
            if (Path != null)
            {
                foreach (var provinceId in Path)
                {
                    writer.Write(provinceId);
                }
            }
        }

        public override void Deserialize(BinaryReader reader)
        {
            UnitId = reader.ReadUInt16();
            MovementDays = reader.ReadInt32();
            CountryId = reader.ReadUInt16();

            // Read path
            ushort pathCount = reader.ReadUInt16();
            Path = new List<ushort>(pathCount);
            for (int i = 0; i < pathCount; i++)
            {
                Path.Add(reader.ReadUInt16());
            }
        }
    }
}
