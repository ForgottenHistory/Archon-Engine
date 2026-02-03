using System;
using Unity.Collections;
using UnityEngine;
using Core.Data;

namespace Core.Modifiers
{
    /// <summary>
    /// Identifies the scope level a modifier comes from (for tooltips)
    /// </summary>
    public enum ModifierScopeLevel : byte
    {
        Province = 0,   // From province-local sources (buildings, etc.)
        Country = 1,    // From country sources (policies, government, etc.)
        Global = 2      // From global sources (events, etc.)
    }

    /// <summary>
    /// ENGINE: Central manager for all modifier scopes (global, country, province)
    /// Pattern used by: EU4 (modifier system), CK3 (effect manager), Stellaris (empire modifiers)
    ///
    /// Architecture:
    /// - Global scope (inherited by all)
    /// - Country scopes (inherited by country provinces)
    /// - Province scopes (local only)
    ///
    /// Scope Inheritance:
    /// Province Final = Province Local + Country + Global
    ///
    /// Performance:
    /// - O(1) scope lookup
    /// - O(n) rebuild where n = active modifiers in scope chain
    /// - Dirty flag optimization (only rebuild when changed)
    ///
    /// Multiplayer Safe:
    /// - Deterministic operations only
    /// - Fixed-size allocations
    /// - Command-based modifications
    /// </summary>
    public class ModifierSystem : IDisposable
    {
        private const int MAX_COUNTRIES = 4096;
        private const int MAX_PROVINCES = 65536;

        // Global scope (inherited by everyone)
        private ScopedModifierContainer globalScope;

        // Country scopes (one per country, inherited by provinces)
        private NativeArray<ScopedModifierContainer> countryScopes;

        // Province scopes (one per province, local only)
        private NativeArray<ScopedModifierContainer> provinceScopes;

        private int maxCountries;
        private int maxProvinces;

        /// <summary>
        /// Initialize the modifier system
        /// </summary>
        public ModifierSystem(int maxCountries = MAX_COUNTRIES, int maxProvinces = MAX_PROVINCES)
        {
            this.maxCountries = maxCountries;
            this.maxProvinces = maxProvinces;

            // Create global scope
            globalScope = ScopedModifierContainer.Create();

            // Create country scopes
            countryScopes = new NativeArray<ScopedModifierContainer>(maxCountries, Allocator.Persistent);
            for (int i = 0; i < maxCountries; i++)
            {
                countryScopes[i] = ScopedModifierContainer.Create();
            }

            // Create province scopes
            provinceScopes = new NativeArray<ScopedModifierContainer>(maxProvinces, Allocator.Persistent);
            for (int i = 0; i < maxProvinces; i++)
            {
                provinceScopes[i] = ScopedModifierContainer.Create();
            }

            Debug.Log($"[ModifierSystem] Initialized with {maxCountries} countries, {maxProvinces} provinces");
        }

        #region Global Scope

        /// <summary>
        /// Add a global modifier (inherited by all)
        /// </summary>
        public bool AddGlobalModifier(ModifierSource source)
        {
            bool added = globalScope.Add(source);
            if (added)
            {
                // Mark all countries and provinces as dirty (they inherit global modifiers)
                MarkAllDirty();
            }
            return added;
        }

        /// <summary>
        /// Remove global modifiers by source
        /// </summary>
        public int RemoveGlobalModifiersBySource(ModifierSource.SourceType sourceType, uint sourceId)
        {
            int removed = globalScope.RemoveBySource(sourceType, sourceId);
            if (removed > 0)
            {
                MarkAllDirty();
            }
            return removed;
        }

        #endregion

        #region Country Scope

        /// <summary>
        /// Add a country modifier (inherited by country provinces)
        /// </summary>
        public bool AddCountryModifier(ushort countryId, ModifierSource source)
        {
            if (countryId >= maxCountries)
            {
                Debug.LogWarning($"[ModifierSystem] Country ID {countryId} exceeds max {maxCountries}");
                return false;
            }

            var scope = countryScopes[countryId];
            bool added = scope.Add(source);
            countryScopes[countryId] = scope; // Write back (struct value type)

            if (added)
            {
                // Mark all provinces owned by this country as dirty
                MarkCountryProvincesDirty(countryId);
            }

            return added;
        }

