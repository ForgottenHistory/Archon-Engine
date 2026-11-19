using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using Core.Loaders;
using Core.Modding;
using Map.Rendering;
using Map.Rendering.Border;
using Map.Rendering.Labels;
using Newtonsoft.Json.Linq;

namespace Archon.Engine.Map
{
    /// <summary>
    /// ENGINE: Data-driven visual style values (moddable in shipped builds).
    ///
    /// A JSON5 file, resolved through DataFileResolver (override folder first, then base data),
    /// applied on top of the VisualStyleConfiguration asset. Keys are shader parameter names
    /// from MapShaderParameterRegistry, so types and scope come from the contract:
    ///
    ///   {
    ///     parameters: {
    ///       _OceanColor: [25, 40, 112],                 // color: [r,g,b(,a)] 0-1 or 0-255, or "#rrggbb"
    ///       _CountryBorderStrength: 0.9,               // float / int
    ///       _OverlayTexture: { texture: "gfx/map/parchment.png", filter: "bilinear", wrap: "repeat" },
    ///       _CoastGlow: { value: 0.5, type: "float", scope: "global" }  // unregistered GAME parameter
    ///     }
    ///   }
    ///
    /// Optional borders section: one entry per border class (names from the border classifier,
    /// engine defaults: province, country, sea, coast). Used by the MeshGeometry border renderer.
    ///
    ///     borders: {
    ///       country: { texture: "gfx/map/borders/country.png", width: 1.5, minScreenWidth: 4,
    ///                  tile: 0.4, color: [1,1,1,1], fade: [0.5, 1], order: 10 },
    ///       coast:   { visible: false },
    ///     }
    ///
    /// Optional rivers section (map/rivers.png, MapRiverRenderer): same fields as a border class,
    /// plus widthPerLevel = extra map pixels per river width level above 0.
    ///
    ///     rivers: { texture: "gfx/map/rivers/river.png", width: 0.5, widthPerLevel: 0.25, tile: 0.15 }
    ///
    /// Optional labels section (MapLabelRenderer, country names). All fields optional:
    ///
    ///     labels: { font: "gfx/fonts/Oswald.ttf", color: [0.91, 0.9, 0.95], shadowColor: [0, 0, 0, 0.75],
    ///               shadowOffset: [0.04, 0.05], shadowSoftness: 0.6, sharpness: 1, uppercase: true,
    ///               minScreenHeight: 11, maxScale: 2.5, roomOverflow: 1.1, fade: [1.5, 2.5], spaceBelow: 0.9, maxGap: 0.8,
    ///               maxHeight: 88, fill: 0.75, clear: 0.55, levelPreference: 0.35, arcBonus: 1.1 }
    ///
    /// Label sizes: font size in map px (maxHeight), minScreenHeight in screen px, shadowOffset and
    /// maxGap as fractions of the font size; fade = [visible below, hidden above] screen px per map px.
    ///
    /// width = strip width in map pixels, minScreenWidth = floor in screen pixels, tile = texture
    /// repeats per map pixel along the border, fade = [hidden, visible] in screen px per map px,
    /// color omitted = engine border color globals. Texture U runs along the border, V across.
    ///
    /// Engine-managed parameters (simulation textures, map mode, border mode, fog toggle) are refused.
    /// Re-read on every style apply, so F5 reload picks up edits.
    /// </summary>
    public static class VisualStyleDataFile
    {
        private const string Subsystem = "map_rendering";

        // Loaded textures by resolved path; replaced (and the old one destroyed) on reload
        private static readonly Dictionary<string, Texture2D> loadedTextures = new Dictionary<string, Texture2D>();

