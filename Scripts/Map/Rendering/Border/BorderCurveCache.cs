using System.Collections.Generic;
using UnityEngine;
using Map.Rendering.Border;

namespace Map.Rendering
{
    /// <summary>
    /// Style information for rendering a border arc.
    /// Static geometry (arcs), dynamic appearance (type, visibility).
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
            color = Color.black,
            thickness = 1.0f,
            visible = true
        };

        public static BorderStyle CountryBorder(Color countryColor) => new BorderStyle
        {
            type = BorderType.Country,
            color = Color.black,
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
        Province,   // Same owner
        Country     // Different owner
    }

    /// <summary>
    /// Caches the border graph (static geometry) and a style per arc (dynamic appearance).
    /// Ownership changes only touch styles; geometry is built once at load.
    /// A province pair can have several arcs (e.g. a border interrupted by a third province).
    /// </summary>
    public class BorderCurveCache
    {
        private readonly List<BorderArc> arcs = new List<BorderArc>();
        private readonly List<BorderStyle> styles = new List<BorderStyle>();
        private readonly Dictionary<ushort, List<int>> provinceToArcs = new Dictionary<ushort, List<int>>();

        public int BorderCount => arcs.Count;
        public IReadOnlyList<BorderArc> Arcs => arcs;

        public void Initialize(List<BorderArc> borderArcs)
        {
            Clear();
            for (int i = 0; i < borderArcs.Count; i++)
            {
                var arc = borderArcs[i];
                arcs.Add(arc);
                styles.Add(BorderStyle.ProvinceBorder);
                AddIndex(arc.ProvinceA, i);
                AddIndex(arc.ProvinceB, i);
            }
            ArchonLogger.Log($"BorderCurveCache: Initialized with {arcs.Count} border arcs", "map_initialization");
        }

        private void AddIndex(ushort province, int arcIndex)
        {
            if (!provinceToArcs.TryGetValue(province, out var list))
                provinceToArcs[province] = list = new List<int>();
            list.Add(arcIndex);
        }

        /// <summary>
        /// Reclassify every arc from current ownership. O(arcs), cheap enough to run on any change.
        /// Arcs touching province 0 (unmapped pixels) are hidden.
        /// </summary>
        public void UpdateAllStyles(System.Func<ushort, ushort> getOwner)
        {
            for (int i = 0; i < arcs.Count; i++)
                styles[i] = Classify(arcs[i], getOwner);
        }

        /// <summary>Reclassify the arcs of one province (after its owner changed).</summary>
        public void UpdateProvinceStyles(ushort provinceID, System.Func<ushort, ushort> getOwner)
        {
            if (!provinceToArcs.TryGetValue(provinceID, out var list))
                return;
            foreach (int i in list)
                styles[i] = Classify(arcs[i], getOwner);
        }

        private static BorderStyle Classify(BorderArc arc, System.Func<ushort, ushort> getOwner)
        {
            if (arc.ProvinceA == 0 || arc.ProvinceB == 0)
                return BorderStyle.Hidden;
            ushort ownerA = getOwner(arc.ProvinceA);
            ushort ownerB = getOwner(arc.ProvinceB);
            // Unowned side (sea, wasteland): thin border, so coastlines don't read as country borders
            if (ownerA != ownerB && ownerA != 0 && ownerB != 0)
                return BorderStyle.CountryBorder(Color.black);
            return BorderStyle.ProvinceBorder;
        }

        /// <summary>All visible arcs with their style.</summary>
        public IEnumerable<(BorderArc arc, BorderStyle style)> GetAllBordersForRendering()
        {
            for (int i = 0; i < arcs.Count; i++)
                if (styles[i].visible)
                    yield return (arcs[i], styles[i]);
        }

        /// <summary>All arcs with their style (for statistics/debugging).</summary>
        public IEnumerable<(BorderArc arc, BorderStyle style)> GetAllBorderStyles()
        {
            for (int i = 0; i < arcs.Count; i++)
                yield return (arcs[i], styles[i]);
        }

        public void Clear()
        {
            arcs.Clear();
            styles.Clear();
            provinceToArcs.Clear();
        }
    }
}
