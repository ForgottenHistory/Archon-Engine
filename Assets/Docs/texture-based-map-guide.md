# High-Performance Texture-Based Map System Implementation Guide (URP)

## System Overview
Build a Paradox-style map renderer that handles 10,000+ provinces at 200+ FPS using GPU-based texture rendering in Universal Render Pipeline.
The entire map is rendered on a single quad mesh with all logic handled by URP shaders.

## Phase 1: Foundation Setup

### Task 1.1: URP Project Configuration
- [x] Configure URP Asset: Assets > Create > Rendering > URP Asset (Forward Renderer)
- [x] Enable SRP Batcher in URP Asset settings
- [x] Set Rendering Path to Forward+ for better performance
- [x] Disable unnecessary URP features (HDR, MSAA, Screen Space Shadows)
- [x] Install Burst Compiler package
- [x] Install Mathematics package
- [x] Install Shader Graph package (optional, for prototyping)
- [x] Create folder structure: Shaders/, ComputeShaders/, Data/, RenderFeatures/

### Task 1.2: Map Quad Setup
- [x] Create single GameObject called "MapRenderer"
- [x] Add MeshFilter and MeshRenderer components
- [x] Generate a two-triangle quad mesh covering map dimensions
- [x] Set quad pivot point to bottom-left corner
- [x] Ensure UV coordinates map 0-1 across entire quad
- [x] Disable shadow casting and receiving on MeshRenderer
- [x] Set MeshRenderer's Lighting > Cast Shadows to Off
- [x] Enable SRP Batcher compatibility by using single material

### Task 1.3: Texture Infrastructure
- [x] Create Texture2D for province IDs (R16G16 format, point filtering)
- [x] Create Texture2D for province owners (R16 format)
- [x] Create Texture2D for province colors (RGBA32)
- [x] Create RenderTexture for borders (R8 format)
- [x] Create RenderTexture for selection highlights
- [x] Set all map textures to Clamp mode (no wrapping)
- [x] Disable mipmaps on all map textures
- [x] Ensure textures are not compressed

### Task 1.4: Province Data Structure
- [x] Create ProvinceData struct with ID, owner, color fields
- [x] Allocate array for 10,000 provinces
- [x] Create Color32 to Province ID lookup dictionary
- [x] Create Province ID to array index mapping
- [x] Implement fast hash function for color lookups
- [x] Store province center points for label placement

## Phase 2: Province Bitmap Processing

### Task 2.1: Bitmap Loading
- [x] Load provinces.bmp as raw Color32 array
- [x] Validate bitmap dimensions match expected size
- [x] Count unique colors (total provinces)
- [x] Verify province count is under 65,535 (16-bit limit)
- [x] Create error texture for invalid/missing province IDs
- [x] Handle edge case of ocean/wasteland provinces

### Task 2.2: Province ID Encoding
- [x] Convert each unique RGB color to sequential province ID
- [x] Pack province IDs into R16G16 texture format
- [x] Store ID in R channel (0-255) and G channel (256-65535)
- [x] Create reverse lookup table (ID to Color32)
- [x] Validate no ID collisions occurred
- [x] Reserve ID 0 for "no province" / ocean

### Task 2.3: Province Neighbor Detection
- [x] Implement scanline neighbor detection (horizontal pass)
- [x] Implement vertical pass for neighbor detection
- [x] Store neighbor relationships in packed array
- [x] Remove duplicate neighbor pairs
- [x] Calculate province bounding boxes during scan
- [x] Flag coastal provinces (neighbors with ocean)

### Task 2.4: Province Metadata Generation
- [x] Calculate province pixel count (size)
- [x] Find province center of mass
- [x] Determine if province has multiple disconnected parts
- [x] Generate province convex hull for labels
- [x] Store bounding box for frustum culling
- [x] Mark impassable provinces (mountains, lakes)

## Phase 3: GPU Shader System (URP)

