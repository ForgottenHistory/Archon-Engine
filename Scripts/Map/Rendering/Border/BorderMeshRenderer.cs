using System.Collections.Generic;
using UnityEngine;
using Map.Rendering.Border;

namespace Map.Rendering
{
    /// <summary>
    /// Draws the per-class border meshes with Graphics.DrawMesh.
    /// One material per class; draw order between classes comes from the render queue
    /// (same-queue transparent draws are distance sorted, so order would otherwise be arbitrary).
    /// </summary>
    public class BorderMeshRenderer
    {
        private readonly int baseQueue; // render queue of the first class (Transparent-100 = after opaque terrain)

        private static readonly int BorderTexId = Shader.PropertyToID("_BorderTex");
        private static readonly int UseTextureId = Shader.PropertyToID("_BorderUseTexture");
        private static readonly int TexAlphaOnlyId = Shader.PropertyToID("_BorderTexAlphaOnly");
        private static readonly int SoftnessId = Shader.PropertyToID("_BorderSoftness");
        private static readonly int SrcBlendId = Shader.PropertyToID("_BorderSrcBlend");
        private static readonly int DstBlendId = Shader.PropertyToID("_BorderDstBlend");
        private static readonly int BlendOpId = Shader.PropertyToID("_BorderBlendOp");
        private static readonly int PremultiplyId = Shader.PropertyToID("_BorderPremultiply");
        private static readonly int ColorId = Shader.PropertyToID("_BorderColor");
        private static readonly int ColorSourceId = Shader.PropertyToID("_BorderColorSource");
        private static readonly int WidthId = Shader.PropertyToID("_BorderWidth");
        private static readonly int WidthPerLevelId = Shader.PropertyToID("_BorderWidthPerLevel");
        private static readonly int MinScreenWidthId = Shader.PropertyToID("_BorderMinScreenWidth");
        private static readonly int TileId = Shader.PropertyToID("_BorderTile");
        private static readonly int FadeId = Shader.PropertyToID("_BorderFade");
        private static readonly int MapWorldBoundsId = Shader.PropertyToID("_MapWorldBounds");
        private static readonly int HeightmapId = Shader.PropertyToID("_HeightmapTexture");
        private static readonly int HeightScaleId = Shader.PropertyToID("_HeightScale");

        private readonly Shader shader;
        private readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
        private readonly List<(Mesh mesh, Material material)> drawList = new List<(Mesh, Material)>();

        private Vector4 mapWorldBounds;
        private Texture heightmap;
        private float heightScale;

        public BorderMeshRenderer(int baseQueue = 2900)
        {
            this.baseQueue = baseQueue;
            shader = Shader.Find("Archon/BorderMesh");
            if (shader == null)
                ArchonLogger.LogError("BorderMeshRenderer: Archon/BorderMesh shader not found", "map_rendering");
        }

        public void SetMapWorldBounds(Vector3 worldMin, Vector3 worldSize)
        {
            mapWorldBounds = new Vector4(worldMin.x, worldMin.z, worldSize.x, worldSize.z);
            foreach (var m in materials.Values) m.SetVector(MapWorldBoundsId, mapWorldBounds);
        }

        public void SetHeightmapParams(Texture heightmapTexture, float scale)
        {
            heightmap = heightmapTexture;
            heightScale = scale;
            foreach (var m in materials.Values) ApplyHeight(m);
        }

        private void ApplyHeight(Material m)
        {
            if (heightmap != null) m.SetTexture(HeightmapId, heightmap);
            m.SetFloat(HeightScaleId, heightScale);
        }

