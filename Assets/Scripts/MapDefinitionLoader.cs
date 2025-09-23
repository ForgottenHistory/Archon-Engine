using UnityEngine;
using Unity.Collections;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;
using ParadoxParser.Tokenization;
using ParadoxParser.Core;
using ParadoxParser.Data;
using System.Text;

[System.Serializable]
public class MapDefinition
{
    public int width;
    public int height;
    public int maxProvinces;
    public HashSet<int> seaProvinces = new HashSet<int>();
    public HashSet<int> lakeProvinces = new HashSet<int>();
    public HashSet<int> landProvinces = new HashSet<int>();

    public bool IsSeaProvince(int provinceId) => seaProvinces.Contains(provinceId);
    public bool IsLakeProvince(int provinceId) => lakeProvinces.Contains(provinceId);
    public bool IsLandProvince(int provinceId) => landProvinces.Contains(provinceId);
}

public class MapDefinitionLoader : MonoBehaviour
{
    [Header("Map Definition Settings")]
    public string mapDefinitionFile = "default.map";

    [Header("Debug")]
    public bool showDebugInfo = false;

    public MapDefinition MapData { get; private set; }
    public bool IsLoaded { get; private set; }

    public async Task<bool> LoadMapDefinition()
    {
        string mapDataPath = Path.Combine(Application.dataPath, "Data", "map");
        string mapFilePath = Path.Combine(mapDataPath, mapDefinitionFile);

        if (!File.Exists(mapFilePath))
        {
            Debug.LogError($"Map definition file not found: {mapFilePath}");
            return false;
        }

        if (showDebugInfo)
            Debug.Log($"Loading map definition: {mapFilePath}");

        try
        {
            string content = await File.ReadAllTextAsync(mapFilePath);
            return ParseMapDefinition(content);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to read map definition file: {e.Message}");
            return false;
        }
    }

    private bool ParseMapDefinition(string content)
    {
        try
        {
            MapData = ParseMapDefinitionWithParadoxParser(content);
            IsLoaded = true;

            if (showDebugInfo)
            {
                Debug.Log($"Map definition loaded: {MapData.width}x{MapData.height}, " +
                         $"Max provinces: {MapData.maxProvinces}, " +
                         $"Sea provinces: {MapData.seaProvinces.Count}");
            }

            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to parse map definition: {e.Message}");
            return false;
        }
    }

    private MapDefinition ParseMapDefinitionWithParadoxParser(string content)
    {
        var mapDef = new MapDefinition();

        // Convert string to byte array
        var contentBytes = Encoding.UTF8.GetBytes(content);
        var data = new NativeArray<byte>(contentBytes, Allocator.TempJob);

        // Create parser components
        using var stringPool = new NativeStringPool(1000, Allocator.TempJob);
        using var errorAccumulator = new ErrorAccumulator(Allocator.TempJob);

        try
        {
            // Create tokenizer and tokenize the data
            using var tokenizer = new Tokenizer(data, stringPool, errorAccumulator);
            using var tokenStream = tokenizer.Tokenize(Allocator.TempJob);

            if (showDebugInfo)
                Debug.Log($"Tokenized map definition: {tokenStream.Count} tokens");

            // Parse tokens to extract map data
            ParseTokensIntoMapDefinition(tokenStream, mapDef, stringPool, data);

            // Mark all other provinces as land (up to max_provinces)
            for (int i = 1; i <= mapDef.maxProvinces; i++)
            {
                if (!mapDef.seaProvinces.Contains(i))
                {
                    mapDef.landProvinces.Add(i);
                }
            }

            // Check for parsing errors
            if (errorAccumulator.ErrorCount > 0 && showDebugInfo)
            {
                Debug.LogWarning($"Map definition parsing had {errorAccumulator.ErrorCount} errors");
            }
        }
        finally
        {
            if (data.IsCreated)
                data.Dispose();
        }

        return mapDef;
    }

    private void ParseTokensIntoMapDefinition(TokenStream tokenStream, MapDefinition mapDef, NativeStringPool stringPool, NativeArray<byte> sourceData)
    {
        while (tokenStream.Position < tokenStream.Count && !tokenStream.Current.IsEndOfFile)
        {
            var token = tokenStream.Current;

            if (token.Type == TokenType.Identifier)
            {
                // Extract the identifier string
                string key = ExtractTokenString(token, sourceData);

                // Move to next token (should be '=')
                tokenStream.Next();
                if (tokenStream.Current.Type == TokenType.Equals)
                {
                    tokenStream.Next(); // Move to value

                    switch (key)
                    {
                        case "width":
                            if (tokenStream.Current.Type == TokenType.Number)
                            {
                                string numberStr = ExtractTokenString(tokenStream.Current, sourceData);
                                if (int.TryParse(numberStr, out int width))
                                {
                                    mapDef.width = width;
                                    if (showDebugInfo)
                                        Debug.Log($"Parsed width: {mapDef.width}");
                                }
                            }
                            break;

                        case "height":
                            if (tokenStream.Current.Type == TokenType.Number)
                            {
                                string numberStr = ExtractTokenString(tokenStream.Current, sourceData);
                                if (int.TryParse(numberStr, out int height))
                                {
                                    mapDef.height = height;
                                    if (showDebugInfo)
                                        Debug.Log($"Parsed height: {mapDef.height}");
                                }
                            }
                            break;

                        case "max_provinces":
                            if (tokenStream.Current.Type == TokenType.Number)
                            {
                                string numberStr = ExtractTokenString(tokenStream.Current, sourceData);
                                if (int.TryParse(numberStr, out int maxProvinces))
                                {
                                    mapDef.maxProvinces = maxProvinces;
                                    if (showDebugInfo)
                                        Debug.Log($"Parsed max_provinces: {mapDef.maxProvinces}");
                                }
                            }
                            break;

                        case "sea_starts":
                            if (showDebugInfo)
                                Debug.Log($"Found sea_starts, current token type: {tokenStream.Current.Type}");
                            if (tokenStream.Current.Type == TokenType.LeftBrace)
                            {
                                ParseSeaProvincesFromTokens(tokenStream, mapDef, sourceData);
                            }
                            else
                            {
                                Debug.LogWarning($"Expected LeftBrace after sea_starts, got: {tokenStream.Current.Type}");
                            }
                            break;
                    }
                }
            }

            tokenStream.Next();
        }
    }

    private void ParseSeaProvincesFromTokens(TokenStream tokenStream, MapDefinition mapDef, NativeArray<byte> sourceData)
    {
        tokenStream.Next(); // Skip opening brace

        while (tokenStream.Position < tokenStream.Count &&
               tokenStream.Current.Type != TokenType.RightBrace &&
               !tokenStream.Current.IsEndOfFile)
        {
            if (tokenStream.Current.Type == TokenType.Number)
            {
                // Use string extraction instead of NumericValue
                string numberStr = ExtractTokenString(tokenStream.Current, sourceData);
                if (int.TryParse(numberStr, out int provinceId))
                {
                    mapDef.seaProvinces.Add(provinceId);
                }
            }

            tokenStream.Next();
        }

        if (showDebugInfo)
            Debug.Log($"Parsed {mapDef.seaProvinces.Count} sea provinces using ParadoxParser");
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


    [ContextMenu("Load Map Definition")]
    public async void LoadMapDefinitionManual()
    {
        await LoadMapDefinition();
    }

    public MapDefinition GetMapDefinition()
    {
        return MapData;
    }
}