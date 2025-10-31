using System.Collections.Generic;
using UnityEngine;

namespace Map.Rendering
{
    /// <summary>
    /// Style information for rendering a border segment
    /// Static geometry (curves), dynamic appearance (colors, thickness)
    /// </summary>
    public struct BorderStyle
    {
        public BorderType type;
        public Color color;
        public float thickness;
        public bool visible;

        public static BorderStyle ProvinceBorder => new BorderStyle
        {
            type = BorderType.Province,
            color = Color.black, // Black for visibility
            thickness = 1.0f,
            visible = true
        };

        public static BorderStyle CountryBorder(Color countryColor) => new BorderStyle
        {
            type = BorderType.Country,
            color = Color.black, // Black for visibility
            thickness = 2.0f,
            visible = true
        };

        public static BorderStyle Hidden => new BorderStyle
        {
            type = BorderType.None,
            color = Color.clear,
            thickness = 0f,
            visible = false
        };
    }

    public enum BorderType
    {
        None,       // No border (hidden)
        Province,   // Same owner (thin gray line)
        Country     // Different owner (thick colored line)
    }

    /// <summary>
    /// Caches pre-computed smooth border polylines and their runtime styles
    /// Separates static geometry (expensive to compute) from dynamic appearance (cheap to update)
    ///
    /// Pattern: Static Geometry + Dynamic Appearance
    /// - Smooth polylines (RDP + Chaikin) computed once at map load
    /// - Styles updated at runtime when ownership changes
    /// </summary>
    public class BorderCurveCache
    {
        // Static geometry: pre-computed smooth polylines (Chaikin smoothed)
        private readonly Dictionary<(ushort, ushort), List<Vector2>> borderPolylines;

        // Dynamic appearance: runtime style for each border
        private readonly Dictionary<(ushort, ushort), BorderStyle> borderStyles;

        // Reverse lookup: province ID -> list of border keys it participates in
        private readonly Dictionary<ushort, List<(ushort, ushort)>> provinceToBorders;

        public int BorderCount => borderPolylines.Count;

        public BorderCurveCache()
        {
            borderPolylines = new Dictionary<(ushort, ushort), List<Vector2>>();
            borderStyles = new Dictionary<(ushort, ushort), BorderStyle>();
            provinceToBorders = new Dictionary<ushort, List<(ushort, ushort)>>();
        }

        /// <summary>
        /// Initialize cache with pre-computed smooth polylines (Chaikin smoothed)
        /// </summary>
        public void Initialize(Dictionary<(ushort, ushort), List<Vector2>> polylines)
        {
            borderPolylines.Clear();
            borderStyles.Clear();
            provinceToBorders.Clear();

            // Store polylines and build reverse lookup
            foreach (var kvp in polylines)
            {
                var (provinceA, provinceB) = kvp.Key;
                List<Vector2> points = kvp.Value;

                // Store polyline points
                borderPolylines[kvp.Key] = points;

                // Default style: province border (will be updated based on ownership)
                borderStyles[kvp.Key] = BorderStyle.ProvinceBorder;

                // Build reverse lookup
                if (!provinceToBorders.ContainsKey(provinceA))
                    provinceToBorders[provinceA] = new List<(ushort, ushort)>();
                if (!provinceToBorders.ContainsKey(provinceB))
                    provinceToBorders[provinceB] = new List<(ushort, ushort)>();

                provinceToBorders[provinceA].Add(kvp.Key);
                provinceToBorders[provinceB].Add(kvp.Key);
            }

            ArchonLogger.Log($"BorderCurveCache: Initialized with {borderPolylines.Count} border polylines", "map_initialization");
        }

        /// <summary>
        /// Update border styles for a province when its ownership changes
        /// Only updates style flags - geometry remains unchanged
        /// </summary>
        public void UpdateProvinceBorderStyles(ushort provinceID, ushort newOwner, System.Func<ushort, ushort> getOwner, System.Func<ushort, Color> getCountryColor)
        {
            if (!provinceToBorders.TryGetValue(provinceID, out var borderKeys))
                return;

            int updatedCount = 0;

            foreach (var borderKey in borderKeys)
            {
                var (provinceA, provinceB) = borderKey;

                // Get owner of the OTHER province in this border
                ushort otherProvince = (provinceA == provinceID) ? provinceB : provinceA;
                ushort otherOwner = getOwner(otherProvince);

                // Determine border style based on ownership
                BorderStyle newStyle;

                if (newOwner == 0 || otherOwner == 0)
                {
                    // One province unowned -> hide border or show as province border
                    newStyle = BorderStyle.ProvinceBorder;
                }
                else if (newOwner == otherOwner)
                {
                    // Same owner -> province border (thin gray)
                    newStyle = BorderStyle.ProvinceBorder;
                }
                else
                {
                    // Different owner -> country border (thick colored)
                    Color countryColor = getCountryColor(newOwner);
                    newStyle = BorderStyle.CountryBorder(countryColor);
                }

                borderStyles[borderKey] = newStyle;
                updatedCount++;
            }

            ArchonLogger.Log($"BorderCurveCache: Updated {updatedCount} border styles for province {provinceID}", "map_rendering");
        }

        /// <summary>
        /// Get all borders for rendering
        /// Returns enumerable of (polyline, style) pairs
        /// </summary>
        public IEnumerable<(List<Vector2> polyline, BorderStyle style)> GetAllBordersForRendering()
        {
            foreach (var kvp in borderPolylines)
            {
                if (borderStyles.TryGetValue(kvp.Key, out BorderStyle style))
                {
                    if (style.visible)
                    {
                        yield return (kvp.Value, style);
                    }
                }
            }
        }

        /// <summary>
        /// Get smooth polyline for a specific border
        /// </summary>
        public List<Vector2> GetPolyline(ushort provinceA, ushort provinceB)
        {
            // Normalize key (smaller ID first)
            var key = provinceA < provinceB ? (provinceA, provinceB) : (provinceB, provinceA);

            borderPolylines.TryGetValue(key, out List<Vector2> points);
            return points;
        }

        /// <summary>
        /// Get style for a specific border
        /// </summary>
        public BorderStyle GetStyle(ushort provinceA, ushort provinceB)
        {
            // Normalize key (smaller ID first)
            var key = provinceA < provinceB ? (provinceA, provinceB) : (provinceB, provinceA);

            if (borderStyles.TryGetValue(key, out BorderStyle style))
                return style;

            return BorderStyle.Hidden;
        }

        /// <summary>
        /// Get all border styles (for debugging/statistics)
        /// </summary>
        public IEnumerable<((ushort, ushort) key, BorderStyle style)> GetAllBorderStyles()
        {
            foreach (var kvp in borderStyles)
            {
                yield return (kvp.Key, kvp.Value);
            }
        }

        /// <summary>
        /// Clear all cached data
        /// </summary>
        public void Clear()
        {
            borderPolylines.Clear();
            borderStyles.Clear();
            provinceToBorders.Clear();
        }
    }
}
