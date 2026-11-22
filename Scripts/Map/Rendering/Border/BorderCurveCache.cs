using System.Collections.Generic;
using Map.Rendering.Border;

namespace Map.Rendering
{
    /// <summary>
    /// Caches the border graph (static geometry) and a class per arc (dynamic appearance).
    /// Ownership changes only reclassify arcs; geometry is built once at load.
    /// A province pair can have several arcs (e.g. a border interrupted by a third province).
    /// Reclassify updates only the arcs of the given provinces (province -> arcs index).
    /// </summary>
    public class BorderCurveCache
    {
        public const int Hidden = -1;

        private readonly List<BorderArc> arcs = new List<BorderArc>();
        private readonly List<int> arcClass = new List<int>();
        private readonly List<string> classNames = new List<string>();
        private readonly Dictionary<string, int> classIndex = new Dictionary<string, int>();
        private readonly Dictionary<ushort, List<int>> arcsOfProvince = new Dictionary<ushort, List<int>>();

        public int BorderCount => arcs.Count;
        public IReadOnlyList<BorderArc> Arcs => arcs;

        /// <summary>Class names in use; index = class id stored per arc.</summary>
        public IReadOnlyList<string> ClassNames => classNames;

        public void Initialize(List<BorderArc> borderArcs)
        {
            arcs.Clear();
            arcClass.Clear();
            arcs.AddRange(borderArcs);
            arcsOfProvince.Clear();
            for (int i = 0; i < arcs.Count; i++)
            {
                arcClass.Add(Hidden);
                AddArcOf(arcs[i].ProvinceA, i);
                if (arcs[i].ProvinceB != arcs[i].ProvinceA) AddArcOf(arcs[i].ProvinceB, i);
            }
            ArchonLogger.Log($"BorderCurveCache: Initialized with {arcs.Count} border arcs", "map_initialization");
        }

        /// <summary>Reclassify every arc. O(arcs), cheap enough to run on any ownership change.</summary>
        public void ClassifyAll(System.Func<BorderArc, string> classify)
        {
            for (int i = 0; i < arcs.Count; i++)
                arcClass[i] = IndexOf(classify(arcs[i]));
        }

        private void AddArcOf(ushort province, int arc)
        {
            if (!arcsOfProvince.TryGetValue(province, out var list))
                arcsOfProvince[province] = list = new List<int>(6);
            list.Add(arc);
        }

        /// <summary>Class id of an arc (Hidden = not drawn).</summary>
        public int ClassOf(int arc) => arcClass[arc];

        /// <summary>
        /// Reclassify only the arcs of these provinces (an ownership change). Returns the arcs whose class
        /// changed, with their old class, sorted by arc index. O(arcs of the provinces).
        /// </summary>
        public List<(int arc, int oldClass)> Reclassify(IEnumerable<ushort> provinces, System.Func<BorderArc, string> classify)
        {
            var seen = new HashSet<int>();
            var changed = new List<(int arc, int oldClass)>();
            foreach (ushort p in provinces)
            {
                if (!arcsOfProvince.TryGetValue(p, out var list)) continue;
                foreach (int i in list)
                {
                    if (!seen.Add(i)) continue;
                    int old = arcClass[i];
                    int now = IndexOf(classify(arcs[i]));
                    if (now == old) continue;
                    arcClass[i] = now;
                    changed.Add((i, old));
                }
            }
            changed.Sort((a, b) => a.arc.CompareTo(b.arc));
            return changed;
        }

        private int IndexOf(string name)
        {
            if (name == null) return Hidden;
            if (!classIndex.TryGetValue(name, out int index))
            {
                index = classNames.Count;
                classNames.Add(name);
                classIndex[name] = index;
            }
            return index;
        }

        /// <summary>All visible arcs with their class id.</summary>
        public IEnumerable<(BorderArc arc, int classId)> GetVisibleArcs()
        {
            for (int i = 0; i < arcs.Count; i++)
                if (arcClass[i] != Hidden)
                    yield return (arcs[i], arcClass[i]);
        }

        /// <summary>Arc count per class id (for logging).</summary>
        public int[] CountPerClass()
        {
            var counts = new int[classNames.Count];
            foreach (int c in arcClass)
                if (c != Hidden) counts[c]++;
            return counts;
        }

        public void Clear()
        {
            arcs.Clear();
            arcClass.Clear();
            arcsOfProvince.Clear();
        }
    }
}
