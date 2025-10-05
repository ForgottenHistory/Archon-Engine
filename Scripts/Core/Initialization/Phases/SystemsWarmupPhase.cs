using System.Collections;
using UnityEngine;

namespace Core.Initialization.Phases
{
    /// <summary>
    /// Phase 7 & 8: Initialize derived systems, warm caches, and validate
    /// Final phase before game is ready to play
    /// </summary>
    public class SystemsWarmupPhase : IInitializationPhase
    {
        public string PhaseName => "Systems Warmup";
        public float ProgressStart => 75f;
        public float ProgressEnd => 100f;

        public IEnumerator ExecuteAsync(InitializationContext context)
        {
            context.ReportProgress(75f, "Initializing game systems...");

            // TODO: Initialize AI, Economy, Military systems
            context.ReportProgress(80f, "Initializing AI...");
            yield return null;

            context.ReportProgress(85f, "Initializing economy...");
            yield return null;

            context.ReportProgress(90f, "Warming caches...");
            yield return null;

            // Warm up query caches
            WarmUpCaches(context);

            context.ReportProgress(95f, "Validating data...");
            yield return null;

            // Validate loaded data
            ValidateLoadedData(context);

            context.ReportProgress(100f, "Game ready!");

            if (context.EnableDetailedLogging)
            {
                ArchonLogger.Log("Phase complete: All systems ready");
            }
        }

        public void Rollback(InitializationContext context)
        {
            // No specific cleanup needed - GameState handles all state
            ArchonLogger.Log("Rolling back systems warmup phase");
        }

        /// <summary>
        /// Warm up frequently accessed caches
        /// </summary>
        private void WarmUpCaches(InitializationContext context)
        {
            // Warm up some basic queries
            var provinceCount = context.GameState.ProvinceQueries.GetTotalProvinceCount();
            var countryCount = context.GameState.CountryQueries.GetTotalCountryCount();

            ArchonLogger.Log($"Cache warm-up complete: {provinceCount} provinces, {countryCount} countries");
        }

        /// <summary>
        /// Validate that loaded data is consistent
        /// </summary>
        private void ValidateLoadedData(InitializationContext context)
        {
            var issues = 0;

            // Validate province system
            if (!context.ProvinceSystem.IsInitialized)
            {
                ArchonLogger.LogError("ProvinceSystem not properly initialized!");
                issues++;
            }

            // Validate country system
            if (!context.CountrySystem.IsInitialized)
            {
                ArchonLogger.LogError("CountrySystem not properly initialized!");
                issues++;
            }

            if (issues > 0)
            {
                ArchonLogger.LogWarning($"Data validation found {issues} issues");
            }
            else
            {
                ArchonLogger.Log("Data validation passed");
            }
        }
    }
}
