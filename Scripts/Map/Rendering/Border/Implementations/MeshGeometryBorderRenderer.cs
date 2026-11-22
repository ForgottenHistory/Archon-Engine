using System.Collections.Generic;
using UnityEngine;

namespace Map.Rendering.Border
{
    /// <summary>
    /// ENGINE: Mesh-based border renderer.
    /// Builds the border graph (BorderGraph) from the province ID texture once. Each arc gets a
    /// class from the IBorderClassifier (MapRendererRegistry.BorderClassifier); each class is one
    /// mesh, styled by BorderClassStyles (visual style data file, hot reloaded).
    /// Ownership changes only reclassify arcs and rebuild meshes; the graph is static. The meshes are
    /// spatial chunks per class (BorderMeshGenerator.BuildChunked): an ownership change reclassifies only the
    /// changed provinces' arcs and rebuilds only the chunks they touch (UpdateProvinces).
    /// </summary>
    public class MeshGeometryBorderRenderer : BorderRendererBase, IBorderHighlightOutline, IBorderArcSource, IIncrementalBorderRenderer
    {
        public override string RendererId => "MeshGeometry";
        public override string DisplayName => "Mesh Geometry (3D)";
        public override bool RequiresPerFrameUpdate => true;

        private BorderCurveCache curveCache;
        private BorderMeshGenerator meshGenerator;
        private BorderMeshRenderer meshRenderer;
        private BorderSmoothingSettings smoothing = BorderSmoothingSettings.Default;
        private int appliedStyleVersion = -1;

        // Highlight outline: the highlighted province/country's own arcs as one strip mesh,
        // drawn above every border class (queue 2990 > 2900 + class count)
        private BorderMeshGenerator highlightGenerator;
        private BorderMeshRenderer highlightRenderer;
        private Color highlightColor;
        private bool highlightActive;
        private int highlightStyleVersion = -1;
        // Secondary outline (e.g. the state around a selected province), drawn just under the primary
        private BorderMeshGenerator secondaryGenerator;
        private BorderMeshRenderer secondaryRenderer;
        private Color secondaryColor;
        private bool secondaryActive;
        private int secondaryStyleVersion = -1;
        private static readonly string[] HighlightClassNames = { BorderClassNames.Highlight };

        public System.Collections.Generic.IReadOnlyList<BorderArc> Arcs => curveCache != null ? curveCache.Arcs : null;

        protected override void OnInitialize()
        {
            if (context.ProvinceSystem == null || textureManager.ProvinceIDTexture == null)
            {
                ArchonLogger.LogWarning("MeshGeometryBorderRenderer: Missing ProvinceSystem or ProvinceIDTexture", "map_rendering");
                return;
            }

            curveCache = new BorderCurveCache();
            BuildGraph();

            meshGenerator = new BorderMeshGenerator(textureManager.MapWidth, textureManager.MapHeight, context.MapPlaneTransform);
            meshRenderer = new BorderMeshRenderer();
            highlightGenerator = new BorderMeshGenerator(textureManager.MapWidth, textureManager.MapHeight, context.MapPlaneTransform);
            highlightRenderer = new BorderMeshRenderer(2990);
            secondaryGenerator = new BorderMeshGenerator(textureManager.MapWidth, textureManager.MapHeight, context.MapPlaneTransform);
            secondaryRenderer = new BorderMeshRenderer(2970);
            SetupMeshRenderer(meshRenderer);
            SetupMeshRenderer(secondaryRenderer);
            SetupMeshRenderer(highlightRenderer);

            ArchonLogger.Log($"MeshGeometryBorderRenderer: Initialized with {curveCache.BorderCount} border arcs", "map_rendering");
        }

        /// <summary>Read back province IDs and trace the border graph.</summary>
        private void BuildGraph()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int width = textureManager.MapWidth, height = textureManager.MapHeight;
            ushort[] ids = ProvinceIdReadback.Read(textureManager);
            long readbackMs = sw.ElapsedMilliseconds;

            // The graph only depends on the province pixels: reuse the disk cache when they match
            ulong idHash = BorderGraphCache.HashIds(ids);
            string cachePath = BorderGraphCache.GetCachePath();
            long hashMs = sw.ElapsedMilliseconds - readbackMs;
            List<BorderArc> arcs = BorderGraphCache.TryLoad(cachePath, idHash, width, height, smoothing);
            bool cached = arcs != null;
            if (!cached)
            {
                arcs = BorderGraph.Build(ids, width, height, smoothing);
                BorderGraphCache.Save(cachePath, idHash, width, height, smoothing, arcs);
            }
            curveCache.Initialize(arcs);

