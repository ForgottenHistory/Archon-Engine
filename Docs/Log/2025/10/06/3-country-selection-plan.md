# Country Selection System - Implementation Plan

**Goal**: Simple country selection screen where player clicks a country on the map, sees it highlighted, then clicks Play to start.

---

## What We're Building

**User Flow:**
1. Game starts → country selection screen active
2. Player clicks any province on the map
3. Entire country (all provinces owned by that country) highlights in gold
4. "Play" button becomes enabled
5. Player clicks Play → selection UI hides, game starts

---

## Implementation

### 1. GPU Shader Enhancement
**File**: `ProvinceHighlight.compute`

Added `HighlightCountry` kernel:
- Reads `ProvinceOwnerTexture` to find all provinces owned by target country
- Highlights all matching provinces in one GPU dispatch
- Performance: <1ms for 11M+ pixels

**Critical Fix - Normalization Bug:**
```hlsl
// WRONG (initial implementation):
uint ownerID = (uint)(ownerFloat + 0.5);  // Always returns 0!

// CORRECT (fixed):
uint ownerID = (uint)(ownerFloat * 65535.0 + 0.5);  // Denormalize properly
```

**Issue**: ProvinceOwnerTexture stores `ownerID / 65535.0` (normalized), but we weren't denormalizing when reading. For Castile (ID=151): stored as `151/65535 = 0.002304`, read back as `(uint)(0.002304 + 0.5) = 0`. All countries matched ID 0, never the target.

**Result**: Country highlighting now works correctly.

### 2. ENGINE Layer API
**File**: `ProvinceHighlighter.cs`

Added method:
```csharp
public void HighlightCountry(ushort countryID, Color color)
```

### 3. GAME Layer UI
**File**: `CountrySelectionUI.cs`

**Logic:**
- Subscribe to `ProvinceSelector.OnProvinceClicked` in OnEnable()
- When province clicked → get owner ID from `ProvinceQueries`
- Call `ProvinceHighlighter.HighlightCountry(ownerID, color)`
- Enable Play button
- When Play clicked → unsubscribe, hide UI, store selected country

**Critical Fixes - Event Lifecycle & Unowned Provinces:**

1. **Memory Leak Fix**: Unsubscribe in two places for safety:
```csharp
void OnDestroy() {
    provinceSelector.OnProvinceClicked -= HandleProvinceClicked;  // Cleanup
}

void OnPlayButtonClicked() {
    provinceSelector.OnProvinceClicked -= HandleProvinceClicked;  // Before deactivation
    gameObject.SetActive(false);
}
```

2. **Unowned Province Fix**: Disable ProvinceSelectionVisualizer during country selection, but explicitly clear highlights when clicking unowned provinces (event handlers still fire even when disabled):
```csharp
if (ownerID == 0) {
    provinceHighlighter.ClearHighlight();  // Clear ProvinceSelectionVisualizer highlight
    return;
}
```

3. **Type Safety Fix**: Changed from string-based lookup to type-safe:
```csharp
// Before: Type.GetType("Game.UI.ProvinceSelectionVisualizer")
// After:  FindFirstObjectByType<ProvinceSelectionVisualizer>()
```

**Required UI Elements:**
- Text: "Choose a country" → updates to "Selected: [TAG]"
- Button: "Play" → disabled until selection

---

## Unity Setup Required

**Create Canvas:**
1. GameObject → UI → Canvas
2. Add `CountrySelectionUI` component
3. Create child objects:
   - Text: "Choose a country"
   - Button: "Play"
4. Assign references in inspector:
   - `instructionText` → Text component
   - `playButton` → Button component

**Configuration:**
- Canvas: Screen Space - Overlay
- Sort Order: High (render on top of map)
- Play button: Initially disabled

---

## Next Steps

1. ✅ GPU shader kernel added
2. ✅ ENGINE API implemented
3. ✅ GAME UI script created
4. ✅ Create UI Canvas in Unity scene
5. ✅ Test country selection
6. ✅ Architecture compliance verified
7. ⏳ Add player state storage (future)

---

## Testing Results

**Test Cases (All Passed):**
1. ✅ Start game → see "Choose a country"
2. ✅ Click Songhai (SON) → all Songhai provinces highlight in gold
3. ✅ Click Castile → switches to Castilian provinces
4. ✅ Play button enables after selection
5. ✅ Click Play → UI disappears, province selection re-enabled
6. ✅ Unowned provinces do NOT highlight during country selection
7. ✅ Province-level selection works after Play clicked

**Bugs Fixed:**
1. GPU normalization bug (country highlighting not working)
2. Event subscription memory leak
3. Province highlighting persisting after Play
4. Unowned provinces highlightable during country selection

---

## Files Modified

- `Assets/Archon-Engine/Shaders/ProvinceHighlight.compute` (+30 lines)
  - Added `HighlightCountry` kernel with proper denormalization
  - Added `ProvinceOwnerTexture` input
  - Added `TargetCountryID` parameter

- `Assets/Archon-Engine/Scripts/Map/Interaction/ProvinceHighlighter.cs` (+62 lines)
  - Added `highlightCountryKernel` field
  - Added `HighlightCountry(ushort, Color)` method

- `Assets/Game/UI/CountrySelectionUI.cs` (+252 lines, new file)
  - Full country selection UI implementation
  - Event lifecycle management (OnEnable/OnDestroy)
  - ProvinceSelectionVisualizer coordination
  - Unowned province handling

- `Assets/Game/HegemonInitializer.cs` (+9 lines)
  - Added `countrySelectionUI` field
  - Added UI activation after initialization

---

## Architecture Compliance

**Verified Against**: `engine-game-separation.md`

✅ **Event-Driven Architecture** (lines 363-368)
- ENGINE emits events (ProvinceSelector.OnProvinceClicked)
- GAME subscribes and implements policy (CountrySelectionUI)
- Proper cleanup in OnDestroy() - no memory leaks

✅ **Engine/Game Separation**
- No game logic in engine layer
- No engine internals accessed from game
- Clean API boundaries (HighlightCountry method)

✅ **Type Safety**
- Direct type references (FindFirstObjectByType<T>)
- No string-based lookups

✅ **Memory Safety**
- Fixed event subscription leaks
- Proper component lifecycle management

---

*Simple, focused implementation - foundation for future player state system*
