using UnityEngine;
using Unity.Collections;
using System.IO;
using System.Collections.Generic;
using ParadoxParser.Tokenization;
using ParadoxParser.Core;
using ParadoxParser.Data;
using System.Text;

[System.Serializable]
public class CountryData
{
    public string tag;
    public string name;
    public Color color1;
    public Color color2;
    public Color color3;
    public Color displayColor; // Primary color for map display
}

[System.Serializable]
public class ProvinceOwnership
{
    public int provinceId;
    public string owner;
    public string controller;
    public string culture;
    public string religion;
}

public class CountryDataLoader : MonoBehaviour
{
    [Header("Data Settings")]
    public bool loadCountryColors = true;
    public bool loadProvinceOwnership = true;

    [Header("Debug")]
    public bool showDebugInfo = false;

    public Dictionary<string, CountryData> CountriesByTag { get; private set; }
    public Dictionary<int, ProvinceOwnership> ProvinceOwners { get; private set; }
    private Dictionary<string, string> countryTagToFile = new Dictionary<string, string>();
    public bool IsLoaded { get; private set; }

    public bool LoadCountryData()
    {
        CountriesByTag = new Dictionary<string, CountryData>();
        ProvinceOwners = new Dictionary<int, ProvinceOwnership>();

        bool success = true;

        if (loadCountryColors)
        {
            success &= LoadCountryTags();
            if (success)
            {
                success &= LoadCountryFiles();
            }
        }

        if (loadProvinceOwnership)
        {
            success &= LoadProvinceOwnership();
        }

        IsLoaded = success;
        return success;
    }

    private bool LoadCountryTags()
    {
        string commonPath = Path.Combine(Application.dataPath, "Data", "common", "country_tags");
        string tagsFilePath = Path.Combine(commonPath, "00_countries.txt");

        if (!File.Exists(tagsFilePath))
        {
            Debug.LogError($"Country tags file not found: {tagsFilePath}");
            return false;
        }

        if (showDebugInfo)
            Debug.Log($"Loading country tags: {tagsFilePath}");

        try
        {
            string content = File.ReadAllText(tagsFilePath);
            return ParseCountryTags(content);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to read country tags file: {e.Message}");
            return false;
        }
    }

    private bool ParseCountryTags(string content)
    {
        var contentBytes = Encoding.UTF8.GetBytes(content);
        var data = new NativeArray<byte>(contentBytes, Allocator.Temp);

        using var stringPool = new NativeStringPool(1000, Allocator.Temp);
        using var errorAccumulator = new ErrorAccumulator(Allocator.Temp);

        try
        {
            using var tokenizer = new Tokenizer(data, stringPool, errorAccumulator);
            using var tokenStream = tokenizer.Tokenize(Allocator.Temp);

            int tagsLoaded = 0;

            while (tokenStream.Position < tokenStream.Count && !tokenStream.Current.IsEndOfFile)
            {
                var token = tokenStream.Current;

                if (token.Type == TokenType.Identifier)
                {
                    string countryTag = ExtractTokenString(token, data);

                    // Skip comments and special cases
                    if (countryTag.StartsWith("#") || countryTag.Length != 3)
                    {
                        tokenStream.Next();
                        continue;
                    }

                    // Look for = "filename"
                    tokenStream.Next();
                    if (tokenStream.Current.Type == TokenType.Equals)
                    {
                        tokenStream.Next();
                        if (tokenStream.Current.Type == TokenType.String)
                        {
                            string fileName = ExtractTokenString(tokenStream.Current, data);
                            // Remove quotes
                            fileName = fileName.Trim('"');

                            countryTagToFile[countryTag] = fileName;
                            tagsLoaded++;
                        }
                    }
                }

                tokenStream.Next();
            }

            if (showDebugInfo)
                Debug.Log($"Loaded {tagsLoaded} country tag mappings");

            return true;
        }
        finally
        {
            data.Dispose();
        }
    }

