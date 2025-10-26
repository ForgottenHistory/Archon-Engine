using System;
using System.Collections.Generic;
using Core.Data;
using Utils;

namespace Core.Resources
{
    /// <summary>
    /// ENGINE LAYER: Generic resource storage and management system
    ///
    /// Stores and manages multiple resource types (gold, manpower, prestige, etc.) for all countries.
    /// Uses deterministic fixed-point math for multiplayer compatibility.
    ///
    /// Architecture:
    /// - Dictionary storage: resourceId → array of country values
    /// - Fixed-size arrays: countryId → resource amount
    /// - Deterministic: FixedPoint64 only, no float operations
    /// - Event-driven: Emits OnResourceChanged for reactive UI
    ///
    /// Usage:
    /// 1. Register resource types with RegisterResource(resourceId, definition)
    /// 2. Initialize with country capacity
    /// 3. Use AddResource/RemoveResource/GetResource for all operations
    ///
    /// Performance:
    /// - GetResource: O(1) dictionary lookup + O(1) array access
    /// - Memory: sizeof(FixedPoint64) × resourceCount × countryCount
    /// - Example: 10 resources × 200 countries × 8 bytes = 16 KB
    /// </summary>
    public class ResourceSystem
    {
        // Storage: resourceId → array of country values
        private Dictionary<ushort, FixedPoint64[]> resourceStorageByType;

        // Resource definitions (for validation and metadata)
        private Dictionary<ushort, ResourceDefinition> resourceDefinitions;

        // Country capacity (set during initialization)
        private int maxCountries;

        // Initialization state
        private bool isInitialized = false;

        // Events
        public event Action<ushort, ushort, FixedPoint64, FixedPoint64> OnResourceChanged; // (countryId, resourceId, oldAmount, newAmount)

        /// <summary>
        /// Initialize ResourceSystem with country capacity
        /// Must be called before any resource operations
        /// </summary>
        public void Initialize(int countryCapacity)
        {
            if (isInitialized)
            {
                ArchonLogger.LogWarning("ResourceSystem: Already initialized", "core_simulation");
                return;
            }

            if (countryCapacity <= 0)
            {
                ArchonLogger.LogError($"ResourceSystem: Invalid country capacity {countryCapacity}", "core_simulation");
                return;
            }

            maxCountries = countryCapacity;
            resourceStorageByType = new Dictionary<ushort, FixedPoint64[]>();
            resourceDefinitions = new Dictionary<ushort, ResourceDefinition>();

            isInitialized = true;

            ArchonLogger.Log($"ResourceSystem: Initialized for {maxCountries} countries", "core_simulation");
        }

        /// <summary>
        /// Register a resource type with its definition
        /// Creates storage array for all countries and initializes with starting amount
        /// </summary>
        public void RegisterResource(ushort resourceId, ResourceDefinition definition)
        {
            if (!isInitialized)
            {
                ArchonLogger.LogError("ResourceSystem: Not initialized, call Initialize() first", "core_simulation");
                return;
            }

            if (definition == null)
            {
                ArchonLogger.LogError($"ResourceSystem: Cannot register null resource definition", "core_simulation");
                return;
            }

            // Validate definition
            if (!definition.Validate(out string errorMessage))
            {
                ArchonLogger.LogError($"ResourceSystem: Invalid resource definition '{definition.id}': {errorMessage}", "core_simulation");
                return;
            }

            // Check if already registered
            if (resourceDefinitions.ContainsKey(resourceId))
            {
                ArchonLogger.LogWarning($"ResourceSystem: Resource {resourceId} already registered, overwriting", "core_simulation");
            }

            // Create storage array for this resource
            FixedPoint64[] countryValues = new FixedPoint64[maxCountries];
            FixedPoint64 startingAmount = definition.GetStartingAmountFixed();

            // Initialize all countries with starting amount
            for (int i = 0; i < maxCountries; i++)
            {
                countryValues[i] = startingAmount;
            }

            // Store in dictionaries
            resourceStorageByType[resourceId] = countryValues;
            resourceDefinitions[resourceId] = definition;

            ArchonLogger.Log($"ResourceSystem: Registered resource '{definition.id}' (ID: {resourceId}) with starting amount {startingAmount.ToString("F1")}", "core_simulation");
        }

        #region Resource Operations

        /// <summary>
        /// Get current resource amount for a country
        /// </summary>
        public FixedPoint64 GetResource(ushort countryId, ushort resourceId)
        {
            if (!ValidateOperation(countryId, resourceId, out FixedPoint64[] storage))
            {
                return FixedPoint64.Zero;
            }

            return storage[countryId];
        }

        /// <summary>
        /// Add resource to a country (clamped to max value)
        /// </summary>
        public void AddResource(ushort countryId, ushort resourceId, FixedPoint64 amount)
        {
            if (!ValidateOperation(countryId, resourceId, out FixedPoint64[] storage))
            {
                return;
            }

            if (amount < FixedPoint64.Zero)
            {
                ArchonLogger.LogWarning($"ResourceSystem: Attempted to add negative amount ({amount}), use RemoveResource instead", "core_simulation");
                return;
            }

            FixedPoint64 oldAmount = storage[countryId];
            FixedPoint64 newAmount = oldAmount + amount;

            // Clamp to max value
            ResourceDefinition definition = resourceDefinitions[resourceId];
            FixedPoint64 maxValue = definition.GetMaxValueFixed();
            if (newAmount > maxValue)
            {
                newAmount = maxValue;
            }

            storage[countryId] = newAmount;

            // Emit event
            OnResourceChanged?.Invoke(countryId, resourceId, oldAmount, newAmount);
        }

