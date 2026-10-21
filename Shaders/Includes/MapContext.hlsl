// MapContext.hlsl
// ENGINE: Per-pixel inputs passed to map fragment stages (see MapFragment.hlsl).
// Declared before GAME code so GAME stage overrides can use it.
// New fields may be added; stage signatures stay the same.

#ifndef MAP_CONTEXT_INCLUDED
#define MAP_CONTEXT_INCLUDED

struct ArchonMapContext
{
    float2 uv;          // Map UV (0-1)
    float3 positionWS;  // World position (displaced on terrain pass)
    uint provinceID;    // 0 = ocean / no province
    int mapMode;        // Current _MapMode
};

#endif // MAP_CONTEXT_INCLUDED
