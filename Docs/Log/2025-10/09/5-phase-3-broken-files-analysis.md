# Phase 3: Broken Files Analysis

**Date**: 2025-10-09
**Status**: Analysis Complete
**Action**: Ready to fix

---

## Summary

Identified all files broken by Phase 2 ProvinceState refactoring. Total: **8 files** need updates (excluding backups and already-fixed compatibility code).

---

## Files Broken by `.development` Removal

### Core Layer (4 files - CRITICAL)

1. **ProvinceDataManager.cs** ⚠️ **CRITICAL PATH**
   - Location: `Assets/Archon-Engine/Scripts/Core/Systems/Province/ProvinceDataManager.cs`
   - Impact: Core data access layer
   - Fix: Remove development accessors OR deprecate with compatibility bridge

2. **CountryQueries.cs**
   - Location: `Assets/Archon-Engine/Scripts/Core/Queries/CountryQueries.cs`
   - Impact: Likely GetTotalDevelopment() method
   - Fix: Update to use game layer OR remove game-specific queries

3. **ProvinceSimulation.cs**
   - Location: `Assets/Archon-Engine/Scripts/Core/Systems/ProvinceSimulation.cs`
   - Impact: Development simulation logic
   - Fix: Move to game layer OR make generic

4. **ProvinceColdData.cs**
   - Location: `Assets/Archon-Engine/Scripts/Core/Data/ProvinceColdData.cs`
   - Impact: May store development in cold data
   - Fix: Verify if actually broken

### Game Layer (4 files - EXPECTED)

5. **HegemonProvinceDataTests.cs**
   - Location: `Assets/Game/Tests/HegemonProvinceDataTests.cs`
   - Impact: FromLegacyProvinceState() test
   - Fix: Update test to handle new ProvinceState structure

6. **HegemonProvinceSystem.cs**
   - Location: `Assets/Game/Systems/HegemonProvinceSystem.cs`
   - Impact: InitializeFromLegacyData() method
   - Fix: Update migration logic

7. **VisualStyleManager.cs**
   - Location: `Assets/Game/VisualStyles/VisualStyleManager.cs`
   - Impact: Unknown - may reference development
   - Fix: Investigate and fix if needed

8. **VisualStyleConfiguration.cs**
   - Location: `Assets/Game/VisualStyles/VisualStyleConfiguration.cs`
   - Impact: Unknown - may reference development
   - Fix: Investigate and fix if needed

---

## Files Broken by `.fortLevel` Removal

### Core Layer (1 file)

1. **ProvinceColdData.cs** (already listed above)
   - May reference fortLevel

---

## Files Broken by `.flags` / `ProvinceFlags` Removal

### Core Layer (1 file)

1. **ProvinceSimulation.cs** (already listed above)
   - May use ProvinceFlags enum

### False Positives (OK)

- **CountryDataManager.cs** - Uses `.flags` on CountryHotData (different struct, OK)
- **CountryData.cs** - Uses `.flags` on CountryHotData (different struct, OK)

---

## Priority Matrix

### Priority 1: CRITICAL PATH (Must fix first)

These break core engine functionality:

1. **ProvinceDataManager.cs** - Core data access
2. **ProvinceSimulation.cs** - Simulation logic
3. **CountryQueries.cs** - Query layer

### Priority 2: GAME LAYER (Fix after engine)

These break game-specific code:

4. **HegemonProvinceSystem.cs** - Migration logic
5. **HegemonProvinceDataTests.cs** - Test updates

### Priority 3: INVESTIGATE (Unknown impact)

May or may not be broken:

6. **ProvinceColdData.cs** - Verify if actually broken
7. **VisualStyleManager.cs** - Investigate usage
8. **VisualStyleConfiguration.cs** - Investigate usage

---

## Recommended Fix Order

### Step 1: Core Engine (Priority 1)

1. ✅ **ProvinceDataManager.cs** - Remove or deprecate development accessors
2. ✅ **CountryQueries.cs** - Update or remove GetTotalDevelopment()
3. ✅ **ProvinceSimulation.cs** - Move to game layer or make generic

### Step 2: Verification (Priority 3)

