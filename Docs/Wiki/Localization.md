# Localization System

The localization system provides multi-language support with YAML parsing, fallback chains, and dynamic string replacement.

## Architecture

```
Localization System
├── LocalizationManager      - High-level facade
├── MultiLanguageExtractor   - YAML file loading
├── LocalizationFallbackChain - Language fallback
├── StringReplacementSystem  - Parameter substitution
├── DynamicKeyResolver       - Complex key resolution
└── YAMLParser              - Low-level YAML parsing

File Structure:
localisation/
├── english/
│   ├── provinces.yml
│   ├── countries.yml
│   └── ui.yml
├── german/
│   └── ...
└── french/
    └── ...
```

**Key Principles:**
- YAML format compatible with Paradox games
- Language fallback chains (regional → base → english)
- Parameter substitution with multiple syntaxes
- Zero-allocation hot path using FixedString

## Basic Usage

### Initialization

```csharp
// Initialize with localisation folder path
LocalizationManager.Initialize("path/to/localisation", "english");

// Check status
if (LocalizationManager.IsInitialized)
{
    Debug.Log($"Languages: {LocalizationManager.AvailableLanguages.Count}");
}
```

### Getting Strings

```csharp
// Simple lookup
string provinceName = LocalizationManager.Get("PROV123");
string countryName = LocalizationManager.Get("RED");
string buttonText = LocalizationManager.Get("UI_OK");

// With parameters
string message = LocalizationManager.Get("INCOME_MESSAGE",
    ("gold", "500"),
    ("source", "trade"));
// "You earned {gold} gold from {source}" → "You earned 500 gold from trade"
```

### Changing Language

```csharp
// Get available languages
IReadOnlyList<string> languages = LocalizationManager.AvailableLanguages;

// Change language
LocalizationManager.SetLanguage("german");

// Cache is automatically cleared on language change
```

### Checking Keys

```csharp
// Check if key exists
if (LocalizationManager.HasKey("PROV123"))
{
    // Key exists
}

// If key doesn't exist, Get() returns the key itself
string unknown = LocalizationManager.Get("UNKNOWN_KEY"); // Returns "UNKNOWN_KEY"
```

## YAML File Format

### Standard Format

```yaml
l_english:
  PROV123: "Province Name"
  RED: "Red Empire"
  UI_OK: "OK"
  UI_CANCEL: "Cancel"
  INCOME_MESSAGE: "You earned $gold$ gold from $source$"
```

### Parameter Syntaxes

```yaml
# Dollar signs (Paradox style)
MESSAGE_1: "$gold$ gold earned"

# Curly braces
MESSAGE_2: "{gold} gold earned"

# Square brackets
MESSAGE_3: "[gold] gold earned"
```

## Fallback Chains

### Standard Fallback

When a key isn't found in the current language:

```
l_german_de → l_german → l_english → key itself
```

### Creating Custom Chain

```csharp
var languages = new NativeArray<FixedString64Bytes>(3, Allocator.Persistent);
languages[0] = new FixedString64Bytes("l_spanish_mx");
languages[1] = new FixedString64Bytes("l_spanish");
languages[2] = new FixedString64Bytes("l_english");

var chain = LocalizationFallbackChain.CreateCustomFallbackChain(
    languages, Allocator.Persistent);
```

### Regional Fallbacks

Built-in regional fallbacks:
- `l_english_us` → `l_english`
- `l_english_gb` → `l_english`
- `l_german_de` → `l_german`
- `l_german_at` → `l_german`
- `l_french_fr` → `l_french`
- `l_french_ca` → `l_french`
- `l_spanish_es` → `l_spanish`
- `l_spanish_mx` → `l_spanish`
- `l_trad_chinese` → `l_simp_chinese`

## String Replacement

### Simple Replacement

```csharp
string result = LocalizationManager.Get("MESSAGE",
    ("gold", "500"),
    ("province", "Rome"));
```

### Advanced Replacement

