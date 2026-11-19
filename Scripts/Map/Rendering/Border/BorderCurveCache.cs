using System.Collections.Generic;
using Map.Rendering.Border;

namespace Map.Rendering
{
    /// <summary>
    /// Caches the border graph (static geometry) and a class per arc (dynamic appearance).
    /// Ownership changes only reclassify arcs; geometry is built once at load.
    /// A province pair can have several arcs (e.g. a border interrupted by a third province).
    /// </summary>
    public class BorderCurveCache
    {
        public const int Hidden = -1;

        private readonly List<BorderArc> arcs = new List<BorderArc>();
        private readonly List<int> arcClass = new List<int>();
        private readonly List<string> classNames = new List<string>();
        private readonly Dictionary<string, int> classIndex = new Dictionary<string, int>();

        public int BorderCount => arcs.Count;
        public IReadOnlyList<BorderArc> Arcs => arcs;

        /// <summary>Class names in use; index = class id stored per arc.</summary>
        public IReadOnlyList<string> ClassNames => classNames;

        public void Initialize(List<BorderArc> borderArcs)
        {
            arcs.Clear();
            arcClass.Clear();
            arcs.AddRange(borderArcs);
            for (int i = 0; i < arcs.Count; i++)
                arcClass.Add(Hidden);
            ArchonLogger.Log($"BorderCurveCache: Initialized with {arcs.Count} border arcs", "map_initialization");
        }

        /// <summary>Reclassify every arc. O(arcs), cheap enough to run on any ownership change.</summary>
        public void ClassifyAll(System.Func<BorderArc, string> classify)
        {
            for (int i = 0; i < arcs.Count; i++)
                arcClass[i] = IndexOf(classify(arcs[i]));
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
        }
    }
}
