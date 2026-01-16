using UnityEngine;
using System.Collections.Generic;
using Core;
using Core.Events;
using Core.Units;
using Map.Core;
using Map.Rendering;
using Map.Utils;

namespace StarterKit
{
    /// <summary>
    /// Unit visualization. Renders unit count badges at province centers using GPU instancing.
    /// Uses BillboardAtlasGenerator for number display (0-99).
    /// </summary>
    public class UnitVisualization : MonoBehaviour
    {
        [Header("Configuration")]
        [SerializeField] private float unitHeight = 2f;
        [SerializeField] private float badgeScale = 4f;

        [Header("References")]
        [SerializeField] private BillboardAtlasGenerator atlasGenerator;

        [Header("Path Rendering")]
        [SerializeField] private bool enablePathLines = true;

        [Header("Debug")]
        [SerializeField] private bool logDebug = true;

        // Dependencies
        private GameState gameState;
        private UnitSystem unitSystem;
        private ProvinceCenterLookup centerLookup;
        private CompositeDisposable subscriptions;
        private UnitPathRenderer pathRenderer;

        // GPU instancing data
        private Mesh quadMesh;
        private Material badgeMaterial;
        private List<Matrix4x4> matrices = new List<Matrix4x4>();
        private List<float> displayValues = new List<float>();
        private List<float> scaleValues = new List<float>();
        private MaterialPropertyBlock propertyBlock;

        // Tracking
        private Dictionary<ushort, UnitTrackData> trackedUnits = new Dictionary<ushort, UnitTrackData>();
        private Dictionary<ushort, int> provinceUnitCounts = new Dictionary<ushort, int>();
        private bool isDirty = false;
        private bool isInitialized = false;

        private struct UnitTrackData
        {
            public ushort provinceID;
            public ushort countryID;
        }

        public bool IsInitialized => isInitialized;

        public void Initialize(GameState gameStateRef, UnitSystem unitSystemRef)
        {
            if (isInitialized)
            {
                ArchonLogger.LogWarning("UnitVisualization: Already initialized", "starter_kit");
                return;
            }

            gameState = gameStateRef;
            unitSystem = unitSystemRef;

            // Find map dependencies
            var coordinator = FindFirstObjectByType<MapSystemCoordinator>();
            if (coordinator == null || coordinator.ProvinceMapping == null)
            {
                ArchonLogger.LogError("UnitVisualization: MapSystemCoordinator or ProvinceMapping not found", "starter_kit");
                return;
            }

            var mapInitializer = FindFirstObjectByType<MapInitializer>();
            if (mapInitializer == null || mapInitializer.TextureManager == null)
            {
                ArchonLogger.LogError("UnitVisualization: MapInitializer or TextureManager not found", "starter_kit");
                return;
            }

            // Find map mesh
            var meshRenderer = FindFirstObjectByType<MeshRenderer>();
            if (meshRenderer == null)
            {
                ArchonLogger.LogError("UnitVisualization: Map MeshRenderer not found", "starter_kit");
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
                ArchonLogger.LogError("UnitVisualization: Failed to initialize center lookup", "starter_kit");
                return;
            }

            // Initialize path renderer for movement visualization
            if (enablePathLines)
            {
                var pathRendererObj = new GameObject("UnitPathRenderer");
                pathRendererObj.transform.SetParent(transform);
                pathRenderer = pathRendererObj.AddComponent<UnitPathRenderer>();
                pathRenderer.Initialize(gameState, centerLookup);
            }

            // Create rendering resources
            CreateRenderingResources();

            // Subscribe to unit events (tokens auto-disposed on OnDestroy)
            subscriptions = new CompositeDisposable();
            subscriptions.Add(gameState.EventBus.Subscribe<UnitCreatedEvent>(OnUnitCreated));
            subscriptions.Add(gameState.EventBus.Subscribe<UnitDestroyedEvent>(OnUnitDestroyed));
            subscriptions.Add(gameState.EventBus.Subscribe<UnitMovedEvent>(OnUnitMoved));

            isInitialized = true;
            isDirty = true;

            ArchonLogger.Log("UnitVisualization: Initialized", "starter_kit");
        }

        private void CreateRenderingResources()
        {
            // Create vertical quad mesh (shader handles billboarding)
            quadMesh = new Mesh();
            quadMesh.name = "UnitBadgeQuad";
            quadMesh.vertices = new Vector3[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3(0.5f, -0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f)
            };
            quadMesh.uv = new Vector2[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f)
            };
            // Double-sided
            quadMesh.triangles = new int[] { 0, 2, 1, 2, 3, 1, 1, 2, 0, 1, 3, 2 };
            quadMesh.RecalculateNormals();
            quadMesh.RecalculateBounds();

