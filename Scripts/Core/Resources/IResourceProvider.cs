using System.Collections.Generic;
using Core.Data;

namespace Core.Resources
{
    /// <summary>
    /// ENGINE: Interface for resource management systems.
    ///
    /// Allows GAME layer to provide custom implementations (e.g., modded resources,
    /// different storage strategies, or resource calculation overrides).
    ///
    /// Default implementation: ResourceSystem
    /// </summary>
    public interface IResourceProvider
    {
        // === Initialization ===

        /// <summary>Whether the provider is initialized and ready for operations</summary>
        bool IsInitialized { get; }

        /// <summary>Maximum number of countries supported</summary>
        int MaxCountries { get; }

        /// <summary>Number of registered resource types</summary>
        int ResourceCount { get; }

        // === Single Resource Operations ===

        /// <summary>Get current resource amount for a country</summary>
        FixedPoint64 GetResource(ushort countryId, ushort resourceId);

        /// <summary>Add resource to a country (clamped to max)</summary>
        void AddResource(ushort countryId, ushort resourceId, FixedPoint64 amount);

        /// <summary>Remove resource from a country (returns false if insufficient)</summary>
        bool RemoveResource(ushort countryId, ushort resourceId, FixedPoint64 amount);

        /// <summary>Set resource to exact amount (clamped to valid range)</summary>
        void SetResource(ushort countryId, ushort resourceId, FixedPoint64 amount);

        // === Cost Validation ===

        /// <summary>Check if country can afford a single cost</summary>
        bool CanAfford(ushort countryId, ushort resourceId, FixedPoint64 amount);

        /// <summary>Check if country can afford multiple costs</summary>
        bool CanAfford(ushort countryId, ResourceCost[] costs);

        /// <summary>Try to spend multiple costs atomically (all or nothing)</summary>
        bool TrySpend(ushort countryId, ResourceCost[] costs);

        // === Bulk Operations ===

        /// <summary>Add resource to all countries</summary>
        void AddResourceToAll(ushort resourceId, FixedPoint64 amount);

        /// <summary>Set resource for all countries</summary>
        void SetResourceForAll(ushort resourceId, FixedPoint64 amount);

        /// <summary>Transfer resource between countries (returns false if insufficient)</summary>
        bool TransferResource(ushort fromCountryId, ushort toCountryId, ushort resourceId, FixedPoint64 amount);

        // === Queries ===

        /// <summary>Check if a resource type is registered</summary>
        bool IsResourceRegistered(ushort resourceId);

        /// <summary>Get resource definition by ID</summary>
        ResourceDefinition GetResourceDefinition(ushort resourceId);

        /// <summary>Get all registered resource IDs</summary>
        IEnumerable<ushort> GetAllResourceIds();

        /// <summary>Get total amount of a resource across all countries</summary>
        FixedPoint64 GetTotalResourceInWorld(ushort resourceId);

        /// <summary>Get all resources for a country as dictionary (for UI/debugging)</summary>
        Dictionary<ushort, FixedPoint64> GetAllResourcesForCountry(ushort countryId);

        // === Batch Mode ===

        /// <summary>
        /// Begin batch mode - suppresses events until EndBatch is called.
        /// Use during loading/setup to avoid thousands of individual events.
        /// </summary>
        void BeginBatch();

        /// <summary>
        /// End batch mode - optionally emits a single batch event.
        /// </summary>
        void EndBatch();

        /// <summary>Whether currently in batch mode</summary>
        bool IsBatchMode { get; }
    }
}