### Task 3.1: Main URP Map Shader
- [ ] Create new Unlit URP Shader (Assets > Create > Shader > Universal Render Pipeline > Unlit Shader)
- [ ] Convert to HLSLPROGRAM/ENDHLSL blocks
- [ ] Include Core.hlsl: `#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"`
- [ ] Use TEXTURE2D(_MainTex) and SAMPLER(sampler_MainTex) macros
- [ ] Implement province ID decoding in fragment shader
- [ ] Add province color lookup using SAMPLE_TEXTURE2D
- [ ] Use sampler_PointClamp for pixel-perfect provinces
- [ ] Add multi-compile pragmas for shader variants: `#pragma multi_compile _ _MAP_MODE_POLITICAL _MAP_MODE_TERRAIN`
- [ ] Ensure SRP Batcher compatibility with CBUFFER blocks

### Task 3.2: Border Generation Compute Shader
- [ ] Create compute shader for border detection (works identically in URP)
- [ ] Implement 4-way neighbor checking per pixel
- [ ] Write border detection to R8 RenderTexture
- [ ] Add border thickness parameter (1-5 pixels)
- [ ] Implement country vs province border differentiation
- [ ] Add diagonal checking for smoother borders
- [ ] Use RWTexture2D for output in compute shader
- [ ] Dispatch compute shader before main rendering

### Task 3.3: Selection & Highlighting (URP)
- [ ] Implement mouse position to UV conversion
- [ ] Use AsyncGPUReadback for province ID at cursor
- [ ] Add selection outline compute shader
- [ ] Implement province hover highlighting with URP shader properties
- [ ] Add multi-province selection support
- [ ] Create animated selection effect using Time node in Shader Graph or _Time in HLSL

### Task 3.4: Map Modes (URP Shader Variants)
- [ ] Implement political map mode using shader_feature_local
- [ ] Add terrain map mode shader variant
- [ ] Create religion/culture map mode
- [ ] Implement development/economy view
- [ ] Add diplomatic map mode (relations)
- [ ] Create fog of war using URP Render Feature
- [ ] Use Material Property Blocks for per-province data

## Phase 4: Performance Optimization

### Task 4.1: Texture Optimization
- [ ] Pack multiple data into single textures (owner + controller in RG)
- [ ] Use texture arrays for temporal data (monthly changes)
- [ ] Implement texture streaming for huge maps
- [ ] Reduce province color texture to 256x1 (palette)
- [ ] Use 16-bit indices where possible
- [ ] Compress static data textures

### Task 4.2: GPU Optimization (URP-Specific)
- [ ] Batch all map rendering into single draw call
- [ ] Leverage SRP Batcher by using CBUFFER blocks correctly
- [ ] Use GPU instancing for map icons/units with URP instancing macros
- [ ] Implement Z-order sorting on GPU
- [ ] Configure URP Renderer Features for custom passes
- [ ] Use shader_feature_local for reduced variant count
- [ ] Optimize shader keywords with multi_compile_local
- [ ] Remove unnecessary texture samples
- [ ] Use URP's Forward+ rendering for better light culling

### Task 4.3: Update Optimization
- [ ] Implement dirty flag system for province changes
- [ ] Update only changed texture regions
- [ ] Use double buffering for smooth updates
- [ ] Batch province updates per frame
- [ ] Implement temporal coherence for borders
- [ ] Add LOD system based on zoom level

### Task 4.4: Memory Optimization
- [ ] Use object pooling for temporary data
- [ ] Implement province data paging for huge maps
- [ ] Compress province history data
- [ ] Use bit packing for boolean province flags
- [ ] Stream province details on demand
- [ ] Implement aggressive garbage collection strategy

## Phase 5: Advanced URP Rendering Features

### Task 5.1: Heightmap Integration
- [ ] Load heightmap texture
- [ ] Modify vertex shader for height displacement using URP macros
- [ ] Adjust border shader for 3D terrain
- [ ] Use URP's shadow mapping system
- [ ] Add height-based province shading
- [ ] Create water depth rendering with URP Water shader
- [ ] Consider URP Terrain system integration for hybrid approach

### Task 5.2: Visual Effects (URP)
- [ ] Implement smooth zoom transitions
- [ ] Add camera frustum culling for overlays
- [ ] Create animated border effects using Shader Graph
- [ ] Implement province grow/shrink animations
- [ ] Add war front line animations with URP Particle System
- [ ] Create fog of war using URP Render Features
- [ ] Use Volume Overrides for post-processing effects
- [ ] Implement custom ScriptableRenderPass for special effects

