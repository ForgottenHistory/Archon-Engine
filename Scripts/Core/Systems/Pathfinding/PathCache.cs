using System.Collections.Generic;
using Core.Data;

namespace Core.Systems
{
    /// <summary>
    /// ENGINE: LRU cache for pathfinding results.
    ///
    /// Caches computed paths to avoid redundant A* searches.
    /// Uses simple LRU eviction when cache is full.
    ///
    /// Cache key: (start, goal) pair
    /// Cache invalidation: Manual clear or frame-based expiry
    ///
    /// Thread safety: NOT thread-safe (single-threaded pathfinding assumed)
    /// </summary>
    public class PathCache
    {
        private readonly int maxSize;
        private readonly Dictionary<PathCacheKey, PathCacheEntry> cache;
        private readonly LinkedList<PathCacheKey> lruList;
        private int hitCount;
        private int missCount;

        /// <summary>
        /// Create a path cache with specified maximum size.
        /// </summary>
        /// <param name="maxSize">Maximum number of cached paths</param>
        public PathCache(int maxSize = 256)
        {
            this.maxSize = maxSize;
            this.cache = new Dictionary<PathCacheKey, PathCacheEntry>(maxSize);
            this.lruList = new LinkedList<PathCacheKey>();
            this.hitCount = 0;
            this.missCount = 0;
        }

        /// <summary>
        /// Try to get a cached path.
        /// </summary>
        public bool TryGet(ushort start, ushort goal, out List<ushort> path, out FixedPoint64 cost)
        {
            var key = new PathCacheKey(start, goal);

            if (cache.TryGetValue(key, out PathCacheEntry entry))
            {
                // Move to front of LRU list
                lruList.Remove(entry.LruNode);
                lruList.AddFirst(entry.LruNode);

                path = new List<ushort>(entry.Path); // Return copy
                cost = entry.TotalCost;
                hitCount++;
                return true;
            }

            path = null;
            cost = FixedPoint64.Zero;
            missCount++;
            return false;
        }

        /// <summary>
        /// Add a path to the cache.
        /// </summary>
        public void Add(ushort start, ushort goal, List<ushort> path, FixedPoint64 cost)
        {
            var key = new PathCacheKey(start, goal);

            // Already cached - update and move to front
            if (cache.TryGetValue(key, out PathCacheEntry existing))
            {
                existing.Path = new List<ushort>(path);
                existing.TotalCost = cost;
                lruList.Remove(existing.LruNode);
                lruList.AddFirst(existing.LruNode);
                return;
            }

            // Evict LRU if full
            while (cache.Count >= maxSize && lruList.Count > 0)
            {
                var lruKey = lruList.Last.Value;
                lruList.RemoveLast();
                cache.Remove(lruKey);
            }

            // Add new entry
            var node = lruList.AddFirst(key);
            cache[key] = new PathCacheEntry
            {
                Path = new List<ushort>(path),
                TotalCost = cost,
                LruNode = node
            };
        }

        /// <summary>
        /// Invalidate paths that pass through a specific province.
        /// Call when a province becomes blocked/unblocked.
        /// </summary>
        public void InvalidateProvince(ushort provinceId)
        {
            var keysToRemove = new List<PathCacheKey>();

            foreach (var kvp in cache)
            {
                // Check if path contains the province
                if (kvp.Value.Path.Contains(provinceId))
                {
                    keysToRemove.Add(kvp.Key);
                }
            }

            foreach (var key in keysToRemove)
            {
                if (cache.TryGetValue(key, out var entry))
                {
                    lruList.Remove(entry.LruNode);
                    cache.Remove(key);
                }
            }
        }

        /// <summary>
        /// Invalidate all paths for a specific start or goal province.
        /// </summary>
        public void InvalidateEndpoint(ushort provinceId)
        {
            var keysToRemove = new List<PathCacheKey>();

            foreach (var key in cache.Keys)
            {
                if (key.Start == provinceId || key.Goal == provinceId)
                {
                    keysToRemove.Add(key);
                }
            }

            foreach (var key in keysToRemove)
            {
                if (cache.TryGetValue(key, out var entry))
                {
                    lruList.Remove(entry.LruNode);
                    cache.Remove(key);
                }
            }
        }

        /// <summary>
        /// Clear all cached paths.
        /// </summary>
        public void Clear()
        {
            cache.Clear();
            lruList.Clear();
        }

        /// <summary>
        /// Reset statistics.
        /// </summary>
        public void ResetStats()
        {
            hitCount = 0;
            missCount = 0;
        }

        // === Properties ===

        /// <summary>Number of cached paths</summary>
        public int Count => cache.Count;

        /// <summary>Maximum cache size</summary>
        public int MaxSize => maxSize;

        /// <summary>Cache hit count since last reset</summary>
        public int HitCount => hitCount;

        /// <summary>Cache miss count since last reset</summary>
        public int MissCount => missCount;

        /// <summary>Cache hit rate (0-1)</summary>
        public float HitRate
        {
            get
            {
                int total = hitCount + missCount;
                return total > 0 ? (float)hitCount / total : 0f;
            }
        }

        /// <summary>
        /// Get cache statistics as string.
        /// </summary>
        public string GetStats()
        {
            return $"PathCache: {Count}/{MaxSize} entries, {HitCount} hits, {MissCount} misses ({HitRate:P1} hit rate)";
        }
    }

    /// <summary>
    /// Cache key for path lookups.
    /// </summary>
    internal struct PathCacheKey : System.IEquatable<PathCacheKey>
    {
        public readonly ushort Start;
        public readonly ushort Goal;

        public PathCacheKey(ushort start, ushort goal)
        {
            Start = start;
            Goal = goal;
        }

        public bool Equals(PathCacheKey other)
        {
            return Start == other.Start && Goal == other.Goal;
        }

        public override bool Equals(object obj)
        {
            return obj is PathCacheKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return (Start << 16) | Goal;
        }
    }

    /// <summary>
    /// Cache entry with LRU tracking.
    /// </summary>
    internal class PathCacheEntry
    {
        public List<ushort> Path;
        public FixedPoint64 TotalCost;
        public LinkedListNode<PathCacheKey> LruNode;
    }
}
