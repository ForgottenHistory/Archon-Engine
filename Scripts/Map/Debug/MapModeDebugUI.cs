using UnityEngine;
using Map.MapModes;
using System;

namespace Map.Debug
{
    /// <summary>
    /// Debug UI for testing the new MapMode handler system
    /// Provides runtime controls for switching between map modes and testing functionality
    /// Updated for the new handler-based architecture
    /// </summary>
    public class MapModeDebugUI : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField] private MapModeManager mapModeManager;

        [Header("Debug UI Settings")]
        [SerializeField] private bool showDebugUI = true;
        [SerializeField] private bool alignRight = true;
        [SerializeField] private float rightMargin = 10f;
        [SerializeField] private float topMargin = 220f; // Below TimeManager (200px height + 20px spacing)
        [SerializeField] private Vector2 uiSize = new Vector2(280, 400);

        // UI state
        private MapMode[] availableModes;
        private Vector2 scrollPosition;
        private bool showAdvanced = false;

        void Start()
        {
            // Try to find MapModeManager if not assigned
            if (mapModeManager == null)
            {
                mapModeManager = FindFirstObjectByType<MapModeManager>();
                if (mapModeManager == null)
                {
                    ArchonLogger.LogWarning("MapModeDebugUI: No MapModeManager found in scene");
                }
            }

            RefreshAvailableModes();
        }

        void RefreshAvailableModes()
        {
            if (mapModeManager != null && mapModeManager.IsInitialized)
            {
                // Get all available map modes from the enum
                availableModes = (MapMode[])Enum.GetValues(typeof(MapMode));
                ArchonLogger.Log($"MapModeDebugUI: Found {availableModes.Length} map mode types");
            }
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        void OnGUI()
        {
            if (!showDebugUI || mapModeManager == null || !mapModeManager.IsInitialized)
                return;

            // Calculate position (align to right if enabled)
            float xPosition = alignRight ? (Screen.width - uiSize.x - rightMargin) : rightMargin;
            float yPosition = topMargin;

            // Create UI area
            GUILayout.BeginArea(new Rect(xPosition, yPosition, uiSize.x, uiSize.y));

            // Title
            GUILayout.Label("Map Mode Debug Controls", GUI.skin.box);

            // System status
            GUILayout.Label($"System: {(mapModeManager.IsInitialized ? "Ready" : "Initializing...")}");
            GUILayout.Label($"Current: {mapModeManager.CurrentMode}");

            GUILayout.Space(5);

            // Quick mode switches
            GUILayout.Label("Quick Switch:", GUI.skin.box);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Political"))
            {
                SwitchToMapMode(MapMode.Political);
            }
            if (GUILayout.Button("Development"))
            {
                SwitchToMapMode(MapMode.Development);
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Terrain"))
            {
                SwitchToMapMode(MapMode.Terrain);
            }
            if (GUILayout.Button("Culture"))
            {
                SwitchToMapMode(MapMode.Culture);
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(10);

            // Debug visualization modes
            GUILayout.Label("Debug Modes:", GUI.skin.box);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Heightmap"))
            {
                SwitchToMapMode(MapMode.HeightmapDebug);
            }
            if (GUILayout.Button("Normal Map"))
            {
                SwitchToMapMode(MapMode.NormalMapDebug);
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Borders"))
            {
                SwitchToMapMode(MapMode.BorderDebug);
            }
            if (GUILayout.Button("Province IDs"))
            {
                SwitchToMapMode(MapMode.ProvinceIDDebug);
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(10);

            // Advanced controls
            showAdvanced = GUILayout.Toggle(showAdvanced, "Show Advanced Controls");

            if (showAdvanced)
            {
                GUILayout.Label("All Modes:", GUI.skin.box);

                scrollPosition = GUILayout.BeginScrollView(scrollPosition, GUILayout.Height(150));

                if (availableModes != null)
                {
                    foreach (var mode in availableModes)
                    {
                        bool isCurrent = mapModeManager.CurrentMode == mode;

                        // Highlight current mode
                        Color originalColor = GUI.backgroundColor;
                        if (isCurrent)
                        {
                            GUI.backgroundColor = Color.green;
                        }

                        if (GUILayout.Button($"{mode} ({(int)mode})"))
                        {
                            SwitchToMapMode(mode);
                        }

                        GUI.backgroundColor = originalColor;
                    }
                }

                GUILayout.EndScrollView();

                GUILayout.Space(5);

                // Debug actions
                GUILayout.Label("Debug Actions:", GUI.skin.box);

                if (GUILayout.Button("Force Texture Update"))
                {
                    ArchonLogger.Log("MapModeDebugUI: Forced texture update");
                }

                if (GUILayout.Button("Get Tooltip (Province 1)"))
                {
                    var tooltip = mapModeManager.GetProvinceTooltip(1);
                    ArchonLogger.Log($"Province 1 Tooltip: {tooltip}");
                }

                if (GUILayout.Button("Refresh Available Modes"))
                {
                    RefreshAvailableModes();
                }
            }

            GUILayout.EndArea();
        }
#endif

        /// <summary>
        /// Switch to a specific map mode using the new enum-based system
        /// </summary>
        private void SwitchToMapMode(MapMode mode)
        {
            if (mapModeManager != null && mapModeManager.IsInitialized)
            {
                mapModeManager.SetMapMode(mode);
                ArchonLogger.Log($"MapModeDebugUI: Switched to {mode} mode");
            }
            else
            {
                ArchonLogger.LogError($"MapModeDebugUI: Cannot switch to {mode} - manager not ready");
            }
        }

        /// <summary>
        /// Assign MapModeManager at runtime (useful for testing)
        /// </summary>
        public void SetMapModeManager(MapModeManager manager)
        {
            mapModeManager = manager;
            RefreshAvailableModes();
            ArchonLogger.Log("MapModeDebugUI: MapModeManager assigned");
        }

        void Update()
        {
            // Auto-refresh when manager becomes available
            if (mapModeManager != null && mapModeManager.IsInitialized && availableModes == null)
            {
                RefreshAvailableModes();
            }
        }
    }
}