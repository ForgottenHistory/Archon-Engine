# Save System Improvements Plan

**Created**: 2026-01-14
**Status**: Planning
**Priority**: Medium

---

## Current State

The save system is functional with good architecture:
- Atomic writes (temp → rename)
- Binary format with magic bytes
- SerializationHelper for FixedPoint64, NativeArray, sparse arrays
- SystemSerializer reduces boilerplate
- Callback pattern for GAME layer separation
- Version tracking structure

---

## Identified Gaps

### HIGH PRIORITY

#### 1. Checksum Validation
**Issue**: `expectedChecksum` is stored but never validated on load.

**Current**:
```csharp
public uint expectedChecksum; // Stored but ignored
```

**Needed**:
- Calculate checksum during save (CRC32 or xxHash)
- Validate on load, warn if mismatch
- Option to reject corrupted saves or load anyway

#### 2. Save Metadata Header (Quick Preview)
**Issue**: Can't show save list with dates/info without loading entire file.

**Current**: Must deserialize entire save to get name/date.

**Needed**:
- Fixed-size header at file start (256 bytes)
- Contains: name, date, game time, player country, screenshot offset
- `ReadSaveMetadata(path)` reads only header
- Enables fast save browser UI

#### 3. ~~DiplomacySystem Not Saved~~ ✅ OK
**Status**: Verified - DiplomacySystem implements OnSave/OnLoad via DiplomacySaveLoadHandler.
Saved through GameSystem reflection loop, not explicit list. Working correctly.

---

### MEDIUM PRIORITY

#### 4. Compression
**Issue**: Large saves uncompressed.

**Recommendation**:
- GZipStream wrapper around file stream
- ~60-80% size reduction typical for game data
- Minimal CPU overhead

**Implementation**:
```csharp
using (var gzip = new GZipStream(fileStream, CompressionMode.Compress))
using (var writer = new BinaryWriter(gzip))
```

#### 5. Async Save/Load
**Issue**: Synchronous operations block main thread, cause frame stutter.

**Recommendation**:
- Save: Background thread, callback on complete
- Load: Background thread, apply on main thread
- Progress reporting via callback

**API**:
```csharp
void SaveGameAsync(string name, Action<bool> onComplete);
void LoadGameAsync(string name, Action<bool> onComplete, Action<float> onProgress);
```

#### 6. Version Migration
**Issue**: TODOs mention migration but not implemented.

**Recommendation**:
- `ISaveMigrator` interface
- Chain of migrators: v1→v2→v3→current
- Each migrator transforms SaveGameData

**Example**:
```csharp
public interface ISaveMigrator
{
    int FromVersion { get; }
    int ToVersion { get; }
    void Migrate(SaveGameData data);
}
```

#### 7. Save File Integrity Check
**Issue**: No CRC to detect file corruption beyond format errors.

**Recommendation**:
- CRC32 of entire file (excluding CRC bytes)
- Written at end of file
- Verified before deserialization

#### 8. Autosave
**Issue**: No automatic saving.

**Recommendation**:
- Configurable interval (monthly, yearly, or N minutes)
- Rolling autosaves (autosave_1, autosave_2, autosave_3)
- Disable during critical operations

---

### LOW PRIORITY

#### 9. Screenshot Thumbnail
**Issue**: No visual preview in save browser.

**Recommendation**:
- Capture small screenshot (256x144) on save
- Store as JPEG bytes in save file
- Include offset in header for fast access

#### 10. Progress Reporting
**Issue**: Long saves show no feedback.

**Recommendation**:
- `IProgress<float>` parameter
- Report after each system saved/loaded
- UI shows progress bar

#### 11. ISaveable Interface
**Issue**: Uses reflection to call OnSave/OnLoad.

**Recommendation**:
```csharp
public interface ISaveable
{
    string SaveKey { get; }
    void OnSave(SaveGameData data);
    void OnLoad(SaveGameData data);
}
```

#### 12. currentTick Not Populated
**Issue**: Always 0, TODO in code.

**Fix**:
```csharp
data.currentTick = gameState.Time.CurrentTick;
```

#### 13. Ironman Mode
**Issue**: Not supported.

**Future Feature**:
- Single save slot, no manual saves
- Auto-save on exit
- Cloud backup support

---

## Implementation Order

| Phase | Items | Effort |
|-------|-------|--------|
| 1 | Checksum validation, currentTick fix, DiplomacySystem check | Small |
| 2 | Save metadata header, compression | Medium |
| 3 | Async save/load, progress reporting | Medium |
| 4 | Version migration, autosave | Medium |
| 5 | Screenshot, ISaveable, ironman | Low priority |

---

## File Changes

### Phase 1 (Quick Fixes)
| File | Changes |
|------|---------|
| `SaveFileSerializer.cs` | Add checksum calculation + validation |
| `SaveManager.cs` | Fix currentTick, add DiplomacySystem |

### Phase 2 (Metadata + Compression)
| File | Changes |
|------|---------|
| `SaveFileSerializer.cs` | Add fixed header, GZip wrapper |
| `SaveGameData.cs` | Add SaveMetadata struct |
| `SaveManager.cs` | Add ReadSaveMetadata() |

### Phase 3 (Async)
| File | Changes |
|------|---------|
| `SaveManager.cs` | Add async methods with callbacks |
| NEW `SaveLoadJob.cs` | Background thread wrapper |

---

## Quick Reference

**Current save file format**:
```
[Magic: 4 bytes "HGSV"]
[Header: version, name, date, tick, speed, scenario]
[SystemData: count + (name, length, bytes) pairs]
[CommandLog: count + bytes[]]
[Checksum: uint32 (unused)]
```

**Proposed format with header**:
```
[Magic: 4 bytes "HGSV"]
[HeaderSize: 4 bytes]
[QuickHeader: 256 bytes - name, date, tick, country, screenshot offset]
[Compressed payload start]
  [SystemData: count + (name, length, bytes) pairs]
  [CommandLog: count + bytes[]]
[Compressed payload end]
[Checksum: uint32]
```

---

*Save system is functional but needs checksum validation, metadata header for save browser, compression, and async operations for production quality.*
