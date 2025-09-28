# JSON5 Migration from Broken ParadoxParser
**Date**: 2025-09-28
**Status**: Implementation in Progress
**Priority**: Critical (Data Loading Completely Broken)

## Problem Statement
ParadoxParser is fundamentally broken and returning dummy data instead of parsing actual game files. Both province and country loaders produce unusable stub data, preventing the game from functioning.

## Root Cause Analysis
✅ **ParadoxParser Investigation Results:**
- Parser successfully processes tokens (result.Success = true)
- **Critical Issue**: Parsed data is discarded in BatchParseJob.cs
- Province loader: Creates dummy data with "---" values (line 199-210)
- Country loader: Creates basic data from filename hash (line 296-298)
- JSON export revealed: 783 broken key-value pairs, 0 blocks detected

❌ **Broken Data Examples:**
```
Province: ALL provinces have ProvinceID=0, IsValid=false
Country: Tags like '111', '646' instead of 'SWE', 'FRA'
Values: All empty strings or hash-generated garbage
```

## Solution: JSON5 Migration
**Strategy**: Replace complex Paradox format with clean, structured JSON5

### Conversion Results
✅ **Python Converter Created**: `paradox-to-json-converter.py`
✅ **All Countries Converted**: 979 files → clean JSON5 format
✅ **All Provinces Converted**: 3923 files → structured data
✅ **Backup Created**: Original files in `*_backup_paradox` directories

### JSON5 Structure Examples
**Province (1-Uppland.json5):**
```json5
{
  owner: "SWE",
  culture: "swedish",
  base_tax: 5,
  discovered_by: ["eastern", "western", "muslim", "ottoman"],
  "1436.4.28": {
    revolt: { type: "pretender_rebels", size: 1 }
  }
}
```

**Country (Sweden.json5):**
```json5
{
  graphical_culture: "westerngfx",
  color: [8, 82, 165],
  historical_idea_groups: ["administrative_ideas", "offensive_ideas"],
  preferred_religion: "protestant"
}
```

## Architecture Design: Hybrid JSON5 + Burst
**Performance Requirement**: Maintain burst job performance architecture

### Two-Phase Loading Approach
```csharp
// Phase 1: JSON5 Loading (Main Thread)
- Load .json5 files with Newtonsoft.Json
- Extract data to burst-compatible structs
- Convert to NativeArrays

// Phase 2: Burst Processing (Multi-threaded)
- Process struct arrays with burst jobs
- Apply game logic, validation
- Generate final game state
```

**Estimated Performance**: 300-600ms for 4000+ files (acceptable for development)

## Implementation Progress

### Completed
✅ **Data Conversion**: All game data converted to JSON5
✅ **Json5Loader Utility**: Created with helper methods
✅ **File Structure**: Proper .json5 extensions for IDE support

### Next Steps
1. **Create Hybrid Architecture**: JSON5 → Structs → Burst Jobs
2. **Update BurstProvinceHistoryLoader**: Replace ParadoxParser calls
3. **Update JobifiedCountryLoader**: Replace ParadoxParser calls
4. **Test Data Loading**: Verify real data extraction
5. **Performance Validation**: Compare with targets

## Files Modified
```
Core/Loaders/Json5Loader.cs - New utility class
Assets/Data/common/countries/ - Replaced with .json5 files
Assets/Data/history/provinces/ - Replaced with .json5 files
Assets/Data/paradox-to-json-converter.py - Conversion tool
```

## Technical Benefits
- **Immediate Fix**: Replaces completely broken parser
- **Clean Data**: Structured, readable, debuggable format
- **Maintainable**: No complex parsing logic needed
- **Future-Proof**: Easy compilation to binary for release
- **Moddable**: JSON5 is accessible to modders

## Performance Strategy
- **Development**: JSON5 for ease of use and debugging
- **Release**: Pre-compile to optimized binary format (2x+ speed boost)
- **Current Target**: Good enough performance for development workflow

## Context for Future Sessions
This migration fixes the core data loading system that was completely broken. The ParadoxParser was a complex system that never worked properly - JSON5 provides a clean, reliable foundation for the game's data layer.