```csharp
// Create replacement context
var context = StringReplacementSystem.CreateContext(10, Allocator.Temp);

// Add parameters
StringReplacementSystem.AddParameter(ref context,
    new FixedString64Bytes("gold"),
    new FixedString512Bytes("500"));

StringReplacementSystem.AddParameter(ref context,
    new FixedString64Bytes("province"),
    new FixedString512Bytes("Rome"));

// Process string
var inputString = new FixedString512Bytes("You own $province$ with $gold$ gold");
var result = StringReplacementSystem.ProcessString(inputString, context);

// result.ProcessedString = "You own Rome with 500 gold"

context.Dispose();
```

### Conditional Text

```yaml
MESSAGE: "[?HAS_GOLD]You have gold![/HAS_GOLD][?NO_GOLD]No gold![/NO_GOLD]"
```

```csharp
var context = StringReplacementSystem.CreateContext(5, Allocator.Temp);
StringReplacementSystem.AddCondition(ref context,
    new FixedString64Bytes("HAS_GOLD"), true);
StringReplacementSystem.AddCondition(ref context,
    new FixedString64Bytes("NO_GOLD"), false);

// Result: "You have gold!"
```

## Dynamic Key Resolution

For complex runtime key generation:

```csharp
var context = DynamicKeyResolver.CreateContext(5, Allocator.Persistent);

// Add variables
DynamicKeyResolver.AddVariable(ref context,
    new FixedString64Bytes("culture"),
    new FixedString512Bytes("roman"));

// Resolve pattern with variables
var pattern = new FixedString128Bytes("CULTURE_{culture}_NAME");
var result = DynamicKeyResolver.ResolveDynamicKey(
    pattern, context, multiLangResult, preferredLanguage);

// Resolves "CULTURE_roman_NAME" to localized string
```

## Performance

### Caching

LocalizationManager caches resolved strings:

```csharp
// First call: lookup + cache
string name = LocalizationManager.Get("PROV123");

// Subsequent calls: cache hit
string name2 = LocalizationManager.Get("PROV123");

// Clear cache if needed
LocalizationManager.ClearCache();
```

### Statistics

```csharp
var (languages, entries, completeness) = LocalizationManager.GetStatistics();
Debug.Log($"Languages: {languages}");
Debug.Log($"Total entries: {entries}");
Debug.Log($"Average completeness: {completeness:P0}");
```

## Shutdown

```csharp
// Release all resources
LocalizationManager.Shutdown();
```

## Integration Example

```csharp
public class LocalizedUIPanel : MonoBehaviour
{
    [SerializeField] private Label titleLabel;
    [SerializeField] private Label descriptionLabel;
    [SerializeField] private Button okButton;
    [SerializeField] private Button cancelButton;

    void Start()
    {
        // Localize static UI
        okButton.text = LocalizationManager.Get("UI_OK");
        cancelButton.text = LocalizationManager.Get("UI_CANCEL");
    }

    public void ShowProvince(ushort provinceId)
    {
        // Localize dynamic content
        string key = $"PROV{provinceId}";
        titleLabel.text = LocalizationManager.Get(key);

        // With parameters
        int development = GetDevelopment(provinceId);
        descriptionLabel.text = LocalizationManager.Get("PROVINCE_INFO",
            ("development", development.ToString()),
            ("terrain", GetTerrainName(provinceId)));
    }
}
```

## Best Practices

1. **Use consistent key naming** - `PROV_`, `COUNTRY_`, `UI_` prefixes
2. **Provide fallback strings** - English should have all keys
3. **Use parameters for dynamic values** - Not string concatenation
4. **Clear cache on language change** - Automatic with SetLanguage()
5. **Initialize early** - During game startup, before UI creation

## API Reference

- [LocalizationManager](~/api/Core.Localization.LocalizationManager.html) - Main facade
- [LocalizationFallbackChain](~/api/Core.Localization.LocalizationFallbackChain.html) - Fallback handling
- [StringReplacementSystem](~/api/Core.Localization.StringReplacementSystem.html) - Parameter substitution
- [DynamicKeyResolver](~/api/Core.Localization.DynamicKeyResolver.html) - Complex key resolution
