# Loader Factory Pattern
**Date**: 2026-01-14
**Session**: 6
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Standardize data loaders with auto-discovery pattern (like CommandRegistry)
- Clean up game-specific loaders from ENGINE layer

**Success Criteria:**
- ILoaderFactory + LoaderMetadataAttribute + LoaderRegistry created
- TerrainLoader and WaterProvinceLoader using new pattern
- StaticDataLoadingPhase uses LoaderRegistry instead of hardcoded calls

---

## Context & Background

**Previous Work:**
- See: [5-caching-framework.md](5-caching-framework.md)
- CommandRegistry pattern: `Core/Commands/CommandRegistry.cs`

**Why Now:**
- Each loader was custom with no shared pattern
- Adding new loaders required editing StaticDataLoadingPhase
- Game-specific loaders (religion, culture, trade goods) cluttered ENGINE

---

## What We Did

### 1. Cleaned Up Game-Specific Code

Removed from ENGINE (to be reimplemented in GAME later):
- ReligionLoader, CultureLoader, TradeGoodLoader
- ReligionId, CultureId, TradeGoodId
- Related data classes and validation

**Files Deleted:** 6 loaders + 3 ID types
**Files Modified:** GameRegistries, StaticDataLoadingPhase, ReferenceResolver, DataValidator, CrossReferenceBuilder

### 2. Created Loader Factory Pattern

**New Files:**

```
Core/Loaders/
├── ILoaderFactory.cs        # Interface: Load(LoaderContext)
├── LoaderMetadataAttribute.cs # [LoaderMetadata(name, Priority, Required)]
├── LoaderRegistry.cs        # Auto-discovery + priority execution
└── LoaderContext.cs         # Context: Registries, DataPath, Settings
```

**Pattern Usage:**
```csharp
[LoaderMetadata("terrain", Priority = 10, Required = true)]
public class TerrainLoader : ILoaderFactory
{
    public void Load(LoaderContext context)
    {
        // Load data files → populate registries
    }
}
```

### 3. Refactored Existing Loaders

| Loader | Priority | Required |
|--------|----------|----------|
| TerrainLoader | 10 | Yes |
| WaterProvinceLoaderFactory | 20 | No |

### 4. Updated StaticDataLoadingPhase

**Before:** Hardcoded loader calls
```csharp
TerrainLoader.LoadTerrains(registries, path);
WaterProvinceLoader.LoadWaterProvinceData(path);
```

**After:** Auto-discovery
```csharp
var loaderRegistry = new LoaderRegistry();
loaderRegistry.DiscoverLoaders(Assembly.GetExecutingAssembly());
loaderRegistry.DiscoverLoaders(context.AdditionalLoaderAssemblies);
loaderRegistry.ExecuteAll(loaderContext);
```

### 5. Added GAME Layer Hook

`InitializationContext.AdditionalLoaderAssemblies` - GAME layer passes its assembly, loaders get discovered automatically.

---

## Architecture Impact

### Pattern Established: Loader Factory

Mirrors CommandRegistry pattern:
- Interface + Attribute + Registry
- Auto-discovery via reflection
- Priority-ordered execution
- Required vs optional distinction

**Benefits:**
- No manual wiring for new loaders
- GAME layer loaders integrate seamlessly
- Mod support (scan mod assemblies)
- Centralized error handling

---

## Files Changed

| File | Changes |
|------|---------|
| `Core/Loaders/ILoaderFactory.cs` | NEW |
| `Core/Loaders/LoaderMetadataAttribute.cs` | NEW |
| `Core/Loaders/LoaderRegistry.cs` | NEW |
| `Core/Loaders/LoaderContext.cs` | NEW |
| `Core/Loaders/TerrainLoader.cs` | Implements ILoaderFactory |
| `Core/Loaders/WaterProvinceLoader.cs` | Added factory wrapper |
| `Core/Initialization/Phases/StaticDataLoadingPhase.cs` | Uses LoaderRegistry |
| `Core/Initialization/InitializationContext.cs` | +AdditionalLoaderAssemblies |
| `Core/FILE_REGISTRY.md` | Document pattern |

---

## Quick Reference for Future Claude

**Loader Factory Pattern:**
```csharp
// 1. Create loader with attribute
[LoaderMetadata("my_loader", Priority = 50)]
public class MyLoader : ILoaderFactory
{
    public void Load(LoaderContext ctx)
    {
        // ctx.Registries, ctx.DataPath
    }
}

// 2. Auto-discovered and executed in priority order
```

**Adding GAME layer loaders:**
```csharp
// In HegemonInitializer or similar
context.AdditionalLoaderAssemblies = new[] { typeof(MyGameLoader).Assembly };
```

**Key Files:**
- Interface: `Core/Loaders/ILoaderFactory.cs`
- Registry: `Core/Loaders/LoaderRegistry.cs`
- Usage: `Core/Initialization/Phases/StaticDataLoadingPhase.cs`

---

## Session Statistics

**Files Created:** 4
**Files Modified:** 5
**Files Deleted:** 6 (game-specific cleanup)
**Lines Removed:** ~800 (game-specific code)
**Pattern:** Mirrors CommandRegistry

---

## Links & References

### Related Sessions
- [Previous: Caching Framework](5-caching-framework.md)

### Code References
- LoaderRegistry: `Core/Loaders/LoaderRegistry.cs`
- CommandRegistry (model): `Core/Commands/CommandRegistry.cs`

---

*Loader Factory pattern standardizes data file loading with auto-discovery. Game-specific loaders removed from ENGINE, ready for GAME layer reimplementation.*
