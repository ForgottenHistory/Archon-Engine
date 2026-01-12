# Pluggable Map Mode Colorizer Architecture
**Date**: 2026-01-12
**Session**: 6
**Status**: ✅ Complete
**Priority**: Medium

---

## Session Goal

**Primary Objective:**
- Apply pluggable renderer pattern to map mode colorization system

**Secondary Objectives:**
- Update Pattern 20 documentation with fifth implementation
- Complete the pluggable rendering architecture

**Success Criteria:**
- IMapModeColorizer interface + GradientMapModeColorizer implementation
- MapRendererRegistry supports colorizers
- VisualStyleConfiguration has MapModeColorizerStyle settings
- GradientMapMode uses registry instead of hardcoded GradientComputeDispatcher
- Pattern 20 extended - now five rendering systems pluggable

---

## Context & Background

**Previous Work:**
- See: [5-pluggable-terrain-renderer.md](5-pluggable-terrain-renderer.md)
- Border, Highlight, Fog of War, and Terrain renderers now pluggable

**Current State:**
- GradientMapMode had hardcoded GradientComputeDispatcher
- No extension point for custom colorization (discrete bands, multi-gradient, patterns)
- Map mode DATA was pluggable (IMapModeHandler), but colorization was locked

**Why Now:**
- Graphics programmer assessment revealed this gap
- Map mode colorization is key for visual customization
- Complete the pluggable pattern across ALL rendering systems

---

## What We Did

### 1. IMapModeColorizer Interface
**Files Created:** `Map/MapModes/Colorization/IMapModeColorizer.cs`

```csharp
public interface IMapModeColorizer
{
    string ColorizerId { get; }
    string DisplayName { get; }
    bool RequiresPerFrameUpdate { get; }

    void Initialize(MapModeColorizerContext context);
    void Colorize(RenderTexture provinceIDTexture, RenderTexture outputTexture,
                  float[] provinceValues, ColorizationStyleParams styleParams);
    void OnRenderFrame();
    void Dispose();
}
```

Also includes: `MapModeColorizerContext`, `ColorizationStyleParams`

### 2. MapModeColorizerBase Abstract Class
**Files Created:** `Map/MapModes/Colorization/MapModeColorizerBase.cs`

Common utilities:
- Thread group calculation
- Province value buffer management
- Gradient buffer upload helper
- Color conversion utilities

### 3. GradientMapModeColorizer Implementation
**Files Created:** `Map/MapModes/Colorization/Implementations/GradientMapModeColorizer.cs`

Wraps existing GPU compute shader logic:
- 3-color gradient interpolation (low, mid, high)
- Uses GradientMapMode.compute shader
- ~1ms for 11.5M pixels

### 4. MapRendererRegistry Extension
**Files Changed:** `Map/Rendering/MapRendererRegistry.cs`

Added colorizer support:
- `mapModeColorizers` dictionary
- `RegisterMapModeColorizer()`, `GetMapModeColorizer()`, `GetDefaultMapModeColorizer()`
- `SetDefaultMapModeColorizer()`, `GetAvailableMapModeColorizers()`, `HasMapModeColorizer()`
- Disposal in OnDestroy

### 5. VisualStyleConfiguration Extension
**Files Changed:** `Map/VisualStyles/VisualStyleConfiguration.cs`

Added `MapModeColorizerStyle` class:
```csharp
public class MapModeColorizerStyle
{
    public string customColorizerId = "";
    public int discreteBands = 0;        // 0 = continuous
    public bool showValueLabels = false;
    public Color noDataColor = gray;

    public string GetEffectiveColorizerId() { ... }
}
```

### 6. GradientMapMode Integration
**Files Changed:** `Map/MapModes/GradientMapMode.cs`

- Replaced `GradientComputeDispatcher` with `IMapModeColorizer`
- Gets colorizer from `MapRendererRegistry` on activation
- Passes `ColorizationStyleParams` to colorizer
- Fallback: creates GradientMapModeColorizer directly if registry unavailable

### 7. MapModeManager Registration
**Files Changed:** `Map/MapModes/MapModeManager.cs`

