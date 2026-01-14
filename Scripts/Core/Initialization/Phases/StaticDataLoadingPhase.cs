using System.Collections;
using UnityEngine;
using Core.Loaders;
using Core.Localization;

namespace Core.Initialization.Phases
{
    /// <summary>
    /// Phase 2: Load static game data (terrains, water provinces)
    /// Loads all static reference data before loading dynamic province/country data
    /// </summary>
    public class StaticDataLoadingPhase : IInitializationPhase
    {
        public string PhaseName => "Static Data Loading";
        public float ProgressStart => 5f;
        public float ProgressEnd => 15f;

        public IEnumerator ExecuteAsync(InitializationContext context)
        {
            context.ReportProgress(5f, "Loading static data...");

            // Load localization first - makes strings available for all other systems
            context.ReportProgress(5.5f, "Loading localization...");
            yield return null;

            var localisationPath = System.IO.Path.Combine(context.Settings.DataDirectory, "localisation");
            LocalizationManager.Initialize(localisationPath, "english");

            context.ReportProgress(8f, "Loading terrain types...");
            yield return null;

            // Load terrain types
            TerrainLoader.LoadTerrains(context.Registries.Terrains, context.Settings.DataDirectory);

            context.ReportProgress(13f, "Loading water province definitions...");
            yield return null;

            // Load water province definitions from default.json5 and terrain.json5
            WaterProvinceLoader.LoadWaterProvinceData(context.Settings.DataDirectory);

            context.ReportProgress(14f, "Validating static data...");
            yield return null;

            // Validate that static data loaded successfully
            if (!context.Registries.ValidateRegistries())
            {
                context.ReportError("Static data validation failed - some required data could not be loaded");
                yield break;
            }

            context.ReportProgress(15f, "Static data ready");

            // Get localization stats
            var (locLanguages, locEntries, _) = LocalizationManager.GetStatistics();

            // Emit static data ready event
            context.EventBus.Emit(new StaticDataReadyEvent
            {
                TerrainCount = context.Registries.Terrains.Count,
                LocalizationLanguages = locLanguages,
                LocalizationEntries = locEntries,
                TimeStamp = Time.time
            });
            context.EventBus.ProcessEvents();

            if (context.EnableDetailedLogging)
            {
                ArchonLogger.Log($"Phase complete: Static data loaded - {context.Registries.Terrains.Count} terrains, " +
                                $"{locEntries} localization entries ({locLanguages} languages)", "core_data_loading");
            }
        }

        public void Rollback(InitializationContext context)
        {
            // Note: Registries don't have Clear() method - they're immutable once loaded
            // On failure, entire GameState is recreated anyway
            ArchonLogger.Log("Rolling back static data loading phase", "core_data_loading");
        }
    }

    /// <summary>
    /// Event emitted when static data loading completes
    /// </summary>
    public struct StaticDataReadyEvent : IGameEvent
    {
        public int TerrainCount;
        public int LocalizationLanguages;
        public int LocalizationEntries;
        public float TimeStamp { get; set; }
    }
}
