using System.Collections.Generic;

namespace Core.Registries
{
    /// <summary>
    /// Base interface for all game entity registries
    /// Provides type-safe string-to-ID mapping and efficient array lookups
    /// Following data-linking-architecture.md specifications
    /// </summary>
    public interface IRegistry<T> where T : class
    {
        /// <summary>
        /// Register a new entity with a string key
        /// Returns the assigned numeric ID for efficient runtime access
        /// </summary>
        ushort Register(string key, T item);

        /// <summary>
        /// Get entity by numeric ID (O(1) array access)
        /// Primary runtime access method - no strings involved
        /// </summary>
        T Get(ushort id);

        /// <summary>
        /// Get entity by string key (O(1) hash lookup)
        /// Use only during loading phase, not at runtime
        /// </summary>
        T Get(string key);

        /// <summary>
        /// Get numeric ID for a string key
        /// Returns 0 if key not found (0 reserved for "none/invalid")
        /// </summary>
        ushort GetId(string key);

        /// <summary>
        /// Try to get entity by string key without exceptions
        /// Returns false if key not found
        /// </summary>
        bool TryGet(string key, out T item);

        /// <summary>
        /// Try to get ID by string key without exceptions
        /// Returns false if key not found
        /// </summary>
        bool TryGetId(string key, out ushort id);

        /// <summary>
        /// Get all registered entities for iteration
        /// </summary>
        IEnumerable<T> GetAll();

        /// <summary>
        /// Get all registered IDs for iteration
        /// </summary>
        IEnumerable<ushort> GetAllIds();

        /// <summary>
        /// Check if a key exists in the registry
        /// </summary>
        bool Exists(string key);

        /// <summary>
        /// Check if an ID exists in the registry
        /// </summary>
        bool Exists(ushort id);

        /// <summary>
        /// Get the count of registered entities
        /// </summary>
        int Count { get; }

        /// <summary>
        /// Get the type name for error messages
        /// </summary>
        string TypeName { get; }
    }
}