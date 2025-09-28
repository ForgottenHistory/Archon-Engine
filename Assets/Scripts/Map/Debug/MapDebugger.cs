using UnityEngine;
using Map.Rendering;
using Map.MapModes;
using Utils;

namespace Map.Debug
{
    /// <summary>
    /// Provides debug functionality for the map system
    /// Extracted from MapGenerator to follow single responsibility principle
    /// Contains context menu methods for testing and debugging map features
    /// </summary>
    public class MapDebugger : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField] private BorderComputeDispatcher borderDispatcher;
        [SerializeField] private MapModeManager mapModeManager;
        [SerializeField] private MapTextureManager textureManager;
        [SerializeField] private Material mapMaterial;

        [Header("Configuration")]
        [SerializeField] private string provinceBitmapPath;

        // Reference to the main map generator for accessing components
        private MapGenerator mapGenerator;

        private void Awake()
        {
            mapGenerator = GetComponent<MapGenerator>();
        }

        /// <summary>
        /// Initialize debugger with required dependencies
        /// </summary>
        public void Initialize(BorderComputeDispatcher borders, MapModeManager modes, MapTextureManager textures, Material material, string bitmapPath)
        {
            borderDispatcher = borders;
            mapModeManager = modes;
            textureManager = textures;
            mapMaterial = material;
            provinceBitmapPath = bitmapPath;
        }

        /// <summary>
        /// Generate province borders using GPU compute shader
        /// </summary>
        [ContextMenu("Generate Borders")]
        public void GenerateBorders()
        {
            if (borderDispatcher != null)
            {
                borderDispatcher.DetectBorders();
                DominionLogger.Log("MapDebugger: Borders generated");
            }
            else
            {
                DominionLogger.LogError("MapDebugger: BorderComputeDispatcher not found");
            }
        }

        /// <summary>
        /// Toggle between border modes
        /// </summary>
        [ContextMenu("Toggle Border Mode")]
        public void ToggleBorderMode()
        {
            if (borderDispatcher != null)
            {
                // Cycle through border modes
                var currentMode = borderDispatcher.CurrentBorderMode;
                var nextMode = (BorderComputeDispatcher.BorderMode)(((int)currentMode + 1) % 4);
                borderDispatcher.SetBorderMode(nextMode);
                DominionLogger.Log($"MapDebugger: Border mode set to {nextMode}");
            }
            else
            {
                DominionLogger.LogError("MapDebugger: BorderComputeDispatcher not available");
            }
        }

        /// <summary>
        /// Test debug mode - shows province IDs as colors
        /// </summary>
        [ContextMenu("Set Debug Mode")]
        public void SetDebugMode()
        {
            SetMapMode(99); // Debug mode ID
            DominionLogger.Log("MapDebugger: Set to DEBUG mode - showing province IDs as colors");
        }

        /// <summary>
        /// Show border debug mode - displays just the border texture
        /// </summary>
        [ContextMenu("Show Border Debug Mode")]
        public void ShowBorderDebugMode()
        {
            if (mapMaterial != null)
            {
                SetMapMode(10); // Border debug mode
                DominionLogger.Log("MapDebugger: Set to BORDER DEBUG mode - showing border texture only");
            }
            else
            {
                DominionLogger.LogError("MapDebugger: Map material not available");
            }
        }

        /// <summary>
        /// Set border strength to default value (0.3)
        /// </summary>
        [ContextMenu("Set Border Strength")]
        public void SetBorderStrength()
        {
            SetBorderStrength(0.3f); // Default to 30%
            DominionLogger.Log("MapDebugger: Set border strength to 30%");
        }

        /// <summary>
        /// Test terrain mode - shows raw province colors
        /// </summary>
        [ContextMenu("Set Terrain Mode")]
        public void SetTerrainMode()
        {
            SetMapMode(1);
            DominionLogger.Log("MapDebugger: Set to TERRAIN mode - showing raw province colors");
        }

        /// <summary>
        /// Log detailed map information for debugging
        /// </summary>
        [ContextMenu("Log Map Info")]
        public void LogMapInfo()
        {
            if (mapGenerator?.ProvinceMapping != null && textureManager != null)
            {
                DominionLogger.Log($"Map Info: {mapGenerator.ProvinceMapping.ProvinceCount} provinces loaded");
                DominionLogger.Log($"Texture Size: {textureManager.MapWidth} x {textureManager.MapHeight}");
                DominionLogger.Log($"Province Bitmap Path: {provinceBitmapPath}");

                // Additional debug info
                if (borderDispatcher != null)
                {
                    DominionLogger.Log($"Border Mode: {borderDispatcher.CurrentBorderMode}");
                }
                if (mapModeManager != null)
                {
                    DominionLogger.Log($"Current Map Mode: {mapModeManager.CurrentMapMode}");
                }
            }
            else
            {
                DominionLogger.Log("Map Info: No map data loaded");
            }
        }

        /// <summary>
        /// Test Country MapMode
        /// </summary>
        [ContextMenu("Set Country Mode")]
        public void SetCountryMode()
        {
            SetMapMode(2);
            DominionLogger.Log("MapDebugger: Set to COUNTRY mode - showing provinces colored by owner");
        }

        /// <summary>
        /// Test Political MapMode
        /// </summary>
        [ContextMenu("Set Political Mode")]
        public void SetPoliticalMode()
        {
            SetMapMode(3);
            DominionLogger.Log("MapDebugger: Set to POLITICAL mode");
        }

        /// <summary>
        /// Clear all borders
        /// </summary>
        [ContextMenu("Clear Borders")]
        public void ClearBorders()
        {
            if (borderDispatcher != null)
            {
                borderDispatcher.ClearBorders();
                DominionLogger.Log("MapDebugger: Cleared all borders");
            }
        }

        /// <summary>
        /// Regenerate map (useful for testing changes)
        /// </summary>
        [ContextMenu("Regenerate Map")]
        public void RegenerateMap()
        {
            if (mapGenerator != null)
            {
                DominionLogger.Log("MapDebugger: Triggering map regeneration...");
                // This would trigger the map generation process
                // Implementation depends on how MapGenerator exposes this functionality
            }
        }

        // Helper methods

        /// <summary>
        /// Set map mode using MapModeManager
        /// </summary>
        private void SetMapMode(int modeId)
        {
            if (mapModeManager != null)
            {
                mapModeManager.SetMapMode(modeId);
            }
            else
            {
                DominionLogger.LogError("MapDebugger: MapModeManager not available");
            }
        }

        /// <summary>
        /// Set border visibility strength
        /// </summary>
        private void SetBorderStrength(float strength)
        {
            if (mapMaterial != null)
            {
                mapMaterial.SetFloat("_BorderStrength", Mathf.Clamp01(strength));
            }
            else
            {
                DominionLogger.LogError("MapDebugger: Map material not available");
            }
        }

        /// <summary>
        /// Validate that all required dependencies are available
        /// </summary>
        [ContextMenu("Validate Debug Setup")]
        public void ValidateDebugSetup()
        {
            int issues = 0;

            if (borderDispatcher == null)
            {
                DominionLogger.LogError("MapDebugger: BorderComputeDispatcher not assigned");
                issues++;
            }

            if (mapModeManager == null)
            {
                DominionLogger.LogError("MapDebugger: MapModeManager not assigned");
                issues++;
            }

            if (textureManager == null)
            {
                DominionLogger.LogError("MapDebugger: MapTextureManager not assigned");
                issues++;
            }

            if (mapMaterial == null)
            {
                DominionLogger.LogError("MapDebugger: Map Material not assigned");
                issues++;
            }

            if (issues == 0)
            {
                DominionLogger.Log("MapDebugger: All dependencies validated successfully");
            }
            else
            {
                DominionLogger.LogError($"MapDebugger: Found {issues} dependency issues");
            }
        }
    }
}