        /// <summary>
        /// Remove country modifiers by source
        /// </summary>
        public int RemoveCountryModifiersBySource(ushort countryId, ModifierSource.SourceType sourceType, uint sourceId)
        {
            if (countryId >= maxCountries)
                return 0;

            var scope = countryScopes[countryId];
            int removed = scope.RemoveBySource(sourceType, sourceId);
            countryScopes[countryId] = scope; // Write back

            if (removed > 0)
            {
                MarkCountryProvincesDirty(countryId);
            }

            return removed;
        }

        #endregion

        #region Province Scope

        /// <summary>
        /// Add a province modifier (local only)
        /// </summary>
        public bool AddProvinceModifier(ushort provinceId, ModifierSource source)
        {
            if (provinceId >= maxProvinces)
            {
                ArchonLogger.LogWarning($"[ModifierSystem] Province ID {provinceId} exceeds max {maxProvinces}", "core_simulation");
                return false;
            }

            var scope = provinceScopes[provinceId];
            bool added = scope.Add(source);
            provinceScopes[provinceId] = scope; // Write back (struct value type)

            return added;
        }

        /// <summary>
        /// Remove province modifiers by source
        /// </summary>
        public int RemoveProvinceModifiersBySource(ushort provinceId, ModifierSource.SourceType sourceType, uint sourceId)
        {
            if (provinceId >= maxProvinces)
                return 0;

            var scope = provinceScopes[provinceId];
            int removed = scope.RemoveBySource(sourceType, sourceId);
            provinceScopes[provinceId] = scope; // Write back

            return removed;
        }

        /// <summary>
        /// Remove province modifiers by source and type
        /// </summary>
        public int RemoveProvinceModifiersBySourceAndType(ushort provinceId, ModifierSource.SourceType sourceType, uint sourceId, ushort modifierTypeId)
        {
            if (provinceId >= maxProvinces)
                return 0;

            var scope = provinceScopes[provinceId];
            int removed = scope.RemoveBySourceAndType(sourceType, sourceId, modifierTypeId);
            provinceScopes[provinceId] = scope; // Write back

            return removed;
        }

        #endregion

        #region Queries

        /// <summary>
        /// Get province modifier value with full scope inheritance
        /// Province Final = Province Local + Country + Global
        /// </summary>
        public FixedPoint64 GetProvinceModifier(ushort provinceId, ushort countryId, ushort modifierTypeId, FixedPoint64 baseValue)
        {
            if (provinceId >= maxProvinces)
                return baseValue;

            // Build scope chain: Global → Country → Province
            // CRITICAL: Write back after RebuildIfDirty to persist isDirty=false (struct value types!)
            // Without write-back, every call rebuilds the cache (O(512) per province per query)

            // Get country scope (inherits from global)
            ScopedModifierContainer? countryScope = null;
            if (countryId < maxCountries)
            {
                var country = countryScopes[countryId];
                country.RebuildIfDirty(globalScope);
                countryScopes[countryId] = country; // Write back to persist clean state
                countryScope = country;
            }

            // Apply province modifiers (inherits from country → global chain)
            var provinceScope = provinceScopes[provinceId];
            var result = provinceScope.ApplyModifier(modifierTypeId, baseValue, countryScope);
            provinceScopes[provinceId] = provinceScope; // Write back to persist clean state
            return result;
        }

        /// <summary>
        /// Get country modifier value with global inheritance
        /// Country Final = Country Local + Global
        /// </summary>
        public FixedPoint64 GetCountryModifier(ushort countryId, ushort modifierTypeId, FixedPoint64 baseValue)
        {
            if (countryId >= maxCountries)
                return baseValue;

            var countryScope = countryScopes[countryId];
            var result = countryScope.ApplyModifier(modifierTypeId, baseValue, globalScope);
            countryScopes[countryId] = countryScope; // Write back to persist clean state
            return result;
        }

        /// <summary>
        /// Get global modifier value
        /// </summary>
        public FixedPoint64 GetGlobalModifier(ushort modifierTypeId, FixedPoint64 baseValue)
        {
            return globalScope.ApplyModifier(modifierTypeId, baseValue);
        }

        #endregion

        #region Query API

        /// <summary>
        /// Iterate over all province modifiers with full inheritance (Province + Country + Global)
        /// Useful for tooltips showing all modifier sources affecting a province
        /// </summary>
        public void ForEachProvinceModifierWithInheritance(ushort provinceId, ushort countryId, Action<ModifierSource, ModifierScopeLevel> action)
        {
            // Global modifiers first
            globalScope.ForEachLocalModifier(mod => action(mod, ModifierScopeLevel.Global));

            // Country modifiers (if valid)
            if (countryId < maxCountries)
            {
                countryScopes[countryId].ForEachLocalModifier(mod => action(mod, ModifierScopeLevel.Country));
            }

            // Province modifiers
            if (provinceId < maxProvinces)
            {
                provinceScopes[provinceId].ForEachLocalModifier(mod => action(mod, ModifierScopeLevel.Province));
            }
        }