            int points = 0;
            foreach (var arc in arcs) points += arc.Points.Length;
            ArchonLogger.Log($"MeshGeometryBorderRenderer: Border graph {arcs.Count} arcs, {points} points " +
                             $"(readback {readbackMs}ms, hash {hashMs}ms, {(cached ? "cache load" : "trace+smooth+save")} " +
                             $"{sw.ElapsedMilliseconds - readbackMs - hashMs}ms)", "map_initialization");
        }

        private void SetupMeshRenderer(BorderMeshRenderer renderer)
        {
            var mr = context.MapPlaneTransform != null ? context.MapPlaneTransform.GetComponent<MeshRenderer>() : null;
            if (mr != null)
                renderer.SetMapWorldBounds(mr.bounds.min, mr.bounds.size);
            if (textureManager.HeightmapTexture != null)
                renderer.SetHeightmapParams(textureManager.HeightmapTexture, GetTerrainHeightScale());
        }

        private float GetTerrainHeightScale()
        {
            var mr = context.MapPlaneTransform != null ? context.MapPlaneTransform.GetComponent<MeshRenderer>() : null;
            if (mr != null && mr.sharedMaterial != null && mr.sharedMaterial.HasProperty("_HeightScale"))
                return mr.sharedMaterial.GetFloat("_HeightScale");
            return 10f;
        }

        private void Classify()
        {
            var classifier = MapRendererRegistry.BorderClassifier ?? new DefaultBorderClassifier();
            var provinces = context.ProvinceSystem;
            curveCache.ClassifyAll(arc => classifier.Classify(arc, provinces));

            var counts = curveCache.CountPerClass();
            var parts = new List<string>();
            for (int i = 0; i < counts.Length; i++) parts.Add($"{curveCache.ClassNames[i]} {counts[i]}");
            ArchonLogger.Log($"MeshGeometryBorderRenderer: Classified arcs - {string.Join(", ", parts)}", "map_rendering");
        }

        public override void GenerateBorders(BorderGenerationParams parameters)
        {
            if (!isInitialized || meshGenerator == null || meshRenderer == null)
            {
                ArchonLogger.LogWarning("MeshGeometryBorderRenderer: Not properly initialized", "map_rendering");
                return;
            }

            if (parameters.Mode == BorderMode.None)
                return;

            if (parameters.ForceRegenerate)
                BuildGraph();

            // Full rebuild (load, style reload): reclassify every arc (O(arcs)), then every chunk mesh
            Classify();
            meshGenerator.BuildChunked(curveCache);
            ApplyStyles();
        }

        /// <summary>Ownership changed: reclassify the changed provinces' arcs and rebuild only the chunks
        /// whose arcs changed class.</summary>
        public void UpdateProvinces(ushort[] changedProvinces)
        {
            if (!isInitialized || meshGenerator == null || meshRenderer == null || changedProvinces == null) return;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var classifier = MapRendererRegistry.BorderClassifier ?? new DefaultBorderClassifier();
            var provinces = context.ProvinceSystem;
            var changed = curveCache.Reclassify(changedProvinces, arc => classifier.Classify(arc, provinces));
            if (changed.Count == 0) return;
            meshGenerator.UpdateChunks(curveCache, changed);
            ApplyStyles();
            if (sw.ElapsedMilliseconds > 20)
                ArchonLogger.Log($"MeshGeometryBorderRenderer: {changedProvinces.Length} provinces, {changed.Count} arcs reclassified, " +
                                 $"chunks rebuilt in {sw.ElapsedMilliseconds}ms", "map_rendering");
        }

        private void ApplyStyles()
        {
            meshRenderer.SetMeshes(meshGenerator.Meshes, meshGenerator.MeshClassIds, curveCache.ClassNames);
            appliedStyleVersion = BorderClassStyles.Version;
        }

        public override void OnRenderFrame()
        {
            if (meshRenderer == null) return;
            // Visual style data file reloaded: restyle without touching geometry
            if (appliedStyleVersion != BorderClassStyles.Version && meshGenerator.Meshes.Count > 0)
                ApplyStyles();
            meshRenderer.RenderBorders();

            if (highlightActive)
            {
                if (highlightStyleVersion != BorderClassStyles.Version)
                    ApplyHighlightStyle();
                highlightRenderer.RenderBorders();
            }

            if (secondaryActive)
            {
                if (secondaryStyleVersion != BorderClassStyles.Version)
                    ApplySecondaryStyle();
                secondaryRenderer.RenderBorders();
            }
        }

