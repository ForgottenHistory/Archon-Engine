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
    ///
    /// GRACEFUL DEGRADATION:
    /// - No province history: Skip province reference resolution
    /// - No countries: Skip country registration
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
                ArchonLogger.LogWarning($"Failed to load country tags: {countryTagResult.ErrorMessage}", "core_data_loading");
                // Continue with limited functionality
            }

            // Register countries using actual tags from CountrySystem
            var countryIds = context.CountrySystem.GetAllCountryIds();
            ArchonLogger.Log($"Country registration: Found {countryIds.Length} countries to register", "core_data_loading");

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
                        ArchonLogger.Log($"Registered country '{tag}' with ID {countryId}", "core_data_linking");
                    }
                }
                catch (System.Exception e)
                {
                    ArchonLogger.LogError($"Failed to register country {tag} (ID: {countryId}): {e.Message}", "core_data_linking");
                }
            }

            // CRITICAL: Dispose NativeArray to prevent memory leak
            countryIds.Dispose();

            ArchonLogger.Log($"Country registration complete: {context.Registries.Countries.Count} countries registered with real tags", "core_data_loading");

            context.ReportProgress(62f, "Registering provinces...");
            yield return null;

            // Check if we have province history data
            bool hasProvinceHistory = context.ProvinceInitialStates.Success &&
                                      context.ProvinceInitialStates.InitialStates.IsCreated &&
                                      context.ProvinceInitialStates.InitialStates.Length > 0;

            if (hasProvinceHistory)
            {
                // STEP 1: Register provinces that have JSON5 history files (full data)
                ArchonLogger.Log($"Province processing: Found {context.ProvinceInitialStates.LoadedCount} provinces with JSON5 history files", "core_data_loading");

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
                        ArchonLogger.LogWarning($"Failed to register province {initialState.ProvinceID}: {e.Message}", "core_data_loading");
                    }
                }

                ArchonLogger.Log($"Province registration (JSON5): {context.Registries.Provinces.Count} provinces registered with historical data", "core_data_loading");
            }
            else
            {
                ArchonLogger.Log("No province history data - skipping JSON5 province registration", "core_data_loading");
            }

            context.ReportProgress(63f, "Filling in missing provinces from definition.csv...");
            yield return null;

            // STEP 2: Register remaining provinces from definition.csv (uncolonized/water provinces without JSON5)
            // RegisterDefinitions() automatically skips provinces already registered in step 1
            if (context.ProvinceDefinitions != null && context.ProvinceDefinitions.Count > 0)
            {
                DefinitionLoader.RegisterDefinitions(context.ProvinceDefinitions, context.Registries.Provinces);
                ArchonLogger.Log($"Province registration complete: {context.Registries.Provinces.Count} total provinces (JSON5 + definition.csv)", "core_data_loading");
            }
            else
            {
                ArchonLogger.Log($"Province registration complete: {context.Registries.Provinces.Count} total provinces (no definition.csv)", "core_data_loading");
            }

            context.ReportProgress(63f, "Resolving province references...");
            yield return null;

            // Only resolve references if we have province history data
            if (hasProvinceHistory)
            {
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

                context.ReportProgress(64f, "Applying resolved province data...");
                yield return null;

                // Apply the resolved province data to the hot ProvinceSystem
                context.ProvinceSystem.ApplyResolvedInitialStates(provinceStates.InitialStates);

                // Clean up
                provinceStates.Dispose();
            }
            else
            {
                ArchonLogger.Log("No province history data - skipping reference resolution", "core_data_loading");
            }

            context.ReportProgress(63f, "Resolving country references...");
            yield return null;

            // Resolve references for all countries
            // TODO: This will be implemented when country data contains string references

            context.ReportProgress(64f, "Building cross-references...");
            yield return null;

            // Build bidirectional references
            context.CrossReferenceBuilder.BuildAllCrossReferences();

            context.ReportProgress(65f, "Validating data integrity...");
            yield return null;

            // Detect if we're in map-only mode (no province history, no countries, or no definitions)
            bool isMapOnlyMode = !hasProvinceHistory ||
                                 context.Registries.Countries.Count == 0 ||
                                 context.Registries.Provinces.Count == 0;

            if (isMapOnlyMode)
            {
                // Enable map-only mode on validator - missing data becomes warnings instead of errors
                context.DataValidator.SetMapOnlyMode(true);
                ArchonLogger.Log("Running in map-only mode - validation will be relaxed", "core_data_loading");
            }

            // Validate all references
            var validationResult = context.DataValidator.ValidateGameData();
            if (!validationResult)
            {
                // In map-only mode, we've already converted missing data errors to warnings
                // So any remaining errors are real problems
                var errors = context.DataValidator.GetErrors();
                if (errors.Count > 0)
                {
                    context.ReportError("Data validation failed after linking references");
                    yield break;
                }
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
                ArchonLogger.Log($"Phase complete: Linked references: {context.Registries.Countries.Count} countries, {context.Registries.Provinces.Count} provinces", "core_data_loading");
            }
        }

        public void Rollback(InitializationContext context)
        {
            // Registries don't have rollback - on failure, entire GameState is recreated
            ArchonLogger.Log("Rolling back reference linking phase", "core_data_loading");
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
