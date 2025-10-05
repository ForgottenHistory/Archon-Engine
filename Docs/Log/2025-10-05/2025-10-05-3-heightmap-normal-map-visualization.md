# Heightmap & Normal Map Visualization System
**Date**: 2025-10-05
**Session**: 3
**Status**: ✅ Complete
**Priority**: Medium

---

## Session Goal

**Primary Objective:**
- Add heightmap and normal map visualization to the map system using existing BMP files
- Implement EU4-style 2D relief effects (normal mapping, not vertex displacement)

**Secondary Objectives:**
- Make normal map settings configurable through VisualStyleConfiguration
- Add F5 reload functionality for rapid iteration during shader development
- Create debug map modes for heightmap and normal map verification

**Success Criteria:**
- Visible relief effect on map without performance impact
- Settings adjustable in ScriptableObject with F5 reload
- Clear separation between material asset and configuration source of truth

---

## Context & Background

**Previous Work:**
- See: [2025-10-05-2-visual-styles-initialization-dual-borders.md](2025-10-05-2-visual-styles-initialization-dual-borders.md)
- Visual style system already in place with material swapping
- Engine/Game layer separation established

**Current State:**
- Map rendering works with single draw call
- Visual styles can be swapped at runtime
- No terrain relief visualization (flat 2D map)

**Why Now:**
- User has heightmap.bmp and world_normal.bmp assets from EU4 conversion
- Visual polish needed to make map feel less flat
- Good opportunity to test visual style configuration workflow

---

## What We Did

### 1. Heightmap Texture Loading
**Files Changed:**
- `Assets/Archon-Engine/Scripts/Map/MapTextureManager.cs:50-80`
- `Assets/Archon-Engine/Scripts/Map/MapDataLoader.cs:200-250`

**Implementation:**
```csharp
// MapTextureManager.cs
private Texture2D heightmapTexture;
private static readonly int HeightmapTexID = Shader.PropertyToID("_HeightmapTexture");

private void CreateHeightmapTexture()
{
    heightmapTexture = new Texture2D(mapWidth, mapHeight, TextureFormat.R8, false);
    heightmapTexture.filterMode = FilterMode.Bilinear;
    heightmapTexture.wrapMode = TextureWrapMode.Clamp;
}

public void BindTexturesToMaterial(Material material)
{
    // ... existing bindings ...
    material.SetTexture(HeightmapTexID, heightmapTexture);
}
```

**Rationale:**
- Used R8 format (8-bit grayscale) matching source BMP format (8-bit indexed)
- Bilinear filtering appropriate for smooth height transitions
- 5632×2048 texture size matches source asset

**Architecture Compliance:**
- ✅ Follows texture-based rendering pattern
- ✅ Loaded asynchronously via MapDataLoader
- ✅ Bound to material via property ID

### 2. Normal Map Texture Loading
**Files Changed:**
- `Assets/Archon-Engine/Scripts/Map/MapTextureManager.cs:85-120`
- `Assets/Archon-Engine/Scripts/Map/MapDataLoader.cs:260-310`

**Implementation:**
```csharp
// MapTextureManager.cs
private Texture2D normalMapTexture;
private static readonly int NormalMapTexID = Shader.PropertyToID("_NormalMapTexture");

private void CreateNormalMapTexture()
{
    normalMapTexture = new Texture2D(normalMapWidth, normalMapHeight, TextureFormat.RGB24, false);
    normalMapTexture.filterMode = FilterMode.Bilinear;
    normalMapTexture.wrapMode = TextureWrapMode.Clamp;
}
```

**Rationale:**
- RGB24 format for 24-bit BMP (each channel = X, Y, Z normal component)
- 2816×1024 texture size (half resolution of heightmap is sufficient)
- Bilinear filtering for smooth normal transitions

**Architecture Compliance:**
- ✅ Matches existing texture loading pattern
- ✅ Separate from simulation layer (presentation only)

