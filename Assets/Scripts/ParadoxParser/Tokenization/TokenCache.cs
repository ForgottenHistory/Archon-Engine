using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace ParadoxParser.Tokenization
{
    /// <summary>
    /// High-performance cache for tokenized files
    /// Stores tokenization results to avoid re-parsing identical content
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct TokenCache : IDisposable
    {
        private NativeHashMap<uint, CacheEntry> m_Cache;
        private NativeList<NativeArray<Token>> m_TokenArrays;
        private int m_MaxEntries;
        private bool m_IsCreated;
        private bool m_IsDisposed;

        /// <summary>
        /// Number of cached entries
        /// </summary>
        public int Count => m_Cache.IsCreated ? m_Cache.Count : 0;

        /// <summary>
        /// Maximum number of cache entries
        /// </summary>
        public int MaxEntries => m_MaxEntries;

        /// <summary>
        /// Check if cache is created and valid
        /// </summary>
        public bool IsCreated => m_IsCreated && !m_IsDisposed;

        /// <summary>
        /// Cache hit ratio for performance monitoring
        /// </summary>
        public float HitRatio { get; private set; }

        public TokenCache(int maxEntries, Allocator allocator)
        {
            m_Cache = new NativeHashMap<uint, CacheEntry>(maxEntries, allocator);
            m_TokenArrays = new NativeList<NativeArray<Token>>(maxEntries, allocator);
            m_MaxEntries = maxEntries;
            m_IsCreated = true;
            m_IsDisposed = false;
            HitRatio = 0f;
        }

        /// <summary>
        /// Try to get cached tokens for content hash
        /// </summary>
        public bool TryGetTokens(uint contentHash, out NativeArray<Token> tokens)
        {
            tokens = default;

            if (!IsCreated || !m_Cache.TryGetValue(contentHash, out var entry))
                return false;

            if (entry.TokenArrayIndex < 0 || entry.TokenArrayIndex >= m_TokenArrays.Length)
                return false;

            tokens = m_TokenArrays[entry.TokenArrayIndex];
            return tokens.IsCreated;
        }

        /// <summary>
        /// Cache tokens for content hash
        /// </summary>
        public bool TryCache(uint contentHash, NativeArray<Token> tokens, Allocator allocator)
        {
            if (!IsCreated || !tokens.IsCreated)
                return false;

            // Check if already cached
            if (m_Cache.ContainsKey(contentHash))
                return true;

            // Remove oldest entry if at capacity
            if (m_Cache.Count >= m_MaxEntries)
            {
                EvictOldestEntry();
            }

            // Create a copy of the tokens
            var tokensCopy = new NativeArray<Token>(tokens.Length, allocator);
            tokens.CopyTo(tokensCopy);

            // Add to storage
            int arrayIndex = m_TokenArrays.Length;
            m_TokenArrays.Add(tokensCopy);

            // Create cache entry
            var entry = new CacheEntry
            {
                ContentHash = contentHash,
                TokenArrayIndex = arrayIndex,
                AccessCount = 1,
                LastAccessTime = GetCurrentTime()
            };

            return m_Cache.TryAdd(contentHash, entry);
        }

        /// <summary>
        /// Clear all cached entries
        /// </summary>
        public void Clear()
        {
            if (!IsCreated)
                return;

            // Dispose all token arrays
            for (int i = 0; i < m_TokenArrays.Length; i++)
            {
                if (m_TokenArrays[i].IsCreated)
                {
                    m_TokenArrays[i].Dispose();
                }
            }

            m_TokenArrays.Clear();
            m_Cache.Clear();
            HitRatio = 0f;
        }

        /// <summary>
        /// Remove least recently used entries to make space
        /// </summary>
        public void EvictOldestEntry()
        {
            if (!IsCreated || m_Cache.Count == 0)
                return;

            uint oldestHash = 0;
            long oldestTime = long.MaxValue;

            // Find oldest entry
            var keys = m_Cache.GetKeyArray(Allocator.Temp);
            var values = m_Cache.GetValueArray(Allocator.Temp);

            for (int i = 0; i < keys.Length; i++)
            {
                if (values[i].LastAccessTime < oldestTime)
                {
                    oldestTime = values[i].LastAccessTime;
                    oldestHash = keys[i];
                }
            }

            keys.Dispose();
            values.Dispose();

            // Remove oldest entry
            if (m_Cache.TryGetValue(oldestHash, out var entry))
            {
                // Dispose token array
                if (entry.TokenArrayIndex >= 0 && entry.TokenArrayIndex < m_TokenArrays.Length)
                {
                    var tokenArray = m_TokenArrays[entry.TokenArrayIndex];
                    if (tokenArray.IsCreated)
                    {
                        tokenArray.Dispose();
                    }

                    // Remove from list (this leaves a gap, but we'll clean up later)
                    m_TokenArrays[entry.TokenArrayIndex] = default;
                }

                m_Cache.Remove(oldestHash);
            }
        }

        /// <summary>
        /// Compact the token arrays by removing disposed entries
        /// </summary>
        public void Compact()
        {
            if (!IsCreated)
                return;

            var newArrays = new NativeList<NativeArray<Token>>(m_TokenArrays.Length, Allocator.Temp);
            var indexMapping = new NativeHashMap<int, int>(m_TokenArrays.Length, Allocator.Temp);

            // Copy valid arrays and build index mapping
            for (int i = 0; i < m_TokenArrays.Length; i++)
            {
                if (m_TokenArrays[i].IsCreated)
                {
                    indexMapping[i] = newArrays.Length;
                    newArrays.Add(m_TokenArrays[i]);
                }
            }

            // Update cache entries with new indices
            var keys = m_Cache.GetKeyArray(Allocator.Temp);
            var values = m_Cache.GetValueArray(Allocator.Temp);

            for (int i = 0; i < keys.Length; i++)
            {
                var entry = values[i];
                if (indexMapping.TryGetValue(entry.TokenArrayIndex, out int newIndex))
                {
                    entry.TokenArrayIndex = newIndex;
                    m_Cache[keys[i]] = entry;
                }
                else
                {
                    // Entry is invalid, remove it
                    m_Cache.Remove(keys[i]);
                }
            }

            keys.Dispose();
            values.Dispose();

            // Replace arrays
            var originalAllocator = m_TokenArrays.IsCreated ? Allocator.Persistent : Allocator.TempJob;
            m_TokenArrays.Dispose();
            m_TokenArrays = new NativeList<NativeArray<Token>>(newArrays.Length, originalAllocator);
            for (int i = 0; i < newArrays.Length; i++)
            {
                m_TokenArrays.Add(newArrays[i]);
            }

            newArrays.Dispose();
            indexMapping.Dispose();
        }

        /// <summary>
        /// Get cache statistics
        /// </summary>
        public CacheStats GetStats()
        {
            if (!IsCreated)
                return default;

            int totalMemory = 0;
            int validEntries = 0;

            for (int i = 0; i < m_TokenArrays.Length; i++)
            {
                if (m_TokenArrays[i].IsCreated)
                {
                    totalMemory += m_TokenArrays[i].Length * UnsafeUtility.SizeOf<Token>();
                    validEntries++;
                }
            }

            return new CacheStats
            {
                EntryCount = validEntries,
                MaxEntries = m_MaxEntries,
                MemoryUsageBytes = totalMemory,
                HitRatio = HitRatio,
                FragmentationRatio = validEntries > 0 ? (float)(m_TokenArrays.Length - validEntries) / m_TokenArrays.Length : 0f
            };
        }

        /// <summary>
        /// Update access statistics for an entry
        /// </summary>
        private void UpdateAccessStats(uint contentHash)
        {
            if (!IsCreated || !m_Cache.TryGetValue(contentHash, out var entry))
                return;

            entry.AccessCount++;
            entry.LastAccessTime = GetCurrentTime();
            m_Cache[contentHash] = entry;
        }

        /// <summary>
        /// Get current timestamp for cache tracking
        /// </summary>
        private static long GetCurrentTime()
        {
            return DateTime.UtcNow.Ticks;
        }

        public void Dispose()
        {
            if (m_IsDisposed)
                return;

            m_IsDisposed = true;
            m_IsCreated = false;

            try
            {
                // Dispose all token arrays
                if (m_TokenArrays.IsCreated)
                {
                    for (int i = 0; i < m_TokenArrays.Length; i++)
                    {
                        if (m_TokenArrays[i].IsCreated)
                        {
                            m_TokenArrays[i].Dispose();
                        }
                    }
                    m_TokenArrays.Dispose();
                }
            }
            catch (ObjectDisposedException) { /* Already disposed */ }

            try
            {
                if (m_Cache.IsCreated)
                {
                    m_Cache.Dispose();
                }
            }
            catch (ObjectDisposedException) { /* Already disposed */ }
        }

        /// <summary>
        /// Job-safe disposal
        /// </summary>
        public JobHandle Dispose(JobHandle inputDeps)
        {
            if (m_IsDisposed)
                return inputDeps;

            m_IsDisposed = true;
            m_IsCreated = false;

            JobHandle combinedJob = inputDeps;

            if (m_TokenArrays.IsCreated)
            {
                for (int i = 0; i < m_TokenArrays.Length; i++)
                {
                    if (m_TokenArrays[i].IsCreated)
                    {
                        combinedJob = m_TokenArrays[i].Dispose(combinedJob);
                    }
                }
                combinedJob = m_TokenArrays.Dispose(combinedJob);
            }

            if (m_Cache.IsCreated)
            {
                combinedJob = m_Cache.Dispose(combinedJob);
            }

            return combinedJob;
        }
    }

    /// <summary>
    /// Cache entry metadata
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct CacheEntry
    {
        public uint ContentHash;
        public int TokenArrayIndex;
        public int AccessCount;
        public long LastAccessTime;
    }

    /// <summary>
    /// Cache performance statistics
    /// </summary>
    public struct CacheStats
    {
        public int EntryCount;
        public int MaxEntries;
        public int MemoryUsageBytes;
        public float HitRatio;
        public float FragmentationRatio;

        public override string ToString()
        {
            return $"Cache: {EntryCount}/{MaxEntries} entries, {MemoryUsageBytes / 1024}KB, {HitRatio:P1} hit ratio, {FragmentationRatio:P1} fragmentation";
        }
    }
}