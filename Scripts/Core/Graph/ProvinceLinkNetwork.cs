using System.Collections.Generic;

namespace Core.Graph
{
    /// <summary>
    /// ENGINE: a network of links between provinces, each with a level (1..MaxLevel), apart from the province
    /// adjacency graph. Railways, roads, canals, trade routes: the GAME decides what a link means and what its
    /// level does; the engine stores the links and answers path questions over them.
    ///
    /// Storage: links in insertion order (a, b with a &lt; b, a level), an index by province pair, and a compact
    /// per-province list of links (rebuilt lazily after a link is added). A level of 0 means the link is gone:
    /// SetLevel(a, b, 0) removes it without moving other links, so link indices stay stable.
    ///
    /// WidestPaths: from one source province, the best bottleneck level to every province reachable over links
    /// whose provinces the caller allows (the path whose lowest link level is highest). A bucket queue by level,
    /// highest first: O(links + provinces), no heap. Deterministic: links are visited in a fixed order.
    ///
    /// Managed and single-threaded: meant for networks of thousands of links, recomputed when something changes
    /// (daily at most), not for per-frame or Burst use. Version goes up on every change, for callers' caches.
    /// </summary>
    public sealed class ProvinceLinkNetwork
    {
        readonly List<ushort> linkA = new List<ushort>();
        readonly List<ushort> linkB = new List<ushort>();
        readonly List<byte> linkLevel = new List<byte>();
        readonly Dictionary<uint, int> index = new Dictionary<uint, int>();

        // per province links (CSR): links of p are adjLink[adjStart[p] .. adjStart[p + 1]]
        int[] adjStart = System.Array.Empty<int>();
        int[] adjLink = System.Array.Empty<int>();
        bool adjDirty = true;
        int maxProvince = -1;

        /// <summary>The highest level a link can have.</summary>
        public int MaxLevel { get; }

        /// <summary>Goes up on every change (a link added, removed or re-levelled).</summary>
        public int Version { get; private set; }

        public ProvinceLinkNetwork(int maxLevel)
        {
            if (maxLevel < 1 || maxLevel > 100) throw new System.ArgumentOutOfRangeException(nameof(maxLevel));
            MaxLevel = maxLevel;
        }

        /// <summary>Links ever added (indices 0..LinkCount-1; a removed one has level 0).</summary>
        public int LinkCount => linkA.Count;
        public ushort From(int link) => linkA[link];
        public ushort To(int link) => linkB[link];
        public int Level(int link) => linkLevel[link];

        /// <summary>The other end of a link from one of its provinces.</summary>
        public ushort Other(int link, ushort province) => linkA[link] == province ? linkB[link] : linkA[link];

        /// <summary>The link between two provinces, -1 when there is none (or it was removed).</summary>
        public int Find(ushort a, ushort b) => index.TryGetValue(Key(a, b), out int i) && linkLevel[i] > 0 ? i : -1;

        /// <summary>The level of the link between two provinces, 0 when there is none.</summary>
        public int Level(ushort a, ushort b) => index.TryGetValue(Key(a, b), out int i) ? linkLevel[i] : 0;

        /// <summary>
        /// Set the level of the link between two bordering provinces (added when new; 0 removes it). Levels are clamped
        /// to MaxLevel. Returns the link index, or -1 when nothing was or is there.
        /// </summary>
        public int SetLevel(ushort a, ushort b, int level)
        {
            if (a == b) return -1;
            level = System.Math.Max(0, System.Math.Min(MaxLevel, level));
            uint key = Key(a, b);
            if (index.TryGetValue(key, out int i))
            {
                if (linkLevel[i] == level) return level > 0 ? i : -1;
                linkLevel[i] = (byte)level;
                Version++;
                return level > 0 ? i : -1;
            }
            if (level == 0) return -1;
            i = linkA.Count;
            linkA.Add(a < b ? a : b);
            linkB.Add(a < b ? b : a);
            linkLevel.Add((byte)level);
            index[key] = i;
            maxProvince = System.Math.Max(maxProvince, System.Math.Max(a, b));
            adjDirty = true;
            Version++;
            return i;
        }

        /// <summary>The links of a province (indices; removed links included, at level 0).</summary>
        public LinkRange LinksOf(ushort province)
        {
            EnsureAdjacency();
            if (province + 1 >= adjStart.Length) return new LinkRange(adjLink, 0, 0);
            return new LinkRange(adjLink, adjStart[province], adjStart[province + 1]);
        }

        /// <summary>The highest level of any link at the province, 0 when it has none.</summary>
        public int BestLevel(ushort province)
        {
            int best = 0;
            foreach (int link in LinksOf(province)) best = System.Math.Max(best, linkLevel[link]);
            return best;
        }

        /// <summary>Every province with at least one link, in id order.</summary>
        public List<ushort> Provinces()
        {
            EnsureAdjacency();
            var list = new List<ushort>();
            for (int p = 0; p + 1 < adjStart.Length; p++)
            {
                for (int k = adjStart[p]; k < adjStart[p + 1]; k++)
                    if (linkLevel[adjLink[k]] > 0) { list.Add((ushort)p); break; }
            }
            return list;
        }

