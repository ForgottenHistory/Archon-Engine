# Pluggable Border Renderer Architecture
**Date**: 2026-01-12
**Session**: 2
**Status**: ✅ Complete
**Priority**: Medium

---

## Session Goal

**Primary Objective:**
- Make border rendering pluggable so GAME layer can provide custom implementations without modifying ENGINE

**Secondary Objectives:**
- Add configurable pixel-perfect border thickness via VisualStyles
- Fix borders not updating on province ownership change

**Success Criteria:**
- Interface + Registry pattern following IMapModeHandler approach
- ENGINE defaults still work (backwards compatible)
- GAME can register custom renderers by string ID

---

## Context & Background

**Previous Work:**
- See: [1-border-rendering-clean-separation.md](1-border-rendering-clean-separation.md)
- Border system had clean mode separation but modes were hardcoded in ENGINE

**Current State:**
- Border rendering modes (DistanceField, PixelPerfect, MeshGeometry) defined by ENGINE enum
- No extension point for GAME layer to add custom rendering

**Why Now:**
- User asked "how would I create my own art style?" - exposed gap in architecture
- Archon meant to be public project - GAME layer needs customization without ENGINE forks

---

## What We Did

### 1. IBorderRenderer Interface
**Files Created:** `Map/Rendering/Border/IBorderRenderer.cs`

```csharp
public interface IBorderRenderer
{
    string RendererId { get; }           // "DistanceField", "MyCustom"
    string DisplayName { get; }          // For UI/debugging
    bool RequiresPerFrameUpdate { get; } // MeshGeometry needs this

    void Initialize(MapTextureManager textureManager, BorderRendererContext context);
    void GenerateBorders(BorderGenerationParams parameters);
    void ApplyToMaterial(Material material, BorderStyleParams styleParams);
    void OnRenderFrame();
    void Dispose();
}
```

### 2. BorderRendererBase Abstract Class
**Files Created:** `Map/Rendering/Border/BorderRendererBase.cs`

Common utilities for renderers:
- Thread group calculation for compute shaders
- Shader mode value mapping
- Common style parameter application

### 3. MapRendererRegistry Singleton
**Files Created:** `Map/Rendering/MapRendererRegistry.cs`

```csharp
// GAME layer registers custom implementations
MapRendererRegistry.Instance.RegisterBorderRenderer(new MyCustomBorderRenderer());

// VisualStyleManager looks up by ID
var renderer = registry.GetBorderRenderer("MyCustom");
```

### 4. ENGINE Default Implementations
**Files Created:** `Map/Rendering/Border/Implementations/`
- `DistanceFieldBorderRenderer.cs` - Wraps BorderDistanceFieldGenerator
- `PixelPerfectBorderRenderer.cs` - Wraps compute shader dispatch
- `MeshGeometryBorderRenderer.cs` - Wraps curve extraction + mesh rendering
- `NoneBorderRenderer.cs` - No-op for disabled borders

### 5. VisualStyleConfiguration Extension
**Files Changed:** `Map/VisualStyles/VisualStyleConfiguration.cs:41-147`

Added to BorderStyle class:
```csharp
[Header("Custom Renderer (Advanced)")]
public string customRendererId = "";  // Override with registry ID

public string GetEffectiveRendererId()
{
    if (!string.IsNullOrEmpty(customRendererId))
        return customRendererId;
    return MapRenderingModeToId(renderingMode);  // Enum fallback
}
```

### 6. BorderComputeDispatcher Registry Integration
**Files Changed:** `Map/Rendering/Border/BorderComputeDispatcher.cs:259-371`

- `RegisterDefaultRenderers()` - Registers ENGINE defaults on init
- `SetActiveBorderRenderer(string id)` - Switch by ID
- `GetActiveBorderRenderer()` - Get current

### 7. VisualStyleManager Registry Support
**Files Changed:** `Map/VisualStyles/VisualStyleManager.cs:169-284, 426-474, 522-557`

Updated `ApplyBorderConfiguration()`, `SwitchStyle()`, `ReloadMaterialFromAsset()` to:
- Check for `customRendererId` in VisualStyleConfiguration
- Use registry when custom ID specified
- Fall back to enum-based selection otherwise

---

## Decisions Made

### Decision 1: Registry Pattern (Not Reflection)
**Context:** How should GAME register custom renderers?

