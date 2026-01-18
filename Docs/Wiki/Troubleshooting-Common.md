# Troubleshooting: Common Issues

This guide documents common development issues encountered during Archon Engine development.

## Architecture Layer Violations

### Symptom
- Compile errors about missing types
- Circular dependency warnings
- "Type X not found in namespace Y"

### Root Cause
Importing from wrong layer. Import rules:
- **Core** → nothing (pure simulation)
- **Map** → Core only
- **Game** → Core + Map

```csharp
// ❌ WRONG - Map importing Game
using Game.Systems;  // Map cannot import Game!

// ❌ WRONG - Core importing Map
using Map.Rendering;  // Core cannot import Map!
```

### Solution
Check namespace and move code to correct layer:

```csharp
// Core: Pure simulation, no Unity dependencies
namespace Core.Systems { ... }

// Map: Rendering, can use Core
namespace Map.Rendering { ... }

// Game: Policy, can use both
namespace Game.Systems { ... }
```

### Key Lesson
"Archon" in namespace = ENGINE layer. Check FILE_REGISTRY.md for correct locations.

---

## Determinism Violations (Float in Simulation)

### Symptom
- Multiplayer desync
- Different results on different machines
- Replay diverges from original

### Root Cause
Using `float` or `double` in simulation code:

```csharp
// ❌ WRONG - Float is non-deterministic
float income = provinces * 1.5f;

// ❌ WRONG - Double also non-deterministic
double growth = population * 0.02;
```

### Solution
Use `FixedPoint64` for ALL simulation math:

```csharp
// ✅ CORRECT - FixedPoint64 is deterministic
FixedPoint64 income = provinceCount * FixedPoint64.FromFraction(3, 2); // 1.5

// ✅ CORRECT - Exact fractions
FixedPoint64 growth = population * FixedPoint64.FromFraction(2, 100); // 0.02
```

### Key Lesson
NEVER use float/double in Core namespace. FixedPoint64 guarantees identical results across all platforms.

---

## Dictionary Iteration Order Non-Determinism

### Symptom
- Different execution order on different runs
- Multiplayer desync with same commands
- Tests pass locally, fail in CI

### Root Cause
`Dictionary<K,V>` iteration order is undefined:

```csharp
// ❌ WRONG - Order varies between runs
foreach (var kvp in countryData)
{
    ProcessCountry(kvp.Key);  // Different order each time!
}
```

### Solution
Sort keys before iteration or use ordered collections:

```csharp
// ✅ CORRECT - Sorted iteration
var sortedKeys = countryData.Keys.OrderBy(k => k).ToList();
foreach (var key in sortedKeys)
{
    ProcessCountry(key);
}

// ✅ BETTER - Use SortedDictionary if order matters
SortedDictionary<ushort, CountryData> orderedCountryData;
```

### Key Lesson
Any iteration that affects game state must have deterministic order. Sort explicitly.

---

## Missing EventBus.ProcessEvents() Call

### Symptom
- Events queued but handlers never called
- Subscriptions work, emissions work, but no callbacks
- UI never updates despite events

### Root Cause
`EventBus.ProcessEvents()` not called in game loop:

```csharp
// ❌ Missing event processing
void Update()
{
    // Game logic runs but events never fire
}
```

### Solution
Ensure `ProcessEvents()` is called in `GameState.Update()`:

```csharp
// ✅ CORRECT - Process events each frame
void Update()
{
    if (!IsInitialized) return;

    EventBus?.ProcessEvents();  // Frame-coherent batching
}
```

### Key Lesson
EventBus uses frame-coherent batching. Events must be processed explicitly.

---

## Wrong API: GetState vs GetProvinceState

### Symptom
- `'ProvinceSystem' does not contain a definition for 'GetState'`
- Code compiles in documentation but fails in practice

### Root Cause
API evolved but documentation/code not updated:

```csharp
// ❌ WRONG - Old API
var state = provinceSystem.GetState(provinceId);

// ❌ WRONG - Via GameState with old method
var state = gameState.Provinces.GetState(provinceId);
```

### Solution
Use current API:

```csharp
// ✅ CORRECT
var state = gameState.Provinces.GetProvinceState(provinceId);
```

### Key Lesson
When code doesn't compile, check actual source files rather than documentation.

---

## Wrong API: CommandProcessor vs TryExecuteCommand

### Symptom
- `'GameState' does not contain a definition for 'CommandProcessor'`
- Command execution fails to compile

### Root Cause
CommandProcessor is internal. Public API is on GameState:

```csharp
// ❌ WRONG - CommandProcessor is not exposed
gameState.CommandProcessor.Execute(cmd);
```

### Solution
Use GameState's public method:

```csharp
// ✅ CORRECT
bool success = gameState.TryExecuteCommand(cmd);
```

### Key Lesson
GameState is the facade. Use its public API, not internal systems.

---

## Province Names via Wrong API

