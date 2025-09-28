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
                DominionLogger.LogWarning($"Invalid province ID: {ProvinceId}");
                return false;
            }

            // Check if new owner exists
            if (!ValidateCountryId(gameState, NewOwner))
            {
                DominionLogger.LogWarning($"Invalid country ID: {NewOwner}");
                return false;
            }

            // Check if there's actually a change
            ushort currentOwner = gameState.Provinces.GetProvinceOwner(ProvinceId);
            if (currentOwner == NewOwner)
            {
                DominionLogger.LogWarning($"Province {ProvinceId} already owned by country {NewOwner}");
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
    }

    /// <summary>
    /// Command to change province development level
    /// Handles validation for development changes (buildings, investment, etc.)
    /// </summary>
    public class ChangeProvinceDevelopmentCommand : BaseCommand
    {
        public ushort ProvinceId { get; set; }
        public byte NewDevelopment { get; set; }

        // For undo support
        private byte oldDevelopment;

        public override int Priority => 50; // Medium priority

        public override bool Validate(GameState gameState)
        {
            // Check if province exists
            if (!ValidateProvinceId(gameState, ProvinceId))
            {
                DominionLogger.LogWarning($"Invalid province ID: {ProvinceId}");
                return false;
            }

            // Check if province is ocean (can't develop ocean)
            byte terrain = gameState.Provinces.GetProvinceTerrain(ProvinceId);
            if (terrain == 0) // Ocean terrain
            {
                DominionLogger.LogWarning($"Cannot develop ocean province {ProvinceId}");
                return false;
            }

            // Store old development for undo
            oldDevelopment = gameState.Provinces.GetProvinceDevelopment(ProvinceId);

            // Check if there's actually a change
            if (oldDevelopment == NewDevelopment)
            {
                DominionLogger.LogWarning($"Province {ProvinceId} already has development {NewDevelopment}");
                return false;
            }

            return true;
        }

        public override void Execute(GameState gameState)
        {
            LogExecution($"Changing province {ProvinceId} development from {oldDevelopment} to {NewDevelopment}");

            // Execute the development change
            gameState.Provinces.SetProvinceDevelopment(ProvinceId, NewDevelopment);

            // The ProvinceSystem will emit the appropriate events
        }

        public override void Undo(GameState gameState)
        {
            LogExecution($"Undoing province {ProvinceId} development change back to {oldDevelopment}");

            // Restore previous development
            gameState.Provinces.SetProvinceDevelopment(ProvinceId, oldDevelopment);
        }
    }

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
                DominionLogger.LogWarning("No provinces specified for transfer");
                return false;
            }

            // Check if new owner exists
            if (!ValidateCountryId(gameState, NewOwner))
            {
                DominionLogger.LogWarning($"Invalid country ID: {NewOwner}");
                return false;
            }

            // Validate all provinces and store old owners
            oldOwners = new ushort[ProvinceIds.Length];
            for (int i = 0; i < ProvinceIds.Length; i++)
            {
                if (!ValidateProvinceId(gameState, ProvinceIds[i]))
                {
                    DominionLogger.LogWarning($"Invalid province ID in batch: {ProvinceIds[i]}");
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
    }

    /// <summary>
    /// Command to develop all provinces owned by a country by a certain amount
    /// Useful for technology advances, national decisions, etc.
    /// </summary>
    public class DevelopCountryProvincesCommand : BaseCommand
    {
        public ushort CountryId { get; set; }
        public byte DevelopmentIncrease { get; set; }
        public byte MaxDevelopment { get; set; } = 255; // Cap development

        // For undo support
        private ushort[] affectedProvinces;
        private byte[] oldDevelopmentLevels;

        public override int Priority => 30; // Lower priority for mass development

        public override bool Validate(GameState gameState)
        {
            // Check if country exists
            if (!ValidateCountryId(gameState, CountryId))
            {
                DominionLogger.LogWarning($"Invalid country ID: {CountryId}");
                return false;
            }

            if (DevelopmentIncrease == 0)
            {
                DominionLogger.LogWarning("Development increase cannot be zero");
                return false;
            }

            // Get all provinces owned by this country
            var provinces = gameState.ProvinceQueries.GetCountryProvinces(CountryId, Unity.Collections.Allocator.Temp);

            if (provinces.Length == 0)
            {
                DominionLogger.LogWarning($"Country {CountryId} owns no provinces to develop");
                provinces.Dispose();
                return false;
            }

            // Store provinces and old development levels for undo
            affectedProvinces = new ushort[provinces.Length];
            oldDevelopmentLevels = new byte[provinces.Length];

            for (int i = 0; i < provinces.Length; i++)
            {
                affectedProvinces[i] = provinces[i];
                oldDevelopmentLevels[i] = gameState.Provinces.GetProvinceDevelopment(provinces[i]);
            }

            provinces.Dispose();
            return true;
        }

        public override void Execute(GameState gameState)
        {
            LogExecution($"Developing all provinces of country {CountryId} by +{DevelopmentIncrease}");

            // Develop all provinces
            for (int i = 0; i < affectedProvinces.Length; i++)
            {
                byte currentDev = oldDevelopmentLevels[i];
                byte newDev = (byte)Mathf.Min(currentDev + DevelopmentIncrease, MaxDevelopment);

                if (newDev != currentDev)
                {
                    gameState.Provinces.SetProvinceDevelopment(affectedProvinces[i], newDev);
                }
            }
        }

        public override void Undo(GameState gameState)
        {
            LogExecution($"Undoing development of country {CountryId} provinces");

            // Restore all old development levels
            for (int i = 0; i < affectedProvinces.Length; i++)
            {
                gameState.Provinces.SetProvinceDevelopment(affectedProvinces[i], oldDevelopmentLevels[i]);
            }
        }
    }

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

        /// <summary>
        /// Create command to develop a province
        /// </summary>
        public static ChangeProvinceDevelopmentCommand DevelopProvince(ushort provinceId, byte newDevelopment)
        {
            return new ChangeProvinceDevelopmentCommand
            {
                ProvinceId = provinceId,
                NewDevelopment = newDevelopment
            };
        }

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

        /// <summary>
        /// Create command to develop all provinces of a country
        /// </summary>
        public static DevelopCountryProvincesCommand DevelopCountry(ushort countryId, byte developmentIncrease)
        {
            return new DevelopCountryProvincesCommand
            {
                CountryId = countryId,
                DevelopmentIncrease = developmentIncrease
            };
        }
    }
}