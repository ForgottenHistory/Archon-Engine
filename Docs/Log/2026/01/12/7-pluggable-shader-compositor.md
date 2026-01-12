# Pluggable Shader Compositor Architecture
**Date**: 2026-01-12
**Session**: 7
**Status**: ✅ Complete
**Priority**: Medium

---

## Session Goal

**Primary Objective:**
- Make shader layer compositing pluggable for graphics programmers

**Secondary Objectives:**
- Enable mix-and-match of blend modes for different layers
- Provide ENGINE defaults + example custom compositors
- Complete the pluggable rendering architecture

**Success Criteria:**
- IShaderCompositor interface + DefaultShaderCompositor implementation
- MapRendererRegistry supports compositors
- VisualStyleConfiguration has CompositorStyle settings
- Compositing.hlsl provides blend mode functions
- Pattern 20 extended - now six pluggable systems

---

## Context & Background

**Problem Statement:**
Graphics programmers couldn't customize HOW render layers are combined:
- Borders, highlights, fog, overlay all use fixed `lerp()` blending
- Layer ORDER is hardcoded in fragment shader
- Can't skip layers selectively
- Can't use different blend modes (multiply, screen, overlay, etc.)

**Previous Work:**
- IBorderRenderer, IHighlightRenderer, IFogOfWarRenderer, ITerrainRenderer, IMapModeColorizer
- Each outputs to RenderTexture - the DATA is pluggable
- But COMPOSITING of those textures was hardcoded in shader

**Why Now:**
- Final piece for graphics programmer customization
- Enables EU4-style vs Imperator-style vs custom visual styles

---

## What We Did

### 1. IShaderCompositor Interface
**Files Created:** `Map/Rendering/Compositing/IShaderCompositor.cs`

```csharp
public interface IShaderCompositor
{
    string CompositorId { get; }
    string DisplayName { get; }

    void Initialize(CompositorContext context);
    void ConfigureMaterial(Material mapMaterial);
    CompositorConfig GetConfig();
    void OnPreRender();
    Shader GetCustomShader();
    void Dispose();
}
```

Also includes: `CompositorContext`, `CompositorConfig`, `BlendMode` enum

### 2. ShaderCompositorBase Abstract Class
**Files Created:** `Map/Rendering/Compositing/ShaderCompositorBase.cs`

Common utilities:
- Material property setters (float, int, color)
- Shader keyword helpers
- Cached property IDs

### 3. Default Compositor Implementations
**Files Created:** `Map/Rendering/Compositing/Implementations/DefaultShaderCompositor.cs`

Four ENGINE compositors:
- **DefaultShaderCompositor** - All layers, normal blend
- **MinimalShaderCompositor** - No fog/overlay (performance)
- **StylizedShaderCompositor** - Multiply borders, additive highlights (EU4-like)
- **CinematicShaderCompositor** - Overlay blends, high contrast (screenshots)

### 4. MapRendererRegistry Extension
**Files Changed:** `Map/Rendering/MapRendererRegistry.cs`

Added compositor support:
- `shaderCompositors` dictionary
- `RegisterShaderCompositor()`, `GetShaderCompositor()`, `GetDefaultShaderCompositor()`
- `SetDefaultShaderCompositor()`, `GetAvailableShaderCompositors()`, `HasShaderCompositor()`
- Disposal in OnDestroy

### 5. VisualStyleConfiguration Extension
**Files Changed:** `Map/VisualStyles/VisualStyleConfiguration.cs`

Added `CompositorStyle` class:
```csharp
public class CompositorStyle
{
    public string customCompositorId = "";
    public bool enableLighting = true;
    public bool enableBorders = true;
    public bool enableHighlights = true;
    public bool enableFogOfWar = true;
    public bool enableOverlay = true;

    public string GetEffectiveCompositorId() { ... }
}
```

### 6. Compositing.hlsl Shader Include
**Files Created:** `Shaders/Includes/Compositing.hlsl`

Blend mode functions:
- `BlendNormal()` - Standard alpha lerp
- `BlendMultiply()` - Darkening blend
- `BlendScreen()` - Lightening blend
- `BlendOverlay()` - Contrast blend
- `BlendAdditive()` - Additive blend
- `BlendSoftLight()` - Subtle blend

