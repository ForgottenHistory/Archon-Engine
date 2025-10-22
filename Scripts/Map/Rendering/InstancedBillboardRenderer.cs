using System.Collections.Generic;
using UnityEngine;

namespace Map.Rendering
{
    /// <summary>
    /// Base class for GPU instanced billboard rendering.
    /// Renders thousands of billboarded sprites in a single draw call using Graphics.DrawMeshInstanced.
    ///
    /// Architecture:
    /// - Maintains matrix lists for world positions
    /// - Uses MaterialPropertyBlock for per-instance data
    /// - Subclasses implement data source integration (event systems, polling, etc.)
    /// - Dirty flag system for efficient updates
    ///
    /// Performance:
    /// - Single draw call for all instances
    /// - Billboard rotation in vertex shader (GPU-side)
    /// - Event-driven updates (no Update loop overhead)
    ///
    /// Usage:
    /// - Inherit and implement abstract methods
    /// - Call MarkDirty() when data changes
    /// - RebuildInstances() is called automatically on dirty flag
    /// </summary>
    public abstract class InstancedBillboardRenderer : MonoBehaviour
    {
        [Header("Rendering Setup")]
        [SerializeField] protected Mesh quadMesh;
        [SerializeField] protected Material material;

        [Header("Configuration")]
        [SerializeField] protected int initialCapacity = 1024;
        [SerializeField] protected bool autoCreateQuad = true;

        // Instance data
        protected List<Matrix4x4> matrices;
        protected MaterialPropertyBlock propertyBlock;

        // Dirty flag for rebuild
        protected bool isDirty = false;

        protected virtual void Awake()
        {
            // Initialize instance data structures
            matrices = new List<Matrix4x4>(initialCapacity);
            propertyBlock = new MaterialPropertyBlock();

            // Create default quad mesh if needed
            if (autoCreateQuad && quadMesh == null)
            {
                quadMesh = CreateQuadMesh();
            }
        }

        protected virtual void LateUpdate()
        {
            // Rebuild if dirty
            if (isDirty)
            {
                RebuildInstances();
                isDirty = false;
            }

            // Render instances
            if (matrices.Count > 0 && material != null && quadMesh != null)
            {
                Graphics.DrawMeshInstanced(
                    quadMesh,
                    0,
                    material,
                    matrices,
                    propertyBlock
                );
            }
        }

        /// <summary>
        /// Mark renderer as dirty to trigger rebuild on next LateUpdate.
        /// Call this when data changes (events, state changes, etc.).
        /// </summary>
        protected void MarkDirty()
        {
            isDirty = true;
        }

        /// <summary>
        /// Force immediate rebuild of all instances.
        /// Normally called automatically via dirty flag.
        /// </summary>
        public void ForceRebuild()
        {
            RebuildInstances();
            isDirty = false;
        }

        /// <summary>
        /// Rebuild all instance matrices and properties.
        /// Called automatically when dirty flag is set.
        ///
        /// Implementation should:
        /// 1. Clear matrices list
        /// 2. Query data source for current state
        /// 3. Build Matrix4x4 for each instance
        /// 4. Set per-instance properties in propertyBlock
        /// </summary>
        protected abstract void RebuildInstances();

        /// <summary>
        /// Create a simple quad mesh for billboarded sprites.
        /// Centered at origin, 1x1 unit size.
        /// </summary>
        protected virtual Mesh CreateQuadMesh()
        {
            var mesh = new Mesh();
            mesh.name = "Billboard Quad";

            // Vertices (centered at origin)
            mesh.vertices = new Vector3[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3(0.5f, -0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f)
            };

            // UVs
            mesh.uv = new Vector2[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f)
            };

            // Triangles
            mesh.triangles = new int[]
            {
                0, 2, 1, // First triangle
                2, 3, 1  // Second triangle
            };

            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            return mesh;
        }

        protected virtual void OnDestroy()
        {
            // Clean up mesh if we created it
            if (quadMesh != null && quadMesh.name == "Billboard Quad")
            {
                Destroy(quadMesh);
            }
        }
    }
}
