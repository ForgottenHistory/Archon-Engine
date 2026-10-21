using System.Collections.Generic;
using UnityEngine;

namespace Map.Rendering
{
    /// <summary>
    /// ENGINE: Single list of map shader parameters — the engine/shader contract.
    ///
    /// ENGINE registers its parameters (MapShaderGlobals.RegisterEngineParameters).
    /// GAME registers its own (e.g. a global used by an ARCHON_POST_COMPOSITE hook)
    /// before the visual style is applied — no ENGINE changes needed.
    ///
    /// Uses: upload global defaults, apply style-provided custom values, and validate
    /// map materials (engine globals shadowed by Properties, missing required properties).
    /// </summary>
    public static class MapShaderParameterRegistry
    {
        private static readonly Dictionary<int, MapShaderParameter> parameters = new Dictionary<int, MapShaderParameter>();
        private static readonly List<MapShaderParameter> ordered = new List<MapShaderParameter>();

        public static IReadOnlyList<MapShaderParameter> All => ordered;

        public static void RegisterGlobal(string name, ShaderValue defaultValue, string owner)
        {
            Register(new MapShaderParameter(name, defaultValue.Type, ShaderParameterScope.Global, defaultValue, false, owner));
        }

        public static void RegisterMaterial(string name, ShaderParameterType type, bool required, string owner)
        {
            Register(new MapShaderParameter(name, type, ShaderParameterScope.Material, default, required, owner));
        }

        /// <summary>
        /// Register or replace a parameter. Re-registering with the same type/scope is a silent update
        /// (safe across domain-reload-free play sessions); a conflicting definition logs a warning.
        /// </summary>
        public static void Register(MapShaderParameter parameter)
        {
            if (parameters.TryGetValue(parameter.Id, out var existing))
            {
                if (existing.Type != parameter.Type || existing.Scope != parameter.Scope)
                {
                    ArchonLogger.LogWarning($"MapShaderParameterRegistry: '{parameter.Name}' redefined by {parameter.Owner} " +
                        $"({parameter.Scope} {parameter.Type}), was {existing.Owner} ({existing.Scope} {existing.Type})", "map_rendering");
                }
                ordered[ordered.IndexOf(existing)] = parameter;
            }
            else
            {
                ordered.Add(parameter);
            }
            parameters[parameter.Id] = parameter;
        }

        public static bool TryGet(string name, out MapShaderParameter parameter)
        {
            return parameters.TryGetValue(Shader.PropertyToID(name), out parameter);
        }

        /// <summary>Upload the default value of every Global parameter.</summary>
        public static void ApplyGlobalDefaults()
        {
            foreach (var p in ordered)
            {
                if (p.Scope == ShaderParameterScope.Global)
                    p.Default.ApplyGlobal(p.Id);
            }
        }

        /// <summary>
        /// Apply a value by name. Registered parameters use their registered scope (type must match);
        /// unregistered names use the given scope as-is.
        /// </summary>
        public static void Apply(string name, ShaderValue value, ShaderParameterScope scope, Material material)
        {
            if (string.IsNullOrEmpty(name)) return;

            if (TryGet(name, out var p))
            {
                if (p.Type != value.Type)
                {
                    ArchonLogger.LogError($"MapShaderParameterRegistry: '{name}' is {p.Type} ({p.Owner}), got {value.Type}. Skipped.", "map_rendering");
                    return;
                }
                scope = p.Scope;
            }

            if (scope == ShaderParameterScope.Global)
            {
                value.ApplyGlobal(Shader.PropertyToID(name));
            }
            else if (material != null)
            {
                if (material.shader.FindPropertyIndex(name) < 0)
                {
                    ArchonLogger.LogWarning($"MapShaderParameterRegistry: material parameter '{name}' is not declared by shader '{material.shader.name}'", "map_rendering");
                }
                value.ApplyMaterial(material, Shader.PropertyToID(name));
            }
        }

        /// <summary>
        /// Check a map material against the contract. Logs one error per problem; returns problem count.
        /// - Global parameter declared in the shader's Properties block (material value shadows the global)
        /// - Required material parameter missing from the shader
        /// </summary>
        public static int ValidateMaterial(Material material)
        {
            if (material == null || material.shader == null) return 0;

            var shader = material.shader;
            int problems = 0;

            foreach (var p in ordered)
            {
                bool declared = shader.FindPropertyIndex(p.Name) >= 0;

                if (p.Scope == ShaderParameterScope.Global && declared)
                {
                    ArchonLogger.LogError($"Shader '{shader.name}' declares '{p.Name}' in its Properties block, but it is a " +
                        $"{p.Owner} shader global. The material value shadows the global, so the value never updates. " +
                        "Remove it from Properties.", "map_rendering");
                    problems++;
                }
                else if (p.Scope == ShaderParameterScope.Material && p.Required && !declared)
                {
                    ArchonLogger.LogError($"Shader '{shader.name}' is missing required material property '{p.Name}' ({p.Owner}). " +
                        "Add it to Properties (and the UnityPerMaterial CBUFFER).", "map_rendering");
                    problems++;
                }
            }

            return problems;
        }
    }
}