### Task 5.3: Label Rendering
- [ ] Implement GPU-based text rendering
- [ ] Create label placement algorithm
- [ ] Add label LOD based on zoom
- [ ] Implement curved text for large provinces
- [ ] Add icon rendering system
- [ ] Create label occlusion system

### Task 5.4: Overlay Systems
- [ ] Implement trade route rendering
- [ ] Add army movement paths
- [ ] Create weather overlay system
- [ ] Implement supply line visualization
- [ ] Add diplomatic relation lines
- [ ] Create battle location markers

## Phase 6: Unity Integration

### Task 6.1: Input System
- [ ] Implement camera controller with smooth zoom
- [ ] Add province selection via mouse (using CommandBuffer.RequestAsyncReadback)
- [ ] Create box selection for multiple provinces
- [ ] Implement keyboard shortcuts for map modes
- [ ] Add touch input support for mobile
- [ ] Create edge scrolling for camera
- [ ] Use URP's Camera Stack for UI overlay cameras

### Task 6.2: UI Integration
- [ ] Create province tooltip system
- [ ] Add province info panel
- [ ] Implement minimap with GPU rendering
- [ ] Create map mode selector UI
- [ ] Add performance statistics overlay
- [ ] Implement province search functionality

### Task 6.3: Game Logic Integration
- [ ] Create event system for province changes
- [ ] Implement province ownership changes
- [ ] Add province development modifications
- [ ] Create save/load system for map state
- [ ] Implement multiplayer synchronization
- [ ] Add modding support for province data

### Task 6.4: Platform Optimization
- [ ] Implement quality settings (Low/Medium/High/Ultra)
- [ ] Add resolution scaling for weak GPUs
- [ ] Create mobile-specific optimizations
- [ ] Implement console platform adjustments
- [ ] Add DirectX/Vulkan/Metal specific paths
- [ ] Create automated performance scaling

## Phase 7: Testing & Validation

### Task 7.1: Performance Testing
- [ ] Verify 200+ FPS with all provinces visible
- [ ] Test with 10,000 province changes per second
- [ ] Validate memory usage stays under 100MB
- [ ] Ensure no memory leaks during long sessions
- [ ] Test zoom performance (1x to 100x)
- [ ] Profile GPU usage (target: <5ms per frame)

### Task 7.2: Correctness Testing
- [ ] Validate all provinces are selectable
- [ ] Verify no rendering artifacts at borders
- [ ] Test province color accuracy
- [ ] Ensure islands render correctly
- [ ] Validate neighbor relationships
- [ ] Test all map modes render correctly

### Task 7.3: Stress Testing
- [ ] Test with 20,000 provinces
- [ ] Simulate 1000 simultaneous province changes
- [ ] Test 8-hour continuous gameplay session
- [ ] Validate with 4K and 8K resolutions
- [ ] Test rapid map mode switching
- [ ] Simulate maximum overlay density

### Task 7.4: Compatibility Testing
- [ ] Test on minimum spec hardware
- [ ] Validate on all target platforms
- [ ] Test with different GPU vendors
- [ ] Verify with various driver versions
- [ ] Test in WebGL builds
- [ ] Validate on integrated graphics

## Performance Targets (URP Optimized)

### Minimum Requirements (60 FPS)
- [ ] GTX 960 / RX 470 level GPU
- [ ] 10,000 provinces rendered
- [ ] 1920x1080 resolution
- [ ] All borders visible
- [ ] One overlay system active
- [ ] URP Quality: Low preset

### Recommended Requirements (144 FPS)
- [ ] GTX 1070 / RX 580 level GPU
- [ ] 10,000 provinces with all effects
- [ ] 2560x1440 resolution
- [ ] Multiple overlays active
- [ ] Full animation systems
- [ ] URP Quality: Medium preset with SRP Batcher

### Ultra Requirements (200+ FPS)
- [ ] RTX 3070 / RX 6700 level GPU
- [ ] 20,000+ provinces
- [ ] 4K resolution
- [ ] All systems maximum quality
- [ ] URP Forward+ with all features
- [ ] Custom Render Features enabled

