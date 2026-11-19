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
        public Color? Color;               // null = engine border color globals (country / province)
        public float Width = 0.6f;         // full strip width in map pixels
        public float MinScreenWidth = 1f;  // full strip width floor in screen pixels
        public float Tile = 0.4f;          // texture repeats per map pixel along the border
        public Vector2 Fade;               // screen px per map px: invisible at x, fully visible at y (0,0 = no fade)
        public int Order;                  // draw order between classes (higher draws on top)
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

        /// <summary>
        /// Style for a class: data file entry, else engine defaults (country thicker, others like province).
        /// </summary>
        public static BorderClassStyle Resolve(string name)
        {
            if (styles.TryGetValue(name, out var s))
                return s;
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
