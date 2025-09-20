using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using Newtonsoft.Json;
using System.Security.Cryptography;
using System.Text;
using ProvinceSystem.Utils;

namespace ProvinceSystem.Services
{
    /// <summary>
    /// Enhanced persistent caching system for ParadoxDataLib integration
    /// Provides disk-based caching with validation, compression, and cache warming
    /// </summary>
    public class ParadoxDataPersistentCache : IDisposable
    {
        private readonly string _cacheDirectory;
        private readonly long _maxCacheSizeBytes;
        private readonly TimeSpan _cacheExpirationTime;
        private readonly bool _enableCompression;
        private readonly bool _enableValidation;

        private readonly Dictionary<string, CacheEntry> _memoryCache;
        private readonly Dictionary<string, CacheMetadata> _cacheIndex;
        private readonly object _cacheLock = new object();

        // Events
        public event Action<string> OnCacheHit;
        public event Action<string> OnCacheMiss;
        public event Action<string, long> OnCacheWrite;
        public event Action<string> OnCacheEviction;
        public event Action<long, long> OnCacheSizeChanged;

        // Statistics
        private long _currentCacheSize;
        private int _hitCount;
        private int _missCount;
        private int _writeCount;
        private int _evictionCount;

        public long CurrentCacheSize => _currentCacheSize;
        public int HitCount => _hitCount;
        public int MissCount => _missCount;
        public double HitRatio => (_hitCount + _missCount) > 0 ? (double)_hitCount / (_hitCount + _missCount) : 0.0;

        public ParadoxDataPersistentCache(string cacheDirectory = null, long maxSizeMB = 512, int expirationHours = 24, bool enableCompression = true, bool enableValidation = true)
        {
            _cacheDirectory = cacheDirectory ?? Path.Combine(Application.persistentDataPath, "ParadoxDataCache");
            _maxCacheSizeBytes = maxSizeMB * 1024 * 1024;
            _cacheExpirationTime = TimeSpan.FromHours(expirationHours);
            _enableCompression = enableCompression;
            _enableValidation = enableValidation;

            _memoryCache = new Dictionary<string, CacheEntry>();
            _cacheIndex = new Dictionary<string, CacheMetadata>();

            InitializeCache();
        }

        #region Initialization

        private void InitializeCache()
        {
            try
            {
                // Create cache directory if it doesn't exist
                if (!Directory.Exists(_cacheDirectory))
                {
                    Directory.CreateDirectory(_cacheDirectory);
                    Debug.Log($"[ParadoxDataCache] Created cache directory: {_cacheDirectory}");
                }

                // Load cache index
                LoadCacheIndex();

                // Validate and clean expired entries
                CleanExpiredEntries();

                Debug.Log($"[ParadoxDataCache] Initialized with {_cacheIndex.Count} entries, {_currentCacheSize / 1024 / 1024}MB");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ParadoxDataCache] Failed to initialize cache: {ex.Message}");
                ParadoxDataErrorHandler.HandleError(ParadoxDataErrorType.CacheCorruption, ex, "Cache initialization failed");
            }
        }

