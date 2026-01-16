using System;
using System.Collections.Generic;
using UnityEngine;

namespace Core.Common
{
    /// <summary>
    /// Time-based cache that expires entries after a configurable lifetime.
    /// Use for expensive calculations that don't change frequently.
    ///
    /// Usage:
    ///   private TimedCache&lt;ushort, int&gt; devCache = new(lifetime: 1.0f);
    ///
    ///   public int GetTotalDevelopment(ushort countryId)
    ///   {
    ///       return devCache.GetOrCompute(countryId, () => CalculateDevelopment(countryId));
    ///   }
    ///
    /// Thread Safety: Not thread-safe. Use only from main thread.
    /// </summary>
    public class TimedCache<TKey, TValue>
    {
        private readonly Dictionary<TKey, CacheEntry> cache;
        private readonly float lifetime;

        private struct CacheEntry
        {
            public TValue Value;
            public float Timestamp;
        }

        /// <summary>
        /// Create a timed cache with specified lifetime in seconds.
        /// </summary>
        /// <param name="lifetime">How long entries remain valid (seconds)</param>
        /// <param name="initialCapacity">Initial dictionary capacity</param>
        public TimedCache(float lifetime = 1.0f, int initialCapacity = 16)
        {
            this.lifetime = lifetime;
            cache = new Dictionary<TKey, CacheEntry>(initialCapacity);
        }

        /// <summary>
        /// Get cached value or compute and cache it.
        /// Returns cached value if still valid, otherwise recomputes.
        /// </summary>
        public TValue GetOrCompute(TKey key, Func<TValue> computeFunc)
        {
            float currentTime = Time.time;

            if (cache.TryGetValue(key, out CacheEntry entry))
            {
                if (currentTime - entry.Timestamp < lifetime)
                {
                    return entry.Value;
                }
            }

            TValue value = computeFunc();
            cache[key] = new CacheEntry { Value = value, Timestamp = currentTime };
            return value;
        }

        /// <summary>
        /// Try to get a cached value if still valid.
        /// </summary>
        public bool TryGet(TKey key, out TValue value)
        {
            float currentTime = Time.time;

            if (cache.TryGetValue(key, out CacheEntry entry))
            {
                if (currentTime - entry.Timestamp < lifetime)
                {
                    value = entry.Value;
                    return true;
                }
            }

            value = default;
            return false;
        }

        /// <summary>
        /// Manually set a cached value with current timestamp.
        /// </summary>
        public void Set(TKey key, TValue value)
        {
            cache[key] = new CacheEntry { Value = value, Timestamp = Time.time };
        }

        /// <summary>
        /// Check if key has a valid (non-expired) cached value.
        /// </summary>
        public bool Contains(TKey key)
        {
            if (cache.TryGetValue(key, out CacheEntry entry))
            {
                return Time.time - entry.Timestamp < lifetime;
            }
            return false;
        }

        /// <summary>
        /// Invalidate a specific key.
        /// </summary>
        public void Invalidate(TKey key)
        {
            cache.Remove(key);
        }

        /// <summary>
        /// Clear the entire cache.
        /// </summary>
        public void Clear()
        {
            cache.Clear();
        }

        /// <summary>
        /// Remove all expired entries.
        /// Call periodically to prevent unbounded growth.
        /// </summary>
        public void PurgeExpired()
        {
            float currentTime = Time.time;
            var keysToRemove = new List<TKey>();

            foreach (var kvp in cache)
            {
                if (currentTime - kvp.Value.Timestamp >= lifetime)
                {
                    keysToRemove.Add(kvp.Key);
                }
            }

            foreach (var key in keysToRemove)
            {
                cache.Remove(key);
            }
        }

        /// <summary>
        /// Total cached entries (including expired).
        /// </summary>
        public int Count => cache.Count;

        /// <summary>
        /// Cache lifetime in seconds.
        /// </summary>
        public float Lifetime => lifetime;
    }

    /// <summary>
    /// Time-based cache for single values (no key).
    /// Use when caching a single expensive calculation that doesn't change frequently.
    ///
    /// Usage:
    ///   private TimedCacheValue{Stats} statsCache = new(lifetime: 5.0f);
    ///
    ///   public Stats GetStats()
    ///   {
    ///       return statsCache.GetOrCompute(() => CalculateStats());
    ///   }
    /// </summary>
    public class TimedCacheValue<TValue>
    {
        private TValue cachedValue;
        private float timestamp;
        private bool hasValue;
        private readonly float lifetime;

        /// <summary>
        /// Create a timed cache with specified lifetime in seconds.
        /// </summary>
        public TimedCacheValue(float lifetime = 1.0f)
        {
            this.lifetime = lifetime;
            hasValue = false;
            timestamp = 0f;
        }

        /// <summary>
        /// Get cached value or compute and cache it.
        /// </summary>
        public TValue GetOrCompute(Func<TValue> computeFunc)
        {
            float currentTime = Time.time;

            if (hasValue && currentTime - timestamp < lifetime)
            {
                return cachedValue;
            }

            cachedValue = computeFunc();
            timestamp = currentTime;
            hasValue = true;
            return cachedValue;
        }

        /// <summary>
        /// Try to get cached value if still valid.
        /// </summary>
        public bool TryGet(out TValue value)
        {
            if (hasValue && Time.time - timestamp < lifetime)
            {
                value = cachedValue;
                return true;
            }

            value = default;
            return false;
        }

        /// <summary>
        /// Manually set the cached value.
        /// </summary>
        public void Set(TValue value)
        {
            cachedValue = value;
            timestamp = Time.time;
            hasValue = true;
        }

        /// <summary>
        /// Check if value is cached and still valid.
        /// </summary>
        public bool IsValid => hasValue && Time.time - timestamp < lifetime;

        /// <summary>
        /// Clear the cache.
        /// </summary>
        public void Clear()
        {
            hasValue = false;
            cachedValue = default;
        }

        /// <summary>
        /// Cache lifetime in seconds.
        /// </summary>
        public float Lifetime => lifetime;
    }
}