### Symptom
- `'ProvinceQueries' does not contain a definition for 'GetName'`
- Cannot find method to get province name

### Root Cause
Province names are localized, not stored in ProvinceSystem:

```csharp
// ❌ WRONG - Names aren't in queries
string name = gameState.ProvinceQueries.GetName(provinceId);
```

### Solution
Use LocalizationManager:

```csharp
// ✅ CORRECT
string name = LocalizationManager.Get($"PROV{provinceId}");
```

### Key Lesson
Static data (names, descriptions) → LocalizationManager. Dynamic state → Systems.

---

## NativeArray Not Disposed

### Symptom
- Memory leak warnings in console
- `A Native Collection has not been disposed`
- Growing memory usage over time

### Root Cause
NativeArray/NativeList must be explicitly disposed:

```csharp
// ❌ WRONG - Memory leak
var countries = gameState.Countries.GetAllCountryIds();
foreach (var id in countries)
{
    // Process...
}
// countries never disposed!
```

### Solution
Always dispose NativeCollections:

```csharp
// ✅ CORRECT - Using statement
using var countries = gameState.Countries.GetAllCountryIds();
foreach (var id in countries)
{
    // Process...
}

// ✅ CORRECT - Try/finally
var countries = gameState.Countries.GetAllCountryIds();
try
{
    foreach (var id in countries) { ... }
}
finally
{
    countries.Dispose();
}
```

### Key Lesson
NativeCollections are unmanaged memory. Always dispose them.

---

## Subscribing to Wrong Event Type

### Symptom
- Handler never called despite events being emitted
- Subscription appears to work but no callbacks

### Root Cause
Event type mismatch - similar names, different types:

```csharp
// ❌ WRONG - Subscribing to wrong event
eventBus.Subscribe<ProvinceSelectedEvent>(OnProvinceChanged);
// But system emits ProvinceOwnerChangedEvent!
```

### Solution
Verify exact event type being emitted:

```csharp
// Check what event is actually emitted
eventBus.Emit(new ProvinceOwnerChangedEvent { ... });

// ✅ CORRECT - Match the exact type
eventBus.Subscribe<ProvinceOwnerChangedEvent>(OnProvinceChanged);
```

### Key Lesson
Event types must match exactly. Check both emission and subscription sites.

---

## Initialization Order Dependencies

### Symptom
- NullReferenceException during startup
- System A needs System B, but B isn't ready
- Intermittent startup failures

### Root Cause
Systems initialized in wrong order or accessing others too early:

```csharp
// ❌ WRONG - Accessing system in constructor
public MySystem(GameState gameState)
{
    var provinces = gameState.ProvinceQueries.GetAll();  // May not be ready!
}
```

### Solution
Use phase-based initialization or lazy access:

```csharp
// ✅ CORRECT - Initialize method called after all systems ready
public void Initialize()
{
    var provinces = gameState.ProvinceQueries.GetAll();  // Now safe
}

// ✅ CORRECT - Lazy access
public void DoWork()
{
    if (!IsInitialized) return;
    var provinces = gameState.ProvinceQueries.GetAll();
}
```

### Key Lesson
Constructors should only store references. Actual initialization in Initialize() or later.

---

## ProvinceState Field Access Errors

### Symptom
- `'ProvinceState' does not contain a definition for 'development'`
- Accessing field that doesn't exist in struct

### Root Cause
ProvinceState is 8 bytes with specific fields. Other data is in Game layer:

```csharp
// ❌ WRONG - development is in Game layer, not Core
byte dev = state.development;

// ProvinceState (8 bytes) only has:
// - ownerID (ushort)
// - controllerID (ushort)
// - terrainType (byte)
// - gameDataSlot (byte)
// - fortLevel (byte)
// - flags (byte)
```

### Solution
Access Game-layer data through Game systems:

```csharp
// ✅ CORRECT - Use Core fields
byte terrain = state.terrainType;
ushort owner = state.ownerID;

// ✅ CORRECT - Game data through Game systems
int development = economySystem.GetDevelopment(provinceId);
```

### Key Lesson
ProvinceState is ENGINE (Core) - minimal, fixed 8 bytes. Game data lives in GAME layer.

---

## UI Toolkit Layout Timing

### Symptom
- ScrollView doesn't scroll to bottom
- `layout.height` returns 0
- UI operations fail silently after content changes

### Root Cause
UI Toolkit layout calculation is asynchronous. Layout values aren't available immediately:

```csharp
// ❌ WRONG - Layout not calculated yet
outputLabel.text = newText;
float height = scrollView.contentContainer.layout.height;  // Returns 0!
scrollView.scrollOffset = new Vector2(0, height);
```

### Solution
Delay operations until layout is calculated:

