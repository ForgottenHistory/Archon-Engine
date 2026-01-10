using UnityEngine;
using System.Collections.Generic;
using Core;
using Core.Units;
using Map.Core;
using Map.Rendering;
using Map.Utils;

namespace StarterKit
{
    /// <summary>
    /// Simple unit visualization for StarterKit.
    /// Renders units as colored quads at province centers using GPU instancing.
    /// </summary>
    public class StarterKitUnitVisualization : MonoBehaviour
    {
        [Header("Configuration")]
        [SerializeField] private float unitHeight = 2f;
        [SerializeField] private float unitScale = 3f;
        [SerializeField] private Color defaultUnitColor = Color.cyan;

        [Header("Debug")]
        [SerializeField] private bool logDebug = true;

        // Dependencies
        private GameState gameState;
        private StarterKitUnitSystem unitSystem;
        private ProvinceCenterLookup centerLookup;

        // GPU instancing data
        private Mesh quadMesh;
        private Material unitMaterial;
        private List<Matrix4x4> matrices = new List<Matrix4x4>();
        private MaterialPropertyBlock propertyBlock;

        // Tracking
        private Dictionary<ushort, UnitTrackData> trackedUnits = new Dictionary<ushort, UnitTrackData>();
        private bool isDirty = false;
        private bool isInitialized = false;

        private struct UnitTrackData
        {
            public ushort provinceID;
            public ushort countryID;
        }

        public bool IsInitialized => isInitialized;

        public void Initialize(GameState gameStateRef, StarterKitUnitSystem unitSystemRef)
        {
            if (isInitialized)
            {
                ArchonLogger.LogWarning("StarterKitUnitVisualization: Already initialized", "starter_kit");
                return;
            }

            gameState = gameStateRef;
            unitSystem = unitSystemRef;

            // Find map dependencies
            var coordinator = FindFirstObjectByType<MapSystemCoordinator>();
            if (coordinator == null || coordinator.ProvinceMapping == null)
            {
                ArchonLogger.LogError("StarterKitUnitVisualization: MapSystemCoordinator or ProvinceMapping not found", "starter_kit");
                return;
            }

            var mapInitializer = FindFirstObjectByType<MapInitializer>();
            if (mapInitializer == null || mapInitializer.TextureManager == null)
            {
                ArchonLogger.LogError("StarterKitUnitVisualization: MapInitializer or TextureManager not found", "starter_kit");
                return;
            }

            // Find map mesh
            var meshRenderer = FindFirstObjectByType<MeshRenderer>();
            if (meshRenderer == null)
            {
                ArchonLogger.LogError("StarterKitUnitVisualization: Map MeshRenderer not found", "starter_kit");
                return;
            }

            // Initialize province center lookup (shared ENGINE utility)
            centerLookup = new ProvinceCenterLookup();
            centerLookup.Initialize(
                coordinator.ProvinceMapping,
                meshRenderer.transform,
                mapInitializer.TextureManager.MapWidth,
                mapInitializer.TextureManager.MapHeight
            );

            if (!centerLookup.IsInitialized)
            {
                ArchonLogger.LogError("StarterKitUnitVisualization: Failed to initialize center lookup", "starter_kit");
                return;
            }

            // Create rendering resources
            CreateRenderingResources();

            // Subscribe to unit events
            gameState.EventBus.Subscribe<UnitCreatedEvent>(OnUnitCreated);
            gameState.EventBus.Subscribe<UnitDestroyedEvent>(OnUnitDestroyed);
            gameState.EventBus.Subscribe<UnitMovedEvent>(OnUnitMoved);

            isInitialized = true;
            isDirty = true;

            ArchonLogger.Log("StarterKitUnitVisualization: Initialized", "starter_kit");
        }

        private void CreateRenderingResources()
        {
            // Create horizontal quad mesh (lies flat on map, visible from above)
            quadMesh = new Mesh();
            quadMesh.name = "UnitQuad";
            quadMesh.vertices = new Vector3[]
            {
                new Vector3(-0.5f, 0f, -0.5f),
                new Vector3(0.5f, 0f, -0.5f),
                new Vector3(-0.5f, 0f, 0.5f),
                new Vector3(0.5f, 0f, 0.5f)
            };
            quadMesh.uv = new Vector2[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f)
            };
            // Front face visible from above (Y+), back face from below
            quadMesh.triangles = new int[] { 0, 2, 1, 2, 3, 1, 1, 2, 0, 1, 3, 2 };
            quadMesh.RecalculateNormals();
            quadMesh.RecalculateBounds();

