using System.Collections.Generic;
using UnityEngine;

namespace Core.Data
{
    /// <summary>
    /// Cold data storage for provinces (accessed rarely, loaded on-demand)
    /// This data is NOT synchronized in multiplayer - it's presentation/metadata only
    ///
    /// ENGINE layer - generic province data only.
    /// Game-specific data (buildings, trade values, etc.) should be stored in customData dictionary.
    /// </summary>
    public class ProvinceColdData
    {
        public ushort ProvinceID { get; private set; }

        // Presentation layer data
        public string Name { get; set; }
        public Color32 IdentifierColor { get; set; }
        public Vector2 CenterPoint { get; set; }
        public int PixelCount { get; set; }
        public Rect Bounds { get; set; }

        // Historical data (bounded to prevent memory growth)
        public CircularBuffer<HistoricalEvent> RecentHistory { get; private set; }

        // Generic modifier storage (uses FixedPoint64 for determinism)
        public Dictionary<string, FixedPoint64> Modifiers { get; private set; }

        // Game-specific extension point
        // Games can store arbitrary data here (e.g., buildings, cached calculations, etc.)
        public Dictionary<string, object> CustomData { get; private set; }

        // Frame-coherent caching support
        public int CacheFrame { get; set; }

        public ProvinceColdData(ushort provinceID)
        {
            ProvinceID = provinceID;
            Name = $"Province_{provinceID}";
            IdentifierColor = Color.white;
            CenterPoint = Vector2.zero;
            PixelCount = 0;
            Bounds = new Rect();

            // Fixed-size collections to prevent unbounded growth
            RecentHistory = new CircularBuffer<HistoricalEvent>(100); // Last 100 events only
            Modifiers = new Dictionary<string, FixedPoint64>();
            CustomData = new Dictionary<string, object>();

            CacheFrame = -1;
        }

        /// <summary>
        /// Add a historical event (bounded to prevent memory growth)
        /// </summary>
        public void AddHistoricalEvent(HistoricalEvent evt)
        {
            RecentHistory.Add(evt);
        }

        /// <summary>
        /// Get recent historical events
        /// </summary>
        public IReadOnlyList<HistoricalEvent> GetRecentHistory()
        {
            return RecentHistory.Items;
        }

        /// <summary>
        /// Add or update a modifier (uses FixedPoint64 for determinism)
        /// </summary>
        public void SetModifier(string key, FixedPoint64 value)
        {
            Modifiers[key] = value;
            InvalidateCache();
        }

        /// <summary>
        /// Get modifier value (returns FixedPoint64 for determinism)
        /// </summary>
        public FixedPoint64 GetModifier(string key, FixedPoint64 defaultValue)
        {
            return Modifiers.TryGetValue(key, out FixedPoint64 value) ? value : defaultValue;
        }

        /// <summary>
        /// Get modifier value with default zero
        /// </summary>
        public FixedPoint64 GetModifier(string key)
        {
            return GetModifier(key, FixedPoint64.Zero);
        }

        /// <summary>
        /// Get custom data with type safety
        /// </summary>
        public T GetCustomData<T>(string key, T defaultValue = default)
        {
            if (CustomData != null && CustomData.TryGetValue(key, out var value) && value is T typedValue)
                return typedValue;
            return defaultValue;
        }

        /// <summary>
        /// Set custom data
        /// </summary>
        public void SetCustomData(string key, object value)
        {
            CustomData ??= new Dictionary<string, object>();
            CustomData[key] = value;
            InvalidateCache();
        }

        /// <summary>
        /// Remove custom data
        /// </summary>
        public bool RemoveCustomData(string key)
        {
            if (CustomData != null && CustomData.Remove(key))
            {
                InvalidateCache();
                return true;
            }
            return false;
        }

        /// <summary>
        /// Check if custom data exists
        /// </summary>
        public bool HasCustomData(string key)
        {
            return CustomData != null && CustomData.ContainsKey(key);
        }

        /// <summary>
        /// Invalidate cached calculations
        /// </summary>
        public void InvalidateCache()
        {
            CacheFrame = -1;
        }

        /// <summary>
        /// Get estimated memory usage
        /// </summary>
        public int GetEstimatedMemoryUsage()
        {
            int baseSize = 64; // Object overhead
            int nameSize = (Name?.Length ?? 0) * 2; // Unicode string
            int historySize = RecentHistory.Count * 32; // Estimated per event
            int modifiersSize = Modifiers.Count * 40; // String reference + FixedPoint64 (8 bytes)

            // Custom data estimate
            int customDataSize = 0;
            if (CustomData != null)
            {
                foreach (var kvp in CustomData)
                {
                    customDataSize += (kvp.Key?.Length ?? 0) * 2;
                    if (kvp.Value is string s)
                        customDataSize += s.Length * 2;
                    else
                        customDataSize += 8; // default object reference
                }
            }

            return baseSize + nameSize + historySize + modifiersSize + customDataSize;
        }
    }

    /// <summary>
    /// Fixed-size circular buffer to prevent unbounded memory growth
    /// </summary>
    public class CircularBuffer<T>
    {
        private T[] buffer;
        private int head;
        private int count;
        private readonly int capacity;

        public int Count => count;
        public int Capacity => capacity;
        public IReadOnlyList<T> Items => GetItems();

        public CircularBuffer(int capacity)
        {
            this.capacity = capacity;
            buffer = new T[capacity];
            head = 0;
            count = 0;
        }

        public void Add(T item)
        {
            buffer[head] = item;
            head = (head + 1) % capacity;

            if (count < capacity)
                count++;
        }

        public void Clear()
        {
            head = 0;
            count = 0;
            for (int i = 0; i < buffer.Length; i++)
            {
                buffer[i] = default(T);
            }
        }

        private List<T> GetItems()
        {
            var items = new List<T>(count);

            if (count == 0)
                return items;

            int start = count < capacity ? 0 : head;

            for (int i = 0; i < count; i++)
            {
                int index = (start + i) % capacity;
                items.Add(buffer[index]);
            }

            return items;
        }
    }

    // HistoricalEvent and HistoryEventType are defined in ProvinceHistoryDatabase.cs
}