        /// <summary>
        /// Remove resource from a country (returns true if successful, false if insufficient)
        /// </summary>
        public bool RemoveResource(ushort countryId, ushort resourceId, FixedPoint64 amount)
        {
            if (!ValidateOperation(countryId, resourceId, out FixedPoint64[] storage))
            {
                return false;
            }

            if (amount < FixedPoint64.Zero)
            {
                ArchonLogger.LogWarning($"ResourceSystem: Attempted to remove negative amount ({amount}), use AddResource instead", "core_simulation");
                return false;
            }

            FixedPoint64 currentAmount = storage[countryId];

            // Check if sufficient
            if (currentAmount < amount)
            {
                return false; // Insufficient resources
            }

            FixedPoint64 oldAmount = currentAmount;
            FixedPoint64 newAmount = oldAmount - amount;

            // Clamp to min value
            ResourceDefinition definition = resourceDefinitions[resourceId];
            FixedPoint64 minValue = definition.GetMinValueFixed();
            if (newAmount < minValue)
            {
                newAmount = minValue;
            }

            storage[countryId] = newAmount;

            // Emit event
            OnResourceChanged?.Invoke(countryId, resourceId, oldAmount, newAmount);

            return true;
        }

        /// <summary>
        /// Set resource to exact amount (for dev commands/testing)
        /// Clamped to min/max values
        /// </summary>
        public void SetResource(ushort countryId, ushort resourceId, FixedPoint64 amount)
        {
            if (!ValidateOperation(countryId, resourceId, out FixedPoint64[] storage))
            {
                return;
            }

            FixedPoint64 oldAmount = storage[countryId];

            // Clamp to valid range
            ResourceDefinition definition = resourceDefinitions[resourceId];
            FixedPoint64 minValue = definition.GetMinValueFixed();
            FixedPoint64 maxValue = definition.GetMaxValueFixed();

            FixedPoint64 newAmount = amount;
            if (newAmount < minValue)
            {
                newAmount = minValue;
            }
            if (newAmount > maxValue)
            {
                newAmount = maxValue;
            }

            storage[countryId] = newAmount;

            // Emit event
            OnResourceChanged?.Invoke(countryId, resourceId, oldAmount, newAmount);
        }

        #endregion

        #region Validation

        /// <summary>
        /// Validate operation parameters and return storage array
        /// </summary>
        private bool ValidateOperation(ushort countryId, ushort resourceId, out FixedPoint64[] storage)
        {
            storage = null;

            if (!isInitialized)
            {
                ArchonLogger.LogError("ResourceSystem: Not initialized", "core_simulation");
                return false;
            }

            if (countryId >= maxCountries)
            {
                ArchonLogger.LogError($"ResourceSystem: Invalid countryId {countryId} (max: {maxCountries})", "core_simulation");
                return false;
            }

            if (!resourceStorageByType.TryGetValue(resourceId, out storage))
            {
                ArchonLogger.LogError($"ResourceSystem: Unknown resource ID {resourceId} (not registered)", "core_simulation");
                return false;
            }

            return true;
        }

        #endregion

        #region Queries

        /// <summary>
        /// Check if a resource type is registered
        /// </summary>
        public bool IsResourceRegistered(ushort resourceId)
        {
            return isInitialized && resourceDefinitions.ContainsKey(resourceId);
        }

        /// <summary>
        /// Get resource definition by ID
        /// </summary>
        public ResourceDefinition GetResourceDefinition(ushort resourceId)
        {
            if (!isInitialized)
            {
                return null;
            }

            resourceDefinitions.TryGetValue(resourceId, out ResourceDefinition definition);
            return definition;
        }

        /// <summary>
        /// Get all registered resource IDs
        /// </summary>
        public IEnumerable<ushort> GetAllResourceIds()
        {
            if (!isInitialized)
            {
                yield break;
            }

            foreach (ushort resourceId in resourceDefinitions.Keys)
            {
                yield return resourceId;
            }
        }

        /// <summary>
        /// Get total amount of a resource across all countries (for debugging)
        /// </summary>
        public FixedPoint64 GetTotalResourceInWorld(ushort resourceId)
        {
            if (!ValidateOperation(0, resourceId, out FixedPoint64[] storage))
            {
                return FixedPoint64.Zero;
            }

            FixedPoint64 total = FixedPoint64.Zero;
            for (int i = 0; i < maxCountries; i++)
            {
                total += storage[i];
            }
            return total;
        }

        #endregion

        #region State Management

        /// <summary>
        /// Check if initialized
        /// </summary>
        public bool IsInitialized => isInitialized;

        /// <summary>
        /// Get country capacity
        /// </summary>
        public int MaxCountries => maxCountries;

        /// <summary>
        /// Get number of registered resources
        /// </summary>
        public int ResourceCount => isInitialized ? resourceDefinitions.Count : 0;

        /// <summary>
        /// Shutdown resource system (cleanup)
        /// </summary>
        public void Shutdown()
        {
            if (!isInitialized)
            {
                return;
            }

            resourceStorageByType?.Clear();
            resourceDefinitions?.Clear();
            OnResourceChanged = null;

            isInitialized = false;

            ArchonLogger.Log("ResourceSystem: Shutdown complete", "core_simulation");
        }

        #endregion
    }
}