        /// <summary>
        /// Iterate over all country modifiers with global inheritance (Country + Global)
        /// </summary>
        public void ForEachCountryModifierWithInheritance(ushort countryId, Action<ModifierSource, ModifierScopeLevel> action)
        {
            // Global modifiers first
            globalScope.ForEachLocalModifier(mod => action(mod, ModifierScopeLevel.Global));

            // Country modifiers
            if (countryId < maxCountries)
            {
                countryScopes[countryId].ForEachLocalModifier(mod => action(mod, ModifierScopeLevel.Country));
            }
        }

        /// <summary>
        /// Iterate over province modifiers of a specific modifier type (with inheritance)
        /// Useful for tooltips showing all sources affecting a specific stat
        /// </summary>
        public void ForEachProvinceModifierByType(ushort provinceId, ushort countryId, ushort modifierTypeId, Action<ModifierSource, ModifierScopeLevel> action)
        {
            // Global modifiers
            globalScope.ForEachLocalModifierByType(modifierTypeId, mod => action(mod, ModifierScopeLevel.Global));

            // Country modifiers
            if (countryId < maxCountries)
            {
                countryScopes[countryId].ForEachLocalModifierByType(modifierTypeId, mod => action(mod, ModifierScopeLevel.Country));
            }

            // Province modifiers
            if (provinceId < maxProvinces)
            {
                provinceScopes[provinceId].ForEachLocalModifierByType(modifierTypeId, mod => action(mod, ModifierScopeLevel.Province));
            }
        }

        /// <summary>
        /// Iterate over province modifiers from a specific source (local only - not inherited)
        /// Useful for UI showing what modifiers a specific building/tech adds
        /// </summary>
        public void ForEachProvinceModifierBySource(ushort provinceId, ModifierSource.SourceType sourceType, uint sourceId, Action<ModifierSource> action)
        {
            if (provinceId >= maxProvinces)
                return;

            provinceScopes[provinceId].ForEachLocalModifierBySource(sourceType, sourceId, action);
        }

        /// <summary>
        /// Iterate over country modifiers from a specific source
        /// </summary>
        public void ForEachCountryModifierBySource(ushort countryId, ModifierSource.SourceType sourceType, uint sourceId, Action<ModifierSource> action)
        {
            if (countryId >= maxCountries)
                return;

            countryScopes[countryId].ForEachLocalModifierBySource(sourceType, sourceId, action);
        }

        /// <summary>
        /// Iterate over global modifiers from a specific source
        /// </summary>
        public void ForEachGlobalModifierBySource(ModifierSource.SourceType sourceType, uint sourceId, Action<ModifierSource> action)
        {
            globalScope.ForEachLocalModifierBySource(sourceType, sourceId, action);
        }

        /// <summary>
        /// Count province modifiers from a specific source
        /// </summary>
        public int CountProvinceModifiersBySource(ushort provinceId, ModifierSource.SourceType sourceType, uint sourceId)
        {
            if (provinceId >= maxProvinces)
                return 0;

            return provinceScopes[provinceId].CountLocalModifiersBySource(sourceType, sourceId);
        }

        /// <summary>
        /// Count country modifiers from a specific source
        /// </summary>
        public int CountCountryModifiersBySource(ushort countryId, ModifierSource.SourceType sourceType, uint sourceId)
        {
            if (countryId >= maxCountries)
                return 0;

            return countryScopes[countryId].CountLocalModifiersBySource(sourceType, sourceId);
        }

        /// <summary>
        /// Check if a source has any modifiers on a province
        /// </summary>
        public bool HasProvinceModifiersFromSource(ushort provinceId, ModifierSource.SourceType sourceType, uint sourceId)
        {
            return CountProvinceModifiersBySource(provinceId, sourceType, sourceId) > 0;
        }

        /// <summary>
        /// Check if a source has any modifiers on a country
        /// </summary>
        public bool HasCountryModifiersFromSource(ushort countryId, ModifierSource.SourceType sourceType, uint sourceId)
        {
            return CountCountryModifiersBySource(countryId, sourceType, sourceId) > 0;
        }