Layer compositing functions:
- `ApplyBordersComposited()` - With blend mode
- `ApplyHighlightsComposited()` - With blend mode
- `ApplyFogOfWarComposited()` - With blend mode
- `ApplyOverlayComposited()` - With blend mode
- `CompositeAllLayers()` - Master function

---

## Decisions Made

### Decision 1: C# + Shader Hybrid Approach
**Context:** How to make shader compositing pluggable?

**Decision:** C# configures material properties, shader reads them

**Rationale:**
- Pure C# can't change shader code at runtime
- Pure shader variants = combinatorial explosion
- Hybrid: C# sets `_BorderBlendMode` int, shader switches on it
- Best of both worlds: runtime config + GPU performance

### Decision 2: Predefined Blend Modes
**Context:** How many blend modes to support?

**Decision:** 6 modes (Normal, Multiply, Screen, Overlay, Additive, SoftLight)

**Rationale:**
- Covers 95% of use cases
- More modes = more shader branching
- Graphics programmers can add custom compositors for exotic blends

---

## What Worked ✅

1. **Consistent pattern application**
   - Sixth time applying Pattern 20
   - Same structure: Interface → Base → Implementations → Registry

2. **Immediate value**
   - Four ready-to-use compositors out of the box
   - Graphics programmer can switch between them instantly

---

## Architecture Impact

### All Six Rendering Systems Now Pluggable
| System | Interface | Default Implementation |
|--------|-----------|----------------------|
| Border Rendering | IBorderRenderer | DistanceField, PixelPerfect, MeshGeometry |
| Highlight/Selection | IHighlightRenderer | Default (GPU compute) |
| Fog of War | IFogOfWarRenderer | Default (GPU compute) |
| Terrain Blending | ITerrainRenderer | Default (4-channel blend) |
| Map Mode Colorization | IMapModeColorizer | Gradient (3-color) |
| **Layer Compositing** | **IShaderCompositor** | **Default, Minimal, Stylized, Cinematic** |

### Pattern 20 Complete for Graphics
GAME layer graphics programmer can now customize:
- Individual render layer outputs (via IRenderer interfaces)
- How layers are combined (via IShaderCompositor)
- Blend modes per layer
- Layer visibility

---

## What Graphics Programmers Can Now Do

1. **Custom blend modes** - Multiply borders for EU4 look
2. **Skip layers** - Disable fog for performance mode
3. **Layer ordering** - (via custom compositor)
4. **Complete override** - Provide custom shader via `GetCustomShader()`
5. **Runtime switching** - Change compositor without restart

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- Pattern 20 now has 6 implementations
- IShaderCompositor controls HOW layers combine, not WHAT they contain
- Compositing.hlsl provides shader-side blend mode functions
- C# sets material properties, shader reads them

**Key Files:**
- Interface: `Map/Rendering/Compositing/IShaderCompositor.cs`
- Base: `Map/Rendering/Compositing/ShaderCompositorBase.cs`
- Implementations: `Map/Rendering/Compositing/Implementations/DefaultShaderCompositor.cs`
- Registry: `Map/Rendering/MapRendererRegistry.cs` (now has compositor methods)
- Config: `Map/VisualStyles/VisualStyleConfiguration.cs` (CompositorStyle class)
- Shader: `Shaders/Includes/Compositing.hlsl`

**GAME Layer Usage:**
```csharp
// Register custom compositor
MapRendererRegistry.Instance.RegisterShaderCompositor(new MyCustomCompositor());

// In VisualStyleConfiguration
compositor.customCompositorId = "MyCustom";
```

---

## Files Changed Summary

| File | Changes |
|------|---------|
| `IShaderCompositor.cs` | NEW - Interface + config classes |
| `ShaderCompositorBase.cs` | NEW - Abstract base class |
| `DefaultShaderCompositor.cs` | NEW - 4 compositor implementations |
| `Compositing.hlsl` | NEW - Blend mode shader functions |
| `MapRendererRegistry.cs` | Added compositor dictionary + methods |
| `VisualStyleConfiguration.cs` | Added CompositorStyle class |
| `architecture-patterns.md` | Updated Pattern 20 - 6 implementations |

---

*Session complete - pluggable shader compositor architecture - Pattern 20 now has 6 implementations*
