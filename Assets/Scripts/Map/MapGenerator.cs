using UnityEngine;
using Map.Core;
using Map.Rendering;
using ParadoxParser.Jobs;
using System.Threading.Tasks;
using Core;
using Utils;

namespace Map
{
    /// <summary>
    /// Simplified MapGenerator using MapSystemCoordinator
    /// Responsible only for event handling and high-level coordination
    /// All complex map functionality delegated to MapSystemCoordinator
    /// </summary>
    public class MapGenerator : MonoBehaviour
    {
        [Header("Map Configuration")]
        [SerializeField] private string provinceBitmapPath = "Assets/Map/provinces.bmp";
        [SerializeField] private string definitionCsvPath = "Assets/Map/definition.csv";
        [SerializeField] private bool useDefinitionFile = true;
        [SerializeField] private bool logLoadingProgress = true;

        [Header("Rendering")]
        [SerializeField] private Camera mapCamera;
        [SerializeField] private MeshRenderer meshRenderer;

        // Single coordinator handles everything
        private MapSystemCoordinator mapSystem;

        // Public API
        public ProvinceMapping ProvinceMapping => mapSystem?.ProvinceMapping;
        public MapTextureManager TextureManager => mapSystem?.TextureManager;

        void Start()
        {
            // Try to subscribe to events, but handle case where GameState isn't ready yet
            if (!TrySubscribeToEvents())
            {
                if (logLoadingProgress)
                {
                    DominionLogger.Log("MapGenerator: GameState not ready yet, will retry subscription...");
                }
                // Start a coroutine to retry subscription
                StartCoroutine(WaitForGameStateAndSubscribe());
            }
        }

        /// <summary>
        /// Try to subscribe to simulation events
        /// </summary>
        private bool TrySubscribeToEvents()
        {
            var gameState = FindFirstObjectByType<GameState>();
            if (gameState?.EventBus != null)
            {
                gameState.EventBus.Subscribe<SimulationDataReadyEvent>(OnSimulationDataReady);
                if (logLoadingProgress)
                {
                    DominionLogger.Log("MapGenerator: Subscribed to SimulationDataReadyEvent");
                }
                return true;
            }
            return false;
        }

        /// <summary>
        /// Wait for GameState to be ready and subscribe to events
        /// </summary>
        private System.Collections.IEnumerator WaitForGameStateAndSubscribe()
        {
            while (!TrySubscribeToEvents())
            {
                yield return new WaitForSeconds(0.1f);
            }
        }

        /// <summary>
        /// Handle simulation data ready event (preferred method)
        /// </summary>
        private async void OnSimulationDataReady(SimulationDataReadyEvent simulationData)
        {
            if (logLoadingProgress)
            {
                DominionLogger.Log($"MapGenerator: Received simulation data with {simulationData.ProvinceCount} provinces");
            }

            // Initialize map system
            InitializeMapSystem();

            // Generate map from simulation data
            bool success = await mapSystem.GenerateMapFromSimulation(simulationData, provinceBitmapPath, definitionCsvPath, useDefinitionFile);

            if (success && logLoadingProgress)
            {
                DominionLogger.Log($"MapGenerator: Event-driven map generation complete. Rendering {simulationData.ProvinceCount} provinces.");
            }
        }

        /// <summary>
        /// Generate map from files (legacy/fallback method)
        /// </summary>
        [ContextMenu("Generate Map")]
        public async void GenerateMapFromFiles()
        {
            if (logLoadingProgress)
            {
                DominionLogger.Log("MapGenerator: Starting legacy map generation from files...");
            }

            // Initialize map system
            InitializeMapSystem();

            // Generate map from files
            bool success = await mapSystem.GenerateMapFromFiles(provinceBitmapPath, definitionCsvPath, useDefinitionFile);

            if (success && logLoadingProgress)
            {
                DominionLogger.Log($"MapGenerator: File-based map generation complete. Loaded {ProvinceMapping?.ProvinceCount ?? 0} provinces.");
            }
        }

        /// <summary>
        /// Get province ID at world position (for interaction)
        /// </summary>
        public ushort GetProvinceAtWorldPosition(Vector3 worldPosition)
        {
            return mapSystem?.GetProvinceAtWorldPosition(worldPosition) ?? 0;
        }

        /// <summary>
        /// Set map mode
        /// </summary>
        public void SetMapMode(int modeId)
        {
            mapSystem?.SetMapMode(modeId);
        }

        /// <summary>
        /// Initialize the map system coordinator
        /// </summary>
        private void InitializeMapSystem()
        {
            if (mapSystem != null) return; // Already initialized

            // Get or create the coordinator
            mapSystem = GetComponent<MapSystemCoordinator>();
            if (mapSystem == null)
            {
                mapSystem = gameObject.AddComponent<MapSystemCoordinator>();
                if (logLoadingProgress)
                {
                    DominionLogger.Log("MapGenerator: Created MapSystemCoordinator");
                }
            }

            // Initialize the entire system with proper references
            mapSystem.InitializeSystem(mapCamera, meshRenderer);
        }

#if UNITY_EDITOR
        /// <summary>
        /// Editor-only debug information
        /// </summary>
        [ContextMenu("Log Map Info")]
        private void LogMapInfo()
        {
            if (ProvinceMapping != null && TextureManager != null)
            {
                DominionLogger.Log($"Map Info: {ProvinceMapping.ProvinceCount} provinces loaded");
                DominionLogger.Log($"Texture Size: {TextureManager.MapWidth} x {TextureManager.MapHeight}");
                DominionLogger.Log($"Province Bitmap Path: {provinceBitmapPath}");
            }
            else
            {
                DominionLogger.Log("Map Info: No map data loaded");
            }
        }
#endif
    }
}