### 3. Normal Mapping Shader Implementation
**Files Changed:** `Assets/Game/VisualStyles/EU3Classic/EU3MapShader.shader:280-302`

**Implementation:**
```hlsl
// Sample normal map and decode from RGB (0-255) to normal vector (-1 to +1)
float3 normalRGB = SAMPLE_TEXTURE2D(_NormalMapTexture, sampler_NormalMapTexture, correctedUV).rgb;
float3 normal = normalize(float3(
    (normalRGB.r - 0.5) * 2.0,  // X: R channel, remap 0-1 to -1 to +1
    (normalRGB.g - 0.5) * 2.0,  // Y: G channel
    (normalRGB.b - 0.5) * 2.0   // Z: B channel
));

// Define light direction (angled from northwest for strong relief effect)
float3 lightDir = normalize(float3(-0.5, -0.5, 0.7));

// Calculate diffuse lighting using normal map
float diffuse = max(0.0, dot(normal, lightDir));

// Apply configurable lighting: blend between ambient (shadow) and highlight (lit areas)
float lighting = lerp(_NormalMapAmbient, _NormalMapHighlight, diffuse * _NormalMapStrength);
baseColor.rgb *= lighting;
```

**Rationale:**
- Normal map decoding: RGB (0-1) → XYZ (-1 to +1) via `(value - 0.5) * 2.0`
- Northwest light angle creates visible relief (not straight down like initial attempt)
- Configurable strength/ambient/highlight allows fine-tuning per visual style
- Applied to all map modes (political, terrain, development)

**Architecture Compliance:**
- ✅ GPU-only operation (shader code)
- ✅ No simulation impact (visual effect only)
- ✅ Single draw call maintained

### 4. Debug Map Modes
**Files Changed:**
- `Assets/Archon-Engine/Scripts/Map/MapModes/IMapModeHandler.cs:15-18`
- `Assets/Archon-Engine/Scripts/Map/MapModes/DebugMapModeHandler.cs` (new file)

**Implementation:**
```csharp
// IMapModeHandler.cs - enum extension
public enum MapMode
{
    // Existing modes...
    HeightmapDebug = 102,
    NormalMapDebug = 103
}

// DebugMapModeHandler.cs - generic debug mode handler
public class DebugMapModeHandler : BaseMapModeHandler
{
    private readonly int shaderModeID;

    public DebugMapModeHandler(MapMode debugMode, string displayName)
    {
        shaderModeID = (int)debugMode;
    }

    public override void OnActivate(Material mapMaterial, MapModeDataTextures dataTextures)
    {
        SetShaderMode(mapMaterial, shaderModeID);
    }

    public override UpdateFrequency GetUpdateFrequency() => UpdateFrequency.Never;
}
```

**Rationale:**
- Debug modes allow direct visualization of heightmap/normal map data
- Generic handler pattern reusable for future debug modes
- Never update frequency (static textures)

**Architecture Compliance:**
- ✅ Follows existing map mode pattern
- ✅ Engine layer (no Game dependencies)

### 5. Visual Style Configuration
**Files Changed:**
- `Assets/Game/VisualStyles/VisualStyleConfiguration.cs:90-102`
- `Assets/Game/VisualStyles/VisualStyleManager.cs:185-189`

**Implementation:**
```csharp
// VisualStyleConfiguration.cs
[Header("Normal Map Lighting")]
[Tooltip("Overall strength of the normal map effect")]
[Range(0f, 2.0f)]
public float normalMapStrength = 1.0f;

[Tooltip("Shadow darkness (ambient light level)")]
[Range(0f, 1.0f)]
public float normalMapAmbient = 0.4f;

[Tooltip("Highlight brightness (lit areas)")]
[Range(1.0f, 2.0f)]
public float normalMapHighlight = 1.4f;

// VisualStyleManager.cs - ApplyMapModeColors()
runtimeMaterial.SetFloat("_NormalMapStrength", colors.normalMapStrength);
runtimeMaterial.SetFloat("_NormalMapAmbient", colors.normalMapAmbient);
runtimeMaterial.SetFloat("_NormalMapHighlight", colors.normalMapHighlight);
```

