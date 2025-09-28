using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace Core.Data
{
    /// <summary>
    /// Cold data storage for provinces (accessed rarely, loaded on-demand)
    /// This data is NOT synchronized in multiplayer - it's presentation/metadata only
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

        // Gameplay data that changes infrequently
        public Dictionary<string, float> Modifiers { get; private set; }
        public List<BuildingType> Buildings { get; private set; }

        // Cached expensive calculations
        public float CachedTradeValue { get; set; }
        public float CachedSupplyLimit { get; set; }
        public int CacheFrame { get; set; } // For frame-coherent caching

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
            Modifiers = new Dictionary<string, float>();
            Buildings = new List<BuildingType>();

            CachedTradeValue = 0f;
            CachedSupplyLimit = 0f;
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
        /// Add or update a modifier
        /// </summary>
        public void SetModifier(string key, float value)
        {
            Modifiers[key] = value;
            InvalidateCache();
        }

        /// <summary>
        /// Get modifier value
        /// </summary>
        public float GetModifier(string key, float defaultValue = 0f)
        {
            return Modifiers.TryGetValue(key, out float value) ? value : defaultValue;
        }

        /// <summary>
        /// Add a building
        /// </summary>
        public void AddBuilding(BuildingType building)
        {
            if (!Buildings.Contains(building))
            {
                Buildings.Add(building);
                InvalidateCache();
            }
        }

        /// <summary>
        /// Remove a building
        /// </summary>
        public void RemoveBuilding(BuildingType building)
        {
            if (Buildings.Remove(building))
            {
                InvalidateCache();
            }
        }

        /// <summary>
        /// Check if province has a specific building
        /// </summary>
        public bool HasBuilding(BuildingType building)
        {
            return Buildings.Contains(building);
        }

        /// <summary>
        /// Invalidate cached calculations
        /// </summary>
        public void InvalidateCache()
        {
            CacheFrame = -1;
        }

        /// <summary>
        /// Calculate trade value (cached per frame)
        /// </summary>
        public float CalculateTradeValue(ProvinceState hotState)
        {
            if (CacheFrame == Time.frameCount)
                return CachedTradeValue;

            // Expensive calculation - only do once per frame
            float baseValue = hotState.development * 0.5f;
            float modifierBonus = GetModifier("trade_value_modifier", 0f);
            float buildingBonus = Buildings.Count * 2f; // Simplified calculation

            CachedTradeValue = baseValue + modifierBonus + buildingBonus;
            CacheFrame = Time.frameCount;

            return CachedTradeValue;
        }

        /// <summary>
        /// Calculate supply limit (cached per frame)
        /// </summary>
        public float CalculateSupplyLimit(ProvinceState hotState)
        {
            if (CacheFrame == Time.frameCount)
                return CachedSupplyLimit;

            float baseSupply = hotState.development * 0.3f;
            float fortBonus = hotState.fortLevel * 0.5f;
            float buildingBonus = HasBuilding(BuildingType.Granary) ? 5f : 0f;

            CachedSupplyLimit = baseSupply + fortBonus + buildingBonus;
            CacheFrame = Time.frameCount;

            return CachedSupplyLimit;
        }

        /// <summary>
        /// Get estimated memory usage
        /// </summary>
        public int GetEstimatedMemoryUsage()
        {
            int baseSize = 64; // Object overhead
            int nameSize = (Name?.Length ?? 0) * 2; // Unicode string
            int historySize = RecentHistory.Count * 32; // Estimated per event
            int modifiersSize = Modifiers.Count * 32; // Estimated per modifier
            int buildingsSize = Buildings.Count * 4; // Enum size

            return baseSize + nameSize + historySize + modifiersSize + buildingsSize;
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

    // HistoricalEvent and HistoryEventType are now defined in ProvinceHistoryDatabase.cs

    /// <summary>
    /// Building types that can be constructed in provinces
    /// </summary>
    public enum BuildingType : byte
    {
        Farm,           // Increases development
        Market,         // Increases trade value
        Fort,           // Increases fortification
        Temple,         // Religious building
        Workshop,       // Production building
        Granary,        // Increases supply limit
        Road,           // Movement bonus
        Port            // Naval access
    }
}