```csharp
// ✅ CORRECT - Delayed execution
outputLabel.text = newText;
scrollView.schedule.Execute(() =>
{
    float maxScroll = Mathf.Max(0,
        scrollView.contentContainer.layout.height -
        scrollView.contentViewport.layout.height);
    scrollView.scrollOffset = new Vector2(0, maxScroll);
}).ExecuteLater(10);  // 10ms delay for layout
```

### Key Lesson
Always delay scroll/layout operations after content changes. Use `schedule.Execute().ExecuteLater()`.

---

## Province Selection Click-Through UI

### Symptom
- Clicking UI buttons also selects provinces underneath
- Map interactions trigger through UI panels

### Root Cause
Manual UI detection is complex and error-prone:

```csharp
// ❌ WRONG - Manual picking is unreliable
if (panel.Pick(mousePos) != null) return;
```

### Solution
Use Unity's EventSystem - works for both uGUI and UI Toolkit:

```csharp
// ✅ CORRECT - Official approach
private bool IsPointerOverUI()
{
    return EventSystem.current != null &&
           EventSystem.current.IsPointerOverGameObject();
}

// In input handling
if (Input.GetMouseButtonDown(0) && !IsPointerOverUI())
{
    HandleProvinceClick();
}
```

### Key Lesson
Always use `EventSystem.IsPointerOverGameObject()` for UI blocking. Don't manually pick UI elements.

---

## Assembly Reference Name Mismatch

### Symptom
- CS0246: "The type or namespace 'X' could not be found"
- Namespaces exist but aren't accessible
- Works in one assembly, fails in another

### Root Cause
Assembly reference name doesn't match .asmdef file name:

```json
// MyAssembly.asmdef references:
"references": [
    "Map"  // ❌ WRONG - File is actually "MapAssembly.asmdef"
]
```

### Solution
Check actual .asmdef file names and match exactly:

```json
// ✅ CORRECT - Match exact .asmdef name
"references": [
    "MapAssembly"
]
```

### Key Lesson
Assembly reference names must exactly match .asmdef file names (without extension). Check file names, not namespace names.

---

## Hardcoded IDs vs Data-Driven Configuration

### Symptom
- Code works with one data set but fails with another
- Magic numbers scattered through code
- Changes require code modifications

### Root Cause
Hardcoding terrain/building/unit IDs instead of using data:

```csharp
// ❌ WRONG - Hardcoded terrain ID
if (terrainType == 0) // "Ocean"
    return false;

// ❌ WRONG - Hardcoded building cost
int cost = 100;
```

### Solution
Use data-driven configuration in JSON5:

```json5
// terrain_rgb.json5
{
    ocean: { type: "ocean", color: [8, 31, 130], ownable: false },
    plains: { type: "plains", color: [80, 120, 50], ownable: true }
}
```

```csharp
// ✅ CORRECT - Data-driven check
if (!terrainLookup.IsTerrainOwnable(terrainType))
    return false;
```

### Key Lesson
Single source of truth in data files. Use flags in JSON5 definitions, not hardcoded checks in code.

---

## Common Debugging Checklist

1. **Compile error on API?** - Check actual source, not docs
2. **Events not firing?** - Is ProcessEvents() called? Correct event type?
3. **Multiplayer desync?** - Any floats? Dictionary iteration?
4. **Memory leak?** - NativeCollections disposed?
5. **Null reference on startup?** - Initialization order correct?
6. **Wrong data?** - Accessing ENGINE vs GAME layer data?
7. **Layer violation?** - Check import rules (Core→nothing, Map→Core, Game→both)
8. **UI click-through?** - Using EventSystem.IsPointerOverGameObject()?
9. **Layout returning 0?** - Delayed execution after content change?
10. **Assembly not found?** - Reference name matches .asmdef file name?

## Critical Rules Summary

| DO | DON'T |
|----|-------|
| Use `FixedPoint64` in simulation | Use `float`/`double` in Core |
| Dispose NativeCollections | Let them leak |
| Sort before iteration (if determinism needed) | Iterate Dictionary directly |
| Use `GameState.TryExecuteCommand()` | Access `CommandProcessor` directly |
| Use `GetProvinceState()` | Use `GetState()` |
| Access localization via `LocalizationManager` | Expect names in queries |
| Initialize in `Initialize()` method | Access systems in constructor |
| Call `EventBus.ProcessEvents()` | Expect automatic event dispatch |
| Use `EventSystem.IsPointerOverGameObject()` | Manually pick UI elements |
| Delay UI operations after content changes | Read layout values immediately |
| Use data-driven configuration | Hardcode terrain/building IDs |
| Match .asmdef file names in references | Guess assembly names |

## API Reference

- [GameState](~/api/Core.GameState.html) - Central hub, use its public API
- [ProvinceSystem](~/api/Core.Systems.ProvinceSystem.html) - Province state management
- [EventBus](~/api/Core.EventBus.html) - Event system
- [LocalizationManager](~/api/Core.Localization.LocalizationManager.html) - Localized strings
