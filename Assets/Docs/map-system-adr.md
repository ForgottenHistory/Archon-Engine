### URP-Specific Implementation Considerations

#### Shader Development in URP
**Two approaches available**:
1. **Shader Graph** (Visual, easier to learn)
   - Good for prototyping
   - Limitations for complex texture sampling logic
   - May need custom nodes for province ID decoding

2. **HLSL with URP libraries** (Code, more control)
   - Full control over optimization
   - Required for compute shaders
   - Better performance for final implementation

**Recommended approach**: Start with Shader Graph, convert critical paths to HLSL

#### Custom Render Feature for Map System
```csharp
public class MapRenderFeature : ScriptableRendererFeature
{
    // Inject custom passes for:
    // - Border generation compute shader
    // - Province selection buffer
    // - Overlay rendering
}
```

Benefits:
- Precise control over rendering order
- Can optimize render state changes
- Integrate with URP's frame debugger

#### URP Performance Considerations
- **Render Graph** (2023.3+): Automatically optimizes our texture operations
- **GPU Resident Drawer** (Unity 6): Perfect for our single-object approach
- **Forward+ Rendering**: Better than Forward for multiple light sources (cities, effects)# Architecture Decision Record: Texture-Based Map Rendering System

## Status
Proposed

## Context
We need to render a Paradox-style grand strategy map with 10,000+ provinces at 200+ FPS in Unity. Initial approach using individual 3D meshes per province has proven unviable due to performance constraints.

## Decision Drivers
- **Performance**: Must maintain 200+ FPS with 10,000 provinces
- **Scale**: System must handle Paradox-scale maps (up to 20,000 provinces)
- **Accuracy**: Pixel-perfect province boundaries matching source bitmap
- **Memory**: Minimize RAM usage (<100MB for map system)
- **Developer Experience**: Team has limited graphics programming experience

## Considered Options

### Option 1: 3D Mesh Per Province (Current Approach)
**How it works**: Each province is a separate GameObject with its own mesh, material, and collider.

**Graphics Programming Concepts**:
- **Draw Call**: Each mesh requires the CPU to tell the GPU "draw this object" 
- **Vertex Processing**: GPU must transform every vertex of every mesh
- **Batching**: Unity tries to combine similar meshes, but only works if they share materials

**Why it fails at scale**:
- 10,000 GameObjects = 10,000 transform matrices to update
- Even batched: ~100-500 draw calls (GPU can handle ~2000-3000 efficiently)
- Memory: 10,000 meshes × ~10KB each = 100MB just for geometry

### Option 2: Texture-Based Rendering (Recommended)
**How it works**: Entire map is one quad (2 triangles). Province data stored in textures. GPU shader does all the work.

**Graphics Programming Concepts**:
- **Texture Sampling**: GPU reads pixel values from textures extremely fast
- **Fragment Shader**: Code that runs for EVERY pixel on screen in parallel
- **Compute Shader**: General-purpose GPU programs for data processing

**Why it works**:
- 1 draw call for entire map
- Province data in textures: parallel processing on thousands of GPU cores
- Province selection: Read pixel color at mouse position

### Option 3: Hybrid Chunked Meshes
**How it works**: Divide map into chunks (e.g., 100×100 provinces), one mesh per chunk.

**Why we rejected it**:
- Still 100+ draw calls
- Complex province-to-chunk mapping
- Border provinces split across chunks
- No successful examples in shipped games

## Decision
**We will implement Option 2: Texture-Based Rendering using Universal Render Pipeline (URP)**

### Pipeline Sub-Decision Rationale
While Built-in Pipeline would be simpler for our single-quad approach, URP is the correct choice for a new project because:
1. Built-in Pipeline is in maintenance mode with no new features
2. URP receives all performance optimizations and new Unity features
3. The complexity overhead is minimal for our use case
4. Future-proofing is critical for a multi-year game project
5. URP's Render Graph (2023.3+) will auto-optimize our compute shader passes

