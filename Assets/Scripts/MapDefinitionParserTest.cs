using UnityEngine;
using Unity.Collections;
using ParadoxParser.Tokenization;
using ParadoxParser.Core;
using ParadoxParser.Data;
using System.Text;

public class MapDefinitionParserTest : MonoBehaviour
{
    [Header("Test Settings")]
    public bool runTestOnStart = false;
    public bool showDetailedTokens = false;

    [ContextMenu("Test Map Definition Parsing")]
    public void TestMapDefinitionParsing()
    {
        // Sample content from your default.map file
        string testContent = @"width = 5632
height = 2048

max_provinces = 4942
sea_starts = {
	1252 1253 1254 1255 1256 1257 1258 1259 1263 1264 1265 1266 1267 1268 1269 1270 1271 1272 1274 1275 1276 1277 1278 1279
	1280 1281 1282 1283 1284 1285 1286 1287 1288 1289 1290 1291 1292 1293 1294 1295 1296 1297 1298 1299 1300 1301 1302 1303 1304
	1305 1307 1308 1309 1310 1311 1312 1313 1314 1315 1316 1317	1319 1320 1321 1322 1323 1324 1328 1329 1330 1331 1332
	1333 1334 1335 1336 1337 1338 1339 1340 1341 1342 1343 1344 1345 1346 1347 1348 1349 1350 1351 1352 1353 1354 1355 1356 1357
}";

        Debug.Log("=== Starting Map Definition Parser Test ===");
        Debug.Log($"Test content length: {testContent.Length} characters");

        // Convert to byte array
        var contentBytes = Encoding.UTF8.GetBytes(testContent);
        var data = new NativeArray<byte>(contentBytes, Allocator.TempJob);

        // Create parser components
        using var stringPool = new NativeStringPool(1000, Allocator.TempJob);
        using var errorAccumulator = new ErrorAccumulator(Allocator.TempJob);

        try
        {
            // Create tokenizer and tokenize
            using var tokenizer = new Tokenizer(data, stringPool, errorAccumulator);
            using var tokenStream = tokenizer.Tokenize(Allocator.TempJob);

            Debug.Log($"Tokenization completed: {tokenStream.Count} tokens generated");

            if (showDetailedTokens)
            {
                ShowAllTokens(tokenStream, data);
            }

            // Test parsing
            TestParseMapDefinition(tokenStream, data);

            // Check for errors
            if (errorAccumulator.ErrorCount > 0)
            {
                Debug.LogWarning($"Parsing had {errorAccumulator.ErrorCount} errors");
            }
            else
            {
                Debug.Log("Parsing completed with no errors");
            }
        }
        finally
        {
            if (data.IsCreated)
                data.Dispose();
        }

        Debug.Log("=== Map Definition Parser Test Complete ===");
    }

    private void ShowAllTokens(TokenStream tokenStream, NativeArray<byte> sourceData)
    {
        Debug.Log("=== All Tokens ===");
        tokenStream.Reset();

        int tokenIndex = 0;
        while (tokenStream.Position < tokenStream.Count && !tokenStream.Current.IsEndOfFile)
        {
            var token = tokenStream.Current;
            string tokenValue = ExtractTokenString(token, sourceData);

            Debug.Log($"Token {tokenIndex}: Type={token.Type}, Value='{tokenValue}', Pos={token.StartPosition}, Len={token.Length}, Line={token.Line}, Col={token.Column}");

            tokenStream.Next();
            tokenIndex++;

            // Limit output to prevent spam
            if (tokenIndex > 100)
            {
                Debug.Log("... (truncated, showing first 100 tokens)");
                break;
            }
        }

        tokenStream.Reset(); // Reset for actual parsing
    }