        /// <summary>
        /// Apply the style data file at relativePath (relative to the data directory).
        /// Missing file = no-op. Returns the number of values applied.
        /// </summary>
        public static int Apply(string relativePath, Material material)
        {
            if (string.IsNullOrEmpty(relativePath)) return 0;

            if (!DataFileResolver.IsInitialized)
            {
                ArchonLogger.LogWarning($"VisualStyleDataFile: DataFileResolver not initialized, skipping '{relativePath}'", Subsystem);
                return 0;
            }

            string path = DataFileResolver.Resolve(relativePath);
            if (!File.Exists(path)) return 0;

            JObject root;
            try
            {
                root = Json5Loader.LoadJson5File(path);
            }
            catch (System.Exception e)
            {
                ArchonLogger.LogError($"VisualStyleDataFile: {e.Message}", Subsystem);
                return 0;
            }

            string source = $"Style file '{relativePath}'";
            ApplyBorders(root["borders"] as JObject, root["rivers"] as JObject, source);
            MapLabelStyles.Set(ParseLabels(root["labels"] as JObject, source));

            if (!(root["parameters"] is JObject parameters)) return 0;

            int applied = 0;

            foreach (var property in parameters.Properties())
            {
                MapShaderParameterRegistry.TryGet(property.Name, out var registered);

                if (!TryParseEntry(property.Value, registered, out var value, out var scope, out string error))
                {
                    ArchonLogger.LogError($"{source}: '{property.Name}': {error}. Skipped.", Subsystem);
                    continue;
                }

                if (MapShaderParameterRegistry.Apply(property.Name, value, scope, material, source))
                    applied++;
            }

            ArchonLogger.Log($"VisualStyleDataFile: Applied {applied} value(s) from {path}", Subsystem);
            return applied;
        }

        /// <summary>
        /// Both places the file can come from (override folder, base data). Build once per style apply,
        /// then poll with GetChangeStamp so the hot-reload check doesn't build strings every time.
        /// </summary>
        public static void GetCandidatePaths(string relativePath, out string overridePath, out string basePath)
        {
            overridePath = null;
            basePath = null;
            if (string.IsNullOrEmpty(relativePath) || !DataFileResolver.IsInitialized) return;
            overridePath = Path.Combine(DataFileResolver.OverrideDirectory, relativePath);
            basePath = Path.Combine(DataFileResolver.BaseDirectory, relativePath);
        }

        /// <summary>
        /// Change stamp for hot reload. Changes when the active file is edited, created or deleted,
        /// or when an override copy appears/disappears. 0 if neither exists.
        /// </summary>
        public static long GetChangeStamp(string overridePath, string basePath)
        {
            if (overridePath != null && File.Exists(overridePath))
                return File.GetLastWriteTimeUtc(overridePath).Ticks * 2 + 1; // odd = override active
            if (basePath != null && File.Exists(basePath))
                return File.GetLastWriteTimeUtc(basePath).Ticks * 2;
            return 0;
        }

        // ===== Border classes =====

        private static void ApplyBorders(JObject borders, JObject rivers, string source)
        {
            var classes = new List<BorderClassStyle>();
            // Rivers use the same strip styling, as the line class "river"
            if (rivers != null)
                classes.Add(ParseBorderClass("river", rivers, source));
            if (borders != null)
            {
                foreach (var property in borders.Properties())
                {
                    if (!(property.Value is JObject entry))
                    {
                        ArchonLogger.LogError($"{source}: borders.{property.Name}: expected an object. Skipped.", Subsystem);
                        continue;
                    }
                    classes.Add(ParseBorderClass(property.Name, entry, source));
                }
            }
            BorderClassStyles.Set(classes);
        }

        private static BorderClassStyle ParseBorderClass(string name, JObject e, string source)
        {
            var style = BorderClassStyles.Resolve(name); // engine defaults for anything not given
            style = new BorderClassStyle
            {
                Name = name,
                Width = style.Width,
                MinScreenWidth = style.MinScreenWidth,
                Order = style.Order
            };

            if (e["visible"] != null) style.Visible = (bool)e["visible"];
            if (IsNumber(e["width"])) style.Width = (float)e["width"];
            if (IsNumber(e["widthPerLevel"])) style.WidthPerLevel = (float)e["widthPerLevel"];
            if (IsNumber(e["minScreenWidth"])) style.MinScreenWidth = (float)e["minScreenWidth"];
            if (IsNumber(e["tile"])) style.Tile = (float)e["tile"];
            if (IsNumber(e["order"])) style.Order = (int)e["order"];

            if (e["color"] != null)
            {
                if (TryParseColor(e["color"], out var color)) style.Color = color;
                else ArchonLogger.LogError($"{source}: borders.{name}.color: invalid color", Subsystem);
            }

            if (e["fade"] != null)
            {
                if (TryParseFloats(e["fade"], 2, 2, out var fade)) style.Fade = new Vector2(fade[0], fade[1]);
                else ArchonLogger.LogError($"{source}: borders.{name}.fade: expected [hidden, visible]", Subsystem);
            }

            if (e["texture"] != null)
            {
                // Border textures: trilinear, repeat along (U), clamp across (V), slightly sharper mips
                var options = new JObject { ["texture"] = e["texture"], ["filter"] = "trilinear", ["wrap"] = "repeat" };
                var texture = LoadTexture(options, out string error);
                if (texture != null)
                {
                    texture.wrapModeU = TextureWrapMode.Repeat;
                    texture.wrapModeV = TextureWrapMode.Clamp;
                    texture.mipMapBias = -0.5f;
                    texture.anisoLevel = 4;
                    style.Texture = texture;
                }
                else
                {
                    ArchonLogger.LogError($"{source}: borders.{name}.texture: {error}", Subsystem);
                }
            }

            return style;
        }

