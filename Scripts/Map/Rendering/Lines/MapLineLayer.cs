using System.Collections.Generic;
using Map.Rendering.Border;
using UnityEngine;

namespace Map.Rendering.Lines
{
    /// <summary>One line of a MapLineLayer: a polyline in map pixels (x right, y down) and its line class.</summary>
    public struct MapLine
    {
        public Vector2[] Points;
        /// <summary>A line class of the visual style file's "borders" section (texture, width, tile, color, fade, order).</summary>
        public string ClassName;
    }

    /// <summary>
    /// ENGINE: draws lines the GAME supplies (railways, roads, trade routes, supply lines) on the map, with the mesh
    /// border pipeline: each line a strip draped on the terrain, styled by its line class in the visual style file
    /// (the "borders" section takes any class name). The game owns the geometry and decides when it changes; the
    /// layer rebuilds its meshes on SetLines and restyles when the style file reloads.
    ///
    /// Usage (GAME): add the component once the map exists, Initialize with the engine's texture manager and map plane,
    /// then SetLines whenever the lines change (not every frame: a rebuild costs about 1 ms per 1,000 short lines).
    /// Lines draw after rivers (2890) and before borders (2900+) unless a render queue is given; each line class takes
    /// one queue step above it, in style order. A second layer on a higher queue draws on top (an overlay).
    /// Junction caps are off by default: a textured line (track, road) overlaps at its ends instead of fanning.
    /// </summary>
    public sealed class MapLineLayer : MonoBehaviour
    {
        public const int DefaultQueue = 2893; // rivers 2890, borders 2900+: room for a few classes in between

        BorderMeshGenerator generator;
        BorderMeshRenderer meshRenderer;
        Transform mapPlane;
        int mapWidth, mapHeight;
        int appliedStyleVersion = -1;
        bool junctionCaps;
        readonly List<string> classNames = new List<string>();

        public bool IsInitialized => meshRenderer != null;

        /// <summary>Lines in the current meshes.</summary>
        public int LineCount { get; private set; }

        /// <summary>Set up on the map: its size in pixels, its plane, the heightmap the strips drape on.</summary>
        public bool Initialize(MapTextureManager textureManager, Transform mapPlaneTransform,
                               int renderQueue = DefaultQueue, bool withJunctionCaps = false)
        {
            Dispose();
            if (textureManager == null || mapPlaneTransform == null) return false;
            mapPlane = mapPlaneTransform;
            mapWidth = textureManager.MapWidth;
            mapHeight = textureManager.MapHeight;
            junctionCaps = withJunctionCaps;
            meshRenderer = new BorderMeshRenderer(renderQueue);
            var mr = mapPlane.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                meshRenderer.SetMapWorldBounds(mr.bounds.min, mr.bounds.size);
                float heightScale = mr.sharedMaterial != null && mr.sharedMaterial.HasProperty("_HeightScale")
                    ? mr.sharedMaterial.GetFloat("_HeightScale") : 10f;
                if (textureManager.HeightmapTexture != null)
                    meshRenderer.SetHeightmapParams(textureManager.HeightmapTexture, heightScale);
            }
            return true;
        }

        /// <summary>Replace every line (rebuilds the meshes). Lines with fewer than two points are skipped.</summary>
        public void SetLines(IReadOnlyList<MapLine> lines)
        {
            if (meshRenderer == null) return;
            generator?.Dispose();
            generator = new BorderMeshGenerator(mapWidth, mapHeight, mapPlane);
            classNames.Clear();
            var classIds = new Dictionary<string, int>();
            var arcs = new List<(BorderArc, int)>(lines.Count);
            foreach (var line in lines)
            {
                if (line.Points == null || line.Points.Length < 2 || string.IsNullOrEmpty(line.ClassName)) continue;
                if (!classIds.TryGetValue(line.ClassName, out int id))
                {
                    id = classNames.Count;
                    classIds[line.ClassName] = id;
                    classNames.Add(line.ClassName);
                }
                arcs.Add((new BorderArc { Points = line.Points }, id));
            }
            LineCount = arcs.Count;
            generator.GenerateMeshes(arcs, classNames, log: false, junctionCaps: junctionCaps);
            ApplyStyle();
        }

        void ApplyStyle()
        {
            if (generator == null || meshRenderer == null) return;
            meshRenderer.SetMeshes(generator.Meshes, generator.MeshClassIds, classNames);
            appliedStyleVersion = BorderClassStyles.Version;
        }

        void LateUpdate()
        {
            if (meshRenderer == null || generator == null) return;
            if (appliedStyleVersion != BorderClassStyles.Version) ApplyStyle(); // style file reloaded
            meshRenderer.RenderBorders();
        }

        void Dispose()
        {
            generator?.Dispose();
            meshRenderer?.Dispose();
            generator = null;
            meshRenderer = null;
            LineCount = 0;
        }

        void OnDestroy() => Dispose();
    }
}