        #endregion

        #region Batch Removal

        /// <summary>
        /// Remove all modifiers from a province scope
        /// Returns count of modifiers removed
        /// </summary>
        public int ClearProvinceModifiers(ushort provinceId)
        {
            if (provinceId >= maxProvinces)
                return 0;

            var scope = provinceScopes[provinceId];
            int removed = scope.LocalModifierCount;
            scope.Clear();
            provinceScopes[provinceId] = scope; // Write back

            return removed;
        }

        /// <summary>
        /// Remove all modifiers from a country scope
        /// Returns count of modifiers removed
        /// </summary>
        public int ClearCountryModifiers(ushort countryId)
        {
            if (countryId >= maxCountries)
                return 0;

            var scope = countryScopes[countryId];
            int removed = scope.LocalModifierCount;
            scope.Clear();
            countryScopes[countryId] = scope; // Write back

            // Mark owned provinces as dirty
            MarkCountryProvincesDirty(countryId);

            return removed;
        }

        /// <summary>
        /// Remove all global modifiers
        /// Returns count of modifiers removed
        /// </summary>
        public int ClearGlobalModifiers()
        {
            int removed = globalScope.LocalModifierCount;
            globalScope.Clear();
            MarkAllDirty();

            return removed;
        }

        #endregion

        #region Tick Updates

        /// <summary>
        /// Expire temporary modifiers (call every game tick)
        /// </summary>
        public void ExpireModifiers(int currentTick)
        {
            // Expire global modifiers
            int globalExpired = globalScope.ExpireModifiers(currentTick);
            if (globalExpired > 0)
            {
                MarkAllDirty();
            }

            // Expire country modifiers
            for (int i = 0; i < maxCountries; i++)
            {
                var scope = countryScopes[i];
                int countryExpired = scope.ExpireModifiers(currentTick);
                if (countryExpired > 0)
                {
                    countryScopes[i] = scope; // Write back
                    MarkCountryProvincesDirty((ushort)i);
                }
            }

            // Expire province modifiers
            for (int i = 0; i < maxProvinces; i++)
            {
                var scope = provinceScopes[i];
                int provinceExpired = scope.ExpireModifiers(currentTick);
                if (provinceExpired > 0)
                {
                    provinceScopes[i] = scope; // Write back
                }
            }
        }

        #endregion

        #region Dirty Tracking

        /// <summary>
        /// Mark all scopes as dirty (when global modifiers change)
        /// </summary>
        private void MarkAllDirty()
        {
            // Mark all countries dirty
            for (int i = 0; i < maxCountries; i++)
            {
                var scope = countryScopes[i];
                scope.MarkDirty();
                countryScopes[i] = scope;
            }

            // Mark all provinces dirty
            for (int i = 0; i < maxProvinces; i++)
            {
                var scope = provinceScopes[i];
                scope.MarkDirty();
                provinceScopes[i] = scope;
            }
        }

        /// <summary>
        /// Mark all provinces owned by a country as dirty
        /// TODO: This requires province ownership lookup - implement when ProvinceSystem is available
        /// </summary>
        private void MarkCountryProvincesDirty(ushort countryId)
        {
            // For now, mark ALL provinces dirty (inefficient but safe)
            // TODO: Optimize by only marking provinces owned by this country
            for (int i = 0; i < maxProvinces; i++)
            {
                var scope = provinceScopes[i];
                scope.MarkDirty();
                provinceScopes[i] = scope;
            }
        }

        #endregion

        #region Save/Load Support

        /// <summary>
        /// Save ModifierSystem state to binary writer
        /// Serializes: capacities, global scope, country scopes, province scopes
        /// </summary>
        public void SaveState(System.IO.BinaryWriter writer)
        {
            // Write capacities (for validation on load)
            writer.Write(maxCountries);
            writer.Write(maxProvinces);

            // Write global scope
            SaveScopedContainer(writer, globalScope);

            // Write country scopes
            for (int i = 0; i < maxCountries; i++)
            {
                SaveScopedContainer(writer, countryScopes[i]);
            }

            // Write province scopes
            for (int i = 0; i < maxProvinces; i++)
            {
                SaveScopedContainer(writer, provinceScopes[i]);
            }

            Debug.Log($"[ModifierSystem] Saved state (countries: {maxCountries}, provinces: {maxProvinces})");
        }

