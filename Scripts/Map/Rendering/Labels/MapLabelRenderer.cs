using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Core;
using Core.Modding;
using Core.Systems;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TextCore.LowLevel;

namespace Map.Rendering.Labels
{
    /// <summary>
    /// ENGINE: Country name labels on the map (Paradox style: the name spread along the body of the
    /// territory, sized to fit).
    ///
    /// Placement (LabelPlacer) runs on a worker thread from a CPU copy of the province ID map and the
    /// current owners; ownership changes re-place only the affected countries. Text comes from
    /// MapRendererRegistry.LabelSource (GAME can supply names and capitals). Letters are SDF glyph
    /// quads from a runtime TextMeshPro font asset (font file from the "labels" style section), in
    /// one mesh drawn by Archon/MapLabel: draped on the heightmap, scaled up to a minimum screen size
    /// and faded out when zoomed in.
    /// </summary>
    public class MapLabelRenderer : MonoBehaviour
    {
        private const int RenderQueue = 2950; // above border lines (2900+)
        private const int SamplingPointSize = 90;
        private const int AtlasPadding = 9;
        private const int AtlasSize = 2048;
        private const float ChangeSettleSeconds = 0.3f;
        // Changes that keep coming (a world at war: control changes every game hour, at high speed every frame)
        // never settle; place anyway once the oldest pending change has waited this long
        private const float ChangeMaxWaitSeconds = 1.0f;

        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int FaceColorId = Shader.PropertyToID("_FaceColor");
        private static readonly int ShadowColorId = Shader.PropertyToID("_ShadowColor");
        private static readonly int ShadowParamsId = Shader.PropertyToID("_ShadowParams");
        private static readonly int ScreenParamsId = Shader.PropertyToID("_LabelScreen");
        private static readonly int FadeId = Shader.PropertyToID("_LabelFade");
        private static readonly int HeightmapId = Shader.PropertyToID("_HeightmapTexture");
        private static readonly int HeightScaleId = Shader.PropertyToID("_HeightScale");
        private static readonly int MapWorldBoundsId = Shader.PropertyToID("_MapWorldBounds");

        private MapTextureManager textureManager;
        private GameState gameState;
        private LabelPlacer placer;
        private ushort[] owners;
        private int mapWidth, mapHeight;
        private Vector3 worldMin, worldSize;
        private Vector2 worldPerPixel;

        private readonly Dictionary<ushort, List<PlacedLabel>> labels = new Dictionary<ushort, List<PlacedLabel>>();
        private readonly HashSet<ushort> dirtyCountries = new HashSet<ushort>();
        private bool placeAll;
        private float lastChangeTime;
        private float firstChangeTime = -1f; // oldest change not yet placed, -1 = none
        private Task<List<PlacedLabel>> job;
        private HashSet<ushort> jobCountries;

        private TMP_FontAsset font;
        private string loadedFontPath;
        private Material material;
        private Mesh mesh;
        private IDisposable ownershipSubscription;
        private IDisposable controllerSubscription;
        private int appliedStyleVersion = -1;

        public int LabelCount { get; private set; }

