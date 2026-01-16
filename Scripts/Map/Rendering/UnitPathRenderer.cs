using UnityEngine;
using System.Collections.Generic;
using Core;
using Core.Units;
using Map.Utils;
using Map.Core;

namespace Map.Rendering
{
    /// <summary>
    /// ENGINE (Map Layer) - Renders movement path lines for units.
    ///
    /// Simple LineRenderer-based visualization for unit movement paths.
    /// Subscribes to Core movement events and draws paths using ProvinceCenterLookup.
    ///
    /// Can be disabled/replaced by GAME layer if custom visualization is needed.
    /// </summary>
    public class UnitPathRenderer : MonoBehaviour
    {
        [Header("Configuration")]
        [SerializeField] private bool enablePathLines = true;
        [SerializeField] private float lineWidth = 0.15f;
        [SerializeField] private float lineHeightOffset = 0.1f;

        [Header("Debug")]
        [SerializeField] private bool logDebugInfo = false;

        // Dependencies
        private GameState gameState;
        private ProvinceCenterLookup centerLookup;

        // Path line management
        private Dictionary<ushort, GameObject> activePathLines = new Dictionary<ushort, GameObject>();
        private Transform pathLinesContainer;

        private bool isInitialized = false;

        public bool IsInitialized => isInitialized;

        /// <summary>
        /// Initialize the path renderer.
        /// </summary>
        public void Initialize(GameState gameState, ProvinceCenterLookup centerLookup)
        {
            if (gameState == null || centerLookup == null || !centerLookup.IsInitialized)
            {
                ArchonLogger.LogError("[UnitPathRenderer] Cannot initialize with null or uninitialized dependencies", "map_rendering");
                return;
            }

            this.gameState = gameState;
            this.centerLookup = centerLookup;

            // Create container for path lines
            pathLinesContainer = new GameObject("UnitMovementPaths").transform;
            pathLinesContainer.SetParent(transform);

            // Subscribe to movement events
            if (enablePathLines)
            {
                gameState.EventBus.Subscribe<UnitMovementStartedEvent>(OnUnitMovementStarted);
                gameState.EventBus.Subscribe<UnitMovementCompletedEvent>(OnUnitMovementCompleted);
                gameState.EventBus.Subscribe<UnitMovementCancelledEvent>(OnUnitMovementCancelled);
            }

            isInitialized = true;
            ArchonLogger.Log("[UnitPathRenderer] Initialized", "map_rendering");
        }

        /// <summary>
        /// Alternative initialization with ProvinceMapping (creates its own ProvinceCenterLookup).
        /// </summary>
        public void Initialize(GameState gameState, ProvinceMapping provinceMapping, Transform meshTransform, int texWidth, int texHeight)
        {
            var lookup = new ProvinceCenterLookup();
            lookup.Initialize(provinceMapping, meshTransform, texWidth, texHeight);
            Initialize(gameState, lookup);
        }

        private void OnUnitMovementStarted(UnitMovementStartedEvent evt)
        {
            if (!enablePathLines || !isInitialized) return;
            CreatePathLine(evt.UnitID);
        }

        private void OnUnitMovementCompleted(UnitMovementCompletedEvent evt)
        {
            if (!enablePathLines || !isInitialized) return;
            UpdatePathLine(evt.UnitID);
        }

        private void OnUnitMovementCancelled(UnitMovementCancelledEvent evt)
        {
            if (!enablePathLines || !isInitialized) return;
            DestroyPathLine(evt.UnitID);
        }

