# ParadoxData Parser Usage Guide

**A comprehensive guide to loading and parsing Paradox Interactive game files in Unity**

## Table of Contents

1. [Quick Start](#quick-start)
2. [Architecture Overview](#architecture-overview)
3. [Basic Usage Examples](#basic-usage-examples)
4. [Advanced Features](#advanced-features)
5. [Data Extraction Patterns](#data-extraction-patterns)
6. [Integration Guide](#integration-guide)
7. [Performance Best Practices](#performance-best-practices)
8. [Troubleshooting](#troubleshooting)

---

## Quick Start

### 30-Second Setup

```csharp
using ParadoxParser.Core;
using ParadoxParser.Data;
using Unity.Collections;

// 1. Load file data
var fileResult = await AsyncFileReader.ReadFileAsync(filePath, Allocator.TempJob);

// 2. Create parser components
using var stringPool = new NativeStringPool(100, Allocator.TempJob);
using var errorAccumulator = new ErrorAccumulator(Allocator.TempJob);

// 3. Tokenize
using var tokenizer = new Tokenizer(fileResult.Data, stringPool, errorAccumulator);
using var tokenStream = tokenizer.Tokenize(Allocator.Temp);

// 4. Parse
var keyValues = new NativeList<ParsedKeyValue>(100, Allocator.Temp);
var childBlocks = new NativeList<ParsedKeyValue>(100, Allocator.Temp);
var parseResult = ParadoxParser.Parse(tokenStream.GetRemainingSlice(), keyValues, childBlocks, fileResult.Data);

// 5. Extract data
if (parseResult.Success)
{
    foreach (var kvp in keyValues)
    {
        string key = System.Text.Encoding.UTF8.GetString(kvp.Key.ToArray());
        Debug.Log($"Found key: {key}");
    }
}

// 6. Cleanup
fileResult.Dispose();
```

### Simple Country Loading Example

```csharp
public static Country LoadCountryFromFile(string filePath)
{
    try
    {
        string content = File.ReadAllText(filePath);

        // Simple parsing for demonstration
        Color color = ParseCountryColor(content);
        ushort capital = ParseCapital(content);
        string tag = Path.GetFileNameWithoutExtension(filePath).Substring(0, 3).ToUpper();

        return Country.Create(1, tag, "Sample Country", color, capital);
    }
    catch (System.Exception e)
    {
        Debug.LogError($"Failed to load country: {e.Message}");
        return default;
    }
}
```

---

## Architecture Overview

### Core Components

The ParadoxData parser follows a pipeline architecture:

```
Raw File → AsyncFileReader → NativeArray<byte> → Tokenizer → TokenStream → ParadoxParser → ParsedKeyValue[] → GameDataLoader → Typed Data Structures
```

#### 1. File I/O Layer
- **AsyncFileReader**: Loads files asynchronously into native memory
- **CompressionDetector**: Handles compressed files automatically
- **Memory-mapped files**: For large datasets

#### 2. Tokenization Layer
- **Tokenizer**: High-performance, Burst-compiled byte-level tokenizer
- **TokenStream**: Stream of tokens for parsing
- **Token**: Individual language elements (identifiers, numbers, braces, etc.)

#### 3. Parsing Layer
- **ParadoxParser**: Converts tokens into structured data
- **ParsedKeyValue**: Key-value pairs with nested block support
- **ParserStateMachine**: Handles complex nested structures

#### 4. Data Management Layer
- **GameDataManager**: Central registry for all loaded data
- **Specific Loaders**: CountryLoader, AreaLoader, etc.
- **Typed Data Structures**: Country, Province, Area, etc.

### Data Flow

```csharp
// Step 1: File Reading
var fileResult = await AsyncFileReader.ReadFileAsync(filePath, Allocator.TempJob);

// Step 2: Tokenization
using var tokenizer = new Tokenizer(fileResult.Data, stringPool, errorAccumulator);
using var tokenStream = tokenizer.Tokenize(Allocator.Temp);

// Step 3: Parsing
var parseResult = ParadoxParser.Parse(tokenStream.GetRemainingSlice(), keyValues, childBlocks, fileResult.Data);

// Step 4: Data Extraction
foreach (var kvp in keyValues)
{
    ProcessKeyValue(kvp, fileResult.Data);
}

// Step 5: Object Creation
var gameObject = CreateTypedObject(extractedData);
```

---

## Basic Usage Examples

### Loading Countries

```csharp
public static bool LoadCountries(GameDataManager dataManager)
{
    string countriesPath = Path.Combine(Application.dataPath, "Data", "common", "countries");
    string[] countryFiles = Directory.GetFiles(countriesPath, "*.txt");

    ushort nextCountryId = 1;

    foreach (string filePath in countryFiles)
    {
        try
        {
            // Extract country tag from filename
            string fileName = Path.GetFileNameWithoutExtension(filePath);
            string countryTag = fileName.Substring(0, 3).ToUpper();

            // Parse the country file
            Country country = ParseCountryFile(filePath, nextCountryId, countryTag, fileName);

            if (country.IsValid)
            {
                dataManager.AddCountry(country);
                nextCountryId++;
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"Failed to parse {filePath}: {e.Message}");
        }
    }

    return true;
}

private static Country ParseCountryFile(string filePath, ushort countryId, string tag, string name)
{
    string content = File.ReadAllText(filePath);

    // Parse basic properties
    Color countryColor = ParseCountryColor(content);  // Extracts: color = { 255 0 0 }
    ushort capital = ParseCapital(content);           // Extracts: capital = 236

    return Country.Create(
        id: countryId,
        tag: tag,
        name: name,
        color: countryColor,
        capital: capital
    );
}
```

### Loading Province Data

```csharp
public async Task<Province> LoadProvinceAsync(string filePath, ushort provinceId)
{
    // Use the full parser for complex province files
    var fileResult = await AsyncFileReader.ReadFileAsync(filePath, Allocator.TempJob);

    using var stringPool = new NativeStringPool(100, Allocator.TempJob);
    using var errorAccumulator = new ErrorAccumulator(Allocator.TempJob);
    using var tokenizer = new Tokenizer(fileResult.Data, stringPool, errorAccumulator);
    using var tokenStream = tokenizer.Tokenize(Allocator.Temp);

    var keyValues = new NativeList<ParsedKeyValue>(100, Allocator.Temp);
    var childBlocks = new NativeList<ParsedKeyValue>(100, Allocator.Temp);

    var parseResult = ParadoxParser.Parse(
        tokenStream.GetRemainingSlice(),
        keyValues,
        childBlocks,
        fileResult.Data
    );

    if (!parseResult.Success)
    {
        Debug.LogError($"Failed to parse province file: {parseResult.ErrorType}");
        fileResult.Dispose();
        return default;
    }

    // Extract province data
    var province = new Province { id = provinceId };

    foreach (var kvp in keyValues)
    {
        string key = System.Text.Encoding.UTF8.GetString(kvp.Key.ToArray());

        switch (key)
        {
            case "culture":
                province.culture = ExtractStringValue(kvp.Value, fileResult.Data);
                break;
            case "religion":
                province.religion = ExtractStringValue(kvp.Value, fileResult.Data);
                break;
            case "base_tax":
                province.baseTax = ExtractIntValue(kvp.Value, fileResult.Data);
                break;
            case "owner":
                province.owner = ExtractStringValue(kvp.Value, fileResult.Data);
                break;
            case "history":
                // Handle nested historical data
                ProcessHistoryBlock(kvp.Value, childBlocks, fileResult.Data, ref province);
                break;
        }
    }

    fileResult.Dispose();
    return province;
}
```

### Loading Area Definitions

```csharp
public static bool LoadAreas(GameDataManager dataManager)
{
    string areasPath = Path.Combine(Application.dataPath, "Data", "map", "area.txt");

    if (!File.Exists(areasPath))
    {
        Debug.LogError($"Areas file not found: {areasPath}");
        return false;
    }

    string content = File.ReadAllText(areasPath);

    // Parse area definitions
    // Format: area_name = { 123 124 125 }
    string[] lines = content.Split('\n');
    ushort nextAreaId = 1;

    foreach (string line in lines)
    {
        string trimmed = line.Trim();

        if (trimmed.Contains("=") && trimmed.Contains("{"))
        {
            int equalsIndex = trimmed.IndexOf('=');
            string areaName = trimmed.Substring(0, equalsIndex).Trim();

            var provinceIds = ParseProvinceList(trimmed);

            if (provinceIds.Count > 0)
            {
                var area = Area.Create(nextAreaId, areaName, provinceIds);
                dataManager.AddArea(area);
                nextAreaId++;
            }
        }
    }

    return true;
}

private static List<ushort> ParseProvinceList(string line)
{
    var provinceIds = new List<ushort>();

    int startBrace = line.IndexOf('{');
    int endBrace = line.IndexOf('}');

    if (startBrace >= 0 && endBrace > startBrace)
    {
        string provincePart = line.Substring(startBrace + 1, endBrace - startBrace - 1);
        string[] values = provincePart.Split(new char[] { ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries);

        foreach (string value in values)
        {
            if (ushort.TryParse(value, out ushort provinceId))
            {
                provinceIds.Add(provinceId);
            }
        }
    }

    return provinceIds;
}
```

---

## Advanced Features

### Error Handling and Recovery

```csharp
public async Task<ParseResult> ParseWithErrorRecovery(string filePath)
{
    using var errorAccumulator = new ErrorAccumulator(Allocator.TempJob, maxErrors: 100);
    using var recoveryContext = new ErrorRecoveryContext(Allocator.TempJob);

    var fileResult = await AsyncFileReader.ReadFileAsync(filePath, Allocator.TempJob);

    // Configure error handling
    var parseOptions = new ParseOptions
    {
        MaxErrors = 50,
        ContinueOnError = true,
        EnableRecovery = true
    };

    using var tokenizer = new Tokenizer(fileResult.Data, stringPool, errorAccumulator);
    using var tokenStream = tokenizer.Tokenize(Allocator.Temp);

    var keyValues = new NativeList<ParsedKeyValue>(100, Allocator.Temp);
    var parseResult = ParadoxParser.Parse(tokenStream.GetRemainingSlice(), keyValues, childBlocks, fileResult.Data);

    // Handle errors
    if (!parseResult.Success || errorAccumulator.ErrorCount > 0)
    {
        Debug.LogWarning($"Parse completed with {errorAccumulator.ErrorCount} errors, {errorAccumulator.WarningCount} warnings");

        // Attempt recovery for each error
        var errors = errorAccumulator.GetAllErrors();
        foreach (var error in errors)
        {
            string recoveryMessage;
            var recoveryResult = recoveryContext.AttemptRecovery(error, out recoveryMessage);

            if (recoveryResult == RecoveryResult.Recovered)
            {
                Debug.Log($"Recovered from error: {recoveryMessage}");
            }
        }
    }

    fileResult.Dispose();
    return parseResult;
}
```

### Validation

```csharp
public ValidationResult ValidateGameData(GameDataManager dataManager)
{
    using var validationCollection = new ValidationResultCollection(Allocator.Temp);

    // Validate countries
    foreach (var country in dataManager.GetAllCountries())
    {
        if (string.IsNullOrEmpty(country.tag))
        {
            validationCollection.AddResult(
                ErrorResult.Error(ErrorCode.ValidationFieldInvalid, 0, 0, $"Country {country.id} has no tag")
            );
        }

        if (country.capital > 0 && !dataManager.HasProvince(country.capital))
        {
            validationCollection.AddResult(
                ErrorResult.Warning(ErrorCode.ValidationReferenceInvalid, 0, 0, $"Country {country.tag} capital {country.capital} does not exist")
            );
        }
    }

    // Validate areas
    foreach (var area in dataManager.GetAllAreas())
    {
        foreach (var provinceId in area.provinceIds)
        {
            if (!dataManager.HasProvince(provinceId))
            {
                validationCollection.AddResult(
                    ErrorResult.Error(ErrorCode.ValidationReferenceInvalid, 0, 0, $"Area {area.name} references non-existent province {provinceId}")
                );
            }
        }
    }

    return validationCollection.GetSummary();
}
```

### Performance Monitoring

```csharp
public async Task<PerformanceMetrics> LoadDataWithProfiling(string dataPath)
{
    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
    var metrics = new PerformanceMetrics();

    // File I/O timing
    var ioTimer = System.Diagnostics.Stopwatch.StartNew();
    var fileResult = await AsyncFileReader.ReadFileAsync(dataPath, Allocator.TempJob);
    ioTimer.Stop();
    metrics.FileIOTime = ioTimer.ElapsedMilliseconds;

    // Memory usage before parsing
    long memoryBefore = GC.GetTotalMemory(false);

    // Tokenization timing
    var tokenTimer = System.Diagnostics.Stopwatch.StartNew();
    using var tokenizer = new Tokenizer(fileResult.Data, stringPool, errorAccumulator);
    using var tokenStream = tokenizer.Tokenize(Allocator.Temp);
    tokenTimer.Stop();
    metrics.TokenizationTime = tokenTimer.ElapsedMilliseconds;

    // Parsing timing
    var parseTimer = System.Diagnostics.Stopwatch.StartNew();
    var keyValues = new NativeList<ParsedKeyValue>(100, Allocator.Temp);
    var parseResult = ParadoxParser.Parse(tokenStream.GetRemainingSlice(), keyValues, childBlocks, fileResult.Data);
    parseTimer.Stop();
    metrics.ParsingTime = parseTimer.ElapsedMilliseconds;

    // Memory usage after parsing
    long memoryAfter = GC.GetTotalMemory(false);
    metrics.MemoryUsed = memoryAfter - memoryBefore;

    stopwatch.Stop();
    metrics.TotalTime = stopwatch.ElapsedMilliseconds;
    metrics.TokenCount = tokenStream.Count;
    metrics.KeyValueCount = keyValues.Length;

    Debug.Log($"Performance: {metrics.TotalTime}ms total, {metrics.TokenCount} tokens, {metrics.MemoryUsed / 1024}KB memory");

    fileResult.Dispose();
    return metrics;
}

public struct PerformanceMetrics
{
    public long FileIOTime;
    public long TokenizationTime;
    public long ParsingTime;
    public long TotalTime;
    public long MemoryUsed;
    public int TokenCount;
    public int KeyValueCount;
}
```

---

## Data Extraction Patterns

### Generic Parser Helpers (Recommended)

**NEW**: Use the centralized `ParadoxDataExtractionHelpers` for all parsing operations:

```csharp
using ParadoxParser.Utilities;
using static ParadoxParser.Utilities.ParadoxDataExtractionHelpers;

public Country ExtractCountryData(NativeSlice<ParsedKeyValue> keyValues, NativeArray<byte> sourceData)
{
    var country = new Country();

    foreach (var kvp in keyValues)
    {
        switch (kvp.KeyHash)
        {
            case CommonKeyHashes.NAME_HASH:
                country.name = ExtractStringValue(kvp.Value, sourceData);
                break;
            case CommonKeyHashes.COLOR_HASH:
                country.color = ExtractColorValue(kvp, keyValues, sourceData);
                break;
            case CommonKeyHashes.CAPITAL_HASH:
                country.capital = ExtractUshortValue(kvp.Value, sourceData);
                break;
        }
    }

    return country;
}
```

**Benefits of Generic Helpers:**
- ✅ **Pre-computed hashes** - No runtime hash calculation overhead
- ✅ **Consistent error handling** - All helpers include proper validation
- ✅ **Performance monitoring** - Built-in timing and progress tracking
- ✅ **DRY principle** - Single source of truth for all parsing logic

### Available Helper Methods

```csharp
// String extraction
string name = ExtractStringValue(parsedValue, sourceData);
string keyName = ExtractKeyString(kvp.Key, sourceData);

// Numeric extraction
int intValue = ExtractIntValue(parsedValue, sourceData);
ushort shortValue = ExtractUshortValue(parsedValue, sourceData);

// List extraction (for province IDs, etc.)
List<ushort> provinceIds = ExtractUshortListFromBlock(kvp, childBlocks, sourceData);

// Color extraction
Color countryColor = ExtractColorValue(colorBlock, childBlocks, sourceData);

// Date handling
DateTime date = ParseParadoxDate("1444.11.11");
bool isDate = IsDateFormat(keyString);

// Performance monitoring
PerformanceMonitor.LogProgressWithTiming("MyLoader", itemCount, stopwatch);
PerformanceMonitor.ValidateParsingTime(stopwatch, "MyOperation", 100);
```

### Pre-Computed Hash Constants

```csharp
// Use these instead of computing hashes at runtime
uint nameHash = CommonKeyHashes.NAME_HASH;
uint colorHash = CommonKeyHashes.COLOR_HASH;
uint capitalHash = CommonKeyHashes.CAPITAL_HASH;
uint areasHash = CommonKeyHashes.AREAS_HASH;
uint ownerHash = CommonKeyHashes.OWNER_HASH;
uint controllerHash = CommonKeyHashes.CONTROLLER_HASH;
uint cultureHash = CommonKeyHashes.CULTURE_HASH;
uint religionHash = CommonKeyHashes.RELIGION_HASH;
uint historyHash = CommonKeyHashes.HISTORY_HASH;
```

### Hash-Based Key Lookup (Legacy Pattern)

```csharp
// OLD WAY - Don't do this anymore
private static readonly uint NAME_HASH = ParadoxParser.ComputeKeyHash(System.Text.Encoding.UTF8.GetBytes("name"));

// NEW WAY - Use pre-computed hashes
private static readonly uint NAME_HASH = CommonKeyHashes.NAME_HASH;
```

### Block Processing (Updated)

```csharp
using static ParadoxParser.Utilities.ParadoxDataExtractionHelpers;

public void ProcessHistoryBlock(ParsedValue blockValue, NativeList<ParsedKeyValue> allKeyValues, NativeArray<byte> sourceData, ref Province province)
{
    if (!blockValue.IsBlock)
        return;

    // Get the child key-values for this block using safe bounds checking
    int startIndex = blockValue.BlockStartIndex;
    int blockLength = blockValue.BlockLength;

    if (startIndex < 0 || blockLength <= 0 || startIndex >= allKeyValues.Length)
        return;

    int endIndex = Math.Min(startIndex + blockLength, allKeyValues.Length);

    for (int i = startIndex; i < endIndex; i++)
    {
        var kvp = allKeyValues[i];

        // Extract key using generic helper
        string key = ExtractKeyString(kvp.Key, sourceData);

        // Check if this is a date using generic helper
        if (IsDateFormat(key))
        {
            var date = ParseParadoxDate(key);
            ProcessHistoricalEvent(date, kvp.Value, allKeyValues, sourceData, ref province);
        }
        else
        {
            // Process regular properties using hash lookup for common keys
            switch (kvp.KeyHash)
            {
                case CommonKeyHashes.OWNER_HASH:
                    province.owner = ExtractStringValue(kvp.Value, sourceData);
                    break;
                case CommonKeyHashes.CONTROLLER_HASH:
                    province.controller = ExtractStringValue(kvp.Value, sourceData);
                    break;
                case CommonKeyHashes.CULTURE_HASH:
                    province.culture = ExtractStringValue(kvp.Value, sourceData);
                    break;
                case CommonKeyHashes.RELIGION_HASH:
                    province.religion = ExtractStringValue(kvp.Value, sourceData);
                    break;
                default:
                    // Handle unknown properties
                    ProcessUnknownProperty(key, kvp.Value, sourceData, ref province);
                    break;
            }
        }
    }
}

private bool IsDateFormat(string str)
{
    // Check for YYYY.MM.DD pattern
    if (str.Length == 10 && str[4] == '.' && str[7] == '.')
    {
        return int.TryParse(str.Substring(0, 4), out _) &&
               int.TryParse(str.Substring(5, 2), out _) &&
               int.TryParse(str.Substring(8, 2), out _);
    }
    return false;
}
```

### List Processing

```csharp
public List<ushort> ExtractProvinceList(ParsedValue listValue, NativeSlice<byte> sourceData)
{
    var provinces = new List<ushort>();

    if (!listValue.IsList)
        return provinces;

    // Lists store space-separated values in RawData
    string listContent = System.Text.Encoding.UTF8.GetString(listValue.RawData.ToArray());
    string[] values = listContent.Split(new char[] { ' ', '\t', '\n' }, System.StringSplitOptions.RemoveEmptyEntries);

    foreach (string value in values)
    {
        if (ushort.TryParse(value.Trim(), out ushort provinceId))
        {
            provinces.Add(provinceId);
        }
    }

    return provinces;
}
```

### Value Extraction (Legacy - Use Generic Helpers Instead)

**⚠️ DEPRECATED**: The old custom helper methods are deprecated. Use `ParadoxDataExtractionHelpers` instead.

```csharp
// OLD WAY (Don't use anymore)
public static string ExtractStringValue(ParsedValue value, NativeSlice<byte> sourceData) { ... }

// NEW WAY (Recommended)
using static ParadoxParser.Utilities.ParadoxDataExtractionHelpers;
string value = ExtractStringValue(parsedValue, sourceData);
```

### Complete Example: Loading Areas with Generic Helpers

```csharp
using ParadoxParser.Utilities;
using static ParadoxParser.Utilities.ParadoxDataExtractionHelpers;

public static async Task<bool> LoadAreasAsync(GameDataManager dataManager)
{
    // 1. Async file reading
    var fileResult = await AsyncFileReader.ReadFileAsync(areaFilePath, Allocator.TempJob);

    // 2. Parser setup
    using var stringPool = new NativeStringPool(500, Allocator.Temp);
    using var errorAccumulator = new ErrorAccumulator(Allocator.Temp);
    using var tokenizer = new Tokenizer(fileResult.Data, stringPool, errorAccumulator);
    using var tokenStream = tokenizer.Tokenize(Allocator.Temp);

    // 3. Parse
    var keyValues = new NativeList<ParsedKeyValue>(200, Allocator.Temp);
    var childBlocks = new NativeList<ParsedKeyValue>(1000, Allocator.Temp);
    var parseResult = ParadoxParser.Parse(tokenStream.GetRemainingSlice(), keyValues, childBlocks, fileResult.Data);

    // 4. Extract data using generic helpers
    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
    int areasLoaded = 0;

    foreach (var kvp in keyValues)
    {
        if (kvp.Value.IsBlock)
        {
            // Extract area name using generic helper
            string areaName = ExtractKeyString(kvp.Key, fileResult.Data);

            if (!string.IsNullOrEmpty(areaName) && !areaName.StartsWith("#"))
            {
                // Extract province IDs using generic helper
                var provinceIds = ExtractUshortListFromBlock(kvp, childBlocks, fileResult.Data);

                if (provinceIds.Count > 0)
                {
                    // Create area
                    Area area = Area.CreateLandArea((ushort)(areasLoaded + 1), areaName, provinceIds.ToArray());
                    dataManager.AddArea(area);
                    areasLoaded++;

                    // Performance monitoring using generic helper
                    PerformanceMonitor.LogProgressWithTiming("AreaLoader", areasLoaded, stopwatch, 100);
                }
            }
        }
    }

    // Performance validation using generic helper
    PerformanceMonitor.ValidateParsingTime(stopwatch, "AreaLoader", 100);

    fileResult.Dispose();
    return areasLoaded > 0;
}
```

---

## Integration Guide

### Setting Up GameDataManager

```csharp
[System.Serializable]
public class GameDataManager : MonoBehaviour
{
    [Header("Data Paths")]
    public string dataRootPath = "Data";
    public bool logLoadingProgress = true;

    [Header("Loaded Data")]
    public int countryCount;
    public int areaCount;
    public int regionCount;

    private Dictionary<ushort, Country> countriesById = new Dictionary<ushort, Country>();
    private Dictionary<string, Country> countriesByTag = new Dictionary<string, Country>();
    private Dictionary<ushort, Area> areasById = new Dictionary<ushort, Area>();
    private Dictionary<ushort, Region> regionsById = new Dictionary<ushort, Region>();

    private bool isLoaded = false;

    [ContextMenu("Load Game Data")]
    public bool LoadGameData()
    {
        if (isLoaded)
        {
            Debug.LogWarning("GameDataManager: Data already loaded");
            return true;
        }

        Debug.Log("GameDataManager: Starting data loading...");
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        bool success = true;

        // Load in dependency order
        success &= AreaLoader.LoadAreas(this, logLoadingProgress);
        success &= RegionLoader.LoadRegions(this, logLoadingProgress);
        success &= CountryLoader.LoadCountries(this, logLoadingProgress);

        if (success)
        {
            BuildLookupMappings();
            isLoaded = true;

            stopwatch.Stop();
            Debug.Log($"GameDataManager: Successfully loaded all data in {stopwatch.ElapsedMilliseconds}ms");
            Debug.Log($"  - Countries: {countryCount}");
            Debug.Log($"  - Areas: {areaCount}");
            Debug.Log($"  - Regions: {regionCount}");
        }
        else
        {
            Debug.LogError("GameDataManager: Failed to load game data");
        }

        return success;
    }

    private void BuildLookupMappings()
    {
        // Build fast lookup dictionaries
        countriesByTag.Clear();
        foreach (var country in countriesById.Values)
        {
            if (!string.IsNullOrEmpty(country.tag))
            {
                countriesByTag[country.tag] = country;
            }
        }

        // Update counts for inspector
        countryCount = countriesById.Count;
        areaCount = areasById.Count;
        regionCount = regionsById.Count;
    }

    // Public API methods
    public void AddCountry(Country country)
    {
        countriesById[country.id] = country;
    }

    public Country GetCountry(ushort id)
    {
        return countriesById.TryGetValue(id, out Country country) ? country : default;
    }

    public Country GetCountryByTag(string tag)
    {
        return countriesByTag.TryGetValue(tag, out Country country) ? country : default;
    }

    public bool HasCountry(ushort id)
    {
        return countriesById.ContainsKey(id);
    }

    public IEnumerable<Country> GetAllCountries()
    {
        return countriesById.Values;
    }
}
```

### Unity Editor Integration

```csharp
#if UNITY_EDITOR
using UnityEditor;

[CustomEditor(typeof(GameDataManager))]
public class GameDataManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Data Management", EditorStyles.boldLabel);

        GameDataManager manager = (GameDataManager)target;

        if (GUILayout.Button("Load All Data"))
        {
            manager.LoadGameData();
        }

        if (manager.countryCount > 0)
        {
            EditorGUILayout.LabelField($"Loaded {manager.countryCount} countries");
            EditorGUILayout.LabelField($"Loaded {manager.areaCount} areas");
            EditorGUILayout.LabelField($"Loaded {manager.regionCount} regions");

            if (GUILayout.Button("Validate Data"))
            {
                // Run validation
                Debug.Log("Data validation completed");
            }
        }
    }
}
#endif
```

---

## Performance Best Practices

### Memory Management

```csharp
public class ParserMemoryManager
{
    // Pre-allocate common collections
    private NativeStringPool stringPool;
    private ErrorAccumulator errorAccumulator;
    private NativeList<ParsedKeyValue> keyValueBuffer;
    private NativeList<ParsedKeyValue> childBlockBuffer;

    public void Initialize()
    {
        // Pre-allocate with reasonable sizes
        stringPool = new NativeStringPool(1000, Allocator.Persistent);           // 1000 unique strings
        errorAccumulator = new ErrorAccumulator(Allocator.Persistent, 100);     // 100 max errors
        keyValueBuffer = new NativeList<ParsedKeyValue>(500, Allocator.Persistent);  // 500 key-values
        childBlockBuffer = new NativeList<ParsedKeyValue>(200, Allocator.Persistent); // 200 nested blocks
    }

    public async Task<T> ParseFileOptimized<T>(string filePath, System.Func<NativeSlice<ParsedKeyValue>, NativeSlice<byte>, T> extractor)
    {
        // Reuse existing collections
        keyValueBuffer.Clear();
        childBlockBuffer.Clear();
        errorAccumulator.Clear();

        var fileResult = await AsyncFileReader.ReadFileAsync(filePath, Allocator.TempJob);

        try
        {
            using var tokenizer = new Tokenizer(fileResult.Data, stringPool, errorAccumulator);
            using var tokenStream = tokenizer.Tokenize(Allocator.Temp);

            var parseResult = ParadoxParser.Parse(
                tokenStream.GetRemainingSlice(),
                keyValueBuffer,
                childBlockBuffer,
                fileResult.Data
            );

            if (parseResult.Success)
            {
                return extractor(keyValueBuffer.AsArray().AsReadOnlySpan(), fileResult.Data);
            }

            return default(T);
        }
        finally
        {
            fileResult.Dispose();
        }
    }

    public void Cleanup()
    {
        if (stringPool.IsCreated) stringPool.Dispose();
        if (errorAccumulator.IsCreated) errorAccumulator.Dispose();
        if (keyValueBuffer.IsCreated) keyValueBuffer.Dispose();
        if (childBlockBuffer.IsCreated) childBlockBuffer.Dispose();
    }
}
```

### Batch Processing

```csharp
public async Task<List<T>> LoadMultipleFilesAsync<T>(string[] filePaths, System.Func<string, Task<T>> loader)
{
    const int BATCH_SIZE = 10; // Process 10 files at a time
    var results = new List<T>();

    for (int i = 0; i < filePaths.Length; i += BATCH_SIZE)
    {
        int batchEnd = Mathf.Min(i + BATCH_SIZE, filePaths.Length);
        var batch = filePaths.Skip(i).Take(batchEnd - i);

        // Process batch in parallel
        var batchTasks = batch.Select(loader);
        var batchResults = await Task.WhenAll(batchTasks);

        results.AddRange(batchResults);

        // Allow other Unity processes to run
        await Task.Yield();
    }

    return results;
}
```

### Streaming for Large Files

```csharp
public IEnumerable<Province> StreamProvinces(string provincesDirectory)
{
    string[] provinceFiles = Directory.GetFiles(provincesDirectory, "*.txt");

    using var memoryManager = new ParserMemoryManager();
    memoryManager.Initialize();

    foreach (string filePath in provinceFiles)
    {
        try
        {
            var province = await memoryManager.ParseFileOptimized(filePath, ExtractProvinceData);
            if (province.IsValid)
            {
                yield return province;
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"Failed to load province {filePath}: {e.Message}");
        }
    }

    memoryManager.Cleanup();
}
```

---

## Troubleshooting

### Common Issues and Solutions

#### Issue: "TokenStream capacity exceeded"
**Cause**: File has more tokens than estimated
**Solution**: Increase capacity estimation or use streaming
```csharp
// Increase estimation ratio for complex files
int estimatedTokens = math.max(2048, Length / 5); // More conservative estimate
var stream = new TokenStream(estimatedTokens, allocator);
```

#### Issue: "Out of memory during parsing"
**Cause**: Large files or memory leaks
**Solution**: Use streaming or check for disposal issues
```csharp
// Ensure proper disposal
using var fileResult = await AsyncFileReader.ReadFileAsync(filePath, Allocator.TempJob);
using var tokenizer = new Tokenizer(fileResult.Data, stringPool, errorAccumulator);
// ... parsing code
// Automatic disposal via 'using' statements
```

#### Issue: "ParseErrorType.UnmatchedBrace"
**Cause**: Malformed Paradox file with unclosed braces
**Solution**: Enable error recovery
```csharp
using var recoveryContext = new ErrorRecoveryContext(Allocator.TempJob);
// ... parsing with recovery enabled
```

#### Issue: "Key hash collisions"
**Cause**: Different keys producing same hash
**Solution**: Verify key extraction or use string comparison fallback
```csharp
public bool KeyMatches(ParsedKeyValue kvp, string targetKey, NativeSlice<byte> sourceData)
{
    // First check hash for speed
    uint targetHash = ParadoxParser.ComputeKeyHash(System.Text.Encoding.UTF8.GetBytes(targetKey));
    if (kvp.KeyHash != targetHash)
        return false;

    // Verify with string comparison for safety
    string actualKey = System.Text.Encoding.UTF8.GetString(kvp.Key.ToArray());
    return actualKey == targetKey;
}
```

#### Issue: "Slow parsing performance"
**Cause**: Inefficient memory allocation or missing Burst compilation
**Solution**: Pre-allocate buffers and verify Burst is enabled
```csharp
// Check if Burst is compiled
#if UNITY_EDITOR
if (!Unity.Burst.BurstCompiler.IsEnabled)
{
    Debug.LogWarning("Burst compilation is disabled - parsing will be slower");
}
#endif

// Pre-allocate collections
var keyValues = new NativeList<ParsedKeyValue>(estimatedSize, Allocator.Temp);
```

### Debug Utilities

```csharp
public static class ParserDebugUtils
{
    public static void LogParseResult(ParseResult result, string filePath)
    {
        if (result.Success)
        {
            Debug.Log($"✅ Successfully parsed {filePath} ({result.TokensProcessed} tokens)");
        }
        else
        {
            Debug.LogError($"❌ Failed to parse {filePath}: {result.ErrorType} at line {result.ErrorLine}, column {result.ErrorColumn}");
        }
    }

    public static void LogKeyValues(NativeSlice<ParsedKeyValue> keyValues, NativeSlice<byte> sourceData, int maxCount = 10)
    {
        Debug.Log($"📋 Found {keyValues.Length} key-value pairs:");

        for (int i = 0; i < Mathf.Min(keyValues.Length, maxCount); i++)
        {
            var kvp = keyValues[i];
            string key = System.Text.Encoding.UTF8.GetString(kvp.Key.ToArray());
            string valuePreview = GetValuePreview(kvp.Value, sourceData);

            Debug.Log($"  {i}: {key} = {valuePreview}");
        }

        if (keyValues.Length > maxCount)
        {
            Debug.Log($"  ... and {keyValues.Length - maxCount} more");
        }
    }

    private static string GetValuePreview(ParsedValue value, NativeSlice<byte> sourceData)
    {
        if (value.IsLiteral)
        {
            string str = System.Text.Encoding.UTF8.GetString(value.RawData.ToArray());
            return str.Length > 50 ? str.Substring(0, 50) + "..." : str;
        }
        else if (value.IsBlock)
        {
            return $"{{ {value.BlockLength} items }}";
        }
        else if (value.IsList)
        {
            return $"[ {value.BlockLength} items ]";
        }

        return "unknown";
    }
}
```

### Performance Profiling

```csharp
public class ParseProfiler
{
    private System.Diagnostics.Stopwatch totalTimer = new System.Diagnostics.Stopwatch();
    private System.Diagnostics.Stopwatch phaseTimer = new System.Diagnostics.Stopwatch();
    private List<(string phase, long milliseconds)> phaseTimings = new List<(string, long)>();

    public void StartProfiling()
    {
        totalTimer.Restart();
        phaseTimings.Clear();
    }

    public void StartPhase(string phaseName)
    {
        phaseTimer.Restart();
    }

    public void EndPhase(string phaseName)
    {
        phaseTimer.Stop();
        phaseTimings.Add((phaseName, phaseTimer.ElapsedMilliseconds));
    }

    public void EndProfiling(string operationName)
    {
        totalTimer.Stop();

        Debug.Log($"🔍 Performance Profile: {operationName} ({totalTimer.ElapsedMilliseconds}ms total)");
        foreach (var (phase, time) in phaseTimings)
        {
            float percentage = (time / (float)totalTimer.ElapsedMilliseconds) * 100f;
            Debug.Log($"  {phase}: {time}ms ({percentage:F1}%)");
        }
    }
}

// Usage:
var profiler = new ParseProfiler();
profiler.StartProfiling();

profiler.StartPhase("File I/O");
var fileResult = await AsyncFileReader.ReadFileAsync(filePath, Allocator.TempJob);
profiler.EndPhase("File I/O");

profiler.StartPhase("Tokenization");
using var tokenizer = new Tokenizer(fileResult.Data, stringPool, errorAccumulator);
using var tokenStream = tokenizer.Tokenize(Allocator.Temp);
profiler.EndPhase("Tokenization");

profiler.StartPhase("Parsing");
var parseResult = ParadoxParser.Parse(tokenStream.GetRemainingSlice(), keyValues, childBlocks, fileResult.Data);
profiler.EndPhase("Parsing");

profiler.EndProfiling("Country Loading");
```

---

## Summary

The ParadoxData parser provides a high-performance, Unity-native solution for loading Paradox Interactive game files. Key takeaways:

1. **Start Simple**: Use the basic string parsing patterns for simple files
2. **Scale Up**: Use the full parser pipeline for complex nested structures
3. **Optimize Early**: Pre-allocate collections and use memory pooling
4. **Handle Errors**: Always use ErrorAccumulator and recovery contexts
5. **Profile Performance**: Use the provided profiling tools to identify bottlenecks
6. **Follow Patterns**: Use the established data extraction patterns for consistency

The parser is designed to handle everything from simple key-value files to complex nested province histories while maintaining Unity's performance requirements for real-time games.

For additional examples and advanced usage, refer to the test files in `Assets/Scripts/ParadoxParser/Tests/` and the existing loader implementations in `Assets/Scripts/GameData/Loaders/`.