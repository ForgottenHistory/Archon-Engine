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
- Layer compositing (blend modes, visibility, order)

**Trade-offs:**
- Pro: Leverage ENGINE's tested compositing shader
- Pro: Mix and match different implementations
- Pro: Runtime switching without shader recompilation
- Con: Constrained to ENGINE's layer model

### Level 2: Complete Override
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

Each layer supports configurable blend modes:
- Normal (alpha lerp)
- Multiply (darkening)
- Screen (lightening)
- Overlay (contrast)
- Additive
- Soft light

Layers can be enabled/disabled independently for performance or visual style.

---

## When to Use Each Level

**Use Level 1 (Pluggable Interfaces) When:**
- Customizing one or few systems
- Want ENGINE's compositor handling layer blending
- Need runtime switching between presets
- Don't need exotic rendering effects

**Use Level 2 (Custom Material) When:**
- Need fundamentally different rendering approach
- Custom post-processing required
- Layer model doesn't fit your visual style
- Building completely unique visual identity

**Combine Both When:**
- Custom material for unique look
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

**Don't:** Fork ENGINE shaders to customize visuals
**Do:** Implement pluggable interface or provide custom material

**Don't:** Hardcode GAME-specific blend modes in ENGINE
**Do:** Make blend modes configurable via compositor

**Don't:** Create ENGINE→GAME dependencies
**Do:** Use interface + registry pattern

---

## Related Patterns

- **Pattern 1 (Engine-Game Separation):** Philosophy this implements
- **Pattern 7 (Registry):** Data lookup; this extends to implementation lookup
- **Pattern 20 (Pluggable Implementation):** Full pattern documentation

---

*Visual policy belongs in GAME. Rendering mechanism belongs in ENGINE.*
