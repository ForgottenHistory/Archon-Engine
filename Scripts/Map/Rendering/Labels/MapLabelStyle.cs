using UnityEngine;

namespace Map.Rendering.Labels
{
    /// <summary>
    /// ENGINE: Map label look and placement tuning, from the "labels" section of the visual style
    /// data file (see VisualStyleDataFile). Font size is in map pixels; the screen-size floor and
    /// zoom fade are applied in the shader.
    /// </summary>
    public sealed class MapLabelStyle
    {
        public bool Visible = true;
        /// <summary>Font file relative to the data directory (.ttf/.otf). Empty = a system font.</summary>
        public string Font = "";
        public bool Uppercase = true;

        public Color Color = new Color(0.91f, 0.9f, 0.95f, 1f);
        public Color ShadowColor = new Color(0.04f, 0.04f, 0.08f, 0.75f);
        /// <summary>Drop shadow offset, fraction of the font size (x right, y down).</summary>
        public Vector2 ShadowOffset = new Vector2(0.04f, 0.05f);
        /// <summary>Shadow edge softness, fraction of the SDF spread (0 = hard, 1 = very soft).</summary>
        public float ShadowSoftness = 0.6f;
        /// <summary>Glyph edge sharpness multiplier (higher = crisper, lower = smoother).</summary>
        public float Sharpness = 1f;

        /// <summary>Labels smaller than this on screen (px, font size) are scaled up about their centre...</summary>
        public float MinScreenHeight = 11f;
        /// <summary>...by at most this factor, and never beyond RoomOverflow * the label's own room
        /// (the inside run it sits in), so long names in small countries don't spill over. Labels that
        /// would need more fade out.</summary>
        public float MaxScale = 2.5f;
        public float RoomOverflow = 1.1f;
        /// <summary>Zoom fade [visible below, hidden above] in screen px per map px (labels fade when zoomed in);
        /// with FadeIn the other way round: [hidden below, visible above] (region labels appear when zoomed in).</summary>
        public Vector2 Fade = new Vector2(1.5f, 2.5f);
        public bool FadeIn;

        /// <summary>Letter spacing: only when the text is shorter than SpaceBelow * the span...</summary>
        public float SpaceBelow = 0.9f;
        /// <summary>...at most MaxGap * font size between letters.</summary>
        public float MaxGap = 0.8f;

        public LabelPlacementSettings Placement = new LabelPlacementSettings();
    }

    /// <summary>Current label styles: country labels and region labels ("regionLabels" section). Version changes
    /// on every Set so renderers can rebuild.</summary>
    public static class MapLabelStyles
    {
        private static MapLabelStyle current = new MapLabelStyle();
        private static MapLabelStyle region = DefaultRegion();

        public static MapLabelStyle Current => current;
        public static MapLabelStyle Region => region;
        public static int Version { get; private set; }

        public static void Set(MapLabelStyle style)
        {
            current = style ?? new MapLabelStyle();
            Version++;
        }

        public static void SetRegion(MapLabelStyle style)
        {
            region = style ?? DefaultRegion();
            Version++;
        }

        /// <summary>Region defaults: appear when zoomed in, smaller than country names.</summary>
        public static MapLabelStyle DefaultRegion()
        {
            var s = new MapLabelStyle { Fade = new Vector2(3f, 5f), FadeIn = true, MaxScale = 1.5f };
            s.Placement.MaxHeight = 24f;
            return s;
        }
    }
}
