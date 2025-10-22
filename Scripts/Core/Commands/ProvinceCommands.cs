using UnityEngine;
using Core.Systems;

namespace Core.Commands
{
    /// <summary>
    /// Command to change province ownership
    /// Handles validation, execution, and undo for province conquest/transfer
    /// </summary>
    public class ChangeProvinceOwnerCommand : BaseCommand
    {
        public ushort ProvinceId { get; set; }
        public ushort NewOwner { get; set; }

        // For undo support
        private ushort oldOwner;

        public override int Priority => 100; // High priority for ownership changes

        public override bool Validate(GameState gameState)
        {
            // Check if province exists
            if (!ValidateProvinceId(gameState, ProvinceId))
            {
                ArchonLogger.LogCoreCommandsWarning($"Invalid province ID: {ProvinceId}");
                return false;
            }

            // Check if new owner exists
            if (!ValidateCountryId(gameState, NewOwner))
            {
                ArchonLogger.LogCoreCommandsWarning($"Invalid country ID: {NewOwner}");
                return false;
            }

            // Check if there's actually a change
            ushort currentOwner = gameState.Provinces.GetProvinceOwner(ProvinceId);
            if (currentOwner == NewOwner)
            {
                ArchonLogger.LogCoreCommandsWarning($"Province {ProvinceId} already owned by country {NewOwner}");
                return false;
            }

            // Store old owner for undo
            oldOwner = currentOwner;

            return true;
        }

        public override void Execute(GameState gameState)
        {
            LogExecution($"Changing province {ProvinceId} ownership from {oldOwner} to {NewOwner}");

            // Execute the ownership change
            gameState.Provinces.SetProvinceOwner(ProvinceId, NewOwner);

            // The ProvinceSystem will emit the appropriate events
        }

        public override void Undo(GameState gameState)
        {
            LogExecution($"Undoing province {ProvinceId} ownership change back to {oldOwner}");

            // Restore previous ownership
            gameState.Provinces.SetProvinceOwner(ProvinceId, oldOwner);
        }

        public override void Serialize(System.IO.BinaryWriter writer)
        {
            writer.Write(ProvinceId);
            writer.Write(NewOwner);
        }

        public override void Deserialize(System.IO.BinaryReader reader)
        {
            ProvinceId = reader.ReadUInt16();
            NewOwner = reader.ReadUInt16();
        }
    }

    // REMOVED: ChangeProvinceDevelopmentCommand - game-specific, moved to Game/Commands/
    // Development is game-specific and should not be in the engine layer
    // Migration: Create HegemonProvinceDevelopmentCommand in Game layer using HegemonProvinceSystem

    /// <summary>
    /// Command to transfer multiple provinces at once (useful for peace deals, vassal integration)
    /// Validates all provinces before executing any changes
    /// </summary>
    public class TransferProvincesCommand : BaseCommand
    {
        public ushort[] ProvinceIds { get; set; }
        public ushort NewOwner { get; set; }

        // For undo support
        private ushort[] oldOwners;

        public override int Priority => 90; // High priority for mass transfers

        public override bool Validate(GameState gameState)
        {
            if (ProvinceIds == null || ProvinceIds.Length == 0)
            {
                ArchonLogger.LogCoreCommandsWarning("No provinces specified for transfer");
                return false;
            }

            // Check if new owner exists
            if (!ValidateCountryId(gameState, NewOwner))
            {
                ArchonLogger.LogCoreCommandsWarning($"Invalid country ID: {NewOwner}");
                return false;
            }

            // Validate all provinces and store old owners
            oldOwners = new ushort[ProvinceIds.Length];
            for (int i = 0; i < ProvinceIds.Length; i++)
            {
                if (!ValidateProvinceId(gameState, ProvinceIds[i]))
                {
                    ArchonLogger.LogCoreCommandsWarning($"Invalid province ID in batch: {ProvinceIds[i]}");
                    return false;
                }

                oldOwners[i] = gameState.Provinces.GetProvinceOwner(ProvinceIds[i]);
            }

            return true;
        }

        public override void Execute(GameState gameState)
        {
            LogExecution($"Transferring {ProvinceIds.Length} provinces to country {NewOwner}");

            // Execute all transfers
            for (int i = 0; i < ProvinceIds.Length; i++)
            {
                gameState.Provinces.SetProvinceOwner(ProvinceIds[i], NewOwner);
            }
        }

        public override void Undo(GameState gameState)
        {
            LogExecution($"Undoing transfer of {ProvinceIds.Length} provinces");

            // Restore all previous ownerships
            for (int i = 0; i < ProvinceIds.Length; i++)
            {
                gameState.Provinces.SetProvinceOwner(ProvinceIds[i], oldOwners[i]);
            }
        }

        public override void Serialize(System.IO.BinaryWriter writer)
        {
            writer.Write(NewOwner);

            // Write province IDs array
            writer.Write(ProvinceIds.Length);
            for (int i = 0; i < ProvinceIds.Length; i++)
            {
                writer.Write(ProvinceIds[i]);
            }
        }

        public override void Deserialize(System.IO.BinaryReader reader)
        {
            NewOwner = reader.ReadUInt16();

            // Read province IDs array
            int count = reader.ReadInt32();
            ProvinceIds = new ushort[count];
            for (int i = 0; i < count; i++)
            {
                ProvinceIds[i] = reader.ReadUInt16();
            }
        }
    }

    // REMOVED: DevelopCountryProvincesCommand - game-specific, moved to Game/Commands/
    // Development is game-specific and should not be in the engine layer
    // Migration: Create HegemonDevelopCountryProvincesCommand in Game layer using HegemonProvinceSystem

    /// <summary>
    /// Command factory for creating common province commands
    /// Provides convenient methods for typical operations
    /// </summary>
    public static class ProvinceCommandFactory
    {
        /// <summary>
        /// Create command to conquer a province
        /// </summary>
        public static ChangeProvinceOwnerCommand ConquerProvince(ushort provinceId, byte conqueror)
        {
            return new ChangeProvinceOwnerCommand
            {
                ProvinceId = provinceId,
                NewOwner = conqueror
            };
        }

        /// <summary>
        /// Create command to transfer province peacefully
        /// </summary>
        public static ChangeProvinceOwnerCommand TransferProvince(ushort provinceId, ushort newOwner)
        {
            return new ChangeProvinceOwnerCommand
            {
                ProvinceId = provinceId,
                NewOwner = newOwner
            };
        }

        // REMOVED: DevelopProvince() - game-specific command factory
        // Migration: Create in Game/Commands/HegemonProvinceCommandFactory

        /// <summary>
        /// Create command to transfer multiple provinces (peace deal, vassal integration)
        /// </summary>
        public static TransferProvincesCommand TransferProvinces(ushort[] provinceIds, ushort newOwner)
        {
            return new TransferProvincesCommand
            {
                ProvinceIds = provinceIds,
                NewOwner = newOwner
            };
        }

        // REMOVED: DevelopCountry() - game-specific command factory
        // Migration: Create in Game/Commands/HegemonProvinceCommandFactory
    }
}