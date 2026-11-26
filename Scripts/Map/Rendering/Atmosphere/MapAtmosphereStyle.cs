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
        /// <summary>The sky light on the clouds: the colour of their shaded sides and valleys.</summary>
        public Color CloudShade = new Color(0.6f, 0.64f, 0.72f, 1f);
        /// <summary>The share of the sky under cloud: 0 = clear, 1 = overcast.</summary>
        public float CloudCoverage = 0.22f;
        /// <summary>How see-through the clouds are: opacity is 1 - exp(-thickness / softness), thickness 0..1 of the
        /// cloud's height. 0.8 lets the map show through all but the thickest cloud; 0.1 is nearly solid.</summary>
        public float CloudSoftness = 0.8f;
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
        /// <summary>False: no masked clouds at all (nor their shadows and lights); the cloud banks don't part for them.</summary>
        public bool MaskVisible = true;
        public float MaskOpacity = 0.85f;
        /// <summary>Noise tile size for masked clouds, map px (smaller than the cloud banks).</summary>
        public float MaskScale = 300f;
        /// <summary>Ground shadow under masked clouds (more under dark ones).</summary>
        public float MaskShadow = 0.5f;
        /// <summary>Screen px per map px: full at or below x, gone at or above y (they reach closer in than the banks).</summary>
        public Vector2 MaskZoom = new Vector2(2f, 3.5f);
        /// <summary>Where masked clouds come in from the far view: none at or below x, full at or above y. Negative
        /// (the default): they start only where the cloud banks are fully gone (CloudZoom.y) and are full at 1.6
        /// times that zoom, so the two are never on screen together. [0, 0] shows them at every distance.</summary>
        public Vector2 MaskZoomIn = new Vector2(-1f, -1f);
        /// <summary>The same for masked clouds.</summary>
        public float MaskSoftness = 0.6f;
        /// <summary>How far the mask is warped by noise, map px (billowy outlines instead of the mask's shapes).</summary>
        public float MaskWarp = 60f;

        // Volume: clouds are a height field over a flat base, with parallax and sun light (see the shader)
        /// <summary>Thickness of the cloud banks from base to highest top, map px (about 5% of CloudScale looks right).</summary>
        public float CloudThickness = 40f;
        /// <summary>Thickness of the masked clouds, map px.</summary>
        public float MaskThickness = 10f;
        /// <summary>Parallax steps through the cloud (the cost of the clouds: 2 texture samples each). 0 = no
        /// parallax, the clouds are only lit.</summary>
        public int CloudSteps = 10;
        /// <summary>How strongly the billows show in the light (0 = evenly lit).</summary>
        public float CloudRelief = 0.7f;
        /// <summary>0 = smooth domes, towards 1 = lumpy billows that also eat the cloud's edge.</summary>
        public float CloudBillows = 0.5f;
        /// <summary>How fast the swirling flow stirs the clouds, map px per second (0 = rigid shapes that only
        /// drift). Every part of a cloud moves, in eddies with no common direction; faster also means each cloud
        /// forms and dissolves sooner.</summary>
        public float CloudChurn = 2f;
        /// <summary>Drift of the masked clouds, map px per second. None by default: weather stays over its region
        /// and is stirred in place; only the cloud banks ride the wind.</summary>
        public Vector2 MaskWind = Vector2.zero;
        /// <summary>Colour variance inside a cloud (0 = one colour): a darker dense heart, broad lighter and darker
        /// patches, a little cooler and warmer grey, lighter wisps. Full on masked clouds, 40% on the banks.</summary>
        public float CloudVariance = 0.6f;
        /// <summary>Thin cloud around the towers' base (0 = none).</summary>
        public float CloudVeil = 1f;
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