    private void TestParseMapDefinition(TokenStream tokenStream, NativeArray<byte> sourceData)
    {
        Debug.Log("=== Testing Map Definition Parsing ===");

        int width = 0, height = 0, maxProvinces = 0;
        int seaProvinceCount = 0;

        while (tokenStream.Position < tokenStream.Count && !tokenStream.Current.IsEndOfFile)
        {
            var token = tokenStream.Current;

            if (token.Type == TokenType.Identifier)
            {
                string key = ExtractTokenString(token, sourceData);
                Debug.Log($"Found identifier: '{key}'");

                // Move to next token (should be '=')
                tokenStream.Next();
                if (tokenStream.Current.Type == TokenType.Equals)
                {
                    Debug.Log($"Found equals after '{key}'");
                    tokenStream.Next(); // Move to value

                    switch (key)
                    {
                        case "width":
                            if (tokenStream.Current.Type == TokenType.Number)
                            {
                                width = (int)tokenStream.Current.NumericValue;
                                Debug.Log($"Parsed width: {width}");
                            }
                            else
                            {
                                Debug.LogWarning($"Expected number for width, got: {tokenStream.Current.Type}");
                            }
                            break;

                        case "height":
                            if (tokenStream.Current.Type == TokenType.Number)
                            {
                                height = (int)tokenStream.Current.NumericValue;
                                Debug.Log($"Parsed height: {height}");
                            }
                            else
                            {
                                Debug.LogWarning($"Expected number for height, got: {tokenStream.Current.Type}");
                            }
                            break;

                        case "max_provinces":
                            if (tokenStream.Current.Type == TokenType.Number)
                            {
                                maxProvinces = (int)tokenStream.Current.NumericValue;
                                Debug.Log($"Parsed max_provinces: {maxProvinces}");
                            }
                            else
                            {
                                Debug.LogWarning($"Expected number for max_provinces, got: {tokenStream.Current.Type}");
                            }
                            break;

                        case "sea_starts":
                            Debug.Log($"Found sea_starts, current token type: {tokenStream.Current.Type}");
                            if (tokenStream.Current.Type == TokenType.LeftBrace)
                            {
                                seaProvinceCount = ParseSeaProvincesTest(tokenStream);
                                Debug.Log($"Parsed {seaProvinceCount} sea provinces");
                            }
                            else
                            {
                                Debug.LogWarning($"Expected LeftBrace after sea_starts, got: {tokenStream.Current.Type}");
                            }
                            break;

                        default:
                            Debug.Log($"Skipping unknown key: '{key}'");
                            break;
                    }
                }
                else
                {
                    Debug.LogWarning($"Expected equals after '{key}', got: {tokenStream.Current.Type}");
                }
            }

            tokenStream.Next();
        }

        Debug.Log($"=== Parsing Results ===");
        Debug.Log($"Width: {width}");
        Debug.Log($"Height: {height}");
        Debug.Log($"Max Provinces: {maxProvinces}");
        Debug.Log($"Sea Provinces Found: {seaProvinceCount}");
    }

    private int ParseSeaProvincesTest(TokenStream tokenStream)
    {
        Debug.Log("Starting sea provinces parsing...");
        tokenStream.Next(); // Skip opening brace

        int count = 0;
        int tokensParsed = 0;

        while (tokenStream.Position < tokenStream.Count &&
               tokenStream.Current.Type != TokenType.RightBrace &&
               !tokenStream.Current.IsEndOfFile)
        {
            tokensParsed++;

            if (tokenStream.Current.Type == TokenType.Number)
            {
                int provinceId = (int)tokenStream.Current.NumericValue;
                count++;

                if (count <= 10) // Show first 10 for debugging
                {
                    Debug.Log($"Sea province #{count}: {provinceId}");
                }
            }
            else
            {
                Debug.Log($"Non-number token in sea_starts: {tokenStream.Current.Type}");
            }

            tokenStream.Next();

            // Safety check to prevent infinite loops
            if (tokensParsed > 1000)
            {
                Debug.LogWarning("Parsed 1000+ tokens in sea_starts, breaking to prevent infinite loop");
                break;
            }
        }

        Debug.Log($"Sea provinces parsing complete. Found {count} provinces after parsing {tokensParsed} tokens");
        return count;
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

    void Start()
    {
        if (runTestOnStart)
        {
            TestMapDefinitionParsing();
        }
    }
}