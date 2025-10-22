using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Core.Loaders;
using Core.Registries;

namespace Core.Initialization.Phases
{
    /// <summary>
    /// Phase 5: Link all string references to numeric IDs
    /// Resolves country tags, province references, and builds cross-references
    /// </summary>
    public class ReferenceLinkingPhase : IInitializationPhase
    {
        public string PhaseName => "Reference Linking";
        public float ProgressStart => 60f;
        public float ProgressEnd => 65f;

        public IEnumerator ExecuteAsync(InitializationContext context)
        {
            context.ReportProgress(60f, "Linking data references...");

            context.ReportProgress(61f, "Registering countries...");
            yield return null;

            // Load real country tags using ManifestLoader pattern
            var countryTagResult = CountryTagLoader.LoadCountryTags(context.Settings.DataDirectory);

            if (!countryTagResult.Success)
            {
                ArchonLogger.LogCoreDataLoadingError($"Failed to load country tags: {countryTagResult.ErrorMessage}");
                // Continue with limited functionality
            }

            // Register countries using actual tags from CountrySystem
            var countryIds = context.CountrySystem.GetAllCountryIds();
            ArchonLogger.LogCoreDataLoading($"Country registration: Found {countryIds.Length} countries to register");

            var tagToIdMapping = new Dictionary<string, ushort>();
            var registeredCount = 0;

            // Use actual tags from CountrySystem instead of assigning by position
            for (int i = 0; i < countryIds.Length; i++)
            {
                var countryId = countryIds[i];

                // Get the ACTUAL tag from CountrySystem
                var tag = context.CountrySystem.GetCountryTag(countryId);
                if (string.IsNullOrEmpty(tag) || tag == "---")
                    continue;

                var countryData = new CountryData
                {
                    Id = countryId,
                    Tag = tag
                };

                try
                {
                    context.Registries.Countries.Register(tag, countryData);
                    tagToIdMapping[tag] = countryId;
                    registeredCount++;

                    if (registeredCount <= 10) // Log first 10 for debugging
                    {
                        ArchonLogger.LogDataLinking($"Registered country '{tag}' with ID {countryId}");
                    }
                }
                catch (System.Exception e)
                {
                    ArchonLogger.LogDataLinkingError($"Failed to register country {tag} (ID: {countryId}): {e.Message}");
                }
            }

            // CRITICAL: Dispose NativeArray to prevent memory leak
            countryIds.Dispose();

            ArchonLogger.LogCoreDataLoading($"Country registration complete: {context.Registries.Countries.Count} countries registered with real tags");

            context.ReportProgress(62f, "Registering provinces with JSON5 history data...");
            yield return null;

            // STEP 1: Register provinces that have JSON5 history files (full data)
            ArchonLogger.LogCoreDataLoading($"Province processing: Found {context.ProvinceInitialStates.LoadedCount} provinces with JSON5 history files");

            for (int i = 0; i < context.ProvinceInitialStates.InitialStates.Length; i++)
            {
                var initialState = context.ProvinceInitialStates.InitialStates[i];

                // Skip provinces with invalid IDs (0 or negative)
                if (initialState.ProvinceID <= 0)
                    continue;

                // Create ProvinceData with real loaded data
                var provinceRegistryData = new ProvinceData
                {
                    RuntimeId = (ushort)initialState.ProvinceID,
                    DefinitionId = initialState.ProvinceID,
                    Name = $"Province {initialState.ProvinceID}",
                    Development = initialState.Development,
                    // Fix terrain: if province has development, it's land (terrain = 1), otherwise ocean (terrain = 0)
                    Terrain = (byte)(initialState.Development > 0 ? 1 : 0),
                    Flags = initialState.Flags,
                    BaseTax = initialState.BaseTax,
                    BaseProduction = initialState.BaseProduction,
                    BaseManpower = initialState.BaseManpower,
                    CenterOfTrade = initialState.CenterOfTrade
                };

                try
                {
                    context.Registries.Provinces.Register(initialState.ProvinceID, provinceRegistryData);
                }
                catch (System.Exception e)
                {
                    ArchonLogger.LogCoreDataLoadingWarning($"Failed to register province {initialState.ProvinceID}: {e.Message}");
                }
            }

            ArchonLogger.LogCoreDataLoading($"Province registration (JSON5): {context.Registries.Provinces.Count} provinces registered with historical data");

            context.ReportProgress(63f, "Filling in missing provinces from definition.csv...");
            yield return null;

            // STEP 2: Register remaining provinces from definition.csv (uncolonized/water provinces without JSON5)
            // RegisterDefinitions() automatically skips provinces already registered in step 1
            DefinitionLoader.RegisterDefinitions(context.ProvinceDefinitions, context.Registries.Provinces);

            ArchonLogger.LogCoreDataLoading($"Province registration complete: {context.Registries.Provinces.Count} total provinces (JSON5 + definition.csv)");

            context.ReportProgress(63f, "Resolving province references...");
            yield return null;

            // Get local copy to avoid struct property modification issues
            var provinceStates = context.ProvinceInitialStates;

            // Resolve string references in province data
            for (int i = 0; i < provinceStates.InitialStates.Length; i++)
            {
                var initialState = provinceStates.InitialStates[i];

                // Skip provinces with invalid IDs
                if (initialState.ProvinceID <= 0)
                    continue;

                var existingProvinceData = context.Registries.Provinces.GetByDefinition(initialState.ProvinceID);
                if (existingProvinceData != null)
                {
                    context.ReferenceResolver.ResolveProvinceReferences(ref initialState, existingProvinceData);

                    // CRITICAL: Save the updated initialState back to the array
                    provinceStates.InitialStates[i] = initialState;
                }
            }

            // Store modified struct back to context
            context.ProvinceInitialStates = provinceStates;

            context.ReportProgress(63f, "Resolving country references...");
            yield return null;

            // Resolve references for all countries
            // TODO: This will be implemented when country data contains string references

            context.ReportProgress(64f, "Applying resolved province data...");
            yield return null;

            // Apply the resolved province data to the hot ProvinceSystem
            context.ProvinceSystem.ApplyResolvedInitialStates(provinceStates.InitialStates);

            context.ReportProgress(64f, "Building cross-references...");
            yield return null;

            // Build bidirectional references
            context.CrossReferenceBuilder.BuildAllCrossReferences();

            context.ReportProgress(65f, "Validating data integrity...");
            yield return null;

            // Validate all references
            if (!context.DataValidator.ValidateGameData())
            {
                context.ReportError("Data validation failed after linking references");
                yield break;
            }

            context.ReportProgress(65f, "Reference linking complete");

            // Emit references linked event
            context.EventBus.Emit(new ReferencesLinkedEvent
            {
                CountriesLinked = context.Registries.Countries.Count,
                ProvincesLinked = context.Registries.Provinces.Count,
                ValidationErrors = context.DataValidator.GetErrors().Count,
                ValidationWarnings = context.DataValidator.GetWarnings().Count,
                TimeStamp = Time.time
            });
            context.EventBus.ProcessEvents();

            if (context.EnableDetailedLogging)
            {
                ArchonLogger.LogCoreDataLoading($"Phase complete: Linked references: {context.Registries.Countries.Count} countries, {context.Registries.Provinces.Count} provinces");
            }

            // Clean up - reuse the local copy we already have
            provinceStates.Dispose();
        }

        public void Rollback(InitializationContext context)
        {
            // Registries don't have rollback - on failure, entire GameState is recreated
            ArchonLogger.LogCoreDataLoading("Rolling back reference linking phase");
        }
    }

    /// <summary>
    /// Emitted when all string references have been resolved to numeric IDs
    /// Indicates the data linking phase is complete
    /// </summary>
    public struct ReferencesLinkedEvent : IGameEvent
    {
        public int CountriesLinked;
        public int ProvincesLinked;
        public int ValidationErrors;
        public int ValidationWarnings;
        public float TimeStamp { get; set; }
    }
}
