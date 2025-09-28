using UnityEngine;
using Map.MapModes;
using Utils;
using System.Collections.Generic;
using System.Linq;

namespace Map.Debug
{
    /// <summary>
    /// Debug UI for testing MapMode switching
    /// Provides runtime buttons to switch between different map display modes
    /// </summary>
    public class MapModeDebugUI : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField] private MapModeManager mapModeManager;

        [Header("Debug UI Settings")]
        [SerializeField] private bool showDebugUI = true;
        [SerializeField] private Vector2 uiPosition = new Vector2(220, 10);
        [SerializeField] private Vector2 uiSize = new Vector2(250, 300);

        // UI state
        private Dictionary<int, string> availableMapModes;
        private Vector2 scrollPosition;

        void Start()
        {
            // Try to find MapModeManager if not assigned
            if (mapModeManager == null)
            {
                mapModeManager = FindFirstObjectByType<MapModeManager>();
                if (mapModeManager == null)
                {
                    DominionLogger.LogWarning("MapModeDebugUI: No MapModeManager found in scene");
                }
            }

            RefreshAvailableMapModes();
        }

        void RefreshAvailableMapModes()
        {
            if (mapModeManager != null)
            {
                availableMapModes = mapModeManager.GetAvailableMapModes();
                DominionLogger.Log($"MapModeDebugUI: Found {availableMapModes.Count} available map modes");
            }
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        void OnGUI()
        {
            if (!showDebugUI || mapModeManager == null || availableMapModes == null)
                return;

            // Create UI area
            GUILayout.BeginArea(new Rect(uiPosition.x, uiPosition.y, uiSize.x, uiSize.y));

            // Title
            GUILayout.Label("Map Mode Debug Controls", GUI.skin.box);

            // Current mode info
            if (mapModeManager.CurrentMapMode != null)
            {
                GUILayout.Label($"Current: {mapModeManager.CurrentMapMode.Name} (ID: {mapModeManager.CurrentMapModeID})");
            }
            else
            {
                GUILayout.Label("Current: None");
            }

            GUILayout.Space(5);

            // Scrollable area for map mode buttons
            scrollPosition = GUILayout.BeginScrollView(scrollPosition);

            // Sort map modes by ID for consistent ordering
            var sortedModes = availableMapModes.OrderBy(kvp => kvp.Key).ToList();

            foreach (var mapMode in sortedModes)
            {
                int modeID = mapMode.Key;
                string modeName = mapMode.Value;

                // Highlight current mode
                bool isCurrent = mapModeManager.CurrentMapModeID == modeID;

                // Set button color for current mode
                Color originalColor = GUI.backgroundColor;
                if (isCurrent)
                {
                    GUI.backgroundColor = Color.green;
                }

                // Map mode button
                if (GUILayout.Button($"{modeName} ({modeID})"))
                {
                    SwitchToMapMode(modeID, modeName);
                }

                // Restore original color
                GUI.backgroundColor = originalColor;
            }

            GUILayout.EndScrollView();

            GUILayout.Space(10);

            // Quick action buttons
            GUILayout.Label("Quick Actions:", GUI.skin.box);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Political"))
            {
                SwitchToMapMode(0, "Political");
            }
            if (GUILayout.Button("Terrain"))
            {
                SwitchToMapMode(1, "Terrain");
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Development"))
            {
                SwitchToMapMode(2, "Development");
            }
            if (GUILayout.Button("Culture"))
            {
                SwitchToMapMode(3, "Culture");
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Country"))
            {
                SwitchToMapMode(4, "Country");
            }
            if (GUILayout.Button("Debug"))
            {
                SwitchToMapMode(99, "Debug");
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(5);

            // Utility buttons
            if (GUILayout.Button("Refresh Map Modes"))
            {
                RefreshAvailableMapModes();
            }

            if (GUILayout.Button("Force Refresh Current"))
            {
                mapModeManager.RefreshCurrentMapMode();
                DominionLogger.Log("MapModeDebugUI: Forced refresh of current map mode");
            }

            GUILayout.EndArea();
        }
#endif

        /// <summary>
        /// Switch to a specific map mode with logging
        /// </summary>
        private void SwitchToMapMode(int modeID, string modeName)
        {
            if (mapModeManager.SetMapMode(modeID))
            {
                DominionLogger.Log($"MapModeDebugUI: Switched to {modeName} (ID: {modeID})");
            }
            else
            {
                DominionLogger.LogError($"MapModeDebugUI: Failed to switch to {modeName} (ID: {modeID})");
            }
        }

        /// <summary>
        /// Assign MapModeManager at runtime (useful for testing)
        /// </summary>
        public void SetMapModeManager(MapModeManager manager)
        {
            mapModeManager = manager;
            RefreshAvailableMapModes();
            DominionLogger.Log("MapModeDebugUI: MapModeManager assigned");
        }
    }
}