    private bool LoadCountryFiles()
    {
        string commonPath = Path.Combine(Application.dataPath, "Data", "common");

        if (!Directory.Exists(commonPath))
        {
            Debug.LogError($"Common directory not found: {commonPath}");
            return false;
        }

        int countriesLoaded = 0;

        foreach (var kvp in countryTagToFile)
        {
            string tag = kvp.Key;
            string fileName = kvp.Value; // Already includes "countries/" prefix
            string filePath = Path.Combine(commonPath, fileName);

            if (File.Exists(filePath))
            {
                var countryData = ParseCountryFile(filePath, tag);
                if (countryData != null)
                {
                    CountriesByTag[tag] = countryData;
                    countriesLoaded++;
                }
            }
            else if (showDebugInfo)
            {
                Debug.LogWarning($"Country file not found: {filePath}");
            }
        }

        if (showDebugInfo)
            Debug.Log($"Loaded {countriesLoaded} country definitions");

        return true;
    }

    private CountryData ParseCountryFile(string filePath, string tag)
    {
        try
        {
            string content = File.ReadAllText(filePath);
            return ParseCountryContent(content, tag);
        }
        catch (System.Exception e)
        {
            if (showDebugInfo)
                Debug.LogWarning($"Failed to parse country file {filePath}: {e.Message}");
            return null;
        }
    }

    private CountryData ParseCountryContent(string content, string tag)
    {
        var countryData = new CountryData { tag = tag, name = tag };
        var contentBytes = Encoding.UTF8.GetBytes(content);
        var data = new NativeArray<byte>(contentBytes, Allocator.Temp);

        using var stringPool = new NativeStringPool(1000, Allocator.Temp);
        using var errorAccumulator = new ErrorAccumulator(Allocator.Temp);

        try
        {
            using var tokenizer = new Tokenizer(data, stringPool, errorAccumulator);
            using var tokenStream = tokenizer.Tokenize(Allocator.Temp);

            int braceDepth = 0;

            while (tokenStream.Position < tokenStream.Count && !tokenStream.Current.IsEndOfFile)
            {
                var token = tokenStream.Current;

                // Track brace depth
                if (token.Type == TokenType.LeftBrace)
                {
                    braceDepth++;
                }
                else if (token.Type == TokenType.RightBrace)
                {
                    braceDepth--;
                }
                // Only process top-level key-value pairs
                else if (token.Type == TokenType.Identifier && braceDepth == 0)
                {
                    string key = ExtractTokenString(token, data);

                    // Look for = value
                    tokenStream.Next();
                    if (tokenStream.Current.Type == TokenType.Equals)
                    {
                        tokenStream.Next();

                        if (key == "color" && tokenStream.Current.Type == TokenType.LeftBrace)
                        {
                            countryData.displayColor = ParseRGBColor(tokenStream, data);
                            countryData.color1 = countryData.displayColor;
                        }
                    }
                }

                tokenStream.Next();
            }
        }
        finally
        {
            data.Dispose();
        }

        return countryData;
    }

    private bool LoadCountryColors()
    {
        string commonPath = Path.Combine(Application.dataPath, "Data", "common", "country_colors");
        string colorsFilePath = Path.Combine(commonPath, "00_country_colors.txt");

        if (!File.Exists(colorsFilePath))
        {
            Debug.LogError($"Country colors file not found: {colorsFilePath}");
            return false;
        }

        if (showDebugInfo)
            Debug.Log($"Loading country colors: {colorsFilePath}");

        try
        {
            string content = File.ReadAllText(colorsFilePath);
            return ParseCountryColors(content);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to read country colors file: {e.Message}");
            return false;
        }
    }

    private bool ParseCountryColors(string content)
    {
        var contentBytes = Encoding.UTF8.GetBytes(content);
        var data = new NativeArray<byte>(contentBytes, Allocator.Temp);

        using var stringPool = new NativeStringPool(1000, Allocator.Temp);
        using var errorAccumulator = new ErrorAccumulator(Allocator.Temp);

        try
        {
            using var tokenizer = new Tokenizer(data, stringPool, errorAccumulator);
            using var tokenStream = tokenizer.Tokenize(Allocator.Temp);

            int countriesLoaded = 0;

            while (tokenStream.Position < tokenStream.Count && !tokenStream.Current.IsEndOfFile)
            {
                var token = tokenStream.Current;

                if (token.Type == TokenType.Identifier)
                {
                    string countryTag = ExtractTokenString(token, data);

                    // Skip template and comments
                    if (countryTag.StartsWith("#") || countryTag.Contains("template"))
                    {
                        tokenStream.Next();
                        continue;
                    }

                    // Look for = {
                    tokenStream.Next();
                    if (tokenStream.Current.Type == TokenType.Equals)
                    {
                        tokenStream.Next();
                        if (tokenStream.Current.Type == TokenType.LeftBrace)
                        {
                            var countryData = ParseCountryColorBlock(tokenStream, data, countryTag);
                            if (countryData != null)
                            {
                                CountriesByTag[countryTag] = countryData;
                                countriesLoaded++;
                            }
                        }
                    }
                }

                tokenStream.Next();
            }

            if (showDebugInfo)
                Debug.Log($"Loaded colors for {countriesLoaded} countries");

            return true;
        }
        finally
        {
            data.Dispose();
        }
    }