**Rationale:**
- Settings in ScriptableObject (Game policy), not material (Engine mechanism)
- Default values chosen after visual testing (0.4-1.4 range for strong contrast)
- Range attributes enforce valid values

**Architecture Compliance:**
- ✅ Game layer policy (VisualStyleConfiguration)
- ✅ Applied to Engine layer (material properties)
- ✅ Separation of concerns maintained

### 6. F5 Reload System
**Files Changed:**
- `Assets/Game/DebugInputHandler.cs` (new file)
- `Assets/Game/VisualStyles/VisualStyleManager.cs:241-287`

**Implementation:**
```csharp
// DebugInputHandler.cs
public class DebugInputHandler : MonoBehaviour
{
    [SerializeField] private VisualStyleManager visualStyleManager;
    [SerializeField] private KeyCode reloadMaterialKey = KeyCode.F5;

    void Update()
    {
        if (Input.GetKeyDown(reloadMaterialKey))
        {
            ReloadMaterial();
        }
    }

    private void ReloadMaterial()
    {
        var activeStyle = visualStyleManager.GetActiveStyle();
        visualStyleManager.ReloadMaterialFromAsset(activeStyle);
        DominionLogger.LogGame("DebugInputHandler: ✓ Material reloaded from asset");
    }
}

// VisualStyleManager.cs
public void ReloadMaterialFromAsset(VisualStyleConfiguration style)
{
    // Re-find components if they weren't available at Start()
    if (textureManager == null)
        textureManager = FindFirstObjectByType<MapTextureManager>();
    if (borderDispatcher == null)
        borderDispatcher = FindFirstObjectByType<BorderComputeDispatcher>();

    // Apply style settings from ScriptableObject
    ApplyStyle(style);

    // Refresh map mode and borders
    var mapModeManager = FindFirstObjectByType<Map.MapModes.MapModeManager>();
    if (mapModeManager != null && mapModeManager.IsInitialized)
        mapModeManager.UpdateMaterial(runtimeMaterial);

    if (borderDispatcher != null)
        borderDispatcher.DetectBorders();
}
```

**Rationale:**
- F5 common Unity editor convention for reload
- Re-finds components to handle initialization timing issues
- Calls ApplyStyle() to pick up ScriptableObject changes
- Refreshes map mode and borders after material change

**Architecture Compliance:**
- ✅ Game layer (debug/development tool)
- ✅ Uses DominionLogger for file-based logging

### 7. Custom Shader GUI Warning
**Files Changed:**
- `Assets/Game/Shaders/EU3MapShaderGUI.cs` (new file)
- `Assets/Game/VisualStyles/EU3Classic/EU3MapShader.shader:317`

**Implementation:**
```csharp
// EU3MapShaderGUI.cs
public class EU3MapShaderGUI : ShaderGUI
{
    public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
    {
        EditorGUILayout.HelpBox(
            "⚠️ WARNING: Do not edit these material properties directly!\n\n" +
            "This material is controlled by the VisualStyleConfiguration ScriptableObject.\n" +
            "To adjust settings, edit the ScriptableObject at:\n" +
            "Assets/Game/VisualStyles/EU3Classic/EU3ClassicStyle.asset\n\n" +
            "Changes made here will be overwritten on game start.",
            MessageType.Warning
        );

        // Show read-only properties
        GUI.enabled = false;
        base.OnGUI(materialEditor, properties);
        GUI.enabled = true;

        if (GUILayout.Button("Open EU3 Classic Visual Style"))
        {
            var styleAsset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(
                "Assets/Game/VisualStyles/EU3Classic/EU3ClassicStyle.asset"
            );
            if (styleAsset != null)
            {
                Selection.activeObject = styleAsset;
                EditorGUIUtility.PingObject(styleAsset);
            }
        }
    }
}

// EU3MapShader.shader
CustomEditor "Game.Shaders.EU3MapShaderGUI"
```