## Technical Architecture Explanation

### Core Concept: Data as Textures
Instead of geometry, we store province information as colored pixels in textures:

```
Traditional 3D Approach:
Province → Mesh with vertices → GPU draws triangles

Our Approach:
Province → Colored pixels in texture → GPU colors screen based on texture
```

### Key Graphics Programming Concepts

#### 1. The GPU Parallel Processing Model
**What beginners need to know**:
- GPUs have thousands of tiny processors (cores)
- Each core is simple but there are MANY (3000+ on modern GPUs)
- They all execute the same program on different data simultaneously

**Why this matters for our map**:
- Checking "is this pixel a border?" happens for ALL pixels AT ONCE
- With meshes: process provinces sequentially
- With textures: process all provinces simultaneously

#### 2. Texture Formats and Bit Packing
**R16G16 Format Explanation**:
- Normal color: RGBA, 8 bits each = 32 bits total
- R16G16: Only Red+Green, 16 bits each = 32 bits total
- Can store values 0-65,535 instead of 0-255

**Why we use it**:
```
Province ID 5000:
- Can't fit in R8 (max 255)
- Perfect fit in R16 (max 65,535)
- Store Province ID in R channel, other data in G
```

#### 3. Fragment Shaders vs Vertex Shaders
**Vertex Shader**: Runs once per vertex (corner of triangle)
- 3D meshes: millions of vertices to process
- Our quad: only 4 vertices

**Fragment Shader**: Runs once per pixel on screen
- Both approaches: same number of pixels
- But we avoid vertex processing overhead

#### 4. GPU Memory Hierarchy
**Texture Cache**: GPUs have special fast memory for textures
- Optimized for 2D spatial access patterns
- Reading nearby pixels is nearly free
- Perfect for map data (provinces are spatially coherent)

**Why meshes are slower**:
- Vertex data doesn't use texture cache
- Random memory access patterns
- Cache misses cause stalls

#### 5. Compute Shaders for Border Generation
**Traditional CPU Approach**:
```
for each pixel:
    check neighbors  // Sequential, slow
```

**Compute Shader Approach**:
```
Thread Group [8×8 pixels]:
    All 64 pixels check neighbors simultaneously
    Write results to border texture
```

**Performance difference**: 
- CPU: 11 million iterations
- GPU: 172,000 thread groups executing in parallel

### Render Pipeline Considerations

#### Built-in vs URP vs HDRP
**Built-in Render Pipeline**:
- ⚠️ **In maintenance mode - no new features**
- ⚠️ **Will likely be deprecated in 2-3 years**
- Direct control over rendering
- Simple integration with compute shaders
- Less overhead for our single-quad approach
- **Not recommended for new projects**

**Universal Render Pipeline (URP)** ✅ **RECOMMENDED**:
- **Actively developed with regular updates**
- **Unity's focus for performance improvements**
- **Future-proof - will get all new features**
- Optimized for mobile through high-end
- SRP Batcher (though we only have one object)
- Render Graph (Unity 2023+) for optimal GPU scheduling
- Custom Render Features allow map-specific optimizations
- **Small learning curve but worth it for longevity**

**HDRP**:
- For AAA visual fidelity
- Massive overkill for strategy map
- **Not recommended**

#### Why URP Makes Sense Despite Single-Quad Rendering
1. **Future Unity Features**: All new rendering features come to URP first (or only)
2. **Platform Support**: Better console/mobile optimization out of the box
3. **Render Graph**: New URP feature that auto-optimizes render passes
4. **Forward+ Rendering**: URP's new rendering path great for UI overlays
5. **Community & Assets**: Most new Unity assets target URP
6. **Long-term Support**: Your game will be supported for years

### Memory Layout and Access Patterns

#### Why Textures Beat Meshes for Province Data
**Mesh Memory Layout**:
```
Province 1: [vertices...] [indices...] [normals...]
Province 2: [vertices...] [indices...] [normals...]
// Scattered across memory, poor cache usage
```

