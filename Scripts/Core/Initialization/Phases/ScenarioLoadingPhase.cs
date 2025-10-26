using System.Collections;
using UnityEngine;
using Core.Loaders;

namespace Core.Initialization.Phases
{
    /// <summary>
    /// Phase 6: Load scenario data
    /// Loads scenario file or creates default, validates, and applies to game state
    /// </summary>
    public class ScenarioLoadingPhase : IInitializationPhase
    {
        public string PhaseName => "Scenario Loading";
        public float ProgressStart => 65f;
        public float ProgressEnd => 75f;

        public IEnumerator ExecuteAsync(InitializationContext context)
        {
            context.ReportProgress(65f, "Loading scenario...");

            ScenarioLoader.ScenarioLoadResult scenarioResult;

            // Try to load scenario file if specified
            var scenariosDirectory = System.IO.Path.Combine(context.Settings.DataDirectory, "history", "countries");
            if (System.IO.Directory.Exists(scenariosDirectory))
            {
                var scenarioPath = System.IO.Path.Combine(scenariosDirectory, "default_1444.json");

                context.ReportProgress(67f, "Loading scenario file...");
                yield return null;

                if (System.IO.File.Exists(scenarioPath))
                {
                    scenarioResult = ScenarioLoader.LoadFromFile(scenarioPath);
                }
                else
                {
                    ArchonLogger.LogWarning($"Scenario file not found: {scenarioPath}, using default", "core_data_loading");
                    scenarioResult = ScenarioLoader.CreateDefaultScenario();
                }
            }
            else
            {
                ArchonLogger.Log("No scenario directory specified, using default scenario", "core_data_loading");
                scenarioResult = ScenarioLoader.CreateDefaultScenario();
            }

            context.ReportProgress(69f, "Validating scenario...");
            yield return null;

            if (!scenarioResult.Success)
            {
                ArchonLogger.LogWarning($"Scenario loading failed: {scenarioResult.ErrorMessage}, using default", "core_data_loading");
                scenarioResult = ScenarioLoader.CreateDefaultScenario();
            }

            // Validate scenario against loaded data
            var validationIssues = ScenarioLoader.ValidateScenario(scenarioResult.Data, context.GameState);
            if (validationIssues.Count > 0)
            {
                ArchonLogger.LogWarning($"Scenario validation found {validationIssues.Count} issues", "core_data_loading");
                foreach (var issue in validationIssues)
                {
                    ArchonLogger.LogWarning($"  - {issue}", "core_data_loading");
                }
            }

            context.ReportProgress(71f, "Applying scenario...");
            yield return null;

            // Apply scenario to game state
            bool applySuccess = ScenarioLoader.ApplyScenario(scenarioResult.Data, context.GameState);
            if (!applySuccess)
            {
                ArchonLogger.LogError("Failed to apply scenario", "core_data_loading");
                context.ReportError("Scenario application failed");
                yield break;
            }

            // Sync province buffers after scenario load to prevent first-tick empty buffer bug
            // This ensures both read and write buffers have the same initial province data
            context.ProvinceSystem.SyncBuffersAfterLoad();

            context.ReportProgress(75f, "Scenario applied");

            if (context.EnableDetailedLogging)
            {
                ArchonLogger.Log($"Phase complete: Applied scenario: {scenarioResult.Data.Name}", "core_data_loading");
            }
        }

        public void Rollback(InitializationContext context)
        {
            // Scenario data is part of GameState - on failure, entire GameState is recreated
            ArchonLogger.Log("Rolling back scenario loading phase", "core_data_loading");
        }
    }
}