        /// <summary>
        /// Load ModifierSystem state from binary reader
        /// Restores: capacities, global scope, country scopes, province scopes
        /// Note: Must be called AFTER Initialize() with matching capacities
        /// </summary>
        public void LoadState(System.IO.BinaryReader reader)
        {
            // Read and validate capacities
            int savedMaxCountries = reader.ReadInt32();
            int savedMaxProvinces = reader.ReadInt32();

            if (savedMaxCountries != maxCountries)
            {
                Debug.LogWarning($"[ModifierSystem] Country capacity mismatch (saved: {savedMaxCountries}, current: {maxCountries})");
            }

            if (savedMaxProvinces != maxProvinces)
            {
                Debug.LogWarning($"[ModifierSystem] Province capacity mismatch (saved: {savedMaxProvinces}, current: {maxProvinces})");
            }

            // Load global scope
            LoadScopedContainer(reader, ref globalScope);

            // Load country scopes
            for (int i = 0; i < maxCountries; i++)
            {
                var scope = countryScopes[i];
                LoadScopedContainer(reader, ref scope);
                countryScopes[i] = scope; // Write back (struct value type)
            }

            // Load province scopes
            for (int i = 0; i < maxProvinces; i++)
            {
                var scope = provinceScopes[i];
                LoadScopedContainer(reader, ref scope);
                provinceScopes[i] = scope; // Write back (struct value type)
            }

            Debug.Log($"[ModifierSystem] Loaded state (countries: {savedMaxCountries}, provinces: {savedMaxProvinces})");
        }

        /// <summary>
        /// Save a ScopedModifierContainer to binary writer
        /// Only saves local modifiers - cachedModifierSet will be rebuilt on load
        /// </summary>
        private void SaveScopedContainer(System.IO.BinaryWriter writer, ScopedModifierContainer container)
        {
            // Get access to internal state via ActiveModifierList
            // We only need to save local modifiers (cachedModifierSet is derived data)
            int activeCount = container.LocalModifierCount;
            writer.Write(activeCount);

            if (activeCount == 0)
                return; // No modifiers to save

            // Iterate and save all local modifiers
            int savedCount = 0;
            container.ForEachLocalModifier((modifier) =>
            {
                // Write ModifierSource fields
                writer.Write((byte)modifier.Type);
                writer.Write(modifier.SourceID);
                writer.Write(modifier.ModifierTypeId);
                writer.Write(modifier.Value.RawValue);  // FixedPoint64: save as long
                writer.Write(modifier.IsMultiplicative);
                writer.Write(modifier.IsTemporary);
                writer.Write(modifier.ExpirationTick);
                savedCount++;
            });

            if (savedCount != activeCount)
            {
                Debug.LogWarning($"[ModifierSystem] Saved count mismatch (expected: {activeCount}, actual: {savedCount})");
            }
        }

        /// <summary>
        /// Load a ScopedModifierContainer from binary reader
        /// Rebuilds cache after loading by marking as dirty
        /// </summary>
        private void LoadScopedContainer(System.IO.BinaryReader reader, ref ScopedModifierContainer container)
        {
            // Clear existing modifiers
            container.Clear();

            // Read active count
            int activeCount = reader.ReadInt32();

            if (activeCount == 0)
            {
                container.MarkDirty(); // Force rebuild even if empty
                return;
            }

            // Load all modifiers
            for (int i = 0; i < activeCount; i++)
            {
                var modifier = new ModifierSource
                {
                    Type = (ModifierSource.SourceType)reader.ReadByte(),
                    SourceID = reader.ReadUInt32(),
                    ModifierTypeId = reader.ReadUInt16(),
                    Value = FixedPoint64.FromRaw(reader.ReadInt64()),  // FixedPoint64: load from long
                    IsMultiplicative = reader.ReadBoolean(),
                    IsTemporary = reader.ReadBoolean(),
                    ExpirationTick = reader.ReadInt32()
                };

                container.Add(modifier);
            }

            // Mark as dirty to force cache rebuild on next access
            container.MarkDirty();
        }

        #endregion

        #region Cleanup

        /// <summary>
        /// Dispose native collections
        /// </summary>
        public void Dispose()
        {
            globalScope.Dispose();

            for (int i = 0; i < maxCountries; i++)
            {
                countryScopes[i].Dispose();
            }
            countryScopes.Dispose();

            for (int i = 0; i < maxProvinces; i++)
            {
                provinceScopes[i].Dispose();
            }
            provinceScopes.Dispose();

            Debug.Log("[ModifierSystem] Disposed");
        }

        #endregion
    }
}
