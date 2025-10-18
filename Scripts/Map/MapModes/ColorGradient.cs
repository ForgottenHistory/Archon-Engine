using UnityEngine;

namespace Map.MapModes
{
    /// <summary>
    /// ENGINE LAYER - Generic color gradient interpolation system
    ///
    /// Responsibilities:
    /// - Interpolate between color stops based on normalized value (0.0 to 1.0)
    /// - Support any number of color stops (minimum 2)
    /// - Smooth transitions between stops
    ///
    /// Architecture:
    /// - Pure mechanism, no game knowledge
    /// - Reusable across any map mode that needs gradient visualization
    /// - Game layer defines color stops and provides values to normalize
    ///
    /// Performance:
    /// - O(1) evaluation with 2-5 color stops (typical usage)
    /// - No allocations during gradient evaluation
    /// - Color stops stored in managed array (small, infrequent access)
    /// </summary>
    public class ColorGradient
    {
        private readonly Color32[] colorStops;
        private readonly int stopCount;

        /// <summary>
        /// Create a color gradient with specified color stops
        /// </summary>
        /// <param name="stops">Color stops from low (0.0) to high (1.0). Minimum 2 required.</param>
        public ColorGradient(params Color32[] stops)
        {
            if (stops == null || stops.Length < 2)
            {
                ArchonLogger.LogError("ColorGradient: At least 2 color stops required");
                // Fallback to grayscale
                colorStops = new Color32[] { Color.black, Color.white };
                stopCount = 2;
                return;
            }

            colorStops = stops;
            stopCount = stops.Length;
        }

        /// <summary>
        /// Evaluate gradient at normalized value (0.0 to 1.0)
        /// </summary>
        /// <param name="normalizedValue">Value from 0.0 (first color stop) to 1.0 (last color stop)</param>
        /// <returns>Interpolated color at the specified position</returns>
        public Color32 Evaluate(float normalizedValue)
        {
            // Clamp to valid range
            normalizedValue = Mathf.Clamp01(normalizedValue);

            // Special cases for edges
            if (normalizedValue <= 0f)
                return colorStops[0];
            if (normalizedValue >= 1f)
                return colorStops[stopCount - 1];

            // Calculate which segment we're in
            // With N stops, we have N-1 segments
            int segmentCount = stopCount - 1;
            float segmentWidth = 1f / segmentCount;

            // Find segment index and local position within segment
            int segmentIndex = Mathf.FloorToInt(normalizedValue / segmentWidth);

            // Clamp to valid segment (handles edge case where normalizedValue == 1.0)
            if (segmentIndex >= segmentCount)
                segmentIndex = segmentCount - 1;

            // Calculate position within segment (0.0 to 1.0)
            float segmentStart = segmentIndex * segmentWidth;
            float localPosition = (normalizedValue - segmentStart) / segmentWidth;

            // Interpolate between adjacent color stops
            Color32 fromColor = colorStops[segmentIndex];
            Color32 toColor = colorStops[segmentIndex + 1];

            return Color32.Lerp(fromColor, toColor, localPosition);
        }

        /// <summary>
        /// Get the number of color stops in this gradient
        /// </summary>
        public int StopCount => stopCount;

        /// <summary>
        /// Create a common red-to-yellow gradient (useful for development/economy visualization)
        /// </summary>
        public static ColorGradient RedToYellow()
        {
            return new ColorGradient(
                new Color32(139, 0, 0, 255),    // Dark red (VeryLow)
                new Color32(220, 20, 20, 255),  // Red (Low)
                new Color32(255, 140, 0, 255),  // Orange (Medium)
                new Color32(255, 215, 0, 255),  // Gold (High)
                new Color32(255, 255, 0, 255)   // Yellow (VeryHigh)
            );
        }

        /// <summary>
        /// Create a green gradient (useful for positive values like manpower)
        /// </summary>
        public static ColorGradient WhiteToGreen()
        {
            return new ColorGradient(
                new Color32(255, 255, 255, 255), // White (VeryLow)
                new Color32(200, 255, 200, 255), // Light green (Low)
                new Color32(100, 255, 100, 255), // Medium green (Medium)
                new Color32(0, 200, 0, 255),     // Green (High)
                new Color32(0, 128, 0, 255)      // Dark green (VeryHigh)
            );
        }

        /// <summary>
        /// Create a blue gradient (useful for water/naval related values)
        /// </summary>
        public static ColorGradient LightToDeepBlue()
        {
            return new ColorGradient(
                new Color32(173, 216, 230, 255), // Light blue
                new Color32(135, 206, 250, 255), // Sky blue
                new Color32(70, 130, 180, 255),  // Steel blue
                new Color32(30, 144, 255, 255),  // Dodger blue
                new Color32(0, 0, 139, 255)      // Dark blue
            );
        }
    }
}
