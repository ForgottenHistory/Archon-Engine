using System.Collections.Generic;
using Core.Data;
using Utils;

namespace Core.Resources
{
    /// <summary>
    /// ENGINE LAYER: Generic resource storage and management system.
    ///
    /// Stores and manages multiple resource types (gold, manpower, prestige, etc.) for all countries.
    /// Uses deterministic fixed-point math for multiplayer compatibility.
    ///
    /// Architecture:
    /// - Dictionary storage: resourceId → array of country values
    /// - Fixed-size arrays: countryId → resource amount
    /// - Deterministic: FixedPoint64 only, no float operations
    /// - Event-driven: Emits ResourceChangedEvent via EventBus
    ///
    /// Usage:
    /// 1. Initialize with country capacity and EventBus
    /// 2. Register resource types with RegisterResource(resourceId, definition)
    /// 3. Use AddResource/RemoveResource/GetResource for all operations
    /// 4. Use CanAfford/TrySpend for cost validation
    ///
    /// Performance:
    /// - GetResource: O(1) dictionary lookup + O(1) array access
    /// - Memory: sizeof(FixedPoint64) × resourceCount × countryCount
    /// - Example: 10 resources × 200 countries × 8 bytes = 16 KB
    /// </summary>
    public class ResourceSystem : IResourceProvider
    {
        // Storage: resourceId → array of country values
        private Dictionary<ushort, FixedPoint64[]> resourceStorageByType;

        // Resource definitions (for validation and metadata)
        private Dictionary<ushort, ResourceDefinition> resourceDefinitions;

        // Country capacity (set during initialization)
        private int maxCountries;

        // EventBus for emitting events
        private EventBus eventBus;

        // Batch mode state
        private bool isBatchMode;
        private int batchChangeCount;

        // Initialization state
        private bool isInitialized;

        #region Initialization

        /// <summary>
        /// Initialize ResourceSystem with country capacity and EventBus.
        /// Must be called before any resource operations.
        /// </summary>
        public void Initialize(int countryCapacity, EventBus eventBus = null)
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
            this.eventBus = eventBus;
            resourceStorageByType = new Dictionary<ushort, FixedPoint64[]>();
            resourceDefinitions = new Dictionary<ushort, ResourceDefinition>();
            isBatchMode = false;
            batchChangeCount = 0;

            isInitialized = true;

            ArchonLogger.Log($"ResourceSystem: Initialized for {maxCountries} countries", "core_simulation");

            // Emit initialization event
            eventBus?.Emit(new ResourceSystemInitializedEvent { MaxCountries = maxCountries });
        }

        /// <summary>
        /// Register a resource type with its definition.
        /// Creates storage array for all countries and initializes with starting amount.
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
                ArchonLogger.LogError("ResourceSystem: Cannot register null resource definition", "core_simulation");
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