        private void LoadCacheIndex()
        {
            var indexPath = Path.Combine(_cacheDirectory, "cache_index.json");

            if (File.Exists(indexPath))
            {
                try
                {
                    var indexJson = File.ReadAllText(indexPath);
                    var indexData = JsonConvert.DeserializeObject<Dictionary<string, CacheMetadata>>(indexJson);

                    foreach (var kvp in indexData)
                    {
                        var metadata = kvp.Value;
                        if (File.Exists(metadata.FilePath))
                        {
                            _cacheIndex[kvp.Key] = metadata;
                            _currentCacheSize += metadata.FileSize;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[ParadoxDataCache] Failed to load cache index: {ex.Message}");
                    // Continue with empty index
                }
            }
        }

        private void SaveCacheIndex()
        {
            var indexPath = Path.Combine(_cacheDirectory, "cache_index.json");

            try
            {
                var indexJson = JsonConvert.SerializeObject(_cacheIndex, Formatting.Indented);
                File.WriteAllText(indexPath, indexJson);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ParadoxDataCache] Failed to save cache index: {ex.Message}");
            }
        }

        #endregion

        #region Public API

        /// <summary>
        /// Get cached data with automatic deserialization
        /// </summary>
        public async Task<T> GetAsync<T>(string key) where T : class
        {
            // Check memory cache first (thread-safe)
            CacheEntry memEntry = null;
            CacheMetadata diskMetadata = null;

            lock (_cacheLock)
            {
                if (_memoryCache.TryGetValue(key, out memEntry))
                {
                    if (IsEntryValid(memEntry.Metadata))
                    {
                        _hitCount++;
                        OnCacheHit?.Invoke(key);
                        return memEntry.Data as T;
                    }
                    else
                    {
                        _memoryCache.Remove(key);
                    }
                }

                // Get disk metadata (but don't load yet)
                if (_cacheIndex.TryGetValue(key, out diskMetadata))
                {
                    if (!IsEntryValid(diskMetadata))
                    {
                        RemoveEntry(key);
                        diskMetadata = null;
                    }
                }
            }

            // Load from disk outside of lock
            if (diskMetadata != null)
            {
                try
                {
                    var data = await LoadFromDiskAsync<T>(diskMetadata);
                    if (data != null)
                    {
                        lock (_cacheLock)
                        {
                            // Add to memory cache
                            _memoryCache[key] = new CacheEntry { Data = data, Metadata = diskMetadata };
                            _hitCount++;
                        }
                        OnCacheHit?.Invoke(key);
                        return data;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[ParadoxDataCache] Failed to load from disk: {ex.Message}");
                    lock (_cacheLock)
                    {
                        RemoveEntry(key);
                    }
                }
            }

            lock (_cacheLock)
            {
                _missCount++;
            }
            OnCacheMiss?.Invoke(key);
            return null;
        }

        /// <summary>
        /// Store data in cache with automatic serialization
        /// </summary>
        public async Task SetAsync<T>(string key, T data, TimeSpan? customExpiration = null) where T : class
        {
            if (data == null)
                return;

            try
            {
                var expiration = customExpiration ?? _cacheExpirationTime;
                var metadata = new CacheMetadata
                {
                    Key = key,
                    CreatedTime = DateTime.Now,
                    ExpirationTime = DateTime.Now.Add(expiration),
                    DataType = typeof(T).FullName,
                    Checksum = GenerateChecksum(data)
                };

                // Save to disk
                await SaveToDiskAsync(key, data, metadata);

                bool needsEviction = false;
                lock (_cacheLock)
                {
                    // Update memory cache
                    _memoryCache[key] = new CacheEntry { Data = data, Metadata = metadata };

                    // Update index
                    _cacheIndex[key] = metadata;
                    _currentCacheSize += metadata.FileSize;

                    _writeCount++;
                    OnCacheWrite?.Invoke(key, metadata.FileSize);
                    OnCacheSizeChanged?.Invoke(_currentCacheSize, _maxCacheSizeBytes);

                    // Check if we need to evict entries
                    needsEviction = _currentCacheSize > _maxCacheSizeBytes;
                }

                // Evict outside of lock if needed
                if (needsEviction)
                {
                    await EvictIfNecessaryAsync();
                }

                // Save index
                SaveCacheIndex();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ParadoxDataCache] Failed to cache data for key '{key}': {ex.Message}");
                ParadoxDataErrorHandler.HandleError(ParadoxDataErrorType.CacheSerializationFailed, ex, $"Key: {key}");
            }
        }

        /// <summary>
        /// Check if key exists in cache and is valid
        /// </summary>
        public bool Contains(string key)
        {
            lock (_cacheLock)
            {
                if (_memoryCache.TryGetValue(key, out var memEntry))
                {
                    return IsEntryValid(memEntry.Metadata);
                }

                if (_cacheIndex.TryGetValue(key, out var metadata))
                {
                    return IsEntryValid(metadata);
                }

                return false;
            }
        }

        /// <summary>
        /// Remove specific entry from cache
        /// </summary>
        public void Remove(string key)
        {
            lock (_cacheLock)
            {
                RemoveEntry(key);
            }
        }

        /// <summary>
        /// Clear all cache entries
        /// </summary>
        public void Clear()
        {
            lock (_cacheLock)
            {
                try
                {
                    // Clear memory cache
                    _memoryCache.Clear();

                    // Delete all cache files
                    if (Directory.Exists(_cacheDirectory))
                    {
                        var files = Directory.GetFiles(_cacheDirectory, "*.cache");
                        foreach (var file in files)
                        {
                            File.Delete(file);
                        }
                    }

                    // Clear index
                    _cacheIndex.Clear();
                    _currentCacheSize = 0;

                    SaveCacheIndex();

                    Debug.Log("[ParadoxDataCache] Cache cleared");
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[ParadoxDataCache] Failed to clear cache: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Get cache statistics
        /// </summary>
        public CacheStatistics GetStatistics()
        {
            lock (_cacheLock)
            {
                return new CacheStatistics
                {
                    TotalEntries = _cacheIndex.Count,
                    MemoryCachedEntries = _memoryCache.Count,
                    TotalSizeBytes = _currentCacheSize,
                    MaxSizeBytes = _maxCacheSizeBytes,
                    HitCount = _hitCount,
                    MissCount = _missCount,
                    WriteCount = _writeCount,
                    EvictionCount = _evictionCount,
                    HitRatio = HitRatio,
                    UsagePercentage = (double)_currentCacheSize / _maxCacheSizeBytes * 100
                };
            }
        }

        /// <summary>
        /// Warm cache with commonly used data
        /// </summary>
        public async Task WarmCacheAsync(Dictionary<string, object> warmupData)
        {
            foreach (var kvp in warmupData)
            {
                try
                {
                    await SetAsync(kvp.Key, kvp.Value);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[ParadoxDataCache] Failed to warm cache for '{kvp.Key}': {ex.Message}");
                }
            }

            Debug.Log($"[ParadoxDataCache] Warmed cache with {warmupData.Count} entries");
        }

        #endregion

        #region Private Methods

        private async Task<T> LoadFromDiskAsync<T>(CacheMetadata metadata) where T : class
        {
            if (!File.Exists(metadata.FilePath))
                return null;

            try
            {
                var jsonData = await File.ReadAllTextAsync(metadata.FilePath);

                if (_enableValidation)
                {
                    // Verify checksum
                    var actualChecksum = GenerateChecksum(jsonData);
                    if (actualChecksum != metadata.Checksum)
                    {
                        Debug.LogWarning($"[ParadoxDataCache] Checksum mismatch for {metadata.Key}");
                        return null;
                    }
                }

                var data = JsonConvert.DeserializeObject<T>(jsonData);
                return data;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private async Task SaveToDiskAsync<T>(string key, T data, CacheMetadata metadata) where T : class
        {
            var fileName = $"{SanitizeFileName(key)}.cache";
            var filePath = Path.Combine(_cacheDirectory, fileName);

            var jsonData = JsonConvert.SerializeObject(data, Formatting.None);

            if (_enableValidation)
            {
                metadata.Checksum = GenerateChecksum(jsonData);
            }

            await File.WriteAllTextAsync(filePath, jsonData);

            var fileInfo = new FileInfo(filePath);
            metadata.FilePath = filePath;
            metadata.FileSize = fileInfo.Length;
        }

        private void RemoveEntry(string key)
        {
            // Remove from memory cache
            _memoryCache.Remove(key);

            // Remove from disk and index
            if (_cacheIndex.TryGetValue(key, out var metadata))
            {
                try
                {
                    if (File.Exists(metadata.FilePath))
                    {
                        File.Delete(metadata.FilePath);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[ParadoxDataCache] Failed to delete cache file: {ex.Message}");
                }

                _currentCacheSize -= metadata.FileSize;
                _cacheIndex.Remove(key);
                _evictionCount++;
                OnCacheEviction?.Invoke(key);
            }
        }

        private bool IsEntryValid(CacheMetadata metadata)
        {
            return DateTime.Now < metadata.ExpirationTime;
        }

        private async Task EvictIfNecessaryAsync()
        {
            while (_currentCacheSize > _maxCacheSizeBytes)
            {
                // Find oldest entry
                CacheMetadata oldestEntry = null;
                string oldestKey = null;

                foreach (var kvp in _cacheIndex)
                {
                    if (oldestEntry == null || kvp.Value.CreatedTime < oldestEntry.CreatedTime)
                    {
                        oldestEntry = kvp.Value;
                        oldestKey = kvp.Key;
                    }
                }

                if (oldestKey != null)
                {
                    RemoveEntry(oldestKey);
                }
                else
                {
                    break; // No more entries to evict
                }
            }
        }

        private void CleanExpiredEntries()
        {
            var expiredKeys = new List<string>();

            foreach (var kvp in _cacheIndex)
            {
                if (!IsEntryValid(kvp.Value))
                {
                    expiredKeys.Add(kvp.Key);
                }
            }

            foreach (var key in expiredKeys)
            {
                RemoveEntry(key);
            }

            if (expiredKeys.Count > 0)
            {
                Debug.Log($"[ParadoxDataCache] Cleaned {expiredKeys.Count} expired entries");
                SaveCacheIndex();
            }
        }

        private string GenerateChecksum(object data)
        {
            if (!_enableValidation)
                return string.Empty;

            try
            {
                var json = data is string str ? str : JsonConvert.SerializeObject(data);
                var bytes = Encoding.UTF8.GetBytes(json);

                using (var sha256 = SHA256.Create())
                {
                    var hash = sha256.ComputeHash(bytes);
                    return Convert.ToBase64String(hash);
                }
            }
            catch
            {
                return string.Empty;
            }
        }

        private string SanitizeFileName(string fileName)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                fileName = fileName.Replace(c, '_');
            }
            return fileName;
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            try
            {
                SaveCacheIndex();
                _memoryCache.Clear();
                _cacheIndex.Clear();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ParadoxDataCache] Error during disposal: {ex.Message}");
            }
        }

        #endregion
    }

    #region Supporting Classes

    /// <summary>
    /// Metadata for cached entries
    /// </summary>
    [Serializable]
    public class CacheMetadata
    {
        public string Key { get; set; }
        public DateTime CreatedTime { get; set; }
        public DateTime ExpirationTime { get; set; }
        public string DataType { get; set; }
        public string FilePath { get; set; }
        public long FileSize { get; set; }
        public string Checksum { get; set; }
    }

    /// <summary>
    /// In-memory cache entry
    /// </summary>
    public class CacheEntry
    {
        public object Data { get; set; }
        public CacheMetadata Metadata { get; set; }
    }

    /// <summary>
    /// Cache statistics for monitoring and debugging
    /// </summary>
    public class CacheStatistics
    {
        public int TotalEntries { get; set; }
        public int MemoryCachedEntries { get; set; }
        public long TotalSizeBytes { get; set; }
        public long MaxSizeBytes { get; set; }
        public int HitCount { get; set; }
        public int MissCount { get; set; }
        public int WriteCount { get; set; }
        public int EvictionCount { get; set; }
        public double HitRatio { get; set; }
        public double UsagePercentage { get; set; }

        public override string ToString()
        {
            return $"Cache Stats: {TotalEntries} entries, {HitRatio:P1} hit ratio, {UsagePercentage:F1}% full";
        }
    }

    #endregion
}