        // ===== Labels =====

        private static MapLabelStyle ParseLabels(JObject e, string source)
        {
            var style = new MapLabelStyle();
            if (e == null) return style;
            var placement = style.Placement;

            if (e["visible"] != null) style.Visible = (bool)e["visible"];
            if (e["uppercase"] != null) style.Uppercase = (bool)e["uppercase"];
            if (e["font"] != null) style.Font = (string)e["font"];

            if (e["color"] != null)
            {
                if (TryParseColor(e["color"], out var c)) style.Color = c;
                else ArchonLogger.LogError($"{source}: labels.color: invalid color", Subsystem);
            }
            if (e["shadowColor"] != null)
            {
                if (TryParseColor(e["shadowColor"], out var c)) style.ShadowColor = c;
                else ArchonLogger.LogError($"{source}: labels.shadowColor: invalid color", Subsystem);
            }
            if (e["shadowOffset"] != null)
            {
                if (TryParseFloats(e["shadowOffset"], 2, 2, out var v)) style.ShadowOffset = new Vector2(v[0], v[1]);
                else ArchonLogger.LogError($"{source}: labels.shadowOffset: expected [x, y]", Subsystem);
            }
            if (e["fade"] != null)
            {
                if (TryParseFloats(e["fade"], 2, 2, out var v)) style.Fade = new Vector2(v[0], v[1]);
                else ArchonLogger.LogError($"{source}: labels.fade: expected [visible below, hidden above]", Subsystem);
            }

            if (IsNumber(e["shadowSoftness"])) style.ShadowSoftness = (float)e["shadowSoftness"];
            if (IsNumber(e["sharpness"])) style.Sharpness = (float)e["sharpness"];
            if (IsNumber(e["minScreenHeight"])) style.MinScreenHeight = (float)e["minScreenHeight"];
            if (IsNumber(e["maxScale"])) style.MaxScale = (float)e["maxScale"];
            if (IsNumber(e["roomOverflow"])) style.RoomOverflow = (float)e["roomOverflow"];
            if (IsNumber(e["spaceBelow"])) style.SpaceBelow = (float)e["spaceBelow"];
            if (IsNumber(e["maxGap"])) style.MaxGap = (float)e["maxGap"];

            if (IsNumber(e["maxHeight"])) placement.MaxHeight = (float)e["maxHeight"];
            if (IsNumber(e["fill"])) placement.Fill = (float)e["fill"];
            if (IsNumber(e["clear"])) placement.Clear = (float)e["clear"];
            if (IsNumber(e["levelPreference"])) placement.LevelPreference = (float)e["levelPreference"];
            if (IsNumber(e["arcBonus"])) placement.ArcBonus = (float)e["arcBonus"];
            if (IsNumber(e["minArea"])) placement.MinArea = (int)e["minArea"];
            if (IsNumber(e["detached"])) placement.Detached = (float)e["detached"];
            return style;
        }

        // ===== Parsing =====

