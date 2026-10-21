Shader "Archon/DefaultTerrain"
{
    Properties
    {
        // Engine-owned values (textures, map mode, borders, fog, colors, terrain look) are
        // shader globals set by MapShaderGlobals. Do NOT declare them here: a material
        // property shadows the global. Only per-material values below; each must match the
        // UnityPerMaterial CBUFFER in DefaultCommon.hlsl.

        // Tessellation (per-material: describes this shader's geometry)
        _HeightScale ("Height Scale", Range(0, 100)) = 10.0
        _TessellationFactor ("Tessellation Factor", Range(1, 64)) = 16.0
        _TessellationMaxDistance ("Tessellation Max Distance", Float) = 500.0
        _TessellationMinDistance ("Tessellation Min Distance", Float) = 50.0

        // Development gradient (GAME policy, moves to GAME shaders later)
        _DevVeryLow ("Development: Very Low", Color) = (0.545, 0, 0, 1)
        _DevLow ("Development: Low", Color) = (0.863, 0.078, 0.078, 1)
        _DevMedium ("Development: Medium", Color) = (1, 0.549, 0, 1)
        _DevHigh ("Development: High", Color) = (1, 0.843, 0, 1)
        _DevVeryHigh ("Development: Very High", Color) = (1, 1, 0, 1)
        _DevTier1 ("Dev Tier 1 Threshold", Range(0, 1)) = 0.2
        _DevTier2 ("Dev Tier 2 Threshold", Range(0, 1)) = 0.4
        _DevTier3 ("Dev Tier 3 Threshold", Range(0, 1)) = 0.6
        _DevTier4 ("Dev Tier 4 Threshold", Range(0, 1)) = 0.8

        // Material tuning
        _TriPlanarTightenFactor ("Tri-Planar Blend Sharpness", Range(1, 8)) = 4.0
        _HighlightStrength ("Highlight Strength", Range(0, 2)) = 1.0

        // SRP Batcher placeholder
        _MainTex ("Main Texture (unused - for SRP Batcher)", 2D) = "white" {}
    }

    SubShader
    {
        Tags
        {
            "RenderType"="Opaque"
            "RenderPipeline"="UniversalPipeline"
            "Queue"="Geometry"
        }

        LOD 100

        Pass
        {
            Name "ArchonTerrain"
            Tags { "LightMode"="UniversalForward" }

            // Proper depth testing for map rendering
            ZWrite On
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #include "Includes/MapShaderSetup.hlsl"

            // Map mode visual policy (games replace this with their own dispatcher)
            #include "Includes/DefaultMapModes.hlsl"

            // ENGINE pass: entry points, pragmas, fragment composition
            #include_with_pragmas "Includes/MapPassTerrain.hlsl"
            ENDHLSL
        }
    }

    // Fallback to non-tessellated ENGINE shader (no hull/domain support)
    FallBack "Archon/DefaultFlat"

    // Games can create custom ShaderGUI editors if needed
    // CustomEditor "YourGame.Shaders.CustomMapShaderGUI"
}
