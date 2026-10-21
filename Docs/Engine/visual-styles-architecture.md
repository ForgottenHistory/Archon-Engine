# Visual Styles Architecture

**Status:** Production Standard

---

## Core Problem

Map visualization involves both mechanism (how to render) and policy (what to render). Without clear separation, ENGINE becomes polluted with GAME-specific decisions about colors, blending, and visual effects, or GAME ends up owning copies of ENGINE rendering code.

---

## Core Principle: Engine Owns the Pass, Game Owns the Look

ENGINE owns the render pass, the data it consumes, and the contract between them. GAME supplies visual policy through narrow extension points. GAME customizes at three levels.

### Level 1: Pluggable Renderers
Replace individual rendering systems that produce map data, while keeping ENGINE's shader.

**Pluggable Systems:**
- Border generation (algorithm, texture format)
- Selection/highlight visualization
- Fog of war rendering
- Terrain blending
- Map mode colorization (separate from map mode DATA)

**Trade-offs:**
- Pro: Leverage ENGINE's tested map shader
- Pro: Mix and match implementations, switch at runtime
- Con: Constrained to ENGINE's stage model

### Level 2: Thin Shader Shell (Recommended)
GAME owns a thin shader shell; ENGINE owns the render pass.

**How It Works:**
- GAME shader = per-material properties + render state + three includes: ENGINE setup, GAME map mode dispatcher, ENGINE pass
- The ENGINE pass owns entry points, tessellation, and pixel composition; it calls the GAME dispatcher for the base color
- Single stages (ocean, lighting, borders, highlights, fog, overlay) can be replaced, and one extra stage added, through compile-time override hooks; unused hooks cost nothing
- ENGINE map mode files are a starting point; GAME copies and customizes them freely

**What GAME Controls:**
- Map mode rendering (political, terrain, development colors and blending)
- Map mode dispatcher
- Replacement or additional stages

**What ENGINE Provides:**
- Render pass: vertex, tessellation, stage order and composition
- All engine data as shader globals
- Default stages: borders, fog of war, highlights, lighting, overlay, debug modes

**Trade-offs:**
- Pro: Full control over map mode visuals and individual stages
- Pro: ENGINE fixes to the pass and stages reach every game automatically
- Con: Map mode copies must follow ENGINE map mode API changes
- Con: Stage order is fixed; a different order means Level 3

See [Shaders Wiki](../Wiki/Shaders.md) for the step-by-step setup.

### Level 3: Own Pass
GAME writes its own pass (still consuming ENGINE globals and the contract).

**What GAME Controls:**
- Complete shader code, stage order, blending, post-processing

**Trade-offs:**
- Pro: Total visual control
- Con: GAME owns all compositing logic and its maintenance

---

## Engine Data as Shader Globals

**Principle:** Everything ENGINE produces (simulation and generated textures, map mode state, style values) is published as shader globals, not material properties.

- Shaders declare the globals; they never list them as material properties (a material property shadows the global, freezing it at the material default)
- Material swaps need no rebinding; any shader (map, border mesh, units) can read the same data
- Per-material values are limited to what describes the material itself (e.g. tessellation geometry) and GAME tweakables

**Trade-off:** One set of globals per process. A second simultaneous map with different data needs per-material overrides.

---

## The Contract

**Principle:** The ENGINE/shader contract is defined once, in a parameter registry: name, type, scope (global or material), default, required, and whether styles may set it.

- Defaults for globals come from the registry, replacing per-shader property defaults
- Every style application validates the map material: engine globals listed as material properties, and missing required material properties, are reported as errors naming the shader and property
- GAME registers its own parameters (e.g. values read by its override hooks) without ENGINE changes

**Engine-managed vs style-settable:** Simulation data and runtime state (textures, map mode, border mode, fog toggle) are engine-managed and refused from styles, so shader state cannot drift from C# state. Look values (colors, strengths, overlay) are style-settable.

---

## Style Layering

A visual style is applied in layers, later layers winning:
1. Style asset (editor-authored defaults, structural settings)
2. Custom shader parameters carried by the asset (GAME parameters, generic loop)
3. Style data file (data-driven, override-first resolution, moddable in shipped builds)

Structural settings that drive C# systems (border rendering mode, tessellation on/off) stay asset-only. Data files carry shader values only.

**Hot reload:** In the editor (optionally in development builds) the data file is watched and the style re-applied on change.

---

## Layer Compositing Model

Stages combine in a fixed order:
1. Base color (GAME map mode dispatcher)
2. Lighting
3. Borders (province, country)
4. Highlights (selection, hover)
5. Fog of war
6. Overlay effects
7. Optional GAME post-composite stage

Order and blending are fixed in ENGINE. Stages are tuned through style values and replaced through compile-time hooks, not runtime blend-mode configuration.

---

## Architecture Constraints

**ENGINE Never Imports GAME:**
- ENGINE provides interfaces, default implementations and default stages
- GAME registers implementations and parameters; configuration references implementations by string ID
- GAME includes ENGINE shader code, never the reverse

**Initialization Order:**
- ENGINE registers its parameters and uploads global defaults before any binding
- GAME registers implementations and parameters before the style is applied
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

## When to Use Each Level

**Level 1 (Pluggable Renderers):** Replacing how one kind of map data is produced (borders, fog, terrain blending), runtime switching between presets.

**Level 2 (Thin Shell):** Own visual identity for map modes, or replacing individual stages. Most games should use this level.

**Level 3 (Own Pass):** Fundamentally different rendering, different stage order, custom post-processing.

**Combine:** Level 2 shell for visuals plus Level 1 renderers for data generation.

---

## Key Trade-offs

| Aspect | Level 1 (Pluggable) | Level 2 (Thin Shell) | Level 3 (Own Pass) |
|--------|---------------------|----------------------|--------------------|
| Control | Per-system data | Map modes + stages | Complete |
| Maintenance | Lowest | Low (ENGINE owns pass) | High |
| ENGINE updates | Automatic | Automatic (pass + stages) | Manual |
| Stage order | ENGINE | ENGINE | GAME |

---

## Guarantees

**ENGINE Guarantees:**
- Engine data published as shader globals, with defaults before first use
- Contract validated on every style application
- Registered renderers called appropriately; defaults always available
- Engine-managed state never overwritten by styles

**GAME Must Provide:**
- Style asset with settings
- Implementations and parameters registered before use
- Map shaders that declare no engine global as a material property (Level 2/3)

---

## Anti-Patterns

**Don't:** Copy ENGINE pass code into GAME shaders
**Do:** Include the ENGINE pass and replace single stages through hooks

**Don't:** Declare engine data as material properties
**Do:** Read it as shader globals; keep material properties for per-material values

**Don't:** Hardcode GAME-specific colors or blending in ENGINE
**Do:** Keep them in GAME shader code, style assets, or style data files

**Don't:** Create ENGINE→GAME dependencies
**Do:** Use interface + registry patterns

---

## Related Patterns

- **Pattern 1 (Engine-Game Separation):** Philosophy this implements
- **Pattern 7 (Registry):** Data lookup; extended to implementations and shader parameters
- **Pattern 17 (Single Source of Truth):** One contract definition
- **Pattern 20 (Pluggable Implementation):** Full pattern documentation

---

*Visual policy belongs in GAME. Rendering mechanism belongs in ENGINE.*
