using System.Collections.Generic;
using UnityEngine;
using ProvinceSystemType = Core.Systems.ProvinceSystem;

namespace Map.Rendering.Border
{
    /// <summary>
    /// ENGINE: Appearance of one border class (e.g. "province", "country", "sea").
    /// Loaded from the visual style data file (borders section); hot reloaded with the style.
    ///
    /// Strips are expanded in the vertex shader: full width is Width map pixels, but never less
    /// than MinScreenWidth screen pixels. The texture (optional) runs along the border (U, tiled by
    /// Tile per map pixel) and across it (V, 0..1), Paradox style: the visible line is whatever part
    /// of V the texture fills. Without a texture the whole strip is a solid anti-aliased line.
    /// </summary>
    public sealed class BorderClassStyle
    {
        public string Name;
        public bool Visible = true;
        public Texture2D Texture;          // null = solid line
        public bool TextureAlphaOnly;      // texture gives only the line shape (alpha); color comes from Color
        public float Softness;             // solid strips: 0 = crisp anti-aliased edge, 1 = soft glow falloff
        public bool Lighten;               // Max blend: overlaps take the brighter value (no doubling where
                                           // wide strips fold at corners); can only brighten what is below
        public Color? Color;               // null = engine border color globals (country / province)
        public float Width = 0.6f;         // full strip width in map pixels
        public float WidthPerLevel;        // extra width per point width level (rivers)
        public float MinScreenWidth = 1f;  // full strip width floor in screen pixels
        public float Tile = 0.4f;          // texture repeats per map pixel along the border
        public Vector2 Fade;               // screen px per map px: invisible at x, fully visible at y (0,0 = no fade)
        public int Order;                  // draw order between classes (higher draws on top)
        public string ShaderName;          // null = Archon/BorderMesh; a custom line shader built on
                                           // Shaders/Includes/BorderMeshCore.hlsl (e.g. a GAME river surface)
    }

    /// <summary>
    /// ENGINE: Current border class styles. Written by VisualStyleDataFile, read by mesh borders.
    /// Version changes on every update so renderers can pick up hot reloads.
    /// </summary>
    public static class BorderClassStyles
    {
        private static readonly Dictionary<string, BorderClassStyle> styles = new Dictionary<string, BorderClassStyle>();

        public static int Version { get; private set; }

        public static void Set(IEnumerable<BorderClassStyle> classStyles)
        {
            styles.Clear();
            if (classStyles != null)
                foreach (var s in classStyles)
                    styles[s.Name] = s;
            Version++;
        }

        public static bool TryGet(string name, out BorderClassStyle style) => styles.TryGetValue(name, out style);

        // Classes the GAME hides at runtime (e.g. a map tool that works on bigger regions hides province borders).
        // Kept apart from the styles, so a style hot reload doesn't bring them back.
        private static readonly HashSet<string> hidden = new HashSet<string>();

        /// <summary>Hide or show a border class at runtime, whatever its style says.</summary>
        public static void SetHidden(string name, bool hide)
        {
            if (string.IsNullOrEmpty(name)) return;
            if (hide ? hidden.Add(name) : hidden.Remove(name)) Version++;
        }

        public static bool IsHidden(string name) => name != null && hidden.Contains(name);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetHidden() => hidden.Clear();

        /// <summary>
        /// Style for a class: data file entry, else engine defaults (country thicker, others like province).
        /// </summary>
        public static BorderClassStyle Resolve(string name)
        {
            if (styles.TryGetValue(name, out var s))
                return s;
            if (name == "river")
                return new BorderClassStyle
                {
                    Name = name,
                    Color = new Color(0.25f, 0.4f, 0.6f, 1f),
                    Width = 0.5f,
                    WidthPerLevel = 0.2f,
                    MinScreenWidth = 1f
                };
            // Highlight outline passes: color tints the highlight color, alpha = opacity
            if (name == BorderClassNames.Highlight)
                return new BorderClassStyle
                {
                    Name = name,
                    Color = UnityEngine.Color.white,
                    Width = 1.5f,
                    MinScreenWidth = 2f,
                    Order = 101
                };
            if (name == BorderClassNames.HighlightGlow)
                return new BorderClassStyle
                {
                    Name = name,
                    Color = new Color(1f, 1f, 1f, 0.5f),
                    Width = 6f,
                    MinScreenWidth = 12f,
                    Softness = 1f,
                    Lighten = true,
                    Order = 100
                };
            bool country = name == BorderClassNames.Country;
            return new BorderClassStyle
            {
                Name = name,
                Width = country ? 1.2f : 0.6f,
                MinScreenWidth = country ? 1.5f : 1f,
                Order = country ? 10 : 0
            };
        }
    }

    /// <summary>Class names produced by the default classifier.</summary>
    public static class BorderClassNames
    {
        public const string Province = "province";
        public const string Country = "country";
        public const string Sea = "sea";
        public const string Coast = "coast";
        /// <summary>Selection/hover outline (IBorderHighlightOutline), not produced by classifiers.</summary>
        public const string Highlight = "highlight";
        /// <summary>Soft glow drawn under the highlight outline.</summary>
        public const string HighlightGlow = "highlight_glow";
    }

    /// <summary>
    /// ENGINE: Decides the class of each border arc. GAME can replace it through
    /// MapRendererRegistry.BorderClassifier (e.g. to add state or region borders).
    /// Return null to hide an arc.
    /// </summary>
    public interface IBorderClassifier
    {
        string Classify(BorderArc arc, ProvinceSystemType provinces);
    }

    /// <summary>
    /// ENGINE default: hidden next to unmapped pixels (ID 0), sea between two ocean provinces,
    /// coast between ocean and land, country between two different owners, otherwise province.
    /// </summary>
    public sealed class DefaultBorderClassifier : IBorderClassifier
    {
        public string Classify(BorderArc arc, ProvinceSystemType provinces)
        {
            if (arc.ProvinceA == 0 || arc.ProvinceB == 0)
                return null;

            var a = provinces.GetProvinceState(arc.ProvinceA);
            var b = provinces.GetProvinceState(arc.ProvinceB);

            if (a.IsOcean && b.IsOcean) return BorderClassNames.Sea;
            if (a.IsOcean || b.IsOcean) return BorderClassNames.Coast;
            if (a.ownerID != b.ownerID && a.ownerID != 0 && b.ownerID != 0) return BorderClassNames.Country;
            return BorderClassNames.Province;
        }
    }
}