4. ✅ **ProvinceColdData.cs** - Check if actually broken
5. ✅ **VisualStyleManager.cs** - Check if actually broken
6. ✅ **VisualStyleConfiguration.cs** - Check if actually broken

### Step 3: Game Layer (Priority 2)

7. ✅ **HegemonProvinceSystem.cs** - Update InitializeFromLegacyData()
8. ✅ **HegemonProvinceDataTests.cs** - Update FromLegacyProvinceState() test

---

## Fix Strategies

### Strategy A: Remove from Engine (Recommended)

**For**: ProvinceDataManager.cs development accessors

**Action**: Delete methods that access game-specific fields

**Result**: Clean engine-game separation

**Example**:
```csharp
// REMOVE these from ProvinceDataManager:
public byte GetDevelopment(ushort provinceId) { ... }
public void SetDevelopment(ushort provinceId, byte value) { ... }
```

---

### Strategy B: Move to Game Layer (Recommended)

**For**: ProvinceSimulation.cs development logic

**Action**: Move simulation logic to Game/Systems/

**Result**: Game owns simulation, engine provides primitives

**Example**:
```csharp
// MOVE FROM: Core/Systems/ProvinceSimulation.cs
// MOVE TO:   Game/Systems/HegemonProvinceSimulation.cs
public class HegemonProvinceSimulation {
    private HegemonProvinceSystem hegemonSystem;

    public void SimulateDevelopmentGrowth() {
        // Game-specific logic
    }
}
```

---

### Strategy C: Update to Use Game Layer (If Needed)

**For**: CountryQueries.GetTotalDevelopment()

**Option 1**: Remove (development is game-specific)
**Option 2**: Accept HegemonProvinceSystem parameter

**Example**:
```csharp
// Option 1: REMOVE (recommended)
// public long GetTotalDevelopment(ushort countryId) { ... }

// Option 2: UPDATE (if must keep)
public long GetTotalDevelopment(ushort countryId, HegemonProvinceSystem hegemonSystem) {
    // Calculate using hegemonSystem.GetDevelopment()
}
```

---

### Strategy D: Update Tests (Required)

**For**: HegemonProvinceDataTests.cs

**Action**: Update test to use new ProvinceState structure

**Example**:
```csharp
// OLD TEST
[Test]
public void FromLegacyProvinceState_Extracts_Correct_Fields() {
    var legacyState = ProvinceState.CreateDefault();
    legacyState.development = 42;  // ❌ Field doesn't exist

    var hegemonData = HegemonProvinceData.FromLegacyProvinceState(legacyState);
    Assert.AreEqual(42, hegemonData.development);
}

// NEW TEST (Option 1: Remove test)
// This test is no longer valid - ProvinceState doesn't have development

// NEW TEST (Option 2: Test with populated game data)
[Test]
public void CreateWithDevelopment_Sets_Development_Correctly() {
    var data = HegemonProvinceData.CreateWithDevelopment(42);
    Assert.AreEqual(42, data.development);
}
```

---

## Estimated Effort

| File | Complexity | Estimated Time |
|------|-----------|----------------|
| ProvinceDataManager.cs | Medium | 15 min |
| CountryQueries.cs | Low | 10 min |
| ProvinceSimulation.cs | High | 30 min |
| ProvinceColdData.cs | Low | 5 min |
| HegemonProvinceSystem.cs | Low | 10 min |
| HegemonProvinceDataTests.cs | Low | 10 min |
| VisualStyleManager.cs | Low | 5 min |
| VisualStyleConfiguration.cs | Low | 5 min |

**Total**: ~90 minutes (1.5 hours)

---

## Success Criteria

### ✅ Compilation

All files compile without errors

### ✅ Tests Pass

All unit tests pass (with updated tests)

### ✅ Architecture Compliance

- Engine layer has NO game-specific code
- Game layer owns all game mechanics
- Clean separation maintained

---

## Next Actions

1. Start with ProvinceDataManager.cs (critical path)
2. Fix ProvinceSimulation.cs (move to game layer)
3. Fix CountryQueries.cs (remove or update)
4. Verify Priority 3 files
5. Update game layer files
6. Run tests
7. Mark Phase 3 complete

---

*Analysis complete - ready to fix broken files.*
