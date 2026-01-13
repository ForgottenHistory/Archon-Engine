using Core;
using Core.Commands;
using UnityEngine;
using Utils;

namespace StarterKit.Commands
{
    /// <summary>
    /// StarterKit command: Create a unit at a province.
    /// </summary>
    [Command("create_unit",
        Aliases = new[] { "unit", "spawn" },
        Description = "Create a unit at a province",
        Examples = new[] { "create_unit infantry 5", "unit cavalry 10" })]
    public class CreateUnitCommand : SimpleCommand
    {
        [Arg(0, "unitType")]
        public string UnitTypeId { get; set; }

        [Arg(1, "provinceId")]
        public ushort ProvinceId { get; set; }

        // For undo
        private ushort createdUnitId;

        public override bool Validate(GameState gameState)
        {
            var initializer = Object.FindFirstObjectByType<Initializer>();
            if (initializer?.UnitSystem == null)
                return false;

            // Check unit type exists
            if (initializer.UnitSystem.GetUnitType(UnitTypeId) == null)
                return false;

            // Check province is owned by player
            if (!initializer.UnitSystem.IsProvinceOwnedByPlayer(ProvinceId))
                return false;

            return true;
        }

        public override void Execute(GameState gameState)
        {
            var initializer = Object.FindFirstObjectByType<Initializer>();
            if (initializer?.UnitSystem == null)
            {
                ArchonLogger.LogError("CreateUnitCommand: UnitSystem not found", "starter_kit");
                return;
            }

            createdUnitId = initializer.UnitSystem.CreateUnit(ProvinceId, UnitTypeId);

            if (createdUnitId != 0)
                LogExecution($"Created {UnitTypeId} (ID={createdUnitId}) in province {ProvinceId}");
            else
                ArchonLogger.LogWarning($"CreateUnitCommand: Failed to create {UnitTypeId}", "starter_kit");
        }

        public override void Undo(GameState gameState)
        {
            if (createdUnitId == 0) return;

            var initializer = Object.FindFirstObjectByType<Initializer>();
            initializer?.UnitSystem?.DisbandUnit(createdUnitId);
            LogExecution($"Undid unit creation (disbanded {createdUnitId})");
        }
    }
}