**Texture Memory Layout**:
```
Row 0: [province ID][province ID][province ID]...
Row 1: [province ID][province ID][province ID]...
// Sequential, cache-friendly
```

#### Province Selection Performance
**Mesh Approach**:
1. Ray from camera through mouse position
2. Test intersection with 10,000 colliders
3. Physics system overhead
4. ~5-10ms per click

**Texture Approach**:
1. Convert mouse to UV coordinate
2. Single texture read
3. ~0.01ms per click

### Shader Optimization Strategies

#### Branching in Shaders
**Bad** (causes divergence):
```hlsl
if (provinceID == selectedProvince) {
    // Complex calculations
} else {
    // Different complex calculations
}
```

**Good** (uniform branching):
```hlsl
float selected = (provinceID == selectedProvince) ? 1.0 : 0.0;
color = lerp(normalColor, selectedColor, selected);
```

#### Texture Sampling Optimization
**Point Sampling**: No filtering, exact pixel values
- Critical for province IDs (no blending!)
- Must use `FilterMode.Point`

**Bilinear Sampling**: Smooth blending
- Good for heightmaps, color gradients
- Never use for ID textures

## Consequences

### Positive
- **Performance**: Single-digit millisecond frame times
- **Scalability**: Can handle 20,000+ provinces
- **Memory**: ~50MB for entire map system
- **Simplicity**: Fewer moving parts than mesh system
- **Proven**: Used by all major strategy games

### Negative
- **Learning Curve**: Requires shader programming knowledge
- **URP Learning**: Additional complexity of SRP architecture
- **Debugging**: Harder to visualize than GameObjects
- **Flexibility**: Adding 3D province features is harder
- **Unity Integration**: Less intuitive than GameObject approach

### Mitigation Strategies
- Create comprehensive debug visualizers
- Provide URP-compatible shader templates
- Use Shader Graph for prototype, HLSL for optimization
- Build abstraction layer for common operations
- Document GPU programming concepts (this ADR)
- Leverage URP samples and documentation

## Implementation Risks

### Risk 1: Shader Complexity
**Mitigation**: Start with simple shaders, add features incrementally

### Risk 2: GPU Feature Support
**Mitigation**: Target Shader Model 4.5 (2010+ GPUs)

### Risk 3: Precision Issues
**Mitigation**: Use integer province IDs, not floating-point

## Learning Resources for Graphics Programming

### Foundational Concepts to Study
1. **GPU Architecture**: How parallel processing works
2. **Shader Pipeline**: Vertex → Fragment → Pixel
3. **Texture Formats**: How data is stored in GPU memory
4. **Draw Calls**: CPU-GPU communication overhead
5. **Cache Coherency**: Why memory access patterns matter

### Recommended Learning Path
1. Start with Shader Graph (visual, easier)
2. Learn HLSL basics (control, performance)
3. Study compute shaders (advanced optimizations)
4. Profile with RenderDoc (understand what GPU is doing)

## Validation Criteria
- [ ] Render 10,000 provinces at 200+ FPS
- [ ] Province selection under 1ms
- [ ] Memory usage under 100MB
- [ ] Single draw call for base map
- [ ] Pixel-perfect province boundaries

## References
- [GPU Gems: Parallel Processing](https://developer.nvidia.com/gpugems/gpugems/part-i-natural-effects/chapter-1-effective-water-simulation-physical-models)
- [Unity Compute Shader Documentation](https://docs.unity3d.com/Manual/class-ComputeShader.html)
- [Paradox Development Diary: Map Rendering](https://forum.paradoxplaza.com/forum/developer-diary/)
- [RenderDoc GPU Debugger](https://renderdoc.org/)

## Decision Outcome
Proceed with texture-based rendering system. Begin with Phase 1 (Foundation) and Phase 2 (Bitmap Processing) as these require no shader knowledge. Phase 3 (GPU Shaders) will be supported with templates and examples.