        #region Highlight outline (IBorderHighlightOutline)

        public void SetHighlightOutlineProvince(ushort provinceID, Color color)
        {
            if (provinceID == 0) { ClearHighlightOutline(); return; }
            BuildHighlight(new[] { provinceID }, p => p == provinceID, color);
        }

        public void SetHighlightOutlineCountry(ushort countryID, Color color)
        {
            var provinces = context.ProvinceSystem;
            if (countryID == 0 || provinces == null) { ClearHighlightOutline(); return; }
            var owned = new Unity.Collections.NativeList<ushort>(256, Unity.Collections.Allocator.Temp);
            provinces.GetCountryProvinces(countryID, owned);
            var members = new HashSet<ushort>();
            for (int i = 0; i < owned.Length; i++) members.Add(owned[i]);
            owned.Dispose();
            BuildHighlight(members, members.Contains, color);
        }

        public void SetSecondaryOutline(IReadOnlyCollection<ushort> provinces, Color color)
        {
            if (provinces == null || provinces.Count == 0) { ClearSecondaryOutline(); return; }
            if (!isInitialized || curveCache == null || secondaryGenerator == null) return;
            var members = provinces as HashSet<ushort> ?? new HashSet<ushort>(provinces);
            secondaryGenerator.GenerateMeshes(OutlineChains(members, members.Contains), HighlightClassNames, log: false);
            secondaryColor = color;
            secondaryActive = secondaryGenerator.Meshes.Count > 0;
            ApplySecondaryStyle();
        }

        public void ClearSecondaryOutline()
        {
            secondaryActive = false;
            secondaryRenderer?.SetPasses(null, null);
        }

        public void ClearHighlightOutline()
        {
            highlightActive = false;
            highlightRenderer?.SetPasses(null, null);
        }

        /// <summary>Outline = every arc with the highlighted set on exactly one side.</summary>
        private void BuildHighlight(IEnumerable<ushort> members, System.Func<ushort, bool> inSet, Color color)
        {
            if (!isInitialized || curveCache == null || highlightGenerator == null) return;
            highlightGenerator.GenerateMeshes(OutlineChains(members, inSet), HighlightClassNames, log: false);
            highlightColor = color;
            highlightActive = highlightGenerator.Meshes.Count > 0;
            ApplyHighlightStyle();
        }

        /// <summary>members: every province in the set (only their arcs can be on its outline); inSet: membership test.</summary>
        private List<(BorderArc, int)> OutlineChains(IEnumerable<ushort> members, System.Func<ushort, bool> inSet)
        {
            // Candidate arcs: those of the members, in map order so the chains come out the same every time
            var candidates = new List<int>();
            var seen = new HashSet<int>();
            foreach (ushort p in members)
                foreach (int a in curveCache.ArcsOf(p))
                    if (seen.Add(a)) candidates.Add(a);
            candidates.Sort();

            var allArcs = curveCache.Arcs;
            var outlineArcs = new List<BorderArc>();
            foreach (int a in candidates)
            {
                var arc = allArcs[a];
                if (inSet(arc.ProvinceA) != inSet(arc.ProvinceB))
                    outlineArcs.Add(arc);
            }

            // Chain into closed loops: no arc ends means no junction caps, which bled spokes
            // out of every corner; corners are joined like any other bend in the strip
            var arcs = new List<(BorderArc, int)>();
            int open = 0;
            foreach (var loop in ChainArcs(outlineArcs))
            {
                arcs.Add((loop, 0));
                if (!loop.Closed) open++;
            }
            // An open chain gets junction caps again; should not happen for an outline
            if (open > 0)
                ArchonLogger.LogWarning($"Highlight outline: {outlineArcs.Count} arcs -> {arcs.Count} chains, {open} open", "map_rendering");

            return arcs;
        }

        /// <summary>Arc endpoints are shared pixel corners; same key as BorderMeshGenerator junctions.</summary>
        private static Vector2Int EndKey(Vector2 p) =>
            new Vector2Int(Mathf.FloorToInt(p.x + 0.25f), Mathf.FloorToInt(p.y + 0.25f));

        /// <summary>
        /// Join arcs that share endpoints into continuous polylines, closed where they return to
        /// their start (the normal case for an outline). Where the outline touches itself at a
        /// point (4 arcs meet), any continuation is fine: every arc is still used exactly once.
        /// </summary>
        private static List<BorderArc> ChainArcs(List<BorderArc> arcs)
        {
            var byEnd = new Dictionary<Vector2Int, List<int>>();
            for (int i = 0; i < arcs.Count; i++)
            {
                var a = arcs[i];
                if (a.Closed || a.Points.Length < 2) continue;
                AddEnd(byEnd, EndKey(a.Points[0]), i);
                AddEnd(byEnd, EndKey(a.Points[a.Points.Length - 1]), i);
            }

            var used = new bool[arcs.Count];
            var result = new List<BorderArc>();
            for (int s = 0; s < arcs.Count; s++)
            {
                if (used[s] || arcs[s].Points.Length < 2) continue;
                used[s] = true;
                if (arcs[s].Closed) { result.Add(arcs[s]); continue; }

                var pts = new List<Vector2>(arcs[s].Points);
                Vector2Int startKey = EndKey(pts[0]);
                while (true)
                {
                    Vector2Int endKey = EndKey(pts[pts.Count - 1]);
                    if (endKey == startKey && pts.Count > 2) break;

                    int next = -1;
                    if (byEnd.TryGetValue(endKey, out var candidates))
                        foreach (int c in candidates)
                            if (!used[c]) { next = c; break; }
                    if (next < 0) break;
                    used[next] = true;

                    // Append without repeating the shared point, reversing the arc if needed
                    var np = arcs[next].Points;
                    if (EndKey(np[0]) == endKey)
                        for (int k = 1; k < np.Length; k++) pts.Add(np[k]);
                    else
                        for (int k = np.Length - 2; k >= 0; k--) pts.Add(np[k]);
                }

                bool closed = pts.Count > 3 && EndKey(pts[pts.Count - 1]) == startKey;
                if (closed) pts[pts.Count - 1] = pts[0]; // closed polylines repeat the first point exactly
                result.Add(new BorderArc { Closed = closed, Points = pts.ToArray() });
            }
            return result;
        }

        private static void AddEnd(Dictionary<Vector2Int, List<int>> byEnd, Vector2Int key, int arc)
        {
            if (!byEnd.TryGetValue(key, out var list))
                byEnd[key] = list = new List<int>(2);
            list.Add(arc);
        }

        /// <summary>
        /// Style from the "highlight" border class (visual style data file, else engine default).
        /// The caller's color tints it (hover vs selection); style alpha (default 1) is the opacity.
        /// </summary>
        private void ApplyHighlightStyle()
        {
            highlightStyleVersion = BorderClassStyles.Version;
            if (!highlightActive) { highlightRenderer.SetPasses(null, null); return; }

            // Glow under the outline; both tinted by the caller's highlight color
            highlightRenderer.SetPasses(highlightGenerator.Meshes[0], new[]
            {
                TintedHighlightPass(BorderClassNames.HighlightGlow, highlightColor),
                TintedHighlightPass(BorderClassNames.Highlight, highlightColor)
            });
        }

        private void ApplySecondaryStyle()
        {
            secondaryStyleVersion = BorderClassStyles.Version;
            if (!secondaryActive) { secondaryRenderer.SetPasses(null, null); return; }
            secondaryRenderer.SetPasses(secondaryGenerator.Meshes[0], new[]
            {
                TintedHighlightPass(BorderClassNames.HighlightGlow, secondaryColor),
                TintedHighlightPass(BorderClassNames.Highlight, secondaryColor)
            });
        }

        private BorderClassStyle TintedHighlightPass(string className, Color highlightColor)
        {
            var data = BorderClassStyles.Resolve(className); // data file or engine default
            Color tint = data.Color ?? Color.white;
            return new BorderClassStyle
            {
                Name = data.Name,
                Visible = data.Visible,
                Texture = data.Texture,
                TextureAlphaOnly = data.TextureAlphaOnly,
                Softness = data.Softness,
                Lighten = data.Lighten,
                Color = new Color(highlightColor.r * tint.r, highlightColor.g * tint.g, highlightColor.b * tint.b, tint.a),
                Width = data.Width,
                MinScreenWidth = data.MinScreenWidth,
                Tile = data.Tile,
                Fade = data.Fade
            };
        }

        #endregion

        public override void Dispose()
        {
            meshGenerator?.Dispose();
            meshRenderer?.Dispose();
            highlightGenerator?.Dispose();
            highlightRenderer?.Dispose();
            secondaryGenerator?.Dispose();
            secondaryRenderer?.Dispose();
            secondaryGenerator = null;
            secondaryRenderer = null;
            secondaryActive = false;
            highlightGenerator = null;
            highlightRenderer = null;
            highlightActive = false;
            curveCache?.Clear();
            curveCache = null;
            meshGenerator = null;
            meshRenderer = null;
        }
    }
}
