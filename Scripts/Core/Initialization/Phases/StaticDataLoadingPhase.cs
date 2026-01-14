using System.Collections;
using System.Reflection;
using UnityEngine;
using Core.Loaders;
using Core.Localization;

namespace Core.Initialization.Phases
{
    /// <summary>
    /// Phase 2: Load static game data via LoaderRegistry.
    /// Discovers and executes loaders in priority order.
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

            context.ReportProgress(7f, "Discovering loaders...");
            yield return null;

            // Create and populate loader registry
            var loaderRegistry = new LoaderRegistry();
            loaderRegistry.DiscoverLoaders(Assembly.GetExecutingAssembly());

            // Allow GAME layer to add loaders via context
            if (context.AdditionalLoaderAssemblies != null)
            {
                loaderRegistry.DiscoverLoaders(context.AdditionalLoaderAssemblies);
            }

            context.ReportProgress(8f, "Executing loaders...");
            yield return null;

            // Execute all discovered loaders in priority order
            var loaderContext = new LoaderContext(context.Registries, context.Settings.DataDirectory)
            {
                EnableDetailedLogging = context.EnableDetailedLogging
            };

            bool loadSuccess = loaderRegistry.ExecuteAll(loaderContext);

            if (!loadSuccess)
            {
                context.ReportError("Static data loading failed - required loaders encountered errors");
                yield break;
            }

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
                LoaderCount = loaderRegistry.Count,
                LocalizationLanguages = locLanguages,
                LocalizationEntries = locEntries,
                TimeStamp = Time.time
            });
            context.EventBus.ProcessEvents();

            if (context.EnableDetailedLogging)
            {
                ArchonLogger.Log($"Phase complete: Static data loaded via {loaderRegistry.Count} loaders - " +
                                $"{context.Registries.Terrains.Count} terrains, " +
                                $"{locEntries} localization entries ({locLanguages} languages)", "core_data_loading");
            }
        }

        public void Rollback(InitializationContext context)
        {
            ArchonLogger.Log("Rolling back static data loading phase", "core_data_loading");
        }
    }

    /// <summary>
    /// Event emitted when static data loading completes.
    /// </summary>
    public struct StaticDataReadyEvent : IGameEvent
    {
        public int TerrainCount;
        public int LoaderCount;
        public int LocalizationLanguages;
        public int LocalizationEntries;
        public float TimeStamp { get; set; }
    }
}