## Critical Success Metrics
- [ ] Single draw call for entire base map
- [ ] Province selection in <1ms
- [ ] Map mode changes in <16ms (one frame)
- [ ] Memory usage under 100MB for map system
- [ ] Zero dynamic allocations during gameplay
- [ ] Pixel-perfect province borders
- [ ] Support for 65,535 unique provinces

## URP-Specific Shader Code Examples

### Basic Province Map Shader (URP)
```hlsl
Shader "MapSystem/ProvinceMapURP"
{
    Properties
    {
        _ProvinceIDTex ("Province ID Texture", 2D) = "white" {}
        _ProvinceColorTex ("Province Colors", 2D) = "white" {}
        _BorderTex ("Border Texture", 2D) = "black" {}
        _SelectedProvince ("Selected Province ID", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAP_MODE_POLITICAL _MAP_MODE_TERRAIN

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            TEXTURE2D(_ProvinceIDTex);
            SAMPLER(sampler_PointClamp);
            TEXTURE2D(_ProvinceColorTex);
            TEXTURE2D(_BorderTex);

            CBUFFER_START(UnityPerMaterial)
                float _SelectedProvince;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // Sample province ID using point sampling
                float2 provinceIDRaw = SAMPLE_TEXTURE2D(_ProvinceIDTex, sampler_PointClamp, input.uv).rg;
                uint provinceID = (uint)(provinceIDRaw.r * 255.0) + (uint)(provinceIDRaw.g * 255.0) * 256;

                // Get province color from palette
                float2 colorUV = float2(provinceID / 256.0, 0.5);
                half4 provinceColor = SAMPLE_TEXTURE2D(_ProvinceColorTex, sampler_PointClamp, colorUV);

                // Apply borders
                half border = SAMPLE_TEXTURE2D(_BorderTex, sampler_PointClamp, input.uv).r;
                provinceColor.rgb = lerp(provinceColor.rgb, half3(0,0,0), border);

                // Highlight selected province
                if(abs(provinceID - _SelectedProvince) < 0.5)
                {
                    provinceColor.rgb = lerp(provinceColor.rgb, half3(1,1,0), 0.3);
                }

                return provinceColor;
            }
            ENDHLSL
        }
    }
}
```

### Border Detection Compute Shader
```hlsl
#pragma kernel DetectBorders

Texture2D<float2> ProvinceIDTexture;
RWTexture2D<float> BorderOutput;

[numthreads(8,8,1)]
void DetectBorders(uint3 id : SV_DispatchThreadID)
{
    uint width, height;
    ProvinceIDTexture.GetDimensions(width, height);

    if(id.x >= width || id.y >= height)
        return;

    float2 currentID = ProvinceIDTexture[id.xy];
    float border = 0;

    // Check 4-way neighbors
    if(id.x > 0 && any(ProvinceIDTexture[uint2(id.x-1, id.y)] != currentID))
        border = 1;
    if(id.x < width-1 && any(ProvinceIDTexture[uint2(id.x+1, id.y)] != currentID))
        border = 1;
    if(id.y > 0 && any(ProvinceIDTexture[uint2(id.x, id.y-1)] != currentID))
        border = 1;
    if(id.y < height-1 && any(ProvinceIDTexture[uint2(id.x, id.y+1)] != currentID))
        border = 1;

    BorderOutput[id.xy] = border;
}
```

## Common Pitfalls to Avoid
- Don't use texture filtering on province ID textures
- Don't readback GPU data every frame
- Don't update entire textures for small changes
- Don't use province GameObjects
- Don't generate geometry for borders
- Don't use colliders for province selection
- Don't store province data in textures larger than needed
- Don't forget CBUFFER blocks for SRP Batcher compatibility
- Don't use legacy CG/HLSL includes in URP shaders

## Debugging Tools to Build
- [ ] Province ID visualizer overlay (URP Render Feature)
- [ ] Border generation preview
- [ ] Performance profiler HUD
- [ ] Texture memory inspector
- [ ] Province neighbor graph visualizer
- [ ] GPU timing breakdown display (Frame Debugger)
- [ ] Province selection accuracy tester