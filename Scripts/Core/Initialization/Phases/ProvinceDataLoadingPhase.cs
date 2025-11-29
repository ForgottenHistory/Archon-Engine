using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Core.Loaders;
using ParadoxParser.Jobs;

namespace Core.Initialization.Phases
{
    /// <summary>
    /// Phase 3: Load province data using definition.csv + JSON5 + Burst architecture
    /// Loads ALL provinces (including uncolonized ones) and initializes ProvinceSystem
    ///
    /// GRACEFUL DEGRADATION:
    /// - No definition.csv: Initialize with 0 provinces (map-only mode)
    /// - No history files: Use definitions only (no ownership/history data)
    /// - Full data: Complete province initialization
    /// </summary>
    public class ProvinceDataLoadingPhase : IInitializationPhase
    {
        public string PhaseName => "Province Data Loading";
        public float ProgressStart => 15f;
        public float ProgressEnd => 40f;

        public IEnumerator ExecuteAsync(InitializationContext context)
        {
            context.ReportProgress(15f, "Loading province data...");

            // Step 1: Try to load definition.csv (OPTIONAL)
            context.ReportProgress(15f, "Loading definition.csv...");
            yield return null;

            context.ProvinceDefinitions = DefinitionLoader.LoadDefinitions(context.Settings.DataDirectory);

            if (context.ProvinceDefinitions.Count == 0)
            {
                // No definition.csv - this is OK for map-only mode
                ArchonLogger.LogWarning("No province definitions found - running in map-only mode", "core_data_loading");

                // Initialize empty province system
                context.ProvinceSystem.InitializeEmpty();

                context.ReportProgress(40f, "Province data loaded (map-only mode)");

                // Emit event with zero provinces
                context.EventBus.Emit(new ProvinceDataReadyEvent
                {
                    ProvinceCount = 0,
                    HasDefinitions = false,
                    HasInitialStates = false,
                    TimeStamp = Time.time
                });
                context.EventBus.ProcessEvents();

                if (context.EnableDetailedLogging)
                {
                    ArchonLogger.Log("Phase complete: Map-only mode (no province data)", "core_data_loading");
                }
                yield break;
            }

            ArchonLogger.Log($"Loaded {context.ProvinceDefinitions.Count} province definitions from definition.csv", "core_data_loading");

            // Step 2: Try to load province initial states from JSON5 files (OPTIONAL)
            context.ReportProgress(20f, "Loading province JSON5 files...");
            yield return null;

            context.ProvinceInitialStates = BurstProvinceHistoryLoader.LoadProvinceInitialStates(context.Settings.DataDirectory);

            if (!context.ProvinceInitialStates.Success)
            {
                // No history data - use definitions only
                ArchonLogger.LogWarning($"Province history not loaded: {context.ProvinceInitialStates.ErrorMessage}", "core_data_loading");
                ArchonLogger.Log("Initializing provinces from definitions only (no ownership data)", "core_data_loading");

                context.ReportProgress(32f, "Initializing province system from definitions...");
                yield return null;

                // Initialize from definitions only
                context.ProvinceSystem.InitializeFromDefinitions(context.ProvinceDefinitions);

                context.ReportProgress(40f, "Province data loaded (definitions only)");

                // Emit event
                context.EventBus.Emit(new ProvinceDataReadyEvent
                {
                    ProvinceCount = context.ProvinceDefinitions.Count,
                    HasDefinitions = true,
                    HasInitialStates = false,
                    TimeStamp = Time.time
                });
                context.EventBus.ProcessEvents();

                if (context.EnableDetailedLogging)
                {
                    ArchonLogger.Log($"Phase complete: Loaded {context.ProvinceDefinitions.Count} provinces from definitions (no history)", "core_data_loading");
                }
                yield break;
            }

            ArchonLogger.Log($"Province loading complete: {context.ProvinceInitialStates.LoadedCount} provinces loaded, {context.ProvinceInitialStates.FailedCount} failed", "core_data_loading");

            context.ReportProgress(32f, "Initializing province system...");
            yield return null;

            // Initialize ProvinceSystem with full data
            context.ProvinceSystem.InitializeFromProvinceStates(context.ProvinceInitialStates);

            context.ReportProgress(40f, "Province data loaded");

            // Emit province data ready event
            context.EventBus.Emit(new ProvinceDataReadyEvent
            {
                ProvinceCount = context.ProvinceInitialStates.LoadedCount,
                HasDefinitions = context.ProvinceDefinitions.Count > 0,
                HasInitialStates = context.ProvinceInitialStates.LoadedCount > 0,
                TimeStamp = Time.time
            });
            context.EventBus.ProcessEvents();

            if (context.EnableDetailedLogging)
            {
                ArchonLogger.Log($"Phase complete: Loaded {context.ProvinceInitialStates.LoadedCount} provinces from JSON5 (ready for reference linking)", "core_data_loading");
            }
        }

        public void Rollback(InitializationContext context)
        {
            // Dispose province initial states if loaded
            if (context.ProvinceInitialStates.Success)
            {
                context.ProvinceInitialStates.Dispose();
            }

            context.ProvinceDefinitions?.Clear();

            ArchonLogger.Log("Rolling back province data loading phase", "core_data_loading");
        }
    }

    /// <summary>
    /// Event emitted when province data is fully loaded and ready
    /// Includes province map, definitions, and initial state data
    /// </summary>
    public struct ProvinceDataReadyEvent : IGameEvent
    {
        public int ProvinceCount;
        public bool HasDefinitions;
        public bool HasInitialStates;
        public float TimeStamp { get; set; }
    }
}
