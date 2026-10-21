using UnityEngine;

namespace Map.Rendering
{
    /// <summary>
    /// ENGINE: One custom shader value carried by a visual style (VisualStyleConfiguration.customShaderParameters).
    /// Lets GAME feed its own shader parameters (e.g. a value read by an ARCHON_POST_COMPOSITE hook)
    /// without ENGINE code. Registered names use their registered scope; others use the scope set here.
    /// </summary>
    [System.Serializable]
    public class ShaderParameterOverride
    {
        [Tooltip("Shader property name, e.g. _CoastGlowStrength")]
        public string name = "";

        public ShaderParameterType type = ShaderParameterType.Float;

        [Tooltip("Used only if the name is not registered in MapShaderParameterRegistry")]
        public ShaderParameterScope scope = ShaderParameterScope.Global;

        public float floatValue;
        public int intValue;
        public Color colorValue = Color.white;
        public Vector4 vectorValue;
        public Texture textureValue;

        public ShaderValue ToValue()
        {
            switch (type)
            {
                case ShaderParameterType.Int: return ShaderValue.Int(intValue);
                case ShaderParameterType.Color: return ShaderValue.Color(colorValue);
                case ShaderParameterType.Vector: return ShaderValue.Vec(vectorValue);
                case ShaderParameterType.Texture: return ShaderValue.Tex(textureValue);
                default: return ShaderValue.Float(floatValue);
            }
        }
    }
}