        private static bool TryParseEntry(JToken token, MapShaderParameter registered,
            out ShaderValue value, out ShaderParameterScope scope, out string error)
        {
            value = default;
            error = null;
            scope = registered?.Scope ?? ShaderParameterScope.Global;
            ShaderParameterType? type = registered?.Type;

            // Long form: { value: ..., type: "...", scope: "..." } or { texture: "...", ... }
            if (token is JObject obj && obj["texture"] == null)
            {
                if (obj["scope"] != null && registered == null)
                    scope = string.Equals((string)obj["scope"], "material", System.StringComparison.OrdinalIgnoreCase)
                        ? ShaderParameterScope.Material : ShaderParameterScope.Global;

                if (obj["type"] != null && registered == null)
                {
                    if (!System.Enum.TryParse((string)obj["type"], true, out ShaderParameterType parsedType))
                    {
                        error = $"unknown type '{obj["type"]}'";
                        return false;
                    }
                    type = parsedType;
                }

                token = obj["value"];
                if (token == null)
                {
                    error = "missing 'value'";
                    return false;
                }
            }

            if (type == null) type = InferType(token);
            if (type == null)
            {
                error = "cannot infer type; use { value, type }";
                return false;
            }

            return TryParseValue(token, type.Value, out value, out error);
        }

        private static ShaderParameterType? InferType(JToken token)
        {
            switch (token.Type)
            {
                case JTokenType.Float:
                case JTokenType.Integer: return ShaderParameterType.Float;
                case JTokenType.Array: return ShaderParameterType.Color;
                case JTokenType.String: return ((string)token).StartsWith("#") ? ShaderParameterType.Color : (ShaderParameterType?)null;
                case JTokenType.Object: return ShaderParameterType.Texture;
                default: return null;
            }
        }

        private static bool TryParseValue(JToken token, ShaderParameterType type, out ShaderValue value, out string error)
        {
            value = default;
            error = null;

            switch (type)
            {
                case ShaderParameterType.Float:
                    if (!IsNumber(token)) { error = "expected a number"; return false; }
                    value = ShaderValue.Float((float)token);
                    return true;

                case ShaderParameterType.Int:
                    if (!IsNumber(token)) { error = "expected an integer"; return false; }
                    value = ShaderValue.Int((int)token);
                    return true;

                case ShaderParameterType.Color:
                    if (!TryParseColor(token, out var color)) { error = "expected [r,g,b(,a)] or \"#rrggbb\""; return false; }
                    value = ShaderValue.Color(color);
                    return true;

                case ShaderParameterType.Vector:
                    if (!TryParseFloats(token, 2, 4, out var f)) { error = "expected [x,y(,z,w)]"; return false; }
                    value = ShaderValue.Vec(new Vector4(f[0], f[1], f.Length > 2 ? f[2] : 0, f.Length > 3 ? f[3] : 0));
                    return true;

                case ShaderParameterType.Texture:
                    var texture = LoadTexture(token, out error);
                    if (texture == null) return false;
                    value = ShaderValue.Tex(texture);
                    return true;
            }

            error = $"unsupported type {type}";
            return false;
        }

        private static bool IsNumber(JToken t) => t != null && (t.Type == JTokenType.Float || t.Type == JTokenType.Integer);

        private static bool TryParseFloats(JToken token, int min, int max, out float[] values)
        {
            values = null;
            if (!(token is JArray arr) || arr.Count < min || arr.Count > max) return false;
            values = new float[arr.Count];
            for (int i = 0; i < arr.Count; i++)
            {
                if (!IsNumber(arr[i])) return false;
                values[i] = (float)arr[i];
            }
            return true;
        }

        private static bool TryParseColor(JToken token, out Color color)
        {
            color = Color.white;

            if (token.Type == JTokenType.String)
                return ColorUtility.TryParseHtmlString((string)token, out color);

            if (!TryParseFloats(token, 3, 4, out var c)) return false;

            // Same convention as other data files: any component > 1 means 0-255
            bool bytes = false;
            foreach (var v in c) if (v > 1f) bytes = true;
            float scale = bytes ? 1f / 255f : 1f;

            color = new Color(c[0] * scale, c[1] * scale, c[2] * scale, c.Length > 3 ? c[3] * scale : 1f);
            return true;
        }