**Options:**
1. Reflection-based auto-discovery
2. Explicit registration via registry
3. ScriptableObject references

**Decision:** Explicit registry registration

**Rationale:**
- Follows IMapModeHandler pattern already in codebase
- No reflection overhead
- Clear, explicit control for GAME layer
- Easy to debug (can list registered renderers)

### Decision 2: String IDs (Not Type References)
**Context:** How to reference renderers in configuration?

**Decision:** String IDs in VisualStyleConfiguration

**Rationale:**
- Serializes to ScriptableObject cleanly
- No assembly reference issues
- Human-readable in Inspector
- Matches enum names for backwards compatibility

### Decision 3: Backwards Compatible
**Context:** Should old enum-based code still work?

**Decision:** Yes, full backwards compatibility

**Rationale:**
- Existing VisualStyleConfiguration assets unchanged
- `customRendererId = ""` means use enum
- `GetEffectiveRendererId()` handles mapping

---

## What Worked ✅

1. **Interface + Registry pattern**
   - Clean separation of mechanism (ENGINE) and policy (GAME)
   - Same pattern as IMapModeHandler - consistent architecture

2. **Wrapper implementations**
   - ENGINE defaults wrap existing code (no rewrite)
   - DistanceFieldBorderRenderer just delegates to BorderDistanceFieldGenerator

---

## Problems Encountered & Solutions

### Problem 1: Namespace Shadowing
**Symptom:** `CS0118: 'ProvinceSystem' is a namespace but is used like a type`

**Root Cause:** `Map.Core` namespace shadows root `Core` namespace when in `Map.Rendering.Border`

**Solution:**
```csharp
using CoreSystems = Core.Systems;  // Alias to avoid shadow

public struct BorderRendererContext
{
    public CoreSystems.ProvinceSystem ProvinceSystem;  // Fully qualified
}
```

---

## Architecture Impact

### New Extension Point
GAME layer can now:
1. Implement `IBorderRenderer` or extend `BorderRendererBase`
2. Register during initialization
3. Reference by ID in VisualStyleConfiguration

### Pattern Established
This pattern can extend to other pluggable systems:
- `IHighlightRenderer` - Custom selection visualization
- `IFogOfWarRenderer` - Custom fog rendering
- `ITerrainRenderer` - Custom terrain blending
- `IMapModeColorizer` - Custom map mode coloring

---

## Next Session

### Potential Extensions
1. Apply same pattern to Highlight system
2. Apply same pattern to FogOfWar system
3. Document extension pattern for GAME developers

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- Pattern: Interface + Registry + BaseClass
- Registration: `MapRendererRegistry.Instance.RegisterBorderRenderer()`
- Selection: `VisualStyleConfiguration.borders.customRendererId`
- Fallback: Empty customRendererId uses enum

**Key Files:**
- Interface: `Map/Rendering/Border/IBorderRenderer.cs`
- Registry: `Map/Rendering/MapRendererRegistry.cs`
- Implementations: `Map/Rendering/Border/Implementations/`
- Config extension: `Map/VisualStyles/VisualStyleConfiguration.cs:41-147`

**Gotchas:**
- Namespace shadowing in `Map.Rendering.Border` - use aliases for `Core.Systems`
- Registry must exist before BorderComputeDispatcher.InitializeSmoothBorders()
- Both enum-based and ID-based selection work (backwards compatible)

---

## Files Changed Summary

| File | Changes |
|------|---------|
| `IBorderRenderer.cs` | NEW - Interface + context/params structs |
| `BorderRendererBase.cs` | NEW - Abstract base with utilities |
| `MapRendererRegistry.cs` | NEW - Central registry singleton |
| `DistanceFieldBorderRenderer.cs` | NEW - Wraps existing generator |
| `PixelPerfectBorderRenderer.cs` | NEW - Wraps compute dispatch |
| `MeshGeometryBorderRenderer.cs` | NEW - Wraps mesh generation |
| `NoneBorderRenderer.cs` | NEW - No-op renderer |
| `VisualStyleConfiguration.cs` | Added customRendererId, GetEffectiveRendererId() |
| `BorderComputeDispatcher.cs` | Added registry registration, SetActiveBorderRenderer() |
| `VisualStyleManager.cs` | Added registry lookup in Apply/Switch/Reload methods |

---

*Session complete - pluggable border renderer architecture implemented*