        private void CreatePathLine(ushort unitID)
        {
            // Remove existing path line if any
            DestroyPathLine(unitID);

            // Get full path from movement queue
            var path = gameState.Units.MovementQueue.GetFullPath(unitID);
            if (path == null || path.Count < 2)
                return;

            // Convert province IDs to world positions
            List<Vector3> positions = new List<Vector3>();
            foreach (ushort provinceID in path)
            {
                if (centerLookup.TryGetProvinceCenter(provinceID, out Vector3 worldPosition))
                {
                    worldPosition.y += lineHeightOffset;
                    positions.Add(worldPosition);
                }
            }

            if (positions.Count < 2)
            {
                if (logDebugInfo)
                    ArchonLogger.LogWarning($"[UnitPathRenderer] Failed to get positions for path (unit {unitID})", "map_rendering");
                return;
            }

            // Create GameObject with LineRenderer
            GameObject lineObj = new GameObject($"PathLine_{unitID}");
            lineObj.transform.SetParent(pathLinesContainer);

            LineRenderer lineRenderer = lineObj.AddComponent<LineRenderer>();
            lineRenderer.positionCount = positions.Count;
            lineRenderer.SetPositions(positions.ToArray());
            lineRenderer.startWidth = lineWidth;
            lineRenderer.endWidth = lineWidth;

            // Simple unlit material
            Material lineMaterial = new Material(Shader.Find("Sprites/Default"));
            lineRenderer.material = lineMaterial;

            // Get country color for the unit
            var unit = gameState.Units.GetUnit(unitID);
            Color countryColor = GetCountryColor(unit.countryID);
            lineRenderer.startColor = countryColor;
            lineRenderer.endColor = Color.Lerp(countryColor, Color.white, 0.5f);

            activePathLines[unitID] = lineObj;

            if (logDebugInfo)
                ArchonLogger.Log($"[UnitPathRenderer] Created path line for unit {unitID} ({positions.Count} waypoints)", "map_rendering");
        }

        private void UpdatePathLine(ushort unitID)
        {
            var path = gameState.Units.MovementQueue.GetFullPath(unitID);

            if (path == null || path.Count < 2)
            {
                DestroyPathLine(unitID);
                return;
            }

            if (!activePathLines.TryGetValue(unitID, out GameObject lineObj))
                return;

            List<Vector3> positions = new List<Vector3>();
            foreach (ushort provinceID in path)
            {
                if (centerLookup.TryGetProvinceCenter(provinceID, out Vector3 worldPosition))
                {
                    worldPosition.y += lineHeightOffset;
                    positions.Add(worldPosition);
                }
            }

            if (positions.Count < 2)
            {
                DestroyPathLine(unitID);
                return;
            }

            LineRenderer lineRenderer = lineObj.GetComponent<LineRenderer>();
            if (lineRenderer != null)
            {
                lineRenderer.positionCount = positions.Count;
                lineRenderer.SetPositions(positions.ToArray());

                if (logDebugInfo)
                    ArchonLogger.Log($"[UnitPathRenderer] Updated path line for unit {unitID} ({positions.Count} waypoints)", "map_rendering");
            }
        }

        private void DestroyPathLine(ushort unitID)
        {
            if (activePathLines.TryGetValue(unitID, out GameObject lineObj))
            {
                activePathLines.Remove(unitID);
                Destroy(lineObj);

                if (logDebugInfo)
                    ArchonLogger.Log($"[UnitPathRenderer] Destroyed path line for unit {unitID}", "map_rendering");
            }
        }

        /// <summary>
        /// Get color for a country (simple hue-based).
        /// Override in subclass or use SetCountryColorProvider for custom colors.
        /// </summary>
        protected virtual Color GetCountryColor(ushort countryID)
        {
            float hue = (countryID * 0.618033988749895f) % 1.0f; // Golden ratio
            return Color.HSVToRGB(hue, 0.8f, 0.9f);
        }

        /// <summary>
        /// Enable or disable path rendering at runtime.
        /// </summary>
        public void SetEnabled(bool enabled)
        {
            enablePathLines = enabled;
            if (pathLinesContainer != null)
                pathLinesContainer.gameObject.SetActive(enabled);
        }

        /// <summary>
        /// Get count of active path lines.
        /// </summary>
        public int ActivePathCount => activePathLines.Count;

        void OnDestroy()
        {
            if (gameState?.EventBus != null)
            {
                gameState.EventBus.Unsubscribe<UnitMovementStartedEvent>(OnUnitMovementStarted);
                gameState.EventBus.Unsubscribe<UnitMovementCompletedEvent>(OnUnitMovementCompleted);
                gameState.EventBus.Unsubscribe<UnitMovementCancelledEvent>(OnUnitMovementCancelled);
            }

            foreach (var kvp in activePathLines)
            {
                if (kvp.Value != null)
                    Destroy(kvp.Value);
            }
            activePathLines.Clear();
        }
    }
}
