// MapShaderSetup.hlsl
// ENGINE: First include of every map shader. URP libraries + engine globals and material block.
//
// Map shader layout (see MapPassFlat.hlsl / MapPassTerrain.hlsl):
//   #include "<engine>/Includes/MapShaderSetup.hlsl"            // this file
//   #include "<your map mode dispatcher>.hlsl"                   // defines RenderMapMode()
//   #include_with_pragmas "<engine>/Includes/MapPassTerrain.hlsl" // or MapPassFlat.hlsl
//
// GAME per-material fields: #define ARCHON_GAME_MATERIAL_PROPERTIES before this include.

#ifndef MAP_SHADER_SETUP_INCLUDED
#define MAP_SHADER_SETUP_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

#include "DefaultCommon.hlsl"
#include "MapContext.hlsl"

#endif // MAP_SHADER_SETUP_INCLUDED
