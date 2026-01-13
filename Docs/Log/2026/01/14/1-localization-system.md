# Localization System
**Date**: 2026-01-14
**Session**: 1
**Status**: ✅ Complete
**Priority**: Medium

---

## Session Goal

**Primary Objective:**
- Move localization infrastructure from `ParadoxParser.YAML` to `Core.Localization`
- Create high-level `LocalizationManager` facade for easy API access
- Integrate localization into StarterKit UI

**Success Criteria:**
- All YAML parsing code in `Core.Localization` namespace
- Simple API: `LocalizationManager.Get("KEY")` returns localized string
- StarterKit UI uses localization for all user-facing text
- System works without locale files (graceful fallback)

---

## Context & Background

**Previous Work:**
- See: [4-goal-auto-discovery.md](../13/4-goal-auto-discovery.md)
- YAML parser existed in `ParadoxParser.YAML` but was never integrated

**Current State:**
- Comprehensive parsing infrastructure existed but unused
- No high-level API for accessing localized strings
- StarterKit had hardcoded English strings

**Why Now:**
- Building reusable engine for future games
- Localization is essential for international releases

---

## What We Did

### 1. Moved YAML Parsing to Core.Localization

**Files Moved:** `ParadoxParser/YAML/*.cs` → `Core/Localization/*.cs`

| File | Purpose |
|------|---------|
| `YAMLTokenizer.cs` | Tokenize Paradox-style YAML (`KEY:0 "Value"`) |
| `YAMLParser.cs` | Parse tokens to hash-based lookup |
| `MultiLanguageExtractor.cs` | Load multiple language files |
| `LocalizationFallbackChain.cs` | Language fallback (French → English) |
| `DynamicKeyResolver.cs` | Variable substitution in keys |
| `StringReplacementSystem.cs` | Parameter replacement (`$KEY$`, `{KEY}`) |
| `ColoredTextMarkup.cs` | Paradox color codes to Unity rich text |

Changed namespace from `ParadoxParser.YAML` to `Core.Localization`.

### 2. Created LocalizationManager Facade

**File:** `Core/Localization/LocalizationManager.cs`

```csharp
// Simple API usage
string name = LocalizationManager.Get("PROV123");     // "Province_123"
string country = LocalizationManager.Get("RED");      // "Red Empire"
string missing = LocalizationManager.Get("UNKNOWN");  // "UNKNOWN" (fallback)

// With parameters
string text = LocalizationManager.Get("MSG", ("name", playerName));
```

**Features:**
- Static access (no instance needed)
- Graceful fallback (missing files/keys return key as-is)
- String caching for performance
- Language switching at runtime
- Proper cleanup on shutdown

### 3. Integrated into Initialization

**File:** `Core/Initialization/Phases/StaticDataLoadingPhase.cs:22-27`

```csharp
var localisationPath = Path.Combine(context.Settings.DataDirectory, "localisation");
LocalizationManager.Initialize(localisationPath, "english");
```

**File:** `Core/GameState.cs:329`
```csharp
LocalizationManager.Shutdown();  // In OnDestroy()
```

### 4. Created Localization Generator

**File:** `Template-Data/utils/generate_localisation.py`

```bash
python utils/generate_localisation.py --output . --definition map/definition.csv
```

Generates:
- `provinces_l_english.yml` - 4425 province names
- `countries_l_english.yml` - 10 country names + adjectives
- `terrain_l_english.yml` - 11 terrain types
- `ui_l_english.yml` - 47 UI strings

### 5. Updated StarterKit UI

| File | Changes |
|------|---------|
| `ProvinceInfoPresenter.cs` | Province/country names via `Get("PROV123")`, `Get("RED")` |
| `TimeUI.cs` | Pause/Resume buttons |
| `BuildingInfoUI.cs` | Headers, labels, build buttons |
| `UnitInfoUI.cs` | Headers, unit names, stats labels |

**Fallback Pattern:**
```csharp
string name = LocalizationManager.Get($"UNIT_{unitType.StringID.ToUpperInvariant()}");
if (name.StartsWith("UNIT_")) name = unitType.Name; // Fallback to code name
```

---

## Decisions Made

### Decision 1: Graceful Fallback vs. Strict Mode

**Context:** Should missing locale files cause errors?

**Decision:** Graceful fallback - return keys as-is

**Rationale:**
- Development often starts without localization
- Missing single key shouldn't break UI
- Easy to spot untranslated keys (they show as "UI_PAUSE")

### Decision 2: Static vs. Instance API

**Context:** How should code access localization?

**Decision:** Static `LocalizationManager.Get()`

**Rationale:**
- Localization needed everywhere (UI, tooltips, logs)
- Dependency injection would be cumbersome
- Single global language state makes sense

---

## What Worked ✅

1. **Existing YAML Parser**
   - Already had comprehensive Paradox-style parsing
   - Just needed facade on top
   - Zero parsing code written this session

2. **Generator Script Pattern**
   - Same pattern as other Template-Data generators
   - Easy to regenerate when data changes
   - Supports multiple languages via `--all-languages`

---

## Quick Reference for Future Claude

**Adding New UI Strings:**
1. Add key to `generate_localisation.py` ui_strings list
2. Run generator: `python utils/generate_localisation.py --output . --definition map/definition.csv`
3. Use in code: `LocalizationManager.Get("UI_NEW_KEY")`

**Key Files:**
- `Core/Localization/LocalizationManager.cs` - Main API
- `Template-Data/utils/generate_localisation.py` - Generator
- `Template-Data/localisation/english/*.yml` - Generated files

**Locale File Format (Paradox YAML):**
```yaml
l_english:
 KEY:0 "Value"
 PROV1:0 "Province Name"
```

**Initialization Order:**
1. `StaticDataLoadingPhase` calls `LocalizationManager.Initialize()`
2. Path: `{DataDirectory}/localisation`
3. Loads all `.yml` files from language subdirectories

---

## Session Statistics

**Files Changed:** 12
**Files Created:** 2 (LocalizationManager.cs, generate_localisation.py)
**Files Moved:** 7 (YAML parsers)
**Locale Entries Generated:** 4493 (provinces + countries + terrain + UI)

---

## Links & References

### Related Sessions
- [Previous: Goal Auto-Discovery](../13/4-goal-auto-discovery.md)

### Code References
- LocalizationManager: `Core/Localization/LocalizationManager.cs`
- Generator: `Template-Data/utils/generate_localisation.py`
- Init integration: `Core/Initialization/Phases/StaticDataLoadingPhase.cs:22-27`

---

*Localization system provides simple `Get("KEY")` API with graceful fallback. Works without locale files.*
