using System.Collections;
using UnityEngine;
using Core.Systems;

namespace Core.Initialization.Phases
{
    /// <summary>
    /// Phase 1: Initialize core engine systems (GameState, EventBus, TimeManager)
    /// Creates the foundational systems required for all other initialization phases
    /// </summary>
    public class CoreSystemsInitializationPhase : IInitializationPhase
    {
        public string PhaseName => "Core Systems Initialization";
        public float ProgressStart => 0f;
        public float ProgressEnd => 5f;

        public IEnumerator ExecuteAsync(InitializationContext context)
        {
            context.ReportProgress(0f, "Initializing core systems...");

            // Find or create GameState
            context.GameState = Object.FindFirstObjectByType<GameState>();
            if (context.GameState == null)
            {
                var gameStateGO = new GameObject("GameState");
                context.GameState = gameStateGO.AddComponent<GameState>();
            }

            context.ReportProgress(1f, "Creating event system...");
            yield return null;

            // Initialize GameState (this creates EventBus, TimeManager, etc.)
            context.GameState.InitializeSystems();

            // Store system references in context
            context.ProvinceSystem = context.GameState.Provinces;
            context.CountrySystem = context.GameState.Countries;
            context.TimeManager = context.GameState.Time;
            context.EventBus = context.GameState.EventBus;

            context.ReportProgress(2f, "Initializing data linking systems...");
            yield return null;

            // Initialize data linking systems
            context.Registries = new Registries.GameRegistries();
            context.ReferenceResolver = new Linking.ReferenceResolver(context.Registries);
            context.CrossReferenceBuilder = new Linking.CrossReferenceBuilder(context.Registries);
            context.DataValidator = new Linking.DataValidator(context.Registries);

            // Provide registries to GameState for central access
            context.GameState.SetRegistries(context.Registries);

            context.ReportProgress(3f, "Creating data loaders...");
            yield return null;

            context.ReportProgress(5f, "Core systems ready");

            if (context.EnableDetailedLogging)
            {
                ArchonLogger.Log("Phase complete: Core systems initialized successfully");
            }
        }

        public void Rollback(InitializationContext context)
        {
            // Cleanup if initialization fails
            if (context.GameState != null && context.GameState.gameObject != null)
            {
                Object.Destroy(context.GameState.gameObject);
                context.GameState = null;
            }

            context.ProvinceSystem = null;
            context.CountrySystem = null;
            context.TimeManager = null;
            context.EventBus = null;
            context.Registries = null;
            context.ReferenceResolver = null;
            context.CrossReferenceBuilder = null;
            context.DataValidator = null;
        }
    }
}
