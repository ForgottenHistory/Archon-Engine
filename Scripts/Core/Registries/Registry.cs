using System;
using System.Collections.Generic;
using System.Linq;
using Utils;

namespace Core.Registries
{
    /// <summary>
    /// Generic registry implementation for game entities
    /// Provides O(1) lookups for both string keys and numeric IDs
    /// Follows data-linking-architecture.md specifications
    /// </summary>
    public class Registry<T> : IRegistry<T> where T : class
    {
        private readonly Dictionary<string, ushort> stringToId = new();
        private readonly List<T> items = new();
        private readonly string typeName;

        public string TypeName => typeName;
        public int Count => items.Count - 1; // Subtract 1 for reserved index 0

        public Registry(string typeName)
        {
            this.typeName = typeName ?? typeof(T).Name;

            // Reserve index 0 for "none/invalid" - critical for architecture
            items.Add(null);

            ArchonLogger.LogDataLinking($"Registry<{this.typeName}> initialized");
        }

        /// <summary>
        /// Register a new entity with validation
        /// </summary>
        public ushort Register(string key, T item)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException($"Cannot register {typeName} with null or empty key");

            if (item == null)
                throw new ArgumentNullException(nameof(item), $"Cannot register null {typeName}");

            if (stringToId.ContainsKey(key))
                throw new InvalidOperationException($"Duplicate {typeName} key: '{key}'");

            if (items.Count >= ushort.MaxValue)
                throw new InvalidOperationException($"Registry<{typeName}> exceeded maximum capacity of {ushort.MaxValue}");

            ushort id = (ushort)items.Count;
            items.Add(item);
            stringToId[key] = id;

            ArchonLogger.LogDataLinking($"Registered {typeName} '{key}' with ID {id}");
            return id;
        }

        /// <summary>
        /// Get entity by numeric ID - primary runtime access method
        /// </summary>
        public T Get(ushort id)
        {
            if (id >= items.Count)
                return null;

            return items[id];
        }

        /// <summary>
        /// Get entity by string key - use only during loading
        /// </summary>
        public T Get(string key)
        {
            if (string.IsNullOrEmpty(key))
                return null;

            if (stringToId.TryGetValue(key, out ushort id))
                return items[id];

            return null;
        }

        /// <summary>
        /// Get numeric ID for string key
        /// </summary>
        public ushort GetId(string key)
        {
            if (string.IsNullOrEmpty(key))
                return 0;

            return stringToId.TryGetValue(key, out ushort id) ? id : (ushort)0;
        }

        /// <summary>
        /// Try get entity by string key
        /// </summary>
        public bool TryGet(string key, out T item)
        {
            item = null;

            if (string.IsNullOrEmpty(key))
                return false;

            if (stringToId.TryGetValue(key, out ushort id))
            {
                item = items[id];
                return true;
            }

            return false;
        }

        /// <summary>
        /// Try get ID by string key
        /// </summary>
        public bool TryGetId(string key, out ushort id)
        {
            id = 0;

            if (string.IsNullOrEmpty(key))
                return false;

            return stringToId.TryGetValue(key, out id);
        }

        /// <summary>
        /// Get all entities (excluding null at index 0)
        /// </summary>
        public IEnumerable<T> GetAll()
        {
            return items.Skip(1).Where(item => item != null);
        }

        /// <summary>
        /// Get all valid IDs (excluding 0)
        /// </summary>
        public IEnumerable<ushort> GetAllIds()
        {
            for (ushort i = 1; i < items.Count; i++)
            {
                if (items[i] != null)
                    yield return i;
            }
        }

        /// <summary>
        /// Check if string key exists
        /// </summary>
        public bool Exists(string key)
        {
            return !string.IsNullOrEmpty(key) && stringToId.ContainsKey(key);
        }

        /// <summary>
        /// Check if numeric ID exists
        /// </summary>
        public bool Exists(ushort id)
        {
            return id > 0 && id < items.Count && items[id] != null;
        }

        /// <summary>
        /// Replace an existing entity (for mod support)
        /// </summary>
        public void Replace(string key, T newItem)
        {
            if (!stringToId.TryGetValue(key, out ushort id))
                throw new InvalidOperationException($"Cannot replace non-existent {typeName}: '{key}'");

            items[id] = newItem;
            ArchonLogger.LogDataLinking($"Replaced {typeName} '{key}' (ID {id})");
        }

        /// <summary>
        /// Get diagnostic information
        /// </summary>
        public string GetDiagnostics()
        {
            var validEntities = GetAll().Count();
            return $"Registry<{typeName}>: {validEntities} entities, {stringToId.Count} keys, capacity {items.Count - 1}";
        }
    }
}