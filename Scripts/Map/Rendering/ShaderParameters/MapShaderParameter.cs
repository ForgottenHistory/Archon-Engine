using UnityEngine;

namespace Map.Rendering
{
    public enum ShaderParameterType
    {
        Float,
        Int,
        Color,
        Vector,
        Texture
    }

    /// <summary>
    /// Global: uploaded with Shader.SetGlobal*, shared by all shaders, must NOT be in a Properties block.
    /// Material: lives on the material (Properties block + UnityPerMaterial CBUFFER).
    /// </summary>
    public enum ShaderParameterScope
    {
        Global,
        Material
    }

    /// <summary>
    /// ENGINE: Typed shader value (float, int, color, vector or texture).
    /// </summary>
    public struct ShaderValue
    {
        public ShaderParameterType Type;
        public Vector4 Vector;   // float = x, int = (int)x, color = rgba, vector = xyzw
        public Texture Texture;

        public static ShaderValue Float(float value) => new ShaderValue { Type = ShaderParameterType.Float, Vector = new Vector4(value, 0, 0, 0) };
        public static ShaderValue Int(int value) => new ShaderValue { Type = ShaderParameterType.Int, Vector = new Vector4(value, 0, 0, 0) };
        public static ShaderValue Color(Color value) => new ShaderValue { Type = ShaderParameterType.Color, Vector = value };
        public static ShaderValue Vec(Vector4 value) => new ShaderValue { Type = ShaderParameterType.Vector, Vector = value };
        public static ShaderValue Tex(Texture value) => new ShaderValue { Type = ShaderParameterType.Texture, Texture = value };

        public void ApplyGlobal(int propertyId)
        {
            switch (Type)
            {
                case ShaderParameterType.Float: Shader.SetGlobalFloat(propertyId, Vector.x); break;
                case ShaderParameterType.Int: Shader.SetGlobalInteger(propertyId, (int)Vector.x); break;
                case ShaderParameterType.Color: Shader.SetGlobalColor(propertyId, (Color)Vector); break;
                case ShaderParameterType.Vector: Shader.SetGlobalVector(propertyId, Vector); break;
                case ShaderParameterType.Texture: if (Texture != null) Shader.SetGlobalTexture(propertyId, Texture); break;
            }
        }

        public void ApplyMaterial(Material material, int propertyId)
        {
            if (material == null) return;
            switch (Type)
            {
                case ShaderParameterType.Float: material.SetFloat(propertyId, Vector.x); break;
                case ShaderParameterType.Int: material.SetInteger(propertyId, (int)Vector.x); break;
                case ShaderParameterType.Color: material.SetColor(propertyId, (Color)Vector); break;
                case ShaderParameterType.Vector: material.SetVector(propertyId, Vector); break;
                case ShaderParameterType.Texture: if (Texture != null) material.SetTexture(propertyId, Texture); break;
            }
        }
    }

    /// <summary>
    /// ENGINE: Definition of one map shader parameter (the engine/shader contract entry).
    /// </summary>
    public sealed class MapShaderParameter
    {
        public string Name { get; }
        public int Id { get; }
        public ShaderParameterType Type { get; }
        public ShaderParameterScope Scope { get; }

        /// <summary>Value uploaded by ApplyGlobalDefaults (Global scope only).</summary>
        public ShaderValue Default { get; }

        /// <summary>Material scope only: every map shader must declare it (validated at style apply).</summary>
        public bool Required { get; }

        /// <summary>Who registered it (e.g. "Archon", "Hegemon"), for error messages.</summary>
        public string Owner { get; }

        public MapShaderParameter(string name, ShaderParameterType type, ShaderParameterScope scope,
            ShaderValue defaultValue, bool required, string owner)
        {
            Name = name;
            Id = Shader.PropertyToID(name);
            Type = type;
            Scope = scope;
            Default = defaultValue;
            Required = required;
            Owner = owner;
        }
    }
}