        /// <summary>
        /// Texture entry: "path.png" or { texture: "path.png", filter: "point|bilinear|trilinear",
        /// wrap: "repeat|clamp|mirror", linear: false, mipmaps: true }. Path is relative to the data directory.
        /// </summary>
        private static Texture2D LoadTexture(JToken token, out string error)
        {
            error = null;
            JObject options = token as JObject;
            string relative = options != null ? (string)options["texture"] : (token.Type == JTokenType.String ? (string)token : null);

            if (string.IsNullOrEmpty(relative)) { error = "expected a texture path"; return null; }

            string path = DataFileResolver.Resolve(relative);
            if (!File.Exists(path)) { error = $"texture not found: {path}"; return null; }

            bool linear = options != null && (bool?)options["linear"] == true;
            bool mipmaps = options == null || (bool?)options["mipmaps"] != false;

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipmaps, linear);
            if (!texture.LoadImage(File.ReadAllBytes(path)))
            {
                Object.Destroy(texture);
                error = $"could not decode image: {path}";
                return null;
            }

            texture.name = Path.GetFileName(path);
            texture.filterMode = ParseFilter(options?["filter"]?.ToString());
            texture.wrapMode = ParseWrap(options?["wrap"]?.ToString());

            if (loadedTextures.TryGetValue(path, out var previous) && previous != null)
                Object.Destroy(previous);
            loadedTextures[path] = texture;

            return texture;
        }

        private static FilterMode ParseFilter(string s)
        {
            switch (s?.ToLowerInvariant())
            {
                case "point": return FilterMode.Point;
                case "trilinear": return FilterMode.Trilinear;
                default: return FilterMode.Bilinear;
            }
        }

        private static TextureWrapMode ParseWrap(string s)
        {
            switch (s?.ToLowerInvariant())
            {
                case "clamp": return TextureWrapMode.Clamp;
                case "mirror": return TextureWrapMode.Mirror;
                default: return TextureWrapMode.Repeat;
            }
        }

        // ===== Export =====

        /// <summary>
        /// Write every style-settable parameter's current value to the override folder as a starting
        /// template for modders. Textures are listed as comments. Returns the written path.
        /// </summary>
        public static string Export(string relativePath, Material material)
        {
            var ci = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  // Visual style values (exported). Delete lines you don't want to override.");
            sb.AppendLine("  // Keys are map shader parameter names; see MapShaderParameterRegistry.");
            sb.AppendLine("  parameters: {");

            foreach (var p in MapShaderParameterRegistry.All)
            {
                if (!p.StyleSettable) continue;
                bool isGlobal = p.Scope == ShaderParameterScope.Global;
                if (!isGlobal && (material == null || !material.HasProperty(p.Id))) continue;

                switch (p.Type)
                {
                    case ShaderParameterType.Float:
                        float f = isGlobal ? Shader.GetGlobalFloat(p.Id) : material.GetFloat(p.Id);
                        sb.AppendLine($"    {p.Name}: {f.ToString("0.####", ci)},");
                        break;
                    case ShaderParameterType.Int:
                        int i = isGlobal ? Shader.GetGlobalInteger(p.Id) : material.GetInteger(p.Id);
                        sb.AppendLine($"    {p.Name}: {{ value: {i}, type: \"int\" }},");
                        break;
                    case ShaderParameterType.Color:
                        Color c = isGlobal ? Shader.GetGlobalColor(p.Id) : material.GetColor(p.Id);
                        sb.AppendLine($"    {p.Name}: [{c.r.ToString("0.###", ci)}, {c.g.ToString("0.###", ci)}, {c.b.ToString("0.###", ci)}, {c.a.ToString("0.###", ci)}],");
                        break;
                    case ShaderParameterType.Vector:
                        Vector4 v = isGlobal ? Shader.GetGlobalVector(p.Id) : material.GetVector(p.Id);
                        sb.AppendLine($"    {p.Name}: {{ value: [{v.x.ToString(ci)}, {v.y.ToString(ci)}, {v.z.ToString(ci)}, {v.w.ToString(ci)}], type: \"vector\" }},");
                        break;
                    case ShaderParameterType.Texture:
                        sb.AppendLine($"    // {p.Name}: {{ texture: \"gfx/map/your_texture.png\", filter: \"bilinear\", wrap: \"repeat\" }},");
                        break;
                }
            }

            sb.AppendLine("  }");
            sb.AppendLine("}");

            string path = DataFileResolver.GetWritePath(relativePath);
            File.WriteAllText(path, sb.ToString());
            ArchonLogger.Log($"VisualStyleDataFile: Exported style values to {path}", Subsystem);
            return path;
        }
    }
}
