using System;
using System.Collections.Generic;
using UnityEngine;

namespace Core.Common
{
    /// <summary>
    /// Frame-coherent cache that automatically clears on frame change.
    /// Use for expensive calculations that may be queried multiple times per frame.
    ///
    /// Usage:
    ///   private FrameCache&lt;ushort, int&gt; devCache = new();
    ///
    ///   public int GetTotalDevelopment(ushort countryId)
    ///   {
    ///       return devCache.GetOrCompute(countryId, () => CalculateDevelopment(countryId));
    ///   }
    ///
    /// Thread Safety: Not thread-safe. Use only from main thread.
    /// </summary>
    public class FrameCache<TKey, TValue>
    {
        private readonly Dictionary<TKey, TValue> cache;
        private int lastFrameCount;

        public FrameCache(int initialCapacity = 16)
        {
            cache = new Dictionary<TKey, TValue>(initialCapacity);
            lastFrameCount = -1;
        }

        /// <summary>
        /// Get cached value or compute and cache it.
        /// Cache is cleared automatically on frame change.
        /// </summary>
        public TValue GetOrCompute(TKey key, Func<TValue> computeFunc)
        {
            CheckFrameChange();

            if (cache.TryGetValue(key, out TValue value))
            {
                return value;
            }

            value = computeFunc();
            cache[key] = value;
            return value;
        }

        /// <summary>
        /// Try to get a cached value without computing.
        /// </summary>
        public bool TryGet(TKey key, out TValue value)
        {
            CheckFrameChange();
            return cache.TryGetValue(key, out value);
        }

        /// <summary>
        /// Manually set a cached value.
        /// </summary>
        public void Set(TKey key, TValue value)
        {
            CheckFrameChange();
            cache[key] = value;
        }

        /// <summary>
        /// Check if key is cached (this frame).
        /// </summary>
        public bool Contains(TKey key)
        {
            CheckFrameChange();
            return cache.ContainsKey(key);
        }

        /// <summary>
        /// Manually invalidate a specific key.
        /// </summary>
        public void Invalidate(TKey key)
        {
            cache.Remove(key);
        }

        /// <summary>
        /// Manually clear the entire cache.
        /// </summary>
        public void Clear()
        {
            cache.Clear();
        }

        /// <summary>
        /// Number of cached entries (this frame).
        /// </summary>
        public int Count
        {
            get
            {
                CheckFrameChange();
                return cache.Count;
            }
        }

        private void CheckFrameChange()
        {
            int currentFrame = Time.frameCount;
            if (currentFrame != lastFrameCount)
            {
                cache.Clear();
                lastFrameCount = currentFrame;
            }
        }
    }

    /// <summary>
    /// Frame-coherent cache for single values (no key).
    /// Use when caching a single expensive calculation per frame.
    ///
    /// Usage:
    ///   private FrameCacheValue{int} totalProvinces = new();
    ///
    ///   public int GetTotalProvinces()
    ///   {
    ///       return totalProvinces.GetOrCompute(() => CountAllProvinces());
    ///   }
    /// </summary>
    public class FrameCacheValue<TValue>
    {
        private TValue cachedValue;
        private bool hasValue;
        private int lastFrameCount;

        public FrameCacheValue()
        {
            hasValue = false;
            lastFrameCount = -1;
        }

        /// <summary>
        /// Get cached value or compute and cache it.
        /// </summary>
        public TValue GetOrCompute(Func<TValue> computeFunc)
        {
            CheckFrameChange();

            if (hasValue)
            {
                return cachedValue;
            }

            cachedValue = computeFunc();
            hasValue = true;
            return cachedValue;
        }

        /// <summary>
        /// Try to get cached value.
        /// </summary>
        public bool TryGet(out TValue value)
        {
            CheckFrameChange();
            value = cachedValue;
            return hasValue;
        }

        /// <summary>
        /// Manually set the cached value.
        /// </summary>
        public void Set(TValue value)
        {
            CheckFrameChange();
            cachedValue = value;
            hasValue = true;
        }

        /// <summary>
        /// Check if value is cached (this frame).
        /// </summary>
        public bool HasValue
        {
            get
            {
                CheckFrameChange();
                return hasValue;
            }
        }

        /// <summary>
        /// Manually clear the cache.
        /// </summary>
        public void Clear()
        {
            hasValue = false;
            cachedValue = default;
        }

        private void CheckFrameChange()
        {
            int currentFrame = Time.frameCount;
            if (currentFrame != lastFrameCount)
            {
                hasValue = false;
                cachedValue = default;
                lastFrameCount = currentFrame;
            }
        }
    }
}