    private CountryData ParseCountryColorBlock(TokenStream tokenStream, NativeArray<byte> data, string tag)
    {
        var countryData = new CountryData { tag = tag, name = tag };

        tokenStream.Next(); // Skip opening brace

        while (tokenStream.Position < tokenStream.Count &&
               tokenStream.Current.Type != TokenType.RightBrace &&
               !tokenStream.Current.IsEndOfFile)
        {
            if (tokenStream.Current.Type == TokenType.Identifier)
            {
                string key = ExtractTokenString(tokenStream.Current, data);

                // Look for = {
                tokenStream.Next();
                if (tokenStream.Current.Type == TokenType.Equals)
                {
                    tokenStream.Next();
                    if (tokenStream.Current.Type == TokenType.LeftBrace)
                    {
                        var color = ParseRGBColor(tokenStream, data);

                        switch (key)
                        {
                            case "color1":
                                countryData.color1 = color;
                                countryData.displayColor = color; // Use color1 as primary
                                break;
                            case "color2":
                                countryData.color2 = color;
                                break;
                            case "color3":
                                countryData.color3 = color;
                                break;
                        }
                    }
                }
            }

            tokenStream.Next();
        }

        return countryData;
    }

    private Color ParseRGBColor(TokenStream tokenStream, NativeArray<byte> data)
    {
        tokenStream.Next(); // Skip opening brace

        float r = 0, g = 0, b = 0;
        int colorIndex = 0;

        while (tokenStream.Position < tokenStream.Count &&
               tokenStream.Current.Type != TokenType.RightBrace &&
               !tokenStream.Current.IsEndOfFile)
        {
            if (tokenStream.Current.Type == TokenType.Number)
            {
                string numberStr = ExtractTokenString(tokenStream.Current, data);
                if (float.TryParse(numberStr, out float value))
                {
                    switch (colorIndex)
                    {
                        case 0: r = value / 255f; break;
                        case 1: g = value / 255f; break;
                        case 2: b = value / 255f; break;
                    }
                    colorIndex++;
                }
            }

            tokenStream.Next();
        }

        return new Color(r, g, b, 1f);
    }

    private bool LoadProvinceOwnership()
    {
        string provincesPath = Path.Combine(Application.dataPath, "Data", "history", "provinces");

        if (!Directory.Exists(provincesPath))
        {
            Debug.LogError($"Provinces directory not found: {provincesPath}");
            return false;
        }

        var provinceFiles = Directory.GetFiles(provincesPath, "*.txt");
        int loadedCount = 0;

        foreach (string filePath in provinceFiles)
        {
            // Extract province ID from filename like "100 - Friesland.txt"
            string fileName = Path.GetFileNameWithoutExtension(filePath);
            string[] parts = fileName.Split(new char[] { ' ', '-' }, System.StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length > 0 && int.TryParse(parts[0], out int provinceId))
            {
                var ownership = ParseProvinceFile(filePath, provinceId);
                if (ownership != null)
                {
                    ProvinceOwners[provinceId] = ownership;
                    loadedCount++;
                }
            }
        }

        if (showDebugInfo)
            Debug.Log($"Loaded ownership data for {loadedCount} provinces");

        return true;
    }

    private ProvinceOwnership ParseProvinceFile(string filePath, int provinceId)
    {
        try
        {
            string content = File.ReadAllText(filePath);
            return ParseProvinceContent(content, provinceId);
        }
        catch (System.Exception e)
        {
            if (showDebugInfo)
                Debug.LogWarning($"Failed to parse province file {filePath}: {e.Message}");
            return null;
        }
    }

