using UnityEngine;
using UnityEngine.Rendering;
using Utils;

namespace Map.Rendering
{
    /// <summary>
    /// GPU-driven tree rendering using DrawMeshInstancedIndirect.
    ///
    /// Architecture:
    /// - Renders trees from GPU buffer (no CPU array transfer)
    /// - Single draw call for all trees (millions possible)
    /// - Receives transform matrices from TreeInstanceGenerator
    /// - Fully GPU-driven (instance count read from buffer)
    ///
    /// Performance:
    /// - Zero CPU overhead per frame
    /// - GPU reads instance count and matrices directly
    /// - Frustum culling handled by GPU
    /// - Supports shadows and LOD
    ///
    /// Usage:
    /// - Call SetTreeData() to bind matrix buffer
    /// - Update() automatically renders each frame
    /// </summary>
    public class TreeInstanceRenderer : MonoBehaviour
    {
        [Header("Rendering Setup")]
        [SerializeField] private Mesh treeMesh;
        [SerializeField] private Material[] treeMaterials; // One material per submesh

        [Header("Configuration")]
        [SerializeField] private ShadowCastingMode shadowCasting = ShadowCastingMode.On;
        [SerializeField] private bool receiveShadows = true;
        [SerializeField] private int layer = 0;

        // GPU buffers
        private ComputeBuffer treeMatrixBuffer;
        private ComputeBuffer treeCountBuffer;
        private ComputeBuffer argsBuffer;

        // Rendering bounds (must be large enough to contain all trees)
        private Bounds renderBounds;

        // Material property block for per-instance data
        private MaterialPropertyBlock propertyBlock;

        private static readonly int TreeMatricesID = Shader.PropertyToID("_TreeMatrices");

        private bool isInitialized = false;

        private void Awake()
        {
            propertyBlock = new MaterialPropertyBlock();
        }

        /// <summary>
        /// Set tree rendering data from generator.
        /// Must be called after TreeInstanceGenerator.GenerateTrees()
        /// </summary>
        /// <param name="matrixBuffer">Tree transform matrices</param>
        /// <param name="countBuffer">Tree count (for indirect args)</param>
        /// <param name="maxTrees">Maximum tree count (buffer capacity)</param>
        /// <param name="mapWidth">Map width for bounds calculation</param>
        /// <param name="mapHeight">Map height for bounds calculation</param>
        public void SetTreeData(
            ComputeBuffer matrixBuffer,
            ComputeBuffer countBuffer,
            int maxTrees,
            float mapWidth,
            float mapHeight)
        {
            this.treeMatrixBuffer = matrixBuffer;
            this.treeCountBuffer = countBuffer;

            // Create indirect args buffer
            CreateArgsBuffer(maxTrees);

            // Calculate rendering bounds (must contain all possible trees)
            // Center at map center, extend to cover entire map + tree height
            // NOTE: mapWidth/mapHeight here are WORLD SPACE dimensions (not texture pixels)
            renderBounds = new Bounds(
                new Vector3(mapWidth / 2.0f, 5.0f, mapHeight / 2.0f),  // Center Y at 5 (reasonable tree height)
                new Vector3(mapWidth, 10.0f, mapHeight)  // Extents: full map + 10 units height for trees
            );

            // Set matrix buffer in material
            propertyBlock.SetBuffer(TreeMatricesID, treeMatrixBuffer);

            isInitialized = true;

            ArchonLogger.Log($"TreeInstanceRenderer: Initialized for up to {maxTrees} trees (mesh: {treeMesh != null}, materials: {treeMaterials?.Length ?? 0})", "map_rendering");
        }

        /// <summary>
        /// Create indirect args buffer for DrawMeshInstancedIndirect.
        /// Format: [index count per instance, instance count, start index, base vertex, start instance]
        /// </summary>
        private void CreateArgsBuffer(int maxTrees)
        {
            // Release old buffer if exists
            argsBuffer?.Release();

            // Indirect args: 5 uints
            // [0] = index count per instance (triangle count * 3)
            // [1] = instance count (populated from treeCountBuffer)
            // [2] = start index location
            // [3] = base vertex location
            // [4] = start instance location
            uint[] args = new uint[5];
            args[0] = treeMesh != null ? treeMesh.GetIndexCount(0) : 0;
            args[1] = 0; // Will be set from treeCountBuffer
            args[2] = 0;
            args[3] = 0;
            args[4] = 0;

            argsBuffer = new ComputeBuffer(1, sizeof(uint) * 5, ComputeBufferType.IndirectArguments);
            argsBuffer.SetData(args);

            // Copy instance count from treeCountBuffer to argsBuffer[1]
            // This is a GPU-to-GPU copy (no CPU readback)
            if (treeCountBuffer != null)
            {
                ComputeBuffer.CopyCount(treeCountBuffer, argsBuffer, sizeof(uint));
            }
        }

        private bool hasLoggedRenderStart = false;
        private bool hasLoggedNotRendering = false;

        private void Update()
        {
            if (!isInitialized || treeMesh == null || treeMaterials == null || treeMaterials.Length == 0 || treeMatrixBuffer == null)
            {
                if (!hasLoggedNotRendering)
                {
                    ArchonLogger.LogWarning($"TreeInstanceRenderer: Not rendering - Init:{isInitialized}, Mesh:{treeMesh != null}, Materials:{treeMaterials?.Length ?? 0}, Buffer:{treeMatrixBuffer != null}", "map_rendering");
                    hasLoggedNotRendering = true;
                }
                return;
            }

            // Render all submeshes (one draw call per submesh)
            // For a tree with 3 materials: branches, leaves, twigs = 3 draw calls total
            int submeshCount = Mathf.Min(treeMesh.subMeshCount, treeMaterials.Length);

            if (!hasLoggedRenderStart)
            {
                ArchonLogger.Log($"TreeInstanceRenderer: Starting render - submeshes: {submeshCount}, bounds: {renderBounds}", "map_rendering");
                hasLoggedRenderStart = true;
            }

            for (int i = 0; i < submeshCount; i++)
            {
                if (treeMaterials[i] == null)
                    continue;

                Graphics.DrawMeshInstancedIndirect(
                    treeMesh,
                    i,                      // Submesh index (0=branches, 1=leaves, 2=twigs)
                    treeMaterials[i],
                    renderBounds,           // Culling bounds
                    argsBuffer,             // Indirect args (contains instance count)
                    0,                      // Args byte offset
                    propertyBlock,          // Per-instance data
                    shadowCasting,
                    receiveShadows,
                    layer
                );
            }
        }

        private void OnDestroy()
        {
            // Release args buffer (matrix/count buffers owned by generator)
            argsBuffer?.Release();
        }

        /// <summary>
        /// Update rendering bounds if needed (e.g., camera moved far from map)
        /// </summary>
        public void SetRenderBounds(Bounds bounds)
        {
            renderBounds = bounds;
        }

        /// <summary>
        /// Enable/disable tree rendering
        /// </summary>
        public void SetEnabled(bool enabled)
        {
            this.enabled = enabled;
        }

        #if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            // Visualize rendering bounds in editor
            Gizmos.color = Color.green;
            Gizmos.DrawWireCube(renderBounds.center, renderBounds.size);
        }
        #endif
    }
}
