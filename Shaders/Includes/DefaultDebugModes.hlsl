// DefaultDebugModes.hlsl
// ENGINE: Special debug visualization modes (borders, province IDs, heightmap, normals)
// Part of Default visual style shader architecture

#ifndef DEFAULT_DEBUG_MODES_INCLUDED
#define DEFAULT_DEBUG_MODES_INCLUDED

// Debug mode: Border visualization with vector curves
float4 RenderBorderDebugMode(float2 uv)
{
    float2 correctedUV = float2(uv.x, 1.0 - uv.y);
    float borderMask = SAMPLE_TEXTURE2D(_PixelPerfectBorderTexture, sampler_PixelPerfectBorderTexture, correctedUV).r;

    if (borderMask < 0.01)
    {
        return float4(0, 0, 0, 1);
    }

    if (_UseVectorCurves > 0.5)
    {
        float2 mapSize = float2(_MapWidth, _MapHeight);
        float2 mapPos = correctedUV * mapSize;

        int cellX = (int)(mapPos.x / (float)_GridCellSize);
        int cellY = (int)(mapPos.y / (float)_GridCellSize);
        int cellIdx = cellY * _GridWidth + cellX;

        uint2 cellRange = _GridCellRanges[cellIdx];
        uint startIdx = cellRange.x;
        uint count = cellRange.y;

        float minDistance = 999999.0;
        int closestType = 0;

        for (uint i = 0; i < count; i++)
        {
            uint segIdx = _GridSegmentIndices[startIdx + i];
            BezierSegment seg = _BezierSegments[segIdx];
            float dist = DistanceToBezier(mapPos, seg);

            if (dist < minDistance)
            {
                minDistance = dist;
                closestType = seg.borderType;
            }
        }

        if (minDistance <= 1.5)
        {
            if (closestType == 2) return float4(1, 0, 0, 1); // Country border = red
            else if (closestType == 1) return float4(0, 1, 0, 1); // Province border = green
        }
    }

    return float4(borderMask, borderMask, 0.0, 1.0);
}

// Debug mode: Province ID visualization
float4 RenderProvinceIDDebugMode(float2 uv)
{
    float2 correctedUV = float2(uv.x, 1.0 - uv.y);
    float2 provinceID_raw = SAMPLE_TEXTURE2D(_ProvinceIDTexture, sampler_ProvinceIDTexture, correctedUV).rg;
    uint debugID = DecodeProvinceID(provinceID_raw);

    // Visualize ID as RGB color (low bits = R, mid bits = G, high bits = B)
    float r = (debugID % 256) / 255.0;
    float g = ((debugID / 256) % 256) / 255.0;
    float b = ((debugID / 65536) % 256) / 255.0;

    if (debugID == 0)
        return float4(0.1, 0.1, 0.1, 1.0); // Ocean = dark gray
    else
        return float4(r, g, b, 1.0);
}

// Debug mode: Heightmap visualization
float4 RenderHeightmapDebugMode(float2 uv)
{
    float2 correctedUV = float2(uv.x, 1.0 - uv.y);
    float height = SAMPLE_TEXTURE2D(_HeightmapTexture, sampler_HeightmapTexture, correctedUV).r;
    return float4(height, height, height, 1.0);
}

// Debug mode: Normal map visualization
float4 RenderNormalMapDebugMode(float2 uv)
{
    float2 correctedUV = float2(uv.x, 1.0 - uv.y);
    float3 normalRGB = SAMPLE_TEXTURE2D(_NormalMapTexture, sampler_NormalMapTexture, correctedUV).rgb;
    return float4(normalRGB, 1.0);
}

// Check if current map mode is a debug mode, and render if so
// Returns true if debug mode was rendered (output is valid)
bool TryRenderDebugMode(int mapMode, float2 uv, out float4 output)
{
    if (mapMode == 100) // Border debug mode
    {
        output = RenderBorderDebugMode(uv);
        return true;
    }
    else if (mapMode == 101) // Province ID debug
    {
        output = RenderProvinceIDDebugMode(uv);
        return true;
    }
    else if (mapMode == 102) // Heightmap debug
    {
        output = RenderHeightmapDebugMode(uv);
        return true;
    }
    else if (mapMode == 103) // Normal map debug
    {
        output = RenderNormalMapDebugMode(uv);
        return true;
    }

    output = float4(0, 0, 0, 1);
    return false;
}

#endif // DEFAULT_DEBUG_MODES_INCLUDED