        /// <summary>Read the province map and start placing labels. Returns false if the map isn't ready.</summary>
        public bool Initialize(MapTextureManager textures, Transform mapPlane, GameState state)
        {
            Dispose();
            textureManager = textures;
            gameState = state;
            if (textureManager == null || gameState == null || gameState.Provinces == null || gameState.Countries == null)
                return false;

            var shader = Shader.Find("Archon/MapLabel");
            if (shader == null)
            {
                ArchonLogger.LogError("MapLabelRenderer: Archon/MapLabel shader not found", "map_rendering");
                return false;
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            mapWidth = textureManager.MapWidth;
            mapHeight = textureManager.MapHeight;
            ushort[] ids = ProvinceIdReadback.Read(textureManager);
            if (ids == null) return false;
            placer = new LabelPlacer(ids, mapWidth, mapHeight);

            int maxId = 0;
            foreach (ushort id in ids) if (id > maxId) maxId = id;
            owners = new ushort[maxId + 1];
            for (int i = 1; i <= maxId; i++)
                owners[i] = Holder((ushort)i);

            material = new Material(shader) { renderQueue = RenderQueue, name = "MapLabels" };
            var mr = mapPlane != null ? mapPlane.GetComponent<MeshRenderer>() : null;
            if (mr != null)
            {
                worldMin = mr.bounds.min;
                worldSize = mr.bounds.size;
                float heightScale = mr.sharedMaterial != null && mr.sharedMaterial.HasProperty("_HeightScale")
                    ? mr.sharedMaterial.GetFloat("_HeightScale") : 10f;
                material.SetFloat(HeightScaleId, heightScale);
            }
            else
            {
                worldMin = new Vector3(-5f, 0f, -5f);
                worldSize = new Vector3(10f, 0f, 10f);
            }
            worldPerPixel = new Vector2(worldSize.x / mapWidth, worldSize.z / mapHeight);
            material.SetVector(MapWorldBoundsId, new Vector4(worldMin.x, worldMin.z, worldSize.x, worldSize.z));
            if (textureManager.HeightmapTexture != null)
                material.SetTexture(HeightmapId, textureManager.HeightmapTexture);

            mesh = new Mesh { name = "MapLabels", indexFormat = IndexFormat.UInt32 };
            ownershipSubscription = gameState.EventBus?.Subscribe<ProvinceOwnershipChangedEvent>(OnOwnershipChanged);

            controllerSubscription = gameState.EventBus?.Subscribe<ProvinceControllerChangedEvent>(OnControllerChanged);

            ApplyStyle();
            ArchonLogger.Log($"MapLabelRenderer: initialized ({sw.ElapsedMilliseconds}ms readback + setup)", "map_initialization");
            return true;
        }

        private ushort Holder(ushort province)
        {
            ushort owner = SafeOwner(province);
            var source = MapRendererRegistry.LabelSource;
            if (source == null) return owner;
            try { return source.GetLabelHolder(province, owner, gameState); }
            catch { return owner; }
        }

        private void OnControllerChanged(ProvinceControllerChangedEvent e)
        {
            if (owners == null || e.ProvinceId >= owners.Length) return;
            ushort old = owners[e.ProvinceId];
            ushort now = Holder(e.ProvinceId);
            if (old == now) return;
            owners[e.ProvinceId] = now;
            if (old != 0) dirtyCountries.Add(old);
            if (now != 0) dirtyCountries.Add(now);
            MarkChanged();
        }

        private void MarkChanged()
        {
            lastChangeTime = Time.unscaledTime;
            if (firstChangeTime < 0f) firstChangeTime = lastChangeTime;
        }

        private ushort SafeOwner(ushort province)
        {
            try { return gameState.Provinces.GetProvinceOwner(province); }
            catch { return 0; }
        }

        private void OnOwnershipChanged(ProvinceOwnershipChangedEvent e)
        {
            if (owners == null || e.ProvinceId >= owners.Length) return;
            ushort old = owners[e.ProvinceId];
            ushort now = Holder(e.ProvinceId);
            owners[e.ProvinceId] = now;
            if (old != 0) dirtyCountries.Add(old);
            if (now != 0) dirtyCountries.Add(now);
            if (e.OldOwner != 0) dirtyCountries.Add(e.OldOwner);
            if (e.NewOwner != 0) dirtyCountries.Add(e.NewOwner);
            MarkChanged();
        }

        // ------------------------------------------------------------------ style

        private void ApplyStyle()
        {
            appliedStyleVersion = MapLabelStyles.Version;
            var style = MapLabelStyles.Current;
            if (material == null) return;

            string fontPath = ResolveFontPath(style.Font);
            if (font == null || fontPath != loadedFontPath)
            {
                DestroyFont();
                font = LoadFont(fontPath);
                loadedFontPath = fontPath;
            }

            material.SetColor(FaceColorId, style.Color);
            material.SetColor(ShadowColorId, style.ShadowColor);
            float em = font != null ? font.faceInfo.pointSize / (float)AtlasSize : 0f;
            // Shadow = glyph shifted by the offset: sample at uv - offset (atlas v points up, style y down)
            material.SetVector(ShadowParamsId, new Vector4(-style.ShadowOffset.x * em, style.ShadowOffset.y * em,
                Mathf.Clamp01(style.ShadowSoftness), Mathf.Max(style.Sharpness, 0.05f)));
            material.SetVector(ScreenParamsId, new Vector4(Mathf.Max(style.MinScreenHeight, 0f), Mathf.Max(style.MaxScale, 1f), 0, 0));
            material.SetVector(FadeId, new Vector4(style.Fade.x, style.Fade.y, 0, 0));

            placeAll = true; // text, font or placement settings may have changed
        }

        private static string ResolveFontPath(string relative)
        {
            if (string.IsNullOrEmpty(relative) || !DataFileResolver.IsInitialized) return "";
            string path = DataFileResolver.Resolve(relative);
            if (File.Exists(path)) return path;
            ArchonLogger.LogWarning($"MapLabelRenderer: label font '{relative}' not found, using a system font", "map_rendering");
            return "";
        }

        private static TMP_FontAsset LoadFont(string path)
        {
            TMP_FontAsset asset = null;
            try
            {
                if (!string.IsNullOrEmpty(path))
                    asset = TMP_FontAsset.CreateFontAsset(path, 0, SamplingPointSize, AtlasPadding,
                        GlyphRenderMode.SDFAA, AtlasSize, AtlasSize);
                if (asset == null)
                    asset = TMP_FontAsset.CreateFontAsset("Arial", "Bold", SamplingPointSize)
                            ?? TMP_FontAsset.CreateFontAsset("Arial", "Regular", SamplingPointSize);
            }
            catch (Exception e)
            {
                ArchonLogger.LogError($"MapLabelRenderer: font load failed: {e.Message}", "map_rendering");
            }
            if (asset == null)
            {
                ArchonLogger.LogError("MapLabelRenderer: no label font available", "map_rendering");
                return null;
            }
            asset.isMultiAtlasTexturesEnabled = false; // one atlas texture: the mesh uses one material
            asset.name = "MapLabelFont";
            return asset;
        }

        private void DestroyFont()
        {
            if (font == null) return;
            if (font.atlasTextures != null)
                foreach (var t in font.atlasTextures) if (t != null) Destroy(t);
            if (font.material != null) Destroy(font.material);
            Destroy(font);
            font = null;
        }

        // ------------------------------------------------------------------ placement

        private void Update()
        {
            if (placer == null) return;
            if (appliedStyleVersion != MapLabelStyles.Version)
                ApplyStyle();

            if (job != null && job.IsCompleted)
                FinishJob();

            if (job == null && font != null &&
                (placeAll || (dirtyCountries.Count > 0 && (Time.unscaledTime - lastChangeTime >= ChangeSettleSeconds
                                                            || Time.unscaledTime - firstChangeTime >= ChangeMaxWaitSeconds))))
                StartJob();
        }

        private void StartJob()
        {
            var style = MapLabelStyles.Current;
            var countries = new HashSet<ushort>();
            if (placeAll)
            {
                foreach (ushort o in owners) if (o != 0) countries.Add(o);
                foreach (ushort c in labels.Keys) countries.Add(c);
            }
            else
            {
                countries.UnionWith(dirtyCountries);
            }
            placeAll = false;
            dirtyCountries.Clear();
            firstChangeTime = -1f;

            var source = MapRendererRegistry.LabelSource ?? new DefaultMapLabelSource();
            var requests = new List<LabelRequest>(countries.Count);
            if (style.Visible)
            {
                foreach (ushort c in countries)
                {
                    string text = source.GetLabelText(c, gameState.Countries);
                    if (string.IsNullOrWhiteSpace(text)) continue;
                    if (style.Uppercase) text = text.ToUpperInvariant();
                    font.TryAddCharacters(text);
                    requests.Add(new LabelRequest
                    {
                        Country = c,
                        Text = text,
                        CapitalProvince = source.GetCapitalProvince(c, gameState.Countries),
                        WidthRatio = MeasureWidthRatio(text)
                    });
                }
            }

            var ownerSnapshot = (ushort[])owners.Clone();
            var settings = style.Placement;
            var localPlacer = placer;
            jobCountries = countries;
            job = Task.Run(() => localPlacer.Place(ownerSnapshot, requests, settings));
        }

        private void FinishJob()
        {
            var done = job;
            job = null;
            if (done.IsFaulted)
            {
                ArchonLogger.LogError($"MapLabelRenderer: placement failed: {done.Exception?.GetBaseException().Message}", "map_rendering");
                return;
            }
            foreach (ushort c in jobCountries) labels.Remove(c);
            foreach (var label in done.Result)
            {
                if (!labels.TryGetValue(label.Country, out var list))
                    labels[label.Country] = list = new List<PlacedLabel>();
                list.Add(label);
            }
            RebuildMesh();
        }

        /// <summary>Text width / font size, without letter spacing.</summary>
        private float MeasureWidthRatio(string text)
        {
            float total = 0;
            foreach (char ch in text)
                total += Advance(ch);
            return total / Mathf.Max(font.faceInfo.pointSize, 1f);
        }

        private float Advance(char ch)
        {
            if (font.characterLookupTable.TryGetValue(ch, out var character) && character.glyph != null)
                return character.glyph.metrics.horizontalAdvance;
            return font.faceInfo.pointSize * 0.3f;
        }

        // ------------------------------------------------------------------ mesh

        private void RebuildMesh()
        {
            var style = MapLabelStyles.Current;
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var anchors = new List<Vector4>();
            var caps = new List<Vector2>();
            var indices = new List<int>();

            float pointSize = Mathf.Max(font.faceInfo.pointSize, 1f);
            float capLine = font.faceInfo.capLine > 0 ? font.faceInfo.capLine : pointSize * 0.7f;
            float atlasW = font.atlasWidth, atlasH = font.atlasHeight;
            float pad = font.atlasPadding;
            float worldPerMapPx = 0.5f * (worldPerPixel.x + worldPerPixel.y);

            int count = 0;
            foreach (var list in labels.Values)
            foreach (var label in list)
            {
                if (label.Curve == null || label.Curve.Length < 2 || label.Height <= 0) continue;
                var cum = CumulativeLengths(label.Curve);
                float length = cum[cum.Length - 1];
                if (length <= 0) continue;

                string text = label.Text;
                float size = label.Height;
                float natural = 0;
                foreach (char ch in text) natural += Advance(ch);
                natural *= size / pointSize;
                if (natural > length)
                {
                    size *= length / natural; // safety: shrink to fit
                    natural = length;
                }
                float scale = size / pointSize;
                float gap = 0;
                if (text.Length > 1 && natural < style.SpaceBelow * length)
                    gap = Mathf.Min((length - natural) / (text.Length - 1), size * style.MaxGap);
                float total = natural + gap * (text.Length - 1);
                // Growth cap: the scaled label may span its room (whole inside run), not beyond
                float grow = Mathf.Clamp(style.RoomOverflow * label.Room / Mathf.Max(total, 1e-4f), 1f, Mathf.Max(style.MaxScale, 1f));
                var cap = new Vector2(grow, 0f);

                PointAt(label.Curve, cum, length * 0.5f, out var centerPx, out _);
                Vector3 centerWorld = PixelToWorld(centerPx);
                var anchor = new Vector4(centerWorld.x, centerWorld.z, size, worldPerMapPx);

                float s = (length - total) * 0.5f;
                foreach (char ch in text)
                {
                    float adv = Advance(ch) * scale;
                    if (font.characterLookupTable.TryGetValue(ch, out var character) && character.glyph != null &&
                        character.glyph.glyphRect.width > 0 && !char.IsWhiteSpace(ch))
                    {
                        var glyph = character.glyph;
                        var m = glyph.metrics;
                        var rect = glyph.glyphRect;
                        PointAt(label.Curve, cum, s + adv * 0.5f, out var pos, out var tangent);
                        var up = new Vector2(tangent.y, -tangent.x); // pixel y points down

                        // Glyph box in em units around the pen, caps centred on the curve
                        float left = -adv * 0.5f + (m.horizontalBearingX - pad) * scale;
                        float right = left + (m.width + 2 * pad) * scale;
                        float baseline = -capLine * 0.5f * scale;
                        float bottom = baseline + (m.horizontalBearingY - m.height - pad) * scale;
                        float top = baseline + (m.horizontalBearingY + pad) * scale;

                        int v0 = vertices.Count;
                        vertices.Add(PixelToWorld(pos + tangent * left + up * bottom));
                        vertices.Add(PixelToWorld(pos + tangent * left + up * top));
                        vertices.Add(PixelToWorld(pos + tangent * right + up * top));
                        vertices.Add(PixelToWorld(pos + tangent * right + up * bottom));

                        float u0 = (rect.x - pad) / atlasW, u1 = (rect.x + rect.width + pad) / atlasW;
                        float w0 = (rect.y - pad) / atlasH, w1 = (rect.y + rect.height + pad) / atlasH;
                        uvs.Add(new Vector2(u0, w0));
                        uvs.Add(new Vector2(u0, w1));
                        uvs.Add(new Vector2(u1, w1));
                        uvs.Add(new Vector2(u1, w0));
                        for (int k = 0; k < 4; k++) { anchors.Add(anchor); caps.Add(cap); }

                        indices.Add(v0); indices.Add(v0 + 1); indices.Add(v0 + 2);
                        indices.Add(v0); indices.Add(v0 + 2); indices.Add(v0 + 3);
                    }
                    s += adv + gap;
                }
                count++;
            }

            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetUVs(1, anchors);
            mesh.SetUVs(2, caps);
            mesh.SetTriangles(indices, 0, false);
            mesh.bounds = new Bounds(worldMin + worldSize * 0.5f, worldSize + Vector3.up * 1000f);
            material.SetTexture(MainTexId, font.atlasTexture);
            LabelCount = count;
        }

        private static float[] CumulativeLengths(Vector2[] curve)
        {
            var cum = new float[curve.Length];
            for (int i = 1; i < curve.Length; i++)
                cum[i] = cum[i - 1] + Vector2.Distance(curve[i - 1], curve[i]);
            return cum;
        }

        private static void PointAt(Vector2[] curve, float[] cum, float t, out Vector2 point, out Vector2 tangent)
        {
            int i = Array.BinarySearch(cum, t);
            if (i < 0) i = ~i - 1;
            i = Mathf.Clamp(i, 0, curve.Length - 2);
            float seg = cum[i + 1] - cum[i];
            float f = seg > 1e-6f ? (t - cum[i]) / seg : 0f;
            point = Vector2.LerpUnclamped(curve[i], curve[i + 1], f);
            var d = curve[i + 1] - curve[i];
            tangent = d.sqrMagnitude > 1e-12f ? d.normalized : Vector2.right;
        }

        /// <summary>Pixel (x right, y down) to world on the map plane; height is applied in the shader.</summary>
        private Vector3 PixelToWorld(Vector2 pixel)
        {
            return new Vector3(worldMin.x + pixel.x * worldPerPixel.x, 0f,
                worldMin.z + (mapHeight - pixel.y) * worldPerPixel.y);
        }

        // ------------------------------------------------------------------ draw

        private void LateUpdate()
        {
            if (mesh == null || material == null || LabelCount == 0 || !MapLabelStyles.Current.Visible) return;
            Graphics.DrawMesh(mesh, Matrix4x4.identity, material, 0, null, 0, null, ShadowCastingMode.Off, false);
        }

        private void Dispose()
        {
            ownershipSubscription?.Dispose();
            ownershipSubscription = null;
            controllerSubscription?.Dispose();
            controllerSubscription = null;
            job = null;
            labels.Clear();
            dirtyCountries.Clear();
            DestroyFont();
            loadedFontPath = null;
            if (material != null) Destroy(material);
            if (mesh != null) Destroy(mesh);
            material = null;
            mesh = null;
            placer = null;
            owners = null;
            LabelCount = 0;
        }

        private void OnDestroy() => Dispose();
    }
}