            // Use simple unlit shader for flat quads
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");

            if (shader == null)
            {
                ArchonLogger.LogError("StarterKitUnitVisualization: No unlit shader found!", "starter_kit");
                return;
            }

            unitMaterial = new Material(shader);
            unitMaterial.SetColor("_BaseColor", defaultUnitColor);
            unitMaterial.color = defaultUnitColor;
            unitMaterial.enableInstancing = true;
            unitMaterial.renderQueue = 3000; // Render on top of map

            propertyBlock = new MaterialPropertyBlock();

            ArchonLogger.Log($"StarterKitUnitVisualization: Created material with shader '{shader.name}'", "starter_kit");
        }

        private void OnUnitCreated(UnitCreatedEvent evt)
        {
            trackedUnits[evt.UnitID] = new UnitTrackData
            {
                provinceID = evt.ProvinceID,
                countryID = evt.CountryID
            };
            isDirty = true;

            if (logDebug)
                ArchonLogger.Log($"StarterKitUnitVisualization: Tracking unit {evt.UnitID} in province {evt.ProvinceID}", "starter_kit");
        }

        private void OnUnitDestroyed(UnitDestroyedEvent evt)
        {
            trackedUnits.Remove(evt.UnitID);
            isDirty = true;

            if (logDebug)
                ArchonLogger.Log($"StarterKitUnitVisualization: Removed unit {evt.UnitID}", "starter_kit");
        }

        private void OnUnitMoved(UnitMovedEvent evt)
        {
            if (trackedUnits.TryGetValue(evt.UnitID, out var data))
            {
                data.provinceID = evt.NewProvinceID;
                trackedUnits[evt.UnitID] = data;
                isDirty = true;
            }
        }

        void LateUpdate()
        {
            if (!isInitialized) return;

            if (isDirty)
            {
                RebuildMatrices();
                isDirty = false;
            }

            // Render all units
            if (matrices.Count > 0 && unitMaterial != null && quadMesh != null)
            {
                Graphics.DrawMeshInstanced(quadMesh, 0, unitMaterial, matrices, propertyBlock);
            }
        }

        private void RebuildMatrices()
        {
            matrices.Clear();

            if (logDebug)
                ArchonLogger.Log($"StarterKitUnitVisualization: RebuildMatrices called, {trackedUnits.Count} tracked units", "starter_kit");

            // Group units by province
            var provinceUnits = new Dictionary<ushort, List<ushort>>();
            foreach (var kvp in trackedUnits)
            {
                var provinceId = kvp.Value.provinceID;
                if (provinceId == 0) continue;

                if (!provinceUnits.ContainsKey(provinceId))
                    provinceUnits[provinceId] = new List<ushort>();

                provinceUnits[provinceId].Add(kvp.Key);
            }

            // Create one visual per province with units
            foreach (var kvp in provinceUnits)
            {
                var provinceId = kvp.Key;

                if (!centerLookup.TryGetProvinceCenter(provinceId, out Vector3 worldPos))
                {
                    if (logDebug)
                        ArchonLogger.LogWarning($"StarterKitUnitVisualization: Failed to get center for province {provinceId}", "starter_kit");
                    continue;
                }

                // Position unit above map
                var unitPos = new Vector3(worldPos.x, unitHeight, worldPos.z);
                var matrix = Matrix4x4.TRS(unitPos, Quaternion.identity, Vector3.one * unitScale);
                matrices.Add(matrix);

                if (logDebug)
                    ArchonLogger.Log($"StarterKitUnitVisualization: Unit at province {provinceId}, pos={unitPos}", "starter_kit");
            }

            if (logDebug)
                ArchonLogger.Log($"StarterKitUnitVisualization: Rebuilt {matrices.Count} unit visuals, material={unitMaterial != null}, mesh={quadMesh != null}", "starter_kit");
        }

        void OnDestroy()
        {
            if (gameState?.EventBus != null)
            {
                gameState.EventBus.Unsubscribe<UnitCreatedEvent>(OnUnitCreated);
                gameState.EventBus.Unsubscribe<UnitDestroyedEvent>(OnUnitDestroyed);
                gameState.EventBus.Unsubscribe<UnitMovedEvent>(OnUnitMoved);
            }

            if (quadMesh != null)
                Destroy(quadMesh);
            if (unitMaterial != null)
                Destroy(unitMaterial);
        }
    }
}
