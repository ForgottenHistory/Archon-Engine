using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Map.Rendering
{
    /// <summary>
    /// Renders border meshes using Unity's Graphics.DrawMesh API
    /// Handles province and country borders as separate mesh objects
    /// Uses unlit vertex color material for simple flat-shaded borders
    /// </summary>
    public class BorderMeshRenderer
    {
        private List<Mesh> provinceBorderMeshes = new List<Mesh>();
        private List<Mesh> countryBorderMeshes = new List<Mesh>();
        private Material borderMaterial;
        private Transform mapPlaneTransform;

        private readonly int mapRenderLayer = 0; // Default layer

        private bool hasLoggedFirstRender = false;

        public BorderMeshRenderer(Transform mapPlane = null)
        {
            // Create unlit vertex color material
            borderMaterial = CreateBorderMaterial();
            mapPlaneTransform = mapPlane;
        }

        /// <summary>
        /// Set the meshes to render (may be multiple meshes due to 65k vertex limit)
        /// </summary>
        public void SetMeshes(List<Mesh> provinceMeshes, List<Mesh> countryMeshes)
        {
            provinceBorderMeshes = provinceMeshes ?? new List<Mesh>();
            countryBorderMeshes = countryMeshes ?? new List<Mesh>();

            int provinceVerts = provinceBorderMeshes.Sum(m => m.vertexCount);
            int countryVerts = countryBorderMeshes.Sum(m => m.vertexCount);

            ArchonLogger.Log($"BorderMeshRenderer: Set meshes - Province: {provinceBorderMeshes.Count} meshes ({provinceVerts} verts), Country: {countryBorderMeshes.Count} meshes ({countryVerts} verts)", "map_initialization");
        }

        /// <summary>
        /// Render borders for current frame
        /// Called every frame by MapRenderingCoordinator
        /// </summary>
        public void RenderBorders()
        {
            if (borderMaterial == null)
            {
                if (!hasLoggedFirstRender)
                {
                    ArchonLogger.LogWarning("BorderMeshRenderer: Material is null, cannot render borders", "map_rendering");
                    hasLoggedFirstRender = true;
                }
                return;
            }

            if (!hasLoggedFirstRender)
            {
                ArchonLogger.Log($"BorderMeshRenderer: First render - Province: {provinceBorderMeshes.Count} meshes, Country: {countryBorderMeshes.Count} meshes", "map_rendering");
                ArchonLogger.Log($"BorderMeshRenderer: Material: {borderMaterial.name}, Shader: {borderMaterial.shader.name}", "map_rendering");
                hasLoggedFirstRender = true;
            }

            // Calculate transform to match map plane
            // Map mesh is in pixel coordinates, but map plane is scaled to Unity world space
            Matrix4x4 transform;
            if (mapPlaneTransform != null)
            {
                // Use map plane's transform (position, rotation, scale)
                transform = mapPlaneTransform.localToWorldMatrix;
            }
            else
            {
                // Fallback: identity transform (will be huge and probably not visible)
                transform = Matrix4x4.identity;
                ArchonLogger.LogWarning("BorderMeshRenderer: No map plane transform set, borders may not be visible!", "map_rendering");
            }

            // Draw all province border meshes
            foreach (var mesh in provinceBorderMeshes)
            {
                Graphics.DrawMesh(
                    mesh,
                    transform,
                    borderMaterial,
                    mapRenderLayer,
                    null, // All cameras
                    0,    // Submesh index
                    null, // Material property block
                    UnityEngine.Rendering.ShadowCastingMode.Off,
                    false // No receiving shadows
                );
            }

            // Draw all country border meshes
            foreach (var mesh in countryBorderMeshes)
            {
                Graphics.DrawMesh(
                    mesh,
                    transform,
                    borderMaterial,
                    mapRenderLayer,
                    null,
                    0,
                    null,
                    UnityEngine.Rendering.ShadowCastingMode.Off,
                    false
                );
            }
        }

        /// <summary>
        /// Create material for borders with texture
        /// </summary>
        private Material CreateBorderMaterial()
        {
            // Use custom URP-compatible vertex color shader
            Shader shader = Shader.Find("Archon/BorderMesh");
            if (shader == null)
            {
                ArchonLogger.LogError("BorderMeshRenderer: Could not find Archon/BorderMesh shader! Make sure BorderMesh.shader exists.", "map_rendering");
                return null;
            }

            Material mat = new Material(shader)
            {
                name = "BorderMeshMaterial"
            };

            // Load border texture from Resources or Template-Data
            Texture2D borderTexture = Resources.Load<Texture2D>("Textures/border_texture");
            if (borderTexture == null)
            {
                // Try loading from Archon-Engine Template-Data path
                borderTexture = Resources.Load<Texture2D>("border_texture");
            }

            if (borderTexture != null)
            {
                mat.SetTexture("_BorderTex", borderTexture);
                ArchonLogger.Log($"BorderMeshRenderer: Loaded border texture ({borderTexture.width}x{borderTexture.height})", "map_initialization");
            }
            else
            {
                // Create a fallback texture: white horizontal line in center
                borderTexture = CreateFallbackBorderTexture();
                mat.SetTexture("_BorderTex", borderTexture);
                ArchonLogger.LogWarning("BorderMeshRenderer: Using fallback border texture. Place border_texture.png in Resources folder for custom texture.", "map_initialization");
            }

            ArchonLogger.Log("BorderMeshRenderer: Created material with Archon/BorderMesh shader", "map_initialization");
            return mat;
        }

        /// <summary>
        /// Set a custom border texture on the material
        /// </summary>
        public void SetBorderTexture(Texture2D texture)
        {
            if (borderMaterial != null && texture != null)
            {
                borderMaterial.SetTexture("_BorderTex", texture);
                ArchonLogger.Log($"BorderMeshRenderer: Set custom border texture ({texture.width}x{texture.height})", "map_rendering");
            }
        }

        /// <summary>
        /// Create a simple fallback border texture: white line in center, black edges
        /// </summary>
        private Texture2D CreateFallbackBorderTexture()
        {
            int width = 64;
            int height = 64;
            Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);

            Color black = new Color(0, 0, 0, 0); // Transparent black
            Color white = Color.white;

            // Create horizontal line in center with soft edges
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    // V coordinate (0 to 1) - determines if we're in the line
                    float v = y / (float)(height - 1);

                    // Line is in center (V = 0.3 to 0.7) with soft falloff
                    float centerDist = Mathf.Abs(v - 0.5f);
                    float lineWidth = 0.2f; // Half-width of line
                    float softness = 0.1f;

                    float alpha = 1f - Mathf.Clamp01((centerDist - lineWidth) / softness);

                    tex.SetPixel(x, y, new Color(1, 1, 1, alpha));
                }
            }

            tex.Apply();
            tex.wrapModeU = TextureWrapMode.Repeat; // Tile along border length
            tex.wrapModeV = TextureWrapMode.Clamp;  // Clamp across border width
            tex.filterMode = FilterMode.Bilinear;

            return tex;
        }

        /// <summary>
        /// Clean up resources
        /// </summary>
        public void Dispose()
        {
            if (borderMaterial != null)
                Object.Destroy(borderMaterial);
        }
    }
}