        /// <summary>
        /// Rebuild the draw list for new meshes and/or new class styles.
        /// </summary>
        public void SetMeshes(IReadOnlyList<Mesh> meshes, IReadOnlyList<int> meshClassIds, IReadOnlyList<string> classNames)
        {
            drawList.Clear();
            if (shader == null) return;

            var ordered = new List<(Mesh mesh, BorderClassStyle style)>();
            for (int i = 0; i < meshes.Count; i++)
            {
                var style = BorderClassStyles.Resolve(classNames[meshClassIds[i]]);
                if (style.Visible)
                    ordered.Add((meshes[i], style));
            }
            ordered.Sort((a, b) => a.style.Order != b.style.Order ? a.style.Order.CompareTo(b.style.Order)
                                                                    : string.CompareOrdinal(a.style.Name, b.style.Name));

            // One queue step per style (a class can have many meshes, e.g. spatial chunks), so the queues
            // stay below other renderers' (highlight outlines) however many meshes there are
            int rank = -1;
            string lastStyle = null;
            for (int i = 0; i < ordered.Count; i++)
            {
                var (mesh, style) = ordered[i];
                var material = GetMaterial(style.Name);
                if (style.Name != lastStyle)
                {
                    rank++;
                    lastStyle = style.Name;
                    ApplyStyle(material, style);
                    material.renderQueue = baseQueue + rank;
                }
                drawList.Add((mesh, material));
            }
        }

        /// <summary>
        /// Draw one mesh in several passes with explicit styles (no class lookup), in list order,
        /// e.g. highlight glow then outline. Null mesh or styles clears the draw list.
        /// </summary>
        public void SetPasses(Mesh mesh, IReadOnlyList<BorderClassStyle> passes)
        {
            drawList.Clear();
            if (shader == null || mesh == null || passes == null) return;

            for (int i = 0; i < passes.Count; i++)
            {
                var style = passes[i];
                if (style == null || !style.Visible) continue;
                var material = GetMaterial(style.Name);
                ApplyStyle(material, style);
                material.renderQueue = baseQueue + i;
                drawList.Add((mesh, material));
            }
        }

        private Material GetMaterial(string className)
        {
            if (!materials.TryGetValue(className, out var m))
            {
                m = new Material(shader) { name = $"BorderMesh ({className})" };
                m.SetVector(MapWorldBoundsId, mapWorldBounds);
                ApplyHeight(m);
                materials[className] = m;
            }
            return m;
        }

        private static void ApplyStyle(Material m, BorderClassStyle s)
        {
            m.SetTexture(BorderTexId, s.Texture != null ? s.Texture : Texture2D.whiteTexture);
            m.SetFloat(UseTextureId, s.Texture != null ? 1f : 0f);
            m.SetFloat(TexAlphaOnlyId, s.TextureAlphaOnly ? 1f : 0f);
            m.SetFloat(SoftnessId, s.Softness);
            m.SetFloat(SrcBlendId, (float)(s.Lighten ? UnityEngine.Rendering.BlendMode.One : UnityEngine.Rendering.BlendMode.SrcAlpha));
            m.SetFloat(DstBlendId, (float)(s.Lighten ? UnityEngine.Rendering.BlendMode.One : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
            m.SetFloat(BlendOpId, (float)(s.Lighten ? UnityEngine.Rendering.BlendOp.Max : UnityEngine.Rendering.BlendOp.Add));
            m.SetFloat(PremultiplyId, s.Lighten ? 1f : 0f);
            // Color source: 0 = explicit color, 1 = country globals, 2 = province globals
            m.SetFloat(ColorSourceId, s.Color.HasValue ? 0f : (s.Name == BorderClassNames.Country ? 1f : 2f));
            m.SetColor(ColorId, s.Color ?? Color.white);
            m.SetFloat(WidthId, s.Width);
            m.SetFloat(WidthPerLevelId, s.WidthPerLevel);
            m.SetFloat(MinScreenWidthId, s.MinScreenWidth);
            m.SetFloat(TileId, s.Tile);
            m.SetVector(FadeId, new Vector4(s.Fade.x, s.Fade.y, 0f, 0f));
        }

        public void RenderBorders()
        {
            foreach (var (mesh, material) in drawList)
            {
                Graphics.DrawMesh(mesh, Matrix4x4.identity, material, 0, null, 0, null,
                    UnityEngine.Rendering.ShadowCastingMode.Off, false);
            }
        }

        public void Dispose()
        {
            foreach (var m in materials.Values)
                if (m != null) Object.Destroy(m);
            materials.Clear();
            drawList.Clear();
        }
    }
}
