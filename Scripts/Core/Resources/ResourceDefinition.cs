using System;
using Core.Data;
using UnityEngine;

namespace Core.Resources
{
    /// <summary>
    /// ENGINE LAYER: Defines a single resource type (gold, manpower, prestige, etc.)
    ///
    /// This is the data structure that represents a resource's properties.
    /// Resources are loaded from JSON5 files and registered in ResourceRegistry.
    ///
    /// Architecture:
    /// - Engine layer provides the mechanism (ResourceDefinition + ResourceSystem)
    /// - Game layer provides the policy (which resources exist, their properties)
    /// - Data files define specific resources (gold, manpower, etc.)
    /// </summary>
    [Serializable]
    public class ResourceDefinition
    {
        /// <summary>
        /// Unique identifier for this resource (e.g., "gold", "manpower")
        /// Used in commands, save files, and lookups
        /// </summary>
        public string id;

        /// <summary>
        /// Display name for UI (e.g., "Gold", "Manpower")
        /// </summary>
        public string displayName;

        /// <summary>
        /// Icon identifier for UI (e.g., "icon_gold", "icon_manpower")
        /// Maps to sprite resources
        /// </summary>
        public string icon;

        /// <summary>
        /// Starting amount for all countries at game start
        /// </summary>
        public float startingAmount;

        /// <summary>
        /// Minimum value this resource can have (default: 0)
        /// Can be negative for resources like prestige
        /// </summary>
        public float minValue;

        /// <summary>
        /// Maximum value this resource can have (default: infinity)
        /// Use 0 for no maximum
        /// </summary>
        public float maxValue;

        /// <summary>
        /// Color for UI display (hex format: #FFD700 for gold)
        /// </summary>
        public string color;

        /// <summary>
        /// Optional: Category for grouping (e.g., "economic", "military", "diplomatic")
        /// </summary>
        public string category;

        /// <summary>
        /// Optional: Tooltip description for UI
        /// </summary>
        public string description;

        /// <summary>
        /// Validate this resource definition (called after loading from JSON5)
        /// </summary>
        public bool Validate(out string errorMessage)
        {
            errorMessage = null;

            if (string.IsNullOrWhiteSpace(id))
            {
                errorMessage = "Resource ID cannot be empty";
                return false;
            }

            if (string.IsNullOrWhiteSpace(displayName))
            {
                errorMessage = $"Resource '{id}' missing display name";
                return false;
            }

            if (minValue > maxValue && maxValue != 0)
            {
                errorMessage = $"Resource '{id}' has minValue ({minValue}) > maxValue ({maxValue})";
                return false;
            }

            if (startingAmount < minValue || (maxValue != 0 && startingAmount > maxValue))
            {
                errorMessage = $"Resource '{id}' starting amount ({startingAmount}) outside valid range [{minValue}, {maxValue}]";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Get FixedPoint64 value from float (for deterministic storage)
        /// </summary>
        public FixedPoint64 GetStartingAmountFixed()
        {
            return FixedPoint64.FromFloat(startingAmount);
        }

        /// <summary>
        /// Get FixedPoint64 min value
        /// </summary>
        public FixedPoint64 GetMinValueFixed()
        {
            return FixedPoint64.FromFloat(minValue);
        }

        /// <summary>
        /// Get FixedPoint64 max value
        /// </summary>
        public FixedPoint64 GetMaxValueFixed()
        {
            return maxValue == 0 ? FixedPoint64.MaxValue : FixedPoint64.FromFloat(maxValue);
        }

        /// <summary>
        /// Get Unity Color from hex string
        /// </summary>
        public Color GetColor()
        {
            if (string.IsNullOrWhiteSpace(color))
            {
                return Color.white;
            }

            if (ColorUtility.TryParseHtmlString(color, out Color parsedColor))
            {
                return parsedColor;
            }

            return Color.white;
        }
    }
}
