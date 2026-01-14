using Core.Data;

namespace Core.AI
{
    /// <summary>
    /// ENGINE: Interface for goal constraints.
    ///
    /// Constraints filter when a goal is applicable.
    /// If any constraint fails, goal returns score 0.
    ///
    /// Benefits over putting logic in Evaluate():
    /// - Declarative (self-documenting)
    /// - Debuggable (can list why goal was skipped)
    /// - Reusable (same constraint on multiple goals)
    ///
    /// GAME layer provides concrete constraints.
    /// </summary>
    public interface IGoalConstraint
    {
        /// <summary>
        /// Constraint name for debugging.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Check if constraint is satisfied.
        /// </summary>
        /// <param name="countryID">Country being evaluated</param>
        /// <param name="gameState">Current game state</param>
        /// <returns>True if constraint passes, false to skip this goal</returns>
        bool IsSatisfied(ushort countryID, GameState gameState);
    }

    /// <summary>
    /// Constraint: Country must have minimum number of provinces.
    /// </summary>
    public class MinProvincesConstraint : IGoalConstraint
    {
        private readonly int minProvinces;

        public string Name => $"MinProvinces({minProvinces})";

        public MinProvincesConstraint(int minProvinces)
        {
            this.minProvinces = minProvinces;
        }

        public bool IsSatisfied(ushort countryID, GameState gameState)
        {
            int count = gameState.Provinces.GetProvinceCountForCountry(countryID);
            return count >= minProvinces;
        }
    }

    /// <summary>
    /// Constraint: Country must have minimum resource amount.
    /// </summary>
    public class MinResourceConstraint : IGoalConstraint
    {
        private readonly ushort resourceID;
        private readonly FixedPoint64 minAmount;
        private readonly string resourceName;

        public string Name => $"MinResource({resourceName}, {minAmount})";

        public MinResourceConstraint(ushort resourceID, FixedPoint64 minAmount, string resourceName = null)
        {
            this.resourceID = resourceID;
            this.minAmount = minAmount;
            this.resourceName = resourceName ?? resourceID.ToString();
        }

        public bool IsSatisfied(ushort countryID, GameState gameState)
        {
            var amount = gameState.Resources.GetResource(countryID, resourceID);
            return amount >= minAmount;
        }
    }

    /// <summary>
    /// Constraint: Country must be at war (or not at war).
    /// </summary>
    public class AtWarConstraint : IGoalConstraint
    {
        private readonly bool mustBeAtWar;

        public string Name => mustBeAtWar ? "MustBeAtWar" : "MustBeAtPeace";

        public AtWarConstraint(bool mustBeAtWar)
        {
            this.mustBeAtWar = mustBeAtWar;
        }

        public bool IsSatisfied(ushort countryID, GameState gameState)
        {
            if (gameState.Diplomacy == null)
                return !mustBeAtWar; // If no diplomacy system, assume peace

            bool isAtWar = gameState.Diplomacy.IsAtWar(countryID);
            return isAtWar == mustBeAtWar;
        }
    }

    /// <summary>
    /// Constraint: Country must be at war with specific country.
    /// </summary>
    public class AtWarWithConstraint : IGoalConstraint
    {
        private readonly ushort targetCountryID;
        private readonly bool mustBeAtWar;

        public string Name => mustBeAtWar ? $"AtWarWith({targetCountryID})" : $"NotAtWarWith({targetCountryID})";

        public AtWarWithConstraint(ushort targetCountryID, bool mustBeAtWar = true)
        {
            this.targetCountryID = targetCountryID;
            this.mustBeAtWar = mustBeAtWar;
        }

        public bool IsSatisfied(ushort countryID, GameState gameState)
        {
            if (gameState.Diplomacy == null)
                return !mustBeAtWar;

            bool isAtWar = gameState.Diplomacy.IsAtWar(countryID, targetCountryID);
            return isAtWar == mustBeAtWar;
        }
    }

    /// <summary>
    /// Constraint: Custom delegate for one-off constraints.
    /// Use sparingly - prefer dedicated constraint classes for reusability.
    /// </summary>
    public class DelegateConstraint : IGoalConstraint
    {
        public delegate bool ConstraintCheck(ushort countryID, GameState gameState);

        private readonly ConstraintCheck check;
        private readonly string name;

        public string Name => name;

        public DelegateConstraint(string name, ConstraintCheck check)
        {
            this.name = name;
            this.check = check;
        }

        public bool IsSatisfied(ushort countryID, GameState gameState)
        {
            return check(countryID, gameState);
        }
    }
}