        /// <summary>
        /// Best bottleneck level from source to every province reachable over links (level &gt; 0) whose both ends
        /// canUse allows. The source must pass canUse too, or nothing is reached. Results go into result (reused
        /// across calls without clearing). The path found for each province is a widest one, not necessarily the
        /// one with the fewest links.
        /// </summary>
        public void WidestPaths(ushort source, System.Func<ushort, bool> canUse, WidestPathResult result)
        {
            EnsureAdjacency();
            result.Begin(System.Math.Max(adjStart.Length, source + 2), MaxLevel, source);
            if (!canUse(source)) return;
            int top = MaxLevel + 1;
            var buckets = result.Buckets;
            result.Settle(source, top, -1, 0);
            buckets[top].Push(source);
            for (int level = top; level >= 1; level--)
            {
                var bucket = buckets[level];
                while (bucket.Count > 0)
                {
                    ushort u = bucket.Pop();
                    if (result.RawLevel(u) != level) continue; // reached at a better level since
                    if (u + 1 >= adjStart.Length) continue;
                    int hops = result.Hops(u) + 1;
                    for (int k = adjStart[u]; k < adjStart[u + 1]; k++)
                    {
                        int link = adjLink[k];
                        int l = linkLevel[link];
                        if (l == 0) continue;
                        ushort v = linkA[link] == u ? linkB[link] : linkA[link];
                        int nl = System.Math.Min(level, l);
                        if (result.RawLevel(v) >= nl) continue;
                        if (!canUse(v)) continue;
                        result.Settle(v, nl, link, hops);
                        buckets[nl].Push(v);
                    }
                }
            }
        }

        void EnsureAdjacency()
        {
            if (!adjDirty) return;
            int size = maxProvince + 1;
            var count = new int[size + 1];
            for (int i = 0; i < linkA.Count; i++) { count[linkA[i]]++; count[linkB[i]]++; }
            adjStart = new int[size + 1];
            for (int p = 0; p < size; p++) adjStart[p + 1] = adjStart[p] + count[p];
            adjLink = new int[adjStart[size]];
            var fill = new int[size];
            for (int i = 0; i < linkA.Count; i++)
            {
                adjLink[adjStart[linkA[i]] + fill[linkA[i]]++] = i;
                adjLink[adjStart[linkB[i]] + fill[linkB[i]]++] = i;
            }
            adjDirty = false;
        }

        static uint Key(ushort a, ushort b) => a < b ? ((uint)a << 16) | b : ((uint)b << 16) | a;

        /// <summary>A province's link indices (allocation-free foreach).</summary>
        public readonly struct LinkRange
        {
            readonly int[] links;
            readonly int start, end;
            public LinkRange(int[] links, int start, int end) { this.links = links; this.start = start; this.end = end; }
            public int Count => end - start;
            public Enumerator GetEnumerator() => new Enumerator(links, start, end);

            public struct Enumerator
            {
                readonly int[] links;
                readonly int end;
                int i;
                public Enumerator(int[] links, int start, int end) { this.links = links; this.end = end; i = start - 1; }
                public int Current => links[i];
                public bool MoveNext() => ++i < end;
            }
        }
    }

    /// <summary>
    /// The result of ProvinceLinkNetwork.WidestPaths, reusable across calls (a stamp marks which entries are from
    /// the latest call, so nothing is cleared). Level: the bottleneck level to the province (0: not reached;
    /// the source has MaxLevel + 1). Via: the link the path arrives by (-1 for the source and unreached
    /// provinces); follow Via and Other back to the source to trace the path. Hops: links on that path.
    /// </summary>
    public sealed class WidestPathResult
    {
        int[] stamp = System.Array.Empty<int>();
        sbyte[] level = System.Array.Empty<sbyte>();
        int[] via = System.Array.Empty<int>();
        ushort[] hops = System.Array.Empty<ushort>();
        int current;
        internal Stack<ushort>[] Buckets = System.Array.Empty<Stack<ushort>>();

        /// <summary>The province the latest call started from.</summary>
        public ushort Source { get; private set; }

        internal void Begin(int size, int maxLevel, ushort source)
        {
            if (stamp.Length < size)
            {
                System.Array.Resize(ref stamp, size);
                System.Array.Resize(ref level, size);
                System.Array.Resize(ref via, size);
                System.Array.Resize(ref hops, size);
            }
            if (Buckets.Length < maxLevel + 2)
            {
                Buckets = new Stack<ushort>[maxLevel + 2];
                for (int i = 0; i < Buckets.Length; i++) Buckets[i] = new Stack<ushort>();
            }
            else foreach (var b in Buckets) b.Clear();
            current++;
            Source = source;
        }

        internal void Settle(ushort province, int lvl, int link, int hopCount)
        {
            stamp[province] = current;
            level[province] = (sbyte)lvl;
            via[province] = link;
            hops[province] = (ushort)System.Math.Min(hopCount, ushort.MaxValue);
        }

        internal int RawLevel(ushort province) => province < stamp.Length && stamp[province] == current ? level[province] : 0;

        /// <summary>Bottleneck level to the province: 0 when not reached, MaxLevel + 1 for the source.</summary>
        public int Level(ushort province) => RawLevel(province);

        public bool Reached(ushort province) => RawLevel(province) > 0;

        /// <summary>The link the path to the province arrives by, -1 for the source or when not reached.</summary>
        public int Via(ushort province) => RawLevel(province) > 0 ? via[province] : -1;

        /// <summary>Links on the path from the source (0 for the source and when not reached).</summary>
        public int Hops(ushort province) => RawLevel(province) > 0 ? hops[province] : 0;
    }
}