- Added `RegisterDefaultColorizer()` method
- Called during Initialize() to register default gradient colorizer
- GAME layer can register customs before or after

---

## Decisions Made

### Decision 1: Colorizer vs Renderer Naming
**Context:** Should this be IMapModeRenderer or IMapModeColorizer?

**Decision:** Use IMapModeColorizer

**Rationale:**
- More specific - describes what it does (colorizes province values)
- Avoids confusion with other renderers (border, terrain, etc.)
- Matches the operation: Colorize() not Render()

### Decision 2: Ownership of Colorizer Lifecycle
**Context:** Who owns/disposes the colorizer?

**Decision:** MapRendererRegistry owns colorizers, GradientMapMode only holds reference

**Rationale:**
- Consistent with other renderer patterns
- Colorizer shared across all map modes using it
- Single disposal point in registry OnDestroy

---

## What Worked ✅

1. **Pattern consistency**
   - Fifth time applying Pattern 20 - very efficient
   - Same structure as other four renderers
   - Copy-paste with modifications

2. **Clear separation maintained**
   - IMapModeHandler = DATA (province values, gradients)
   - IMapModeColorizer = VISUALIZATION (how to render values)

---

## Architecture Impact

### All Five Rendering Systems Now Pluggable
| System | Interface | Default Implementation |
|--------|-----------|----------------------|
| Border Rendering | IBorderRenderer | DistanceField, PixelPerfect, MeshGeometry, None |
| Highlight/Selection | IHighlightRenderer | Default (GPU compute) |
| Fog of War | IFogOfWarRenderer | Default (GPU compute) |
| Terrain Blending | ITerrainRenderer | Default (4-channel blend) |
| Map Mode Colorization | IMapModeColorizer | Gradient (3-color) |

### Pattern 20 Extended
Now covers five rendering systems. GAME layer can extend any of these without modifying ENGINE code.

---

## Potential Custom Colorizers

Now possible for GAME layer to implement:
1. **DiscreteColorBands** - 5-10 discrete color steps instead of gradient
2. **MultiGradient** - More than 3 color stops
3. **PatternOverlay** - Stripes/dots for contested areas
4. **AnimatedPulse** - Pulsing colors for critical areas
5. **HeatmapStyle** - Blue-to-red scientific visualization

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- Pattern 20 now has 5 implementations (added IMapModeColorizer)
- Map mode DATA (IMapModeHandler) was already pluggable
- Map mode COLORIZATION (IMapModeColorizer) is now pluggable too
- GradientMapMode gets colorizer from registry, not hardcoded

**Key Files:**
- Interface: `Map/MapModes/Colorization/IMapModeColorizer.cs`
- Implementation: `Map/MapModes/Colorization/Implementations/GradientMapModeColorizer.cs`
- Registry: `Map/Rendering/MapRendererRegistry.cs` (now has colorizer methods)
- Config: `Map/VisualStyles/VisualStyleConfiguration.cs` (MapModeColorizerStyle class)
- Consumer: `Map/MapModes/GradientMapMode.cs` (uses colorizer from registry)

**GAME Layer Usage:**
```csharp
// Register custom
MapRendererRegistry.Instance.RegisterMapModeColorizer(new DiscreteColorBandsColorizer());

// In VisualStyleConfiguration
mapModeColorizer.customColorizerId = "DiscreteBands";
```

---

## Files Changed Summary

| File | Changes |
|------|---------|
| `IMapModeColorizer.cs` | NEW - Interface + context/params structs |
| `MapModeColorizerBase.cs` | NEW - Abstract base class |
| `GradientMapModeColorizer.cs` | NEW - 3-color gradient implementation |
| `MapRendererRegistry.cs` | Added colorizer dictionary + methods |
| `VisualStyleConfiguration.cs` | Added MapModeColorizerStyle class |
| `GradientMapMode.cs` | Uses IMapModeColorizer from registry |
| `MapModeManager.cs` | Registers default colorizer on init |
| `architecture-patterns.md` | Updated Pattern 20 - IMapModeColorizer added |

---

*Session complete - pluggable map mode colorizer architecture - Pattern 20 now has 5 implementations*
