# Visual Styles Architecture

**Status:** Production Standard

---

## Core Problem

Map visualization involves both mechanism (how to render) and policy (what to render). Without clear separation, ENGINE becomes polluted with GAME-specific decisions about colors, blending, and visual effects.

---

## Core Principle: Two-Level Customization

ENGINE provides rendering infrastructure with pluggable extension points. GAME customizes at two levels:

### Level 1: Fine-Grained Control
Customize individual rendering systems while using ENGINE's shader infrastructure.

**Pluggable Systems:**
- Border generation (algorithm, texture format)
- Selection/highlight visualization
- Fog of war rendering
- Terrain blending
- Map mode colorization (separate from map mode DATA)

**Trade-offs:**
- Pro: Leverage ENGINE's tested map shader
- Pro: Mix and match different implementations
- Pro: Runtime switching without shader recompilation
- Con: Constrained to ENGINE's layer model

### Level 2: Shader Copy-and-Customize (Recommended for Visual Identity)
GAME owns a thin shader shell; ENGINE owns the render pass. The shell supplies the map mode dispatcher, ENGINE supplies everything around it.

**How It Works:**
- GAME shader = Properties (per-material values only) + render state + three includes: ENGINE setup, GAME map mode dispatcher, ENGINE pass
- The ENGINE pass owns entry points, tessellation, and fragment composition; it calls the GAME dispatcher's `RenderMapMode`
- Engine-owned values (textures, map mode state, style values) are shader globals, so the shell never declares them
- ENGINE map mode files are a starting point; GAME copies and customizes them freely

**What GAME Controls:**
- Map mode rendering (political, terrain, development colors and blending)
- Custom map mode dispatcher
- Visual identity per map mode

**What ENGINE Still Provides:**
- Render pass: vertex, tessellation, fragment composition and stage order
- All texture/buffer declarations and engine globals
- Border rendering, fog of war, highlights, lighting, overlay, debug modes

**Critical Constraint:** GAME includes ENGINE, never reverse. ENGINE is a separate repository and cannot reference GAME paths.

**Trade-offs:**
- Pro: Full control over visual identity
- Pro: Still leverages ENGINE infrastructure (borders, fog, lighting)
- Pro: ENGINE updates to infrastructure and the render pass automatically apply
- Con: Must update map mode copies when ENGINE changes the map mode API
- Pro: Single stages (lighting, borders, highlights, fog, overlay, ocean) can be replaced via compile-time override hooks, without copying the pass
- Con: Stage order is fixed by ENGINE; a different order means writing your own pass (Level 3)

See [Shaders Wiki](../Wiki/Shaders.md) for step-by-step setup guide.

### Level 3: Complete Override
Provide entirely custom shader and material for complete visual control.

**What GAME Controls:**
- Complete shader code
- All rendering effects
- Custom layer ordering
- Custom blend algorithms
- Post-processing effects

**Trade-offs:**
- Pro: Total visual control
- Pro: Can ignore ENGINE's layer model entirely
- Con: Must handle all compositing logic
- Con: More maintenance burden

---

## Architecture Constraints

**ENGINE Never Imports GAME:**
- ENGINE provides interfaces + default implementations
- GAME registers custom implementations via registry
- Configuration references implementations by string ID

**Textures Are Universal:**
- ENGINE binds simulation textures (province IDs, owners, borders, etc.)
- Any shader can use these textures
- Texture format is ENGINE contract, visual interpretation is GAME policy

**Initialization Order:**
- GAME registers custom implementations during startup
- ENGINE uses whatever is registered (or defaults)
- Visual style can switch at runtime

---

## Pattern 20: Pluggable Implementation

Each rendering system follows the same pattern:

**Interface:** Contract for what the system does
**Base Class:** Common utilities and template methods
**Default Implementations:** ENGINE-provided working solutions
**Registry:** Central lookup by string ID
**Configuration:** References by ID, with backwards-compatible enum fallback

This enables GAME to replace any single system without touching others.

---

## Layer Compositing Model

Render layers combine in a defined order:
1. Base terrain/colors
2. Borders (province, country)
3. Highlights (selection, hover)
4. Fog of war
5. Overlay effects

Order and blending are fixed in the ENGINE shader. Individual layers are tuned through style parameters (strengths, colors, enable flags), not configurable blend modes. A single layer's logic can be replaced without copying the shader through compile-time per-stage override hooks; unused hooks cost nothing.

---

## When to Use Each Level

**Use Level 1 (Pluggable Interfaces) When:**
- Customizing one or few systems (e.g., just borders or fog)
- Want ENGINE's fixed layer order and blending
- Need runtime switching between presets
- Don't need custom map mode visuals

**Use Level 2 (Shader Copy-and-Customize) When:**
- Want your own visual identity for map modes
- Need custom political/terrain/development rendering
- Still want ENGINE infrastructure (borders, fog, lighting, effects)
- Most games should use this level

**Use Level 3 (Complete Override) When:**
- Need fundamentally different rendering approach
- Custom post-processing required
- Layer model doesn't fit your visual style
- Building completely unique visual identity

**Combine Levels When:**
- Level 2 shader for custom map mode visuals
- Level 1 pluggable interfaces for border/fog customization
- Still register custom renderers for texture generation

---

## Key Trade-offs

| Aspect | Level 1 (Pluggable) | Level 2 (Custom Material) |
|--------|---------------------|--------------------------|
| Control | Per-system | Complete |
| Maintenance | Lower | Higher |
| Flexibility | Constrained to layer model | Unlimited |
| Runtime switching | Easy (registry swap) | Material swap only |
| Testing burden | Shared with ENGINE | All on GAME |

---

## Guarantees

**ENGINE Guarantees:**
- Simulation textures bound to material
- Registered renderers called appropriately
- Default implementations always available
- Runtime renderer switching supported

**GAME Must Provide:**
- Configuration asset with style settings
- Custom implementations registered before use
- Material that accepts ENGINE texture names (if using Level 2)

---

## Anti-Patterns

**Don't:** Edit ENGINE shader files in place to customize visuals
**Do:** Copy ENGINE Default shaders to GAME (Level 2) or provide custom material (Level 3)

**Don't:** Hardcode GAME-specific blend modes in ENGINE
**Do:** Keep GAME-specific blending in GAME shader code

**Don't:** Create ENGINE→GAME dependencies
**Do:** Use interface + registry pattern

---

## Related Patterns

- **Pattern 1 (Engine-Game Separation):** Philosophy this implements
- **Pattern 7 (Registry):** Data lookup; this extends to implementation lookup
- **Pattern 20 (Pluggable Implementation):** Full pattern documentation

---

*Visual policy belongs in GAME. Rendering mechanism belongs in ENGINE.*
