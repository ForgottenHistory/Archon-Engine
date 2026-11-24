using UnityEngine;

namespace Map.Rendering.Atmosphere
{
    /// <summary>
    /// ENGINE: Map atmosphere (haze and drifting clouds), from the "atmosphere" section of the visual
    /// style data file. Zoom ranges are in screen px per map px, the same measure as border and label
    /// fades (small = zoomed out): effects are full at or below x and gone at or above y.
    /// Distances are in map pixels.
    /// </summary>
    public sealed class MapAtmosphereStyle
    {
        public bool Visible = true;

        // Haze: a tint over the whole map that builds up as you zoom out
        public Color HazeColor = new Color(0.36f, 0.42f, 0.52f, 1f);
        public float HazeMax = 0.3f;
        public Vector2 HazeZoom = new Vector2(0.3f, 1.5f);

        // Clouds: procedural noise drifting with the wind, floating above the map
        public Color CloudColor = new Color(0.95f, 0.95f, 0.97f, 1f);
        /// <summary>Colour of the thin cloud edges and undersides.</summary>
        public Color CloudShade = new Color(0.6f, 0.64f, 0.72f, 1f);
        /// <summary>0 = clear sky, 1 = overcast.</summary>
        public float CloudCoverage = 0.42f;
        /// <summary>Edge softness, in noise units.</summary>
        public float CloudSoftness = 0.22f;
        public float CloudOpacity = 0.85f;
        /// <summary>Size of one noise tile, map px (bigger = bigger cloud banks).</summary>
        public float CloudScale = 1400f;
        /// <summary>Drift, map px per second.</summary>
        public Vector2 CloudWind = new Vector2(5f, 1.5f);
        public Vector2 CloudZoom = new Vector2(0.35f, 0.9f);
        /// <summary>Height above the map, map px (parallax with a perspective camera).</summary>
        public float CloudHeight = 60f;

        // Cloud shadows on the ground
        public float ShadowStrength = 0.35f;
        /// <summary>Shadow offset from its cloud, map px (x right, y down).</summary>
        public Vector2 ShadowOffset = new Vector2(-40f, 30f);

        // Masked clouds: where the GAME cloud mask (MapAtmosphereRenderer.SetCloudMask) gathers extra cloud
        public float MaskOpacity = 0.85f;
        /// <summary>Noise tile size for masked clouds, map px (smaller than the cloud banks).</summary>
        public float MaskScale = 300f;
        /// <summary>Ground shadow under masked clouds (more under dark ones).</summary>
        public float MaskShadow = 0.5f;
        /// <summary>Screen px per map px: full at or below x, gone at or above y (they reach closer in than the banks).</summary>
        public Vector2 MaskZoom = new Vector2(2f, 3.5f);
        /// <summary>Edge softness of masked clouds, in noise units (softer than the cloud banks: wispy).</summary>
        public float MaskSoftness = 0.45f;
        /// <summary>How far the mask is warped by noise, map px (billowy outlines instead of the mask's shapes).</summary>
        public float MaskWarp = 60f;
    }

    /// <summary>Current atmosphere style. Version changes on every Set so the renderer can re-apply.</summary>
    public static class MapAtmosphereStyles
    {
        private static MapAtmosphereStyle current = new MapAtmosphereStyle();

        public static MapAtmosphereStyle Current => current;
        public static int Version { get; private set; }

        public static void Set(MapAtmosphereStyle style)
        {
            current = style ?? new MapAtmosphereStyle();
            Version++;
        }
    }
}
