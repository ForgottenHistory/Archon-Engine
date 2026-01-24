using Core.Data.Ids;
using Core.Validation;

namespace StarterKit.Validation
{
    /// <summary>
    /// STARTERKIT - Extension methods for ValidationBuilder.
    /// Demonstrates how GAME layer can extend ENGINE's fluent validation.
    /// Uses type-safe ProvinceId wrappers for compile-time safety.
    ///
    /// Usage in commands:
    /// public override bool Validate(GameState gs) =>
    ///     Validate.For(gs)
    ///             .Province(provinceId)
    ///             .UnitExists(unitId)
    ///             .ProvinceOwnedByPlayer(provinceId)
    ///             .Result(out var reason);
    /// </summary>
    public static class StarterKitValidationExtensions
    {
        /// <summary>
        /// Validate that a unit exists and is alive.
        /// </summary>
        public static ValidationBuilder UnitExists(this ValidationBuilder v, ushort unitId)
        {
            var units = Initializer.Instance?.UnitSystem;
            if (units == null)
            {
                return v.Fail("UnitSystem not available");
            }

            var unit = units.GetUnit(unitId);
            if (unit.unitCount == 0)
            {
                return v.Fail($"Unit {unitId} does not exist or is dead");
            }

            return v;
        }

        /// <summary>
        /// Validate that a unit type ID is valid.
        /// </summary>
        public static ValidationBuilder UnitTypeExists(this ValidationBuilder v, string unitTypeId)
        {
            var units = Initializer.Instance?.UnitSystem;
            if (units == null)
            {
                return v.Fail("UnitSystem not available");
            }

            if (units.GetUnitType(unitTypeId) == null)
            {
                return v.Fail($"Unit type '{unitTypeId}' does not exist");
            }

            return v;
        }

        /// <summary>
        /// Validate that a province is owned by the current player.
        /// </summary>
        public static ValidationBuilder ProvinceOwnedByPlayer(this ValidationBuilder v, ProvinceId provinceId)
        {
            var units = Initializer.Instance?.UnitSystem;
            if (units == null)
            {
                return v.Fail("UnitSystem not available");
            }

            if (!units.IsProvinceOwnedByPlayer(provinceId))
            {
                return v.Fail($"Province {provinceId} is not owned by player");
            }

            return v;
        }

        /// <summary>
        /// Validate that a building type ID is valid.
        /// </summary>
        public static ValidationBuilder BuildingTypeExists(this ValidationBuilder v, string buildingTypeId)
        {
            var buildings = Initializer.Instance?.BuildingSystem;
            if (buildings == null)
            {
                return v.Fail("BuildingSystem not available");
            }

            if (buildings.GetBuildingType(buildingTypeId) == null)
            {
                return v.Fail($"Building type '{buildingTypeId}' does not exist");
            }

            return v;
        }

        /// <summary>
        /// Validate that a building can be constructed in a province (player only).
        /// </summary>
        public static ValidationBuilder CanConstructBuilding(this ValidationBuilder v, ProvinceId provinceId, string buildingTypeId)
        {
            var buildings = Initializer.Instance?.BuildingSystem;
            if (buildings == null)
            {
                return v.Fail("BuildingSystem not available");
            }

            if (!buildings.CanConstruct(provinceId, buildingTypeId, out string reason))
            {
                return v.Fail(reason);
            }

            return v;
        }

        /// <summary>
        /// Validate that a building can be constructed in a province for a specific country.
        /// Used by both player and AI commands.
        /// </summary>
        public static ValidationBuilder CanConstructBuildingForCountry(this ValidationBuilder v, ProvinceId provinceId, string buildingTypeId, ushort countryId)
        {
            var buildings = Initializer.Instance?.BuildingSystem;
            if (buildings == null)
            {
                return v.Fail("BuildingSystem not available");
            }

            if (!buildings.CanConstructForCountry(provinceId, buildingTypeId, countryId, out string reason))
            {
                return v.Fail(reason);
            }

            return v;
        }

        /// <summary>
        /// Validate that a province is owned by a specific country.
        /// Used by multiplayer commands where country ID is explicit.
        /// </summary>
        public static ValidationBuilder ProvinceOwnedByCountry(this ValidationBuilder v, ProvinceId provinceId, ushort countryId)
        {
            var provinceSystem = v.GameState?.GetComponent<Core.Systems.ProvinceSystem>();
            if (provinceSystem == null)
            {
                return v.Fail("ProvinceSystem not available");
            }

            ushort owner = provinceSystem.GetProvinceOwner(provinceId.Value);
            if (owner != countryId)
            {
                return v.Fail($"Province {provinceId} is not owned by country {countryId} (owned by {owner})");
            }

            return v;
        }

        /// <summary>
        /// Validate that the player has sufficient gold.
        /// </summary>
        public static ValidationBuilder HasGold(this ValidationBuilder v, int amount)
        {
            var economy = Initializer.Instance?.EconomySystem;
            if (economy == null)
            {
                return v.Fail("EconomySystem not available");
            }

            var playerState = Initializer.Instance?.PlayerState;
            if (playerState == null)
            {
                return v.Fail("PlayerState not available");
            }

            int currentGold = economy.GetCountryGoldInt(playerState.PlayerCountryId);
            if (currentGold < amount)
            {
                return v.Fail($"Insufficient gold: need {amount}, have {currentGold}");
            }

            return v;
        }
    }
}