            // Use the InstancedAtlasBadge shader for number display
            Shader shader = Shader.Find("Engine/InstancedAtlasBadge");
            if (shader == null)
            {
                ArchonLogger.LogError("UnitVisualization: Engine/InstancedAtlasBadge shader not found!", "starter_kit");
                return;
            }

            badgeMaterial = new Material(shader);
            badgeMaterial.enableInstancing = true;
            badgeMaterial.renderQueue = 3000; // Render on top of map

            // Set background color (black with some transparency)
            badgeMaterial.SetColor("_BackgroundColor", new Color(0f, 0f, 0f, 0.8f));

            // Set atlas texture if generator is available
            if (atlasGenerator != null)
            {
                var atlas = atlasGenerator.AtlasTexture;
                if (atlas != null)
                {
                    badgeMaterial.SetTexture("_NumberAtlas", atlas);
                    ArchonLogger.Log($"UnitVisualization: Atlas assigned: {atlas.width}x{atlas.height}", "starter_kit");
                }
                else
                {
                    ArchonLogger.LogWarning("UnitVisualization: Atlas texture is null!", "starter_kit");
                }
            }
            else
            {
                ArchonLogger.LogWarning("UnitVisualization: BillboardAtlasGenerator not assigned - create one and assign it", "starter_kit");
            }

            propertyBlock = new MaterialPropertyBlock();

            ArchonLogger.Log($"UnitVisualization: Created material with shader '{shader.name}'", "starter_kit");
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
                ArchonLogger.Log($"UnitVisualization: Tracking unit {evt.UnitID} in province {evt.ProvinceID}", "starter_kit");
        }

        private void OnUnitDestroyed(UnitDestroyedEvent evt)
        {
            trackedUnits.Remove(evt.UnitID);
            isDirty = true;

            if (logDebug)
                ArchonLogger.Log($"UnitVisualization: Removed unit {evt.UnitID}", "starter_kit");
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

            // Render all badges with per-instance display values
            if (matrices.Count > 0 && badgeMaterial != null && quadMesh != null)
            {
                // Set per-instance properties
                if (displayValues.Count > 0)
                {
                    propertyBlock.SetFloatArray("_DisplayValue", displayValues);
                    propertyBlock.SetFloatArray("_Scale", scaleValues);
                }

                Graphics.DrawMeshInstanced(quadMesh, 0, badgeMaterial, matrices, propertyBlock);
            }
        }

        private void RebuildMatrices()
        {
            matrices.Clear();
            displayValues.Clear();
            scaleValues.Clear();
            provinceUnitCounts.Clear();

            if (logDebug)
                ArchonLogger.Log($"UnitVisualization: RebuildMatrices called, {trackedUnits.Count} tracked units", "starter_kit");

            // Count units per province
            foreach (var kvp in trackedUnits)
            {
                var provinceId = kvp.Value.provinceID;
                if (provinceId == 0) continue;

                if (!provinceUnitCounts.ContainsKey(provinceId))
                    provinceUnitCounts[provinceId] = 0;

                provinceUnitCounts[provinceId]++;
            }

            // Create one badge per province with units
            foreach (var kvp in provinceUnitCounts)
            {
                var provinceId = kvp.Key;
                var unitCount = kvp.Value;

                if (!centerLookup.TryGetProvinceCenter(provinceId, out Vector3 worldPos))
                {
                    if (logDebug)
                        ArchonLogger.LogWarning($"UnitVisualization: Failed to get center for province {provinceId}", "starter_kit");
                    continue;
                }

                // Position badge above map
                var badgePos = new Vector3(worldPos.x, unitHeight, worldPos.z);
                var matrix = Matrix4x4.TRS(badgePos, Quaternion.identity, Vector3.one);
                matrices.Add(matrix);

                // Clamp to 0-99 for atlas display
                displayValues.Add(Mathf.Min(unitCount, 99));
                scaleValues.Add(badgeScale);

                if (logDebug)
                    ArchonLogger.Log($"UnitVisualization: Badge at province {provinceId}, count={unitCount}, pos={badgePos}", "starter_kit");
            }

            if (logDebug)
                ArchonLogger.Log($"UnitVisualization: Rebuilt {matrices.Count} badges, material={badgeMaterial != null}, mesh={quadMesh != null}", "starter_kit");
        }

        void OnDestroy()
        {
            subscriptions?.Dispose();

            if (quadMesh != null)
                Destroy(quadMesh);
            if (badgeMaterial != null)
                Destroy(badgeMaterial);
        }
    }
}
