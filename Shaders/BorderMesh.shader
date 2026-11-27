Shader "Archon/BorderMesh"
{
    // One material per border class (see BorderMeshRenderer / BorderClassStyle).
    // Vertices sit on the border center line; the normal is the world offset of one map pixel
    // towards that side of the strip. Width is expanded here, so it can depend on zoom.
    Properties
    {
        _BorderTex ("Border Texture (U along, V across)", 2D) = "white" {}
        _BorderUseTexture ("Use Texture", Float) = 0
        _BorderTexAlphaOnly ("Texture Alpha Only (shape from texture, color from Color)", Float) = 0
        _BorderSoftness ("Softness (solid strips: 0 crisp edge, 1 soft glow falloff)", Float) = 0
        // Blend: normal = SrcAlpha/OneMinusSrcAlpha/Add, premultiply 0.
        // Lighten = One/One/Max, premultiply 1: overlaps take the brighter value instead of adding
        // up (wide soft strips fold at tight corners; alpha blending showed the folds as spokes).
        [Enum(UnityEngine.Rendering.BlendMode)] _BorderSrcBlend ("Src Blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _BorderDstBlend ("Dst Blend", Float) = 10
        [Enum(UnityEngine.Rendering.BlendOp)] _BorderBlendOp ("Blend Op", Float) = 0
        _BorderPremultiply ("Premultiply Output", Float) = 0
        _BorderColor ("Color", Color) = (1, 1, 1, 1)
        _BorderColorSource ("Color Source (0 color, 1 country globals, 2 province globals)", Float) = 2
        _BorderWidth ("Width (map px)", Float) = 0.6
        _BorderWidthPerLevel ("Extra Width per Level (map px)", Float) = 0
        _BorderMinScreenWidth ("Min Width (screen px)", Float) = 1
        _BorderTile ("Texture Repeats per Map Pixel", Float) = 0.4
        _BorderScroll ("Texture Repeats per Second along the Line", Float) = 0
        _BorderFade ("Fade (screen px per map px: hidden at x, visible at y)", Vector) = (0, 0, 0, 0)
        _HeightmapTexture ("Heightmap", 2D) = "gray" {}
        _HeightScale ("Height Scale", Float) = 10.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent-100"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "BorderMesh"

            Blend [_BorderSrcBlend] [_BorderDstBlend]
            BlendOp [_BorderBlendOp]
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex ArchonBorderMeshVert
            #pragma fragment ArchonBorderMeshFrag

            #include "Includes/BorderMeshCore.hlsl"
            ENDHLSL
        }
    }
}