    private ProvinceOwnership ParseProvinceContent(string content, int provinceId)
    {
        var ownership = new ProvinceOwnership { provinceId = provinceId };
        var contentBytes = Encoding.UTF8.GetBytes(content);
        var data = new NativeArray<byte>(contentBytes, Allocator.Temp);

        using var stringPool = new NativeStringPool(1000, Allocator.Temp);
        using var errorAccumulator = new ErrorAccumulator(Allocator.Temp);

        try
        {
            using var tokenizer = new Tokenizer(data, stringPool, errorAccumulator);
            using var tokenStream = tokenizer.Tokenize(Allocator.Temp);

            int braceDepth = 0; // Track nesting depth to skip date blocks

            while (tokenStream.Position < tokenStream.Count && !tokenStream.Current.IsEndOfFile)
            {
                var token = tokenStream.Current;

                // Track brace depth
                if (token.Type == TokenType.LeftBrace)
                {
                    braceDepth++;
                }
                else if (token.Type == TokenType.RightBrace)
                {
                    braceDepth--;
                }
                // Check for date blocks (e.g., "1453.5.29 = {")
                else if (token.Type == TokenType.Date && braceDepth == 0)
                {
                    // Skip to the opening brace of the date block
                    while (tokenStream.Position < tokenStream.Count &&
                           tokenStream.Current.Type != TokenType.LeftBrace &&
                           !tokenStream.Current.IsEndOfFile)
                    {
                        tokenStream.Next();
                    }
                    // The brace will be handled in the next iteration
                }
                // Only process key-value pairs at the base level (outside date blocks)
                else if (token.Type == TokenType.Identifier && braceDepth == 0)
                {
                    string key = ExtractTokenString(token, data);

                    // Look for = value
                    tokenStream.Next();
                    if (tokenStream.Current.Type == TokenType.Equals)
                    {
                        tokenStream.Next();
                        if (tokenStream.Current.Type == TokenType.Identifier)
                        {
                            string value = ExtractTokenString(tokenStream.Current, data);

                            switch (key)
                            {
                                case "owner":
                                    ownership.owner = value;
                                    break;
                                case "controller":
                                    ownership.controller = value;
                                    break;
                                case "culture":
                                    ownership.culture = value;
                                    break;
                                case "religion":
                                    ownership.religion = value;
                                    break;
                            }
                        }
                    }
                }

                tokenStream.Next();
            }
        }
        finally
        {
            data.Dispose();
        }

        return ownership;
    }

    private string ExtractTokenString(Token token, NativeArray<byte> sourceData)
    {
        if (token.StartPosition + token.Length > sourceData.Length)
            return "";

        var bytes = new byte[token.Length];
        for (int i = 0; i < token.Length; i++)
        {
            bytes[i] = sourceData[token.StartPosition + i];
        }

        return Encoding.UTF8.GetString(bytes);
    }

    public CountryData GetCountryByTag(string tag)
    {
        return CountriesByTag?.ContainsKey(tag) == true ? CountriesByTag[tag] : null;
    }

    public ProvinceOwnership GetProvinceOwnership(int provinceId)
    {
        return ProvinceOwners?.ContainsKey(provinceId) == true ? ProvinceOwners[provinceId] : null;
    }

    public string GetProvinceOwner(int provinceId)
    {
        var ownership = GetProvinceOwnership(provinceId);
        return ownership?.owner;
    }

    public Color GetCountryColor(string tag)
    {
        var country = GetCountryByTag(tag);
        return country?.displayColor ?? Color.gray;
    }

    [ContextMenu("Load Country Data")]
    public void LoadCountryDataManual()
    {
        LoadCountryData();
    }

    [ContextMenu("Log Statistics")]
    public void LogStatistics()
    {
        if (!IsLoaded)
        {
            Debug.Log("No country data loaded");
            return;
        }

        Debug.Log($"Country Data Statistics:");
        Debug.Log($"- Countries with colors: {CountriesByTag?.Count ?? 0}");
        Debug.Log($"- Provinces with ownership: {ProvinceOwners?.Count ?? 0}");

        if (ProvinceOwners?.Count > 0)
        {
            var ownerCount = new Dictionary<string, int>();
            foreach (var ownership in ProvinceOwners.Values)
            {
                if (!string.IsNullOrEmpty(ownership.owner))
                {
                    ownerCount[ownership.owner] = ownerCount.GetValueOrDefault(ownership.owner, 0) + 1;
                }
            }

            Debug.Log($"- Countries owning provinces: {ownerCount.Count}");
        }
    }
}