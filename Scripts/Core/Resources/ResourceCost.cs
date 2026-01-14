using System;
using Core.Data;

namespace Core.Resources
{
    /// <summary>
    /// ENGINE: Represents a resource cost (resourceId + amount).
    ///
    /// Used for cost validation and atomic spending operations.
    /// Zero-allocation struct for hot path usage.
    ///
    /// Usage:
    /// <code>
    /// var costs = new[] {
    ///     ResourceCost.Create(goldId, 100),
    ///     ResourceCost.Create(manpowerId, 50)
    /// };
    /// if (resourceSystem.CanAfford(countryId, costs)) {
    ///     resourceSystem.TrySpend(countryId, costs);
    /// }
    /// </code>
    /// </summary>
    public readonly struct ResourceCost : IEquatable<ResourceCost>
    {
        /// <summary>The resource type ID</summary>
        public readonly ushort ResourceId;

        /// <summary>The amount required (always positive)</summary>
        public readonly FixedPoint64 Amount;

        /// <summary>
        /// Create a resource cost
        /// </summary>
        public ResourceCost(ushort resourceId, FixedPoint64 amount)
        {
            ResourceId = resourceId;
            Amount = amount.IsNegative ? -amount : amount; // Ensure positive
        }

        /// <summary>
        /// Factory method for cleaner syntax
        /// </summary>
        public static ResourceCost Create(ushort resourceId, FixedPoint64 amount)
        {
            return new ResourceCost(resourceId, amount);
        }

        /// <summary>
        /// Factory method from integer amount
        /// </summary>
        public static ResourceCost Create(ushort resourceId, int amount)
        {
            return new ResourceCost(resourceId, FixedPoint64.FromInt(amount));
        }

        /// <summary>
        /// Check if this cost is zero (free)
        /// </summary>
        public bool IsZero => Amount.IsZero;

        /// <summary>
        /// Create a scaled version of this cost (e.g., for discounts)
        /// </summary>
        public ResourceCost Scale(FixedPoint64 multiplier)
        {
            return new ResourceCost(ResourceId, Amount * multiplier);
        }

        /// <summary>
        /// Create a cost with added amount
        /// </summary>
        public ResourceCost Add(FixedPoint64 additionalAmount)
        {
            return new ResourceCost(ResourceId, Amount + additionalAmount);
        }

        // === Equality ===

        public bool Equals(ResourceCost other)
        {
            return ResourceId == other.ResourceId && Amount == other.Amount;
        }

        public override bool Equals(object obj)
        {
            return obj is ResourceCost other && Equals(other);
        }

        public override int GetHashCode()
        {
            return (ResourceId, Amount.RawValue).GetHashCode();
        }

        public static bool operator ==(ResourceCost left, ResourceCost right) => left.Equals(right);
        public static bool operator !=(ResourceCost left, ResourceCost right) => !left.Equals(right);

        public override string ToString()
        {
            return $"ResourceCost({ResourceId}: {Amount})";
        }
    }

    /// <summary>
    /// Helper methods for working with ResourceCost arrays
    /// </summary>
    public static class ResourceCostExtensions
    {
        /// <summary>
        /// Calculate total cost for a specific resource from an array of costs
        /// </summary>
        public static FixedPoint64 GetTotalForResource(this ResourceCost[] costs, ushort resourceId)
        {
            FixedPoint64 total = FixedPoint64.Zero;
            for (int i = 0; i < costs.Length; i++)
            {
                if (costs[i].ResourceId == resourceId)
                {
                    total += costs[i].Amount;
                }
            }
            return total;
        }

        /// <summary>
        /// Check if costs array contains a specific resource
        /// </summary>
        public static bool ContainsResource(this ResourceCost[] costs, ushort resourceId)
        {
            for (int i = 0; i < costs.Length; i++)
            {
                if (costs[i].ResourceId == resourceId)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Scale all costs by a multiplier (e.g., for discounts)
        /// </summary>
        public static ResourceCost[] Scale(this ResourceCost[] costs, FixedPoint64 multiplier)
        {
            var scaled = new ResourceCost[costs.Length];
            for (int i = 0; i < costs.Length; i++)
            {
                scaled[i] = costs[i].Scale(multiplier);
            }
            return scaled;
        }
    }
}
