# ParadoxData Parser Usage Guide

**Essential patterns for parsing Paradox Interactive game files in Unity**

## Table of Contents

1. [Single File Pattern](#single-file-pattern)
2. [Multi-File Pattern](#multi-file-pattern)
3. [Data Extraction](#data-extraction)
4. [Memory Management](#memory-management)
5. [Common Issues](#common-issues)

---

## Single File Pattern

For loading single large files (area.txt, region.txt):

```csharp
using ParadoxParser.Core;
using ParadoxParser.Utilities;
using Unity.Collections;
using static ParadoxParser.Utilities.ParadoxDataExtractionHelpers;

public static async Task<bool> LoadSingleFileAsync(string filePath, GameDataManager dataManager)
{
    // 1. Async file reading
    var fileResult = await AsyncFileReader.ReadFileAsync(filePath, Allocator.TempJob).ConfigureAwait(false);

    if (!fileResult.Success) return false;

    try
    {
        // 2. Create parser components
        using var stringPool = new NativeStringPool(500, Allocator.Temp);
        using var errorAccumulator = new ErrorAccumulator(Allocator.Temp);
        using var tokenizer = new Tokenizer(fileResult.Data, stringPool, errorAccumulator);
        using var tokenStream = tokenizer.Tokenize(Allocator.Temp);

        // 3. Parse
        var keyValues = new NativeList<ParsedKeyValue>(200, Allocator.Temp);
        var childBlocks = new NativeList<ParsedKeyValue>(1000, Allocator.Temp);
        var parseResult = ParadoxParser.Parse(tokenStream.GetRemainingSlice(), keyValues, childBlocks, fileResult.Data);

        if (!parseResult.Success) return false;

        // 4. Extract data using generic helpers
        foreach (var kvp in keyValues)
        {
            if (kvp.Value.IsBlock)
            {
                string name = ExtractKeyString(kvp.Key, fileResult.Data);
                var items = ExtractUshortListFromBlock(kvp, childBlocks, fileResult.Data);
                // Process extracted data...
            }
        }

        return true;
    }
    finally
    {
        fileResult.Dispose();
    }
}
```

---

## Multi-File Pattern

**CRITICAL**: For processing many files (country files), use batch processing with proper memory management:

```csharp
public static async Task<bool> LoadMultipleFilesAsync(string[] filePaths, GameDataManager dataManager)
{
    const int BATCH_SIZE = 15; // Small batches prevent memory exhaustion

    // Create reusable components
    using var stringPool = new NativeStringPool(2000, Allocator.Persistent);
    using var errorAccumulator = new ErrorAccumulator(Allocator.Persistent, 100);

    for (int batchStart = 0; batchStart < filePaths.Length; batchStart += BATCH_SIZE)
    {
        int batchEnd = Math.Min(batchStart + BATCH_SIZE, filePaths.Length);
        var batchFiles = filePaths.Skip(batchStart).Take(batchEnd - batchStart);

        // Process batch in parallel
        var batchTasks = batchFiles.Select(async (filePath, index) =>
        {
            return await ParseSingleFileAsync(filePath, stringPool, errorAccumulator);
        });

        var batchResults = await Task.WhenAll(batchTasks);

        // Process results
        foreach (var result in batchResults)
        {
            if (result.IsValid)
                dataManager.AddData(result);
        }

        // Yield control between batches
        await Task.Yield();
        await Task.Delay(10); // Prevent memory pressure
    }

    return true;
}

private static async Task<DataType> ParseSingleFileAsync(string filePath, NativeStringPool stringPool, ErrorAccumulator errorAccumulator)
{
    // Use Persistent allocator for multi-file scenarios
    var fileResult = await AsyncFileReader.ReadFileAsync(filePath, Allocator.Persistent).ConfigureAwait(false);

    if (!fileResult.Success) return default;

    try
    {
        errorAccumulator.Clear();

        using var tokenizer = new Tokenizer(fileResult.Data, stringPool, errorAccumulator);
        using var tokenStream = tokenizer.Tokenize(Allocator.Temp);
        using var keyValues = new NativeList<ParsedKeyValue>(30, Allocator.Temp);
        using var childBlocks = new NativeList<ParsedKeyValue>(60, Allocator.Temp);

        var parseResult = ParadoxParser.Parse(tokenStream.GetRemainingSlice(), keyValues, childBlocks, fileResult.Data);

        return parseResult.Success ? ExtractData(keyValues, childBlocks, fileResult.Data) : default;
    }
    finally
    {
        fileResult.Dispose(); // CRITICAL: Always dispose
    }
}
```

---

## Data Extraction

Use the generic helpers for all parsing operations:

```csharp
using static ParadoxParser.Utilities.ParadoxDataExtractionHelpers;

// Extract strings
string name = ExtractKeyString(kvp.Key, sourceData);
string value = ExtractStringValue(kvp.Value, sourceData);

// Extract numbers
ushort id = ExtractUshortValue(kvp.Value, sourceData);
int number = ExtractIntValue(kvp.Value, sourceData);

// Extract lists (for province IDs, etc.)
List<ushort> provinceIds = ExtractUshortListFromBlock(kvp, childBlocks, sourceData);

// Extract colors
Color color = ExtractColorValue(colorBlock, childBlocks, sourceData);

// Use pre-computed hashes for performance
if (kvp.KeyHash == CommonKeyHashes.COLOR_HASH)
{
    // Handle color
}
else if (kvp.KeyHash == CommonKeyHashes.NAME_HASH)
{
    // Handle name
}
```

### Pre-Computed Hash Constants

```csharp
CommonKeyHashes.NAME_HASH       // "name"
CommonKeyHashes.COLOR_HASH      // "color"
CommonKeyHashes.CAPITAL_HASH    // "capital"
CommonKeyHashes.AREAS_HASH      // "areas"
CommonKeyHashes.OWNER_HASH      // "owner"
CommonKeyHashes.CONTROLLER_HASH // "controller"
CommonKeyHashes.CULTURE_HASH    // "culture"
CommonKeyHashes.RELIGION_HASH   // "religion"
CommonKeyHashes.HISTORY_HASH    // "history"
```

---

## Memory Management

### Allocator Selection

| Scenario | Allocator | Usage |
|----------|-----------|-------|
| Single file | `Allocator.TempJob` | File reading |
| Multiple files | `Allocator.Persistent` | File reading |
| Parser components | `Allocator.Temp` | Short-lived structures |
| Reused components | `Allocator.Persistent` | StringPool, ErrorAccumulator |

### Performance Monitoring

```csharp
var stopwatch = System.Diagnostics.Stopwatch.StartNew();

// ... parsing code ...

PerformanceMonitor.LogProgressWithTiming("MyLoader", itemCount, stopwatch, 100);
PerformanceMonitor.ValidateParsingTime(stopwatch, "MyLoader", targetMs);
```

---

## Common Issues

### Issue: Unity Freezes During Multi-File Loading
**Cause**: Using `Allocator.TempJob` for many files or sequential awaits
**Solution**: Use batch processing with `Allocator.Persistent` and `Task.WhenAll()`

### Issue: Memory Leaks
**Cause**: Not disposing FileReadResult
**Solution**: Always use `try/finally` or `using` statements

### Issue: Parser Errors
**Cause**: Wrong allocator sizes or malformed files
**Solution**: Check file format and increase buffer sizes if needed

### Issue: Poor Performance
**Cause**: Creating new parser components for each file
**Solution**: Reuse StringPool and ErrorAccumulator across files

---

## Coroutine Integration

For Unity coroutines, wrap async calls:

```csharp
public static IEnumerator LoadDataCoroutine(GameDataManager dataManager, System.Action<bool> onComplete)
{
    var loadTask = LoadDataAsync(dataManager);

    while (!loadTask.IsCompleted)
        yield return null;

    bool success = false;
    try
    {
        success = loadTask.Result;
    }
    catch (System.Exception e)
    {
        Debug.LogError($"Loading failed: {e.Message}");
    }

    onComplete?.Invoke(success);
}
```

---

## Summary

- **Single files**: Use `TempJob` allocator, simple pattern
- **Multiple files**: Use `Persistent` allocator, batch processing, parallel execution
- **Always**: Use generic helpers, dispose resources, monitor performance
- **Never**: Sequential awaits with many files, wrong allocator types, skip disposal

The parser handles all Paradox file types - the key is using the right pattern for your scenario.