# Pluggable Fog of War Renderer Architecture
**Date**: 2026-01-12
**Session**: 4
**Status**: ✅ Complete
**Priority**: Medium

---

## Session Goal

**Primary Objective:**
- Apply pluggable renderer pattern to fog of war system

**Secondary Objectives:**
- Update Pattern 20 documentation with new implementation

**Success Criteria:**
- IFogOfWarRenderer interface + DefaultFogOfWarRenderer implementation
- MapRendererRegistry supports fog of war renderers
- VisualStyleConfiguration has fog of war customRendererId
- Pattern 20 updated to list IFogOfWarRenderer

---

## Context & Background

**Previous Work:**
- See: [3-pluggable-highlight-renderer.md](3-pluggable-highlight-renderer.md)
- Border rendering now pluggable via IBorderRenderer
- Highlight rendering now pluggable via IHighlightRenderer

**Current State:**
- FogOfWarSystem was hardcoded GPU compute implementation
- No extension point for custom fog effects (stylized, animated clouds, etc.)

**Why Now:**
- Consistent architecture across all rendering systems
- Pattern proven with borders and highlights, apply to fog of war

---

## What We Did

### 1. IFogOfWarRenderer Interface
**Files Created:** `Map/Rendering/FogOfWar/IFogOfWarRenderer.cs`

```csharp
public interface IFogOfWarRenderer
{
    string RendererId { get; }
    string DisplayName { get; }
    bool RequiresPerFrameUpdate { get; }

    void Initialize(MapTextureManager textureManager, FogOfWarRendererContext context);
    void SetPlayerCountry(ushort countryID);
    void UpdateVisibility();
    void RevealProvince(ushort provinceID);
    void SetEnabled(bool enabled);
    bool IsEnabled { get; }
    void ApplyToMaterial(Material material, FogOfWarStyleParams styleParams);
    void OnRenderFrame();
    float GetProvinceVisibility(ushort provinceID);
    ushort GetPlayerCountry();
    void Dispose();
}
```

Also includes: `VisibilityState` enum, `FogOfWarRendererContext`, `FogOfWarStyleParams`

### 2. FogOfWarRendererBase Abstract Class
**Files Created:** `Map/Rendering/FogOfWar/FogOfWarRendererBase.cs`

Common utilities:
- Thread group calculation
- Style parameter application
- State tracking (provinceVisibility array, playerCountryID, fogEnabled)
- Visibility constants (UNEXPLORED=0.0, EXPLORED=0.5, VISIBLE=1.0)

### 3. DefaultFogOfWarRenderer Implementation
**Files Created:** `Map/Rendering/FogOfWar/Implementations/DefaultFogOfWarRenderer.cs`

Wraps existing GPU compute shader logic:
- PopulateFogOfWar kernel for texture generation
- Visibility tracking with CPU-side cache
- Owned provinces marked visible, lost provinces demoted to explored

### 4. MapRendererRegistry Extension
**Files Changed:** `Map/Rendering/MapRendererRegistry.cs`

Added fog of war renderer support:
- `fogOfWarRenderers` dictionary
- `RegisterFogOfWarRenderer()`, `GetFogOfWarRenderer()`, `GetDefaultFogOfWarRenderer()`
- `SetDefaultFogOfWarRenderer()`, `GetAvailableFogOfWarRenderers()`, `HasFogOfWarRenderer()`
- Disposal in OnDestroy

### 5. VisualStyleConfiguration Extension
**Files Changed:** `Map/VisualStyles/VisualStyleConfiguration.cs`

Added to `FogOfWarSettings` class:
```csharp
public string customRendererId = "";

public string GetEffectiveRendererId()
{
    if (!string.IsNullOrEmpty(customRendererId))
        return customRendererId;
    return "Default";
}
```

### 6. FogOfWarSystem Integration
**Files Changed:** `Map/Rendering/FogOfWarSystem.cs`

- Added `RegisterDefaultRenderer()` - Registers with MapRendererRegistry on Initialize
- Added `GetActiveFogOfWarRenderer()` - Get renderer by ID from registry

### 7. Pattern 20 Documentation Update
**Files Changed:** `Docs/Engine/architecture-patterns.md`

Updated Current Implementations:
- Added `IFogOfWarRenderer` - Fog of war visibility rendering (Default)
- Moved from "Future" to implemented

---

## Decisions Made

### Decision 1: Same Pattern Structure
**Context:** How to structure fog of war renderer files?

**Decision:** Mirror border and highlight renderer folder structure

**Rationale:**
- Consistency across all pluggable systems
- Developers know where to find related files
- Same learning curve for all renderer types

---

## What Worked ✅

1. **Pattern reuse from highlight renderer**
   - Copy-adapt workflow very efficient
   - Same structure, just different domain
   - Minimal design decisions needed

2. **Clear visibility state model**
   - Three states (unexplored, explored, visible) map cleanly to float values
   - Easy for custom renderers to work with

---

## Architecture Impact

### Systems Now Pluggable
| System | Interface | Default Implementation |
|--------|-----------|----------------------|
| Border Rendering | IBorderRenderer | DistanceField, PixelPerfect, MeshGeometry, None |
| Highlight/Selection | IHighlightRenderer | Default (GPU compute) |
| Fog of War | IFogOfWarRenderer | Default (GPU compute) |

### Future Candidate
- ITerrainRenderer (last remaining system)

---

## Next Session

### Potential Extensions
1. Apply pattern to Terrain rendering system (final system)
2. Create example custom fog of war renderer (stylized clouds)
3. Performance comparison between different renderer implementations

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- Pattern 20 now covers borders, highlights, and fog of war
- All three follow identical structure (Interface + Base + Default + Registry)
- FogOfWarSystem still works directly, now also registers with registry

**Key Files:**
- Interface: `Map/Rendering/FogOfWar/IFogOfWarRenderer.cs`
- Implementation: `Map/Rendering/FogOfWar/Implementations/DefaultFogOfWarRenderer.cs`
- Registry: `Map/Rendering/MapRendererRegistry.cs` (now has fog methods)
- Config: `Map/VisualStyles/VisualStyleConfiguration.cs` (FogOfWarSettings.customRendererId)
- Pattern doc: `Docs/Engine/architecture-patterns.md` (Pattern 20)

**GAME Layer Usage:**
```csharp
// Register custom
MapRendererRegistry.Instance.RegisterFogOfWarRenderer(new MyStylizedFogRenderer());

// In VisualStyleConfiguration
fogOfWar.customRendererId = "Stylized";
```

---

## Files Changed Summary

| File | Changes |
|------|---------|
| `IFogOfWarRenderer.cs` | NEW - Interface + enums + structs |
| `FogOfWarRendererBase.cs` | NEW - Abstract base class |
| `DefaultFogOfWarRenderer.cs` | NEW - GPU compute implementation |
| `MapRendererRegistry.cs` | Added fog of war renderer dictionary + methods |
| `VisualStyleConfiguration.cs` | Added customRendererId to FogOfWarSettings |
| `FogOfWarSystem.cs` | Added RegisterDefaultRenderer(), GetActiveFogOfWarRenderer() |
| `architecture-patterns.md` | Updated Pattern 20 implementations list |

---

*Session complete - pluggable fog of war renderer architecture*
