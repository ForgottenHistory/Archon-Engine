using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace Core.Data
{
    /// <summary>
    /// Cold data storage for provinces (accessed rarely, loaded on-demand)
    /// This data is NOT synchronized in multiplayer - it's presentation/metadata only
    /// CRITICAL: Uses FixedPoint64 for all calculations to ensure determinism if data flows to simulation
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

        // Gameplay data that changes infrequently (uses FixedPoint64 for determinism)
        public Dictionary<string, FixedPoint64> Modifiers { get; private set; }
        public List<BuildingType> Buildings { get; private set; }

        // Cached expensive calculations (uses FixedPoint64 for determinism)
        public FixedPoint64 CachedTradeValue { get; set; }
        public FixedPoint64 CachedSupplyLimit { get; set; }
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
            Modifiers = new Dictionary<string, FixedPoint64>();
            Buildings = new List<BuildingType>();

            CachedTradeValue = FixedPoint64.Zero;
            CachedSupplyLimit = FixedPoint64.Zero;
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
        /// Calculate trade value (cached per frame, uses FixedPoint64 for determinism)
        /// </summary>
        public FixedPoint64 CalculateTradeValue(ProvinceState hotState)
        {
            if (CacheFrame == Time.frameCount)
                return CachedTradeValue;

            // Expensive calculation - only do once per frame
            // Use deterministic fixed-point math
            FixedPoint64 baseValue = FixedPoint64.FromInt(hotState.development) * FixedPoint64.FromFraction(1, 2); // * 0.5
            FixedPoint64 modifierBonus = GetModifier("trade_value_modifier");
            FixedPoint64 buildingBonus = FixedPoint64.FromInt(Buildings.Count * 2); // Simplified calculation

            CachedTradeValue = baseValue + modifierBonus + buildingBonus;
            CacheFrame = Time.frameCount;

            return CachedTradeValue;
        }

        /// <summary>
        /// Calculate supply limit (cached per frame, uses FixedPoint64 for determinism)
        /// </summary>
        public FixedPoint64 CalculateSupplyLimit(ProvinceState hotState)
        {
            if (CacheFrame == Time.frameCount)
                return CachedSupplyLimit;

            // Use deterministic fixed-point math
            FixedPoint64 baseSupply = FixedPoint64.FromInt(hotState.development) * FixedPoint64.FromFraction(3, 10); // * 0.3
            FixedPoint64 fortBonus = FixedPoint64.FromInt(hotState.fortLevel) * FixedPoint64.FromFraction(1, 2); // * 0.5
            FixedPoint64 buildingBonus = HasBuilding(BuildingType.Granary) ? FixedPoint64.FromInt(5) : FixedPoint64.Zero;

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
            int modifiersSize = Modifiers.Count * 40; // String reference + FixedPoint64 (8 bytes)
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