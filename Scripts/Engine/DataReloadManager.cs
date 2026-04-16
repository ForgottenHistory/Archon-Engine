using System.Collections;
using UnityEngine;
using Core;
using Core.Initialization;
using Core.Initialization.Phases;
using Core.Linking;
using Map.Core;
using Map.MapModes;
using Utils;

namespace Engine
{
    /// <summary>
    /// ENGINE LAYER: Orchestrates hot reload of game data at runtime.
    /// Re-runs the data loading pipeline (static data, provinces, countries, reference linking)
    /// without restarting the engine. Uses the same InitializationContext + IInitializationPhase
    /// infrastructure as the initial load.
    ///
    /// Lives in Engine layer (not Core) because it coordinates both Core and Map systems.
    ///
    /// Usage:
    ///   yield return DataReloadManager.ReloadAllData(gameState, mapCoordinator);
    /// </summary>
    public static class DataReloadManager
    {
        /// <summary>
        /// Hot reload all game data from disk. Clears registries, re-runs loading phases,
        /// rebuilds GPU textures. Runs as a coroutine.
        /// </summary>
        public static IEnumerator ReloadAllData(
            GameState gameState,
            MapSystemCoordinator mapCoordinator,
            System.Action<float, string> onProgress = null)
        {
            if (gameState == null)
            {
                ArchonLogger.LogError("DataReloadManager: GameState is null", "core_modding");
                yield break;
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            ArchonLogger.Log("DataReloadManager: Starting hot reload...", "core_modding");
            onProgress?.Invoke(0f, "Starting data reload...");

            // 1. Clear all registries
            onProgress?.Invoke(5f, "Clearing registries...");
            gameState.Registries?.Clear();
            yield return null;

            // 2. Clear province system state
            onProgress?.Invoke(10f, "Clearing province data...");
            gameState.Provinces?.ClearForReload();
            yield return null;

            // 3. Build initialization context with existing systems
            var context = new InitializationContext
            {
                Settings = GameSettings.Instance,
                EnableDetailedLogging = true,
                GameState = gameState,
                ProvinceSystem = gameState.Provinces,
                CountrySystem = gameState.Countries,
                TimeManager = Object.FindFirstObjectByType<Core.Systems.TimeManager>(),
                EventBus = gameState.EventBus,
                Registries = gameState.Registries,
                OnProgress = (progress, status) =>
                {
                    float mapped = 15f + progress * 0.65f;
                    onProgress?.Invoke(mapped, status);
                }
            };

            // 4. Re-run static data loading (terrains, buildings, etc.)
            onProgress?.Invoke(15f, "Loading static data...");
            yield return RunPhase(new StaticDataLoadingPhase(), context);
            if (context.HasError) { LogError(context); yield break; }

            // 5. Re-run province data loading (definitions + history files)
            onProgress?.Invoke(30f, "Loading province data...");
            yield return RunPhase(new ProvinceDataLoadingPhase(), context);
            if (context.HasError) { LogError(context); yield break; }

            // 6. Re-run country data loading
            onProgress?.Invoke(50f, "Loading country data...");
            yield return RunPhase(new CountryDataLoadingPhase(), context);
            if (context.HasError) { LogError(context); yield break; }

            // 6b. Create linking systems (depend on repopulated registries)
            context.ReferenceResolver = new ReferenceResolver(context.Registries);
            context.CrossReferenceBuilder = new CrossReferenceBuilder(context.Registries);
            context.DataValidator = new DataValidator(context.Registries);

            // 7. Re-run reference linking
            onProgress?.Invoke(65f, "Linking references...");
            yield return RunPhase(new ReferenceLinkingPhase(), context);
            if (context.HasError) { LogError(context); yield break; }

            // 8. Re-apply terrain from map and rebuild GPU textures
            onProgress?.Invoke(80f, "Rebuilding terrain...");
            mapCoordinator?.ReloadTerrainData(gameState);

            // Sync double-buffered province state so UI reads fresh data
            gameState.Provinces?.SyncBuffersAfterLoad();
            yield return null;

            // 9. Refresh map mode palettes and rebind textures to material
            onProgress?.Invoke(90f, "Refreshing map...");
            var mapModeManager = Object.FindFirstObjectByType<MapModeManager>();
            mapModeManager?.RefreshTerrainPalette();
            mapModeManager?.RebindTextures();
            mapModeManager?.ForceTextureUpdate();
            yield return null;

            onProgress?.Invoke(100f, "Reload complete");

            sw.Stop();
            ArchonLogger.Log($"DataReloadManager: Hot reload complete in {sw.ElapsedMilliseconds}ms", "core_modding");
        }

        private static IEnumerator RunPhase(IInitializationPhase phase, InitializationContext context)
        {
            var enumerator = phase.ExecuteAsync(context);
            while (enumerator.MoveNext())
            {
                yield return enumerator.Current;
            }
        }

        private static void LogError(InitializationContext context)
        {
            ArchonLogger.LogError($"DataReloadManager: Reload failed - {context.ErrorMessage}", "core_modding");
        }
    }
}