            // Emit registration event
            eventBus?.Emit(new ResourceRegisteredEvent { ResourceId = resourceId, ResourceStringId = definition.id });
        }

        #endregion

        #region Single Resource Operations

        /// <summary>
        /// Get current resource amount for a country.
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
        /// Add resource to a country (clamped to max value).
        /// </summary>
        public void AddResource(ushort countryId, ushort resourceId, FixedPoint64 amount)
        {
            if (!ValidateOperation(countryId, resourceId, out FixedPoint64[] storage))
            {
                return;
            }

            if (amount.IsNegative)
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

            EmitResourceChanged(countryId, resourceId, oldAmount, newAmount);
        }

        /// <summary>
        /// Remove resource from a country (returns true if successful, false if insufficient).
        /// </summary>
        public bool RemoveResource(ushort countryId, ushort resourceId, FixedPoint64 amount)
        {
            if (!ValidateOperation(countryId, resourceId, out FixedPoint64[] storage))
            {
                return false;
            }

            if (amount.IsNegative)
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

            EmitResourceChanged(countryId, resourceId, oldAmount, newAmount);

            return true;
        }

        /// <summary>
        /// Set resource to exact amount (for dev commands/testing).
        /// Clamped to min/max values.
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

            FixedPoint64 newAmount = FixedPoint64.Clamp(amount, minValue, maxValue);

            storage[countryId] = newAmount;

            EmitResourceChanged(countryId, resourceId, oldAmount, newAmount);
        }

        #endregion

        #region Cost Validation

        /// <summary>
        /// Check if country can afford a single cost.
        /// </summary>
        public bool CanAfford(ushort countryId, ushort resourceId, FixedPoint64 amount)
        {
            return GetResource(countryId, resourceId) >= amount;
        }

        /// <summary>
        /// Check if country can afford multiple costs.
        /// </summary>
        public bool CanAfford(ushort countryId, ResourceCost[] costs)
        {
            if (costs == null || costs.Length == 0)
                return true;

            for (int i = 0; i < costs.Length; i++)
            {
                if (!CanAfford(countryId, costs[i].ResourceId, costs[i].Amount))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Try to spend multiple costs atomically (all or nothing).
        /// Returns false if insufficient resources (no changes made).
        /// </summary>
        public bool TrySpend(ushort countryId, ResourceCost[] costs)
        {
            if (costs == null || costs.Length == 0)
                return true;

            // First check if we can afford everything
            if (!CanAfford(countryId, costs))
            {
                return false;
            }

            // Now spend all resources
            for (int i = 0; i < costs.Length; i++)
            {
                RemoveResource(countryId, costs[i].ResourceId, costs[i].Amount);
            }

            return true;
        }

        #endregion

        #region Bulk Operations

        /// <summary>
        /// Add resource to all countries.
        /// </summary>
        public void AddResourceToAll(ushort resourceId, FixedPoint64 amount)
        {
            if (!ValidateOperation(0, resourceId, out FixedPoint64[] storage))
            {
                return;
            }

            ResourceDefinition definition = resourceDefinitions[resourceId];
            FixedPoint64 maxValue = definition.GetMaxValueFixed();

            for (ushort i = 0; i < maxCountries; i++)
            {
                FixedPoint64 oldAmount = storage[i];
                FixedPoint64 newAmount = FixedPoint64.Min(oldAmount + amount, maxValue);
                storage[i] = newAmount;

                EmitResourceChanged(i, resourceId, oldAmount, newAmount);
            }
        }

        /// <summary>
        /// Set resource for all countries.
        /// </summary>
        public void SetResourceForAll(ushort resourceId, FixedPoint64 amount)
        {
            if (!ValidateOperation(0, resourceId, out FixedPoint64[] storage))
            {
                return;
            }

            ResourceDefinition definition = resourceDefinitions[resourceId];
            FixedPoint64 minValue = definition.GetMinValueFixed();
            FixedPoint64 maxValue = definition.GetMaxValueFixed();
            FixedPoint64 clampedAmount = FixedPoint64.Clamp(amount, minValue, maxValue);

            for (ushort i = 0; i < maxCountries; i++)
            {
                FixedPoint64 oldAmount = storage[i];
                storage[i] = clampedAmount;

                EmitResourceChanged(i, resourceId, oldAmount, clampedAmount);
            }
        }

        /// <summary>
        /// Transfer resource between countries (returns false if insufficient).
        /// </summary>
        public bool TransferResource(ushort fromCountryId, ushort toCountryId, ushort resourceId, FixedPoint64 amount)
        {
            if (!ValidateOperation(fromCountryId, resourceId, out FixedPoint64[] storage))
            {
                return false;
            }

            if (toCountryId >= maxCountries)
            {
                ArchonLogger.LogError($"ResourceSystem: Invalid toCountryId {toCountryId}", "core_simulation");
                return false;
            }

            // Check if sender has enough
            if (storage[fromCountryId] < amount)
            {
                return false;
            }

            // Perform transfer
            FixedPoint64 fromOld = storage[fromCountryId];
            FixedPoint64 toOld = storage[toCountryId];

            storage[fromCountryId] = fromOld - amount;
            storage[toCountryId] = toOld + amount;

            // Clamp recipient to max
            ResourceDefinition definition = resourceDefinitions[resourceId];
            FixedPoint64 maxValue = definition.GetMaxValueFixed();
            if (storage[toCountryId] > maxValue)
            {
                storage[toCountryId] = maxValue;
            }

            // Emit events
            EmitResourceChanged(fromCountryId, resourceId, fromOld, storage[fromCountryId]);
            EmitResourceChanged(toCountryId, resourceId, toOld, storage[toCountryId]);

            // Emit transfer event
            if (!isBatchMode && eventBus != null)
            {
                eventBus.Emit(new ResourceTransferredEvent
                {
                    FromCountryId = fromCountryId,
                    ToCountryId = toCountryId,
                    ResourceId = resourceId,
                    Amount = amount
                });
            }

            return true;
        }

        #endregion

        #region Batch Mode

        /// <summary>
        /// Begin batch mode - suppresses events until EndBatch is called.
        /// Use during loading/setup to avoid thousands of individual events.
        /// </summary>
        public void BeginBatch()
        {
            if (isBatchMode)
            {
                ArchonLogger.LogWarning("ResourceSystem: Already in batch mode", "core_simulation");
                return;
            }

            isBatchMode = true;
            batchChangeCount = 0;
        }

        /// <summary>
        /// End batch mode - emits a single batch completed event.
        /// </summary>
        public void EndBatch()
        {
            if (!isBatchMode)
            {
                ArchonLogger.LogWarning("ResourceSystem: Not in batch mode", "core_simulation");
                return;
            }

            int changes = batchChangeCount;
            isBatchMode = false;
            batchChangeCount = 0;

            // Emit batch completed event
            eventBus?.Emit(new ResourceBatchCompletedEvent { ChangeCount = changes });

            ArchonLogger.Log($"ResourceSystem: Batch completed with {changes} changes", "core_simulation");
        }

        /// <summary>
        /// Whether currently in batch mode.
        /// </summary>
        public bool IsBatchMode => isBatchMode;

        #endregion

        #region Queries

        /// <summary>
        /// Check if a resource type is registered.
        /// </summary>
        public bool IsResourceRegistered(ushort resourceId)
        {
            return isInitialized && resourceDefinitions.ContainsKey(resourceId);
        }

        /// <summary>
        /// Get resource definition by ID.
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
        /// Get all registered resource IDs.
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
        /// Get total amount of a resource across all countries.
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

        /// <summary>
        /// Get all resources for a country as dictionary (for UI/debugging).
        /// </summary>
        public Dictionary<ushort, FixedPoint64> GetAllResourcesForCountry(ushort countryId)
        {
            var result = new Dictionary<ushort, FixedPoint64>();

            if (!isInitialized || countryId >= maxCountries)
            {
                return result;
            }

            foreach (var kvp in resourceStorageByType)
            {
                result[kvp.Key] = kvp.Value[countryId];
            }

            return result;
        }

        #endregion

        #region State Management

        /// <summary>
        /// Check if initialized.
        /// </summary>
        public bool IsInitialized => isInitialized;

        /// <summary>
        /// Get country capacity.
        /// </summary>
        public int MaxCountries => maxCountries;

        /// <summary>
        /// Get number of registered resources.
        /// </summary>
        public int ResourceCount => isInitialized ? resourceDefinitions.Count : 0;

        /// <summary>
        /// Shutdown resource system (cleanup).
        /// </summary>
        public void Shutdown()
        {
            if (!isInitialized)
            {
                return;
            }

            resourceStorageByType?.Clear();
            resourceDefinitions?.Clear();
            eventBus = null;
            isBatchMode = false;
            batchChangeCount = 0;

            isInitialized = false;

            ArchonLogger.Log("ResourceSystem: Shutdown complete", "core_simulation");
        }

        #endregion

        #region Private Helpers

        /// <summary>
        /// Validate operation parameters and return storage array.
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

        /// <summary>
        /// Emit resource changed event (respects batch mode).
        /// </summary>
        private void EmitResourceChanged(ushort countryId, ushort resourceId, FixedPoint64 oldAmount, FixedPoint64 newAmount)
        {
            // Skip if no actual change
            if (oldAmount == newAmount)
                return;

            if (isBatchMode)
            {
                batchChangeCount++;
                return;
            }

            // Emit EventBus event
            eventBus?.Emit(new ResourceChangedEvent
            {
                CountryId = countryId,
                ResourceId = resourceId,
                OldAmount = oldAmount,
                NewAmount = newAmount
            });
        }

        #endregion
    }
}