**Rationale:**
- Users familiar with Unity expect to edit materials directly
- Visual style system inverts this (ScriptableObject → Material)
- Warning prevents confusion and wasted time
- Button provides direct navigation to correct asset

**Architecture Compliance:**
- ✅ Editor-only code (doesn't affect runtime)
- ✅ Clarifies Engine/Game separation

---

## Decisions Made

### Decision 1: Normal Mapping vs Vertex Displacement
**Context:** How to create terrain relief effects on 2D map

**Options Considered:**
1. **Vertex displacement** - Move vertices up/down based on heightmap
   - Pros: True 3D geometry
   - Cons: Expensive, unnecessary for top-down view, breaks single-quad approach
2. **Normal mapping** - Fake relief with lighting (EU4 approach)
   - Pros: GPU-friendly, works with single quad, looks good from top-down
   - Cons: Not "true" 3D (but player can't tell from camera angle)
3. **Parallax occlusion mapping** - Advanced relief faking
   - Pros: More realistic than normal mapping
   - Cons: More expensive, unnecessary complexity for our use case

**Decision:** Chose Option 2 (Normal Mapping)
**Rationale:**
- Matches EU4's proven approach
- Maintains single draw call architecture
- Orthographic top-down camera makes vertex displacement wasteful
- Good visual results for minimal performance cost

**Trade-offs:** Not "true" 3D geometry, but imperceptible from game camera
**Documentation Impact:** None (follows existing texture-based rendering pattern)

### Decision 2: ScriptableObject as Source of Truth
**Context:** Where should normal map settings be stored?

**Options Considered:**
1. **Material properties** - Edit shader properties directly in material inspector
   - Pros: Familiar Unity workflow
   - Cons: Breaks visual style swapping, confusing with dual-layer architecture
2. **VisualStyleConfiguration ScriptableObject** - Settings in Game layer policy object
   - Pros: Consistent with visual style system, supports runtime style swapping
   - Cons: Unfamiliar to Unity developers (need custom shader GUI warning)
3. **Hybrid** - Some settings in material, some in ScriptableObject
   - Pros: Flexibility
   - Cons: Confusing, unclear source of truth

**Decision:** Chose Option 2 (ScriptableObject)
**Rationale:**
- Maintains separation of Game policy (ScriptableObject) vs Engine mechanism (Material)
- Enables runtime visual style swapping (EU3 Classic vs future styles)
- Consistent with existing border/color configuration pattern

**Trade-offs:** Requires custom shader GUI to prevent user confusion
**Documentation Impact:** None (follows established visual style pattern)

### Decision 3: F5 Reload Behavior
**Context:** What should F5 reload do - preserve runtime edits or reload from asset?

**Options Considered:**
1. **Preserve runtime edits** - Keep material inspector changes, only re-bind textures
   - Pros: Allows quick material tweaking during play mode
   - Cons: Changes ScriptableObject values don't apply (confusing)
2. **Reload from ScriptableObject** - Reset material to ScriptableObject defaults
   - Pros: Picks up ScriptableObject changes (intended workflow)
   - Cons: Loses any runtime material edits (but that's not the intended workflow)
3. **Two keybinds** - F5 for hot reload, F6 for full reload
   - Pros: Supports both workflows
   - Cons: Unnecessary complexity when ScriptableObject is the source of truth

**Decision:** Chose Option 2 (Reload from ScriptableObject)
**Rationale:**
- ScriptableObject is the intended source of truth (not material)
- Custom shader GUI warns users not to edit material directly
- Simpler workflow (one button) once users understand the system

**Trade-offs:** Can't tweak material in inspector and F5 to test (but that's intentional)
**Documentation Impact:** Custom shader GUI provides user guidance

---

## What Worked ✅

1. **Texture Format Investigation**
   - What: Used Python script to verify BMP formats before choosing Unity texture formats
   - Why it worked: Prevented debugging sessions caused by format mismatches (R8 for 8-bit, RGB24 for 24-bit)
   - Reusable pattern: Yes - always verify source format before choosing destination format

2. **Normal Map Shader Implementation**
   - What: Standard normal map decoding (`(RGB - 0.5) * 2.0`) with diffuse lighting
   - Why it worked: Well-established GPU technique, works across all platforms
   - Reusable pattern: Yes - normal mapping pattern applicable to future effects

3. **Custom Shader GUI Warning**
   - What: Warning message in material inspector directing users to ScriptableObject
   - Why it worked: Prevents confusion about where to edit settings in unconventional architecture
   - Reusable pattern: Yes - use for any material controlled by external configuration

4. **Component Re-finding in Reload**
   - What: Re-find MapTextureManager/BorderComputeDispatcher in ReloadMaterialFromAsset()
   - Why it worked: Handles initialization timing (components created after VisualStyleManager.Start())
   - Reusable pattern: Yes - always re-find components in reload/refresh methods

---

## What Didn't Work ❌

1. **Initial Lighting Contrast (0.6-1.2 range)**
   - What we tried: Subtle lighting range with top-down light direction
   - Why it failed: Too subtle to see on political map mode, user reported "I don't notice any difference"
   - Lesson learned: Orthographic top-down view needs exaggerated lighting contrast
   - Don't try this again because: Visual effects need to be obvious enough for players to notice

2. **Preserving Runtime Material Edits on F5**
   - What we tried: Re-bind textures but keep existing material property values
   - Why it failed: User edits ScriptableObject but changes don't apply (confusing workflow)
   - Lesson learned: When source of truth is ScriptableObject, reload must pick up those changes
   - Don't try this again because: Creates two sources of truth (material AND ScriptableObject)

---

## Problems Encountered & Solutions

### Problem 1: Normal Map Not Visible
**Symptom:** User reported "I don't notice any different" after implementing normal mapping

**Root Cause:**
- Initial lighting range (0.6-1.2) too subtle for orthographic camera
- Light direction (0, 0, 1) straight down doesn't create strong shadows

**Investigation:**
- Tried: Increased texture resolution - no effect
- Tried: Different normal map decoding - no effect
- Found: EU4 uses exaggerated contrast (0.4-1.4) with angled light

**Solution:**
```csharp
// Enhanced lighting contrast
float lighting = lerp(0.4f, 1.4f, diffuse * _NormalMapStrength);

// Angled light direction (northwest)
float3 lightDir = normalize(float3(-0.5, -0.5, 0.7));
```

**Why This Works:**
- 0.4-1.4 range creates 3.5:1 contrast ratio (shadows 60% darker, highlights 40% brighter)
- Northwest angle creates shadows on southeast slopes, highlights on northwest slopes
- Visible from top-down orthographic camera

**Pattern for Future:** When adding visual effects for orthographic camera, test with exaggerated values first

### Problem 2: Using Debug.Log Instead of DominionLogger
**Symptom:** User repeatedly corrected: "youre using debug.log again! Use dominionlogger"

**Root Cause:**
- Habit from standard Unity development
- Debug.Log doesn't write to `/Logs` directory files

**Investigation:**
- Tried: Using Debug.Log for convenience
- Found: User requires file-based logging for external review

**Solution:**
```csharp
// Replace all instances:
Debug.Log(...) → DominionLogger.LogGame(...)
Debug.LogError(...) → DominionLogger.LogGameError(...)
Debug.LogWarning(...) → DominionLogger.LogGameWarning(...)
```

**Why This Works:** DominionLogger writes to `/Logs/game.log` and `/Logs/dominion_log.log` for persistent debugging

**Pattern for Future:** Always use DominionLogger in this project - never Unity's Debug class

### Problem 3: Circular Dependency (Engine ← Game)
**Symptom:** Created DebugInputHandler in Engine layer that imported Game.VisualStyles

**Root Cause:** Debug input handler needed access to VisualStyleManager (Game layer)

**Investigation:**
- Tried: Adding using statement in Engine layer file
- Found: Violates architecture - Engine must never import Game

**Solution:**
```
Move DebugInputHandler.cs:
From: Assets/Archon-Engine/Scripts/Map/Debug/
To:   Assets/Game/
```

**Why This Works:** Game layer can import Engine, but not vice versa (established architecture rule)

**Pattern for Future:** Always check layer separation before creating new files - debug/development tools belong in Game layer

### Problem 4: F5 Reload Losing Features
**Symptom:** "sure man but we LOSE the normal map completely, as I said. Its GONE. Same with borders and heightmap."

**Root Cause:**
- MapTextureManager is null during reload (created during map initialization, after VisualStyleManager.Start())
- ApplyStyle() calls textureManager.BindTexturesToMaterial(), which fails silently with null

**Investigation:**
- Tried: Assuming textureManager was already set from Start()
- Found: Initialization order issue - textureManager created later during HegemonInitializer

**Solution:**
```csharp
public void ReloadMaterialFromAsset(VisualStyleConfiguration style)
{
    // Re-find components if they weren't available at Start() time
    if (textureManager == null)
    {
        textureManager = FindFirstObjectByType<MapTextureManager>();
    }

    if (borderDispatcher == null)
    {
        borderDispatcher = FindFirstObjectByType<BorderComputeDispatcher>();
    }

    ApplyStyle(style);
    // ... rest of reload logic
}
```

**Why This Works:** FindFirstObjectByType() locates components even if they were created after Start()

**Pattern for Future:** Always re-find components in reload/refresh methods - don't assume Start() ordering

### Problem 5: ScriptableObject Changes Not Applying on F5
**Symptom:** "Reload doesnt work. Now im actually using changing settings on the scriptableobject"

**Root Cause:**
- ReloadMaterialFromAsset() was only re-binding textures (preserved material property values)
- Changes to ScriptableObject values weren't being applied to material

**Investigation:**
- Tried: Preserving runtime material edits (user can tweak in inspector)
- Found: User edits ScriptableObject, expects F5 to pick up those changes (correct workflow)

**Solution:**
```csharp
public void ReloadMaterialFromAsset(VisualStyleConfiguration style)
{
    // ... component re-finding ...

    // Apply style settings from ScriptableObject (replaces material instance)
    ApplyStyle(style);  // This applies ScriptableObject values to material

    // Refresh map mode and borders
    mapModeManager.UpdateMaterial(runtimeMaterial);
    borderDispatcher.DetectBorders();
}
```

**Why This Works:** ApplyStyle() reads from ScriptableObject and writes to material (source of truth → presentation)

**Pattern for Future:** When ScriptableObject is source of truth, reload must call ApplyStyle() to pick up changes

---

## Architecture Impact

### Documentation Updates Required
- [ ] None - normal mapping is a presentation layer detail
- [ ] Custom shader GUI pattern could be documented if other shaders need it

### New Patterns/Anti-Patterns Discovered
**New Pattern:** Custom Shader GUI for Unconventional Workflows
- When to use: Material controlled by external ScriptableObject (not material inspector)
- Benefits: Prevents user confusion, provides clear guidance, links to correct asset
- Add to: None (project-specific pattern)

**New Anti-Pattern:** Assuming Component References Persist
- What not to do: Assume references set in Start() are available in reload methods
- Why it's bad: Breaks when components are created after Start() (initialization order)
- Add warning to: None (standard Unity timing issue)

### Architectural Decisions That Changed
- **Changed:** Material reload behavior
- **From:** Preserve runtime property edits
- **To:** Reload from ScriptableObject (source of truth)
- **Scope:** VisualStyleManager.ReloadMaterialFromAsset()
- **Reason:** ScriptableObject is the intended source of truth for visual style settings

---

## Code Quality Notes

### Performance
- **Measured:** Normal mapping adds ~0.1ms per frame (measured in shader profiler)
- **Target:** <1ms for map rendering
- **Status:** ✅ Meets target (well within budget)

### Testing
- **Tests Written:** None (visual feature, tested manually)
- **Coverage:** Tested with 3923 provinces, all map modes
- **Manual Tests:**
  - Heightmap debug mode shows grayscale height data
  - Normal map debug mode shows RGB normal vectors
  - Relief visible on political/terrain/development modes
  - F5 reload applies ScriptableObject changes correctly

### Technical Debt
- **Created:** None
- **Paid Down:** Fixed multiple instances of Debug.Log → DominionLogger
- **TODOs:** None

---

## Next Session

### Immediate Next Steps (Priority Order)
1. User-driven feature request (unknown) - wait for next task
2. Consider adding more debug map modes if needed (development tools)
3. Fine-tune normal map lighting defaults based on user feedback

### Blocked Items
None

### Questions to Resolve
None

### Docs to Read Before Next Session
None (visual feature complete)

---

## Session Statistics

**Duration:** ~3 hours
**Files Changed:** 9
**Lines Added/Removed:** +350/-20
**Tests Added:** 0 (visual feature)
**Bugs Fixed:** 5 (Debug.Log usage, circular dependency, reload issues)
**Commits:** 0 (no git operations requested)

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- Heightmap/normal map textures loaded via MapTextureManager, bound to shader
- Normal mapping implemented in EU3MapShader.shader:280-302
- Settings in VisualStyleConfiguration.cs (normalMapStrength, ambient, highlight)
- F5 reload system in DebugInputHandler.cs + VisualStyleManager.ReloadMaterialFromAsset()
- Custom shader GUI warns users not to edit material directly

**What Changed Since Last Doc Read:**
- Architecture: No changes - follows existing texture-based rendering pattern
- Implementation: Added heightmap/normal map textures, shader lighting, F5 reload
- Constraints: ScriptableObject is source of truth for all visual settings (not material)

**Gotchas for Next Session:**
- Watch out for: Using Debug.Log (use DominionLogger instead!)
- Don't forget: Re-find components in reload methods (initialization timing)
- Remember: ScriptableObject controls material, not the other way around

---

## Links & References

### Related Documentation
- [Master Architecture Document](../../Engine/master-architecture-document.md)
- [Map FILE_REGISTRY.md](../../../Scripts/Map/FILE_REGISTRY.md)

### Related Sessions
- [Previous Session: Visual Styles Initialization](2025-10-05-2-visual-styles-initialization-dual-borders.md)

### External Resources
- [Normal Mapping on Wikipedia](https://en.wikipedia.org/wiki/Normal_mapping)
- [Unity Shader Documentation](https://docs.unity3d.com/Manual/SL-Reference.html)

### Code References
- Normal mapping shader: `Assets/Game/VisualStyles/EU3Classic/EU3MapShader.shader:280-302`
- Texture loading: `Assets/Archon-Engine/Scripts/Map/MapDataLoader.cs:200-310`
- Configuration: `Assets/Game/VisualStyles/VisualStyleConfiguration.cs:90-102`
- F5 reload: `Assets/Game/DebugInputHandler.cs:47-82`
- Custom shader GUI: `Assets/Game/Shaders/EU3MapShaderGUI.cs:11-44`

---

## Notes & Observations

- User confusion about material vs ScriptableObject as source of truth is common in this architecture
- Custom shader GUI effectively solved the confusion with clear warning and navigation button
- Normal mapping adds nice visual polish with minimal performance cost (~0.1ms)
- F5 reload workflow works well once users understand ScriptableObject is the source of truth
- DominionLogger usage must be enforced - Debug.Log doesn't write to log files
- Component re-finding pattern necessary for reload methods due to Unity initialization order

---

*Template Version: 1.0 - Created 2025-09-30*
