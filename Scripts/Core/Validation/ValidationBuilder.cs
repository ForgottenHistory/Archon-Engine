using Core.Systems;

namespace Core.Validation
{
    /// <summary>
    /// Fluent validation builder for command validation.
    /// Chains validation checks and collects error reasons.
    ///
    /// Usage:
    /// public override bool Validate(GameState gs) =>
    ///     Validate.For(gs)
    ///             .Country(countryId)
    ///             .Province(provinceId)
    ///             .ProvinceOwnedBy(provinceId, countryId)
    ///             .Result(out var reason);
    ///
    /// Extensibility:
    /// GAME layer can add custom validators via extension methods:
    /// public static ValidationBuilder HasGold(this ValidationBuilder v, ...) { ... }
    /// </summary>
    public class ValidationBuilder
    {
        private readonly GameState gameState;
        private bool isValid;
        private string errorReason;

        /// <summary>
        /// Access to GameState for extension methods.
        /// </summary>
        public GameState GameState => gameState;

        internal ValidationBuilder(GameState gs)
        {
            gameState = gs;
            isValid = true;
            errorReason = null;
        }

        /// <summary>
        /// Record a validation failure. Short-circuits further checks.
        /// Used by extension methods to add custom validation logic.
        /// </summary>
        public ValidationBuilder Fail(string reason)
        {
            if (isValid)
            {
                isValid = false;
                errorReason = reason;
            }
            return this;
        }

        /// <summary>
        /// Add a custom validation check.
        /// </summary>
        public ValidationBuilder Check(bool condition, string failureReason)
        {
            if (isValid && !condition)
            {
                isValid = false;
                errorReason = failureReason;
            }
            return this;
        }

        /// <summary>
        /// Get the validation result.
        /// </summary>
        public bool Result(out string reason)
        {
            reason = errorReason;
            return isValid;
        }

        /// <summary>
        /// Get the validation result (without reason).
        /// </summary>
        public bool Result()
        {
            return isValid;
        }

        // ========== CORE VALIDATORS ==========

        /// <summary>
        /// Validate that a country ID is valid and exists.
        /// </summary>
        public ValidationBuilder Country(ushort countryId)
        {
            if (!isValid) return this; // Short-circuit

            if (countryId == 0)
            {
                return Fail("Invalid country ID: 0");
            }

            var countrySystem = gameState.GetComponent<CountrySystem>();
            if (countrySystem == null)
            {
                return Fail("CountrySystem not available");
            }

            if (!countrySystem.HasCountry(countryId))
            {
                return Fail($"Country {countryId} does not exist");
            }

            return this;
        }

        /// <summary>
        /// Validate that a province ID is valid.
        /// </summary>
        public ValidationBuilder Province(ushort provinceId)
        {
            if (!isValid) return this;

            if (provinceId == 0)
            {
                return Fail("Invalid province ID: 0");
            }

            if (gameState.ProvinceQueries == null)
            {
                return Fail("ProvinceQueries not available");
            }

            int provinceCount = gameState.ProvinceQueries.GetTotalProvinceCount();
            if (provinceId >= provinceCount)
            {
                return Fail($"Province {provinceId} out of range (max: {provinceCount - 1})");
            }

            return this;
        }

        /// <summary>
        /// Validate that a province is owned by a specific country.
        /// </summary>
        public ValidationBuilder ProvinceOwnedBy(ushort provinceId, ushort countryId)
        {
            if (!isValid) return this;

            if (gameState.ProvinceQueries == null)
            {
                return Fail("ProvinceQueries not available");
            }

            ushort actualOwner = gameState.ProvinceQueries.GetOwner(provinceId);
            if (actualOwner != countryId)
            {
                return Fail($"Province {provinceId} is owned by {actualOwner}, not {countryId}");
            }

            return this;
        }

        /// <summary>
        /// Validate that a province is unowned (owner == 0).
        /// </summary>
        public ValidationBuilder ProvinceUnowned(ushort provinceId)
        {
            if (!isValid) return this;

            if (gameState.ProvinceQueries == null)
            {
                return Fail("ProvinceQueries not available");
            }

            ushort owner = gameState.ProvinceQueries.GetOwner(provinceId);
            if (owner != 0)
            {
                return Fail($"Province {provinceId} is already owned by {owner}");
            }

            return this;
        }

        /// <summary>
        /// Validate that two countries are not the same.
        /// </summary>
        public ValidationBuilder NotSameCountry(ushort countryA, ushort countryB, string contextMessage = null)
        {
            if (!isValid) return this;

            if (countryA == countryB)
            {
                string msg = contextMessage ?? $"Countries must be different (both are {countryA})";
                return Fail(msg);
            }

            return this;
        }

        /// <summary>
        /// Validate that two provinces are not the same.
        /// </summary>
        public ValidationBuilder NotSameProvince(ushort provinceA, ushort provinceB, string contextMessage = null)
        {
            if (!isValid) return this;

            if (provinceA == provinceB)
            {
                string msg = contextMessage ?? $"Provinces must be different (both are {provinceA})";
                return Fail(msg);
            }

            return this;
        }

        /// <summary>
        /// Validate that two provinces are adjacent.
        /// </summary>
        public ValidationBuilder ProvincesAdjacent(ushort provinceA, ushort provinceB)
        {
            if (!isValid) return this;

            if (gameState.Adjacencies == null)
            {
                return Fail("Adjacencies not available");
            }

            if (!gameState.Adjacencies.IsAdjacent(provinceA, provinceB))
            {
                return Fail($"Provinces {provinceA} and {provinceB} are not adjacent");
            }

            return this;
        }
    }
}
