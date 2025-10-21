using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Core.Loaders;

namespace Core.Initialization.Phases
{
    /// <summary>
    /// Phase 4: Load country data using JSON5 + Burst architecture
    /// Loads all country files and initializes CountrySystem
    /// </summary>
    public class CountryDataLoadingPhase : IInitializationPhase
    {
        public string PhaseName => "Country Data Loading";
        public float ProgressStart => 40f;
        public float ProgressEnd => 60f;

        public IEnumerator ExecuteAsync(InitializationContext context)
        {
            context.ReportProgress(40f, "Loading countries...");

            // Load country tags FIRST to get correct tag→filename mapping
            var countryTagResult = CountryTagLoader.LoadCountryTags(context.Settings.DataDirectory);
            Dictionary<string, string> tagMapping = null;

            if (countryTagResult.Success)
            {
                tagMapping = countryTagResult.CountryTags;
                ArchonLogger.Log($"Loaded {tagMapping.Count} country tag mappings");
            }
            else
            {
                ArchonLogger.LogWarning($"Failed to load country tags: {countryTagResult.ErrorMessage}. Tags will be extracted from filenames.");
            }

            context.ReportProgress(50f, "Loading country JSON5 files...");
            yield return null;

            // Load country data using JSON5 + Burst architecture
            var countriesPath = System.IO.Path.Combine(context.Settings.DataDirectory, "common", "countries");
            var countryDataResult = BurstCountryLoader.LoadAllCountries(countriesPath, tagMapping);

            if (!countryDataResult.Success)
            {
                context.ReportError($"Country loading failed: {countryDataResult.ErrorMessage}");
                yield break;
            }

            ArchonLogger.Log($"Country loading complete: {countryDataResult.Statistics.FilesProcessed} countries loaded, {countryDataResult.Statistics.FilesSkipped} failed");

            context.ReportProgress(55f, "Initializing country system...");
            yield return null;

            // Initialize CountrySystem with loaded data
            context.CountrySystem.InitializeFromCountryData(countryDataResult);

            // Dispose countryDataResult immediately after use (data already copied into CountrySystem)
            // This frees the NativeArray (CountryDataCollection.hotDataArray) allocated with Allocator.Persistent
            countryDataResult.Dispose();

            context.ReportProgress(60f, "Country phase complete");

            // Emit country data ready event
            context.EventBus.Emit(new CountryDataReadyEvent
            {
                CountryCount = countryDataResult.Statistics.FilesProcessed,
                HasScenarioData = false, // Scenario data loaded in separate phase
                TimeStamp = Time.time
            });
            context.EventBus.ProcessEvents();

            if (context.EnableDetailedLogging)
            {
                ArchonLogger.Log($"Phase complete: Loaded {countryDataResult.Statistics.FilesProcessed} countries from JSON5 (ready for reference linking)");
            }
        }

        public void Rollback(InitializationContext context)
        {
            // CountrySystem doesn't have cleanup method - on failure, entire GameState is recreated
            ArchonLogger.Log("Rolling back country data loading phase");
        }
    }

    /// <summary>
    /// Event emitted when country data is fully loaded and ready
    /// Includes all country files and country system initialization
    /// </summary>
    public struct CountryDataReadyEvent : IGameEvent
    {
        public int CountryCount;
        public bool HasScenarioData;
        public float TimeStamp { get; set; }
    }
}
