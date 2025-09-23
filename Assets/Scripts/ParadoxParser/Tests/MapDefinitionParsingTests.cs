using NUnit.Framework;
using Unity.Collections;
using UnityEngine;
using UnityEngine.TestTools;
using ParadoxParser.Tokenization;
using ParadoxParser.Core;
using ParadoxParser.Data;
using System.Text;

namespace ParadoxParser.Tests
{
    public class MapDefinitionParsingTests
    {
        private NativeStringPool stringPool;
        private ErrorAccumulator errorAccumulator;

        [SetUp]
        public void Setup()
        {
            stringPool = new NativeStringPool(1000, Allocator.TempJob);
            errorAccumulator = new ErrorAccumulator(Allocator.TempJob);
        }

        [TearDown]
        public void TearDown()
        {
            if (stringPool.IsCreated)
                stringPool.Dispose();
            if (errorAccumulator.IsCreated)
                errorAccumulator.Dispose();
        }

        [Test]
        public void ParseMapDefinition_ShouldTokenizeCorrectly()
        {
            // Sample content from default.map
            string testContent = @"width = 5632
height = 2048

max_provinces = 4942
sea_starts = {
	1252 1253 1254 1255 1256 1257 1258 1259 1263 1264 1265 1266 1267 1268 1269 1270 1271 1272 1274 1275 1276 1277 1278 1279
	1280 1281 1282 1283 1284 1285 1286 1287 1288 1289 1290 1291 1292 1293 1294 1295 1296 1297 1298 1299 1300 1301 1302 1303 1304
}";

            var contentBytes = Encoding.UTF8.GetBytes(testContent);
            var data = new NativeArray<byte>(contentBytes, Allocator.Temp);

            try
            {
                using var tokenizer = new Tokenizer(data, stringPool, errorAccumulator);
                using var tokenStream = tokenizer.Tokenize(Allocator.Temp);

                Debug.Log($"Tokenization completed: {tokenStream.Count} tokens generated");

                // Should have more than just a few tokens
                Assert.Greater(tokenStream.Count, 10, "Should generate multiple tokens");

                // Check for errors
                Assert.AreEqual(0, errorAccumulator.ErrorCount, $"Should have no tokenization errors, but had: {errorAccumulator.ErrorCount}");

                // Log first few tokens for debugging
                LogTokens(tokenStream, data, 20);
            }
            finally
            {
                data.Dispose();
            }
        }

        [Test]
        public void ParseMapDefinition_ShouldFindSeaStarts()
        {
            string testContent = @"width = 5632
sea_starts = {
	1252 1253 1254 1255 1256 1257 1258 1259
	1280 1281 1282 1283 1284 1285 1286 1287
}";

            var contentBytes = Encoding.UTF8.GetBytes(testContent);
            var data = new NativeArray<byte>(contentBytes, Allocator.Temp);

            try
            {
                using var tokenizer = new Tokenizer(data, stringPool, errorAccumulator);
                using var tokenStream = tokenizer.Tokenize(Allocator.Temp);

                Debug.Log($"Generated {tokenStream.Count} tokens");

                bool foundSeaStarts = false;
                bool foundLeftBrace = false;
                int numberCount = 0;

                while (tokenStream.Position < tokenStream.Count && !tokenStream.Current.IsEndOfFile)
                {
                    var token = tokenStream.Current;

                    if (token.Type == TokenType.Identifier)
                    {
                        string identifier = ExtractTokenString(token, data);
                        Debug.Log($"Found identifier: '{identifier}'");

                        if (identifier == "sea_starts")
                        {
                            foundSeaStarts = true;
                            Debug.Log("Found sea_starts identifier!");
                        }
                    }
                    else if (token.Type == TokenType.LeftBrace && foundSeaStarts)
                    {
                        foundLeftBrace = true;
                        Debug.Log("Found opening brace after sea_starts");
                    }
                    else if (token.Type == TokenType.Number && foundLeftBrace)
                    {
                        numberCount++;
                        if (numberCount <= 5) // Log first 5 numbers
                        {
                            Debug.Log($"Sea province #{numberCount}: {token.NumericValue}");
                        }
                    }

                    tokenStream.Next();
                }

                Debug.Log($"Results: foundSeaStarts={foundSeaStarts}, foundLeftBrace={foundLeftBrace}, numberCount={numberCount}");

                Assert.IsTrue(foundSeaStarts, "Should find 'sea_starts' identifier");
                Assert.IsTrue(foundLeftBrace, "Should find opening brace after sea_starts");
                Assert.Greater(numberCount, 10, "Should find multiple sea province numbers");
            }
            finally
            {
                data.Dispose();
            }
        }

        [Test]
        public void ParseMapDefinition_ShouldParseAllBasicFields()
        {
            string testContent = @"width = 5632
height = 2048
max_provinces = 4942";

            var contentBytes = Encoding.UTF8.GetBytes(testContent);
            var data = new NativeArray<byte>(contentBytes, Allocator.Temp);

            try
            {
                using var tokenizer = new Tokenizer(data, stringPool, errorAccumulator);
                using var tokenStream = tokenizer.Tokenize(Allocator.Temp);

                int width = 0, height = 0, maxProvinces = 0;
                int fieldsFound = 0;

                while (tokenStream.Position < tokenStream.Count && !tokenStream.Current.IsEndOfFile)
                {
                    var token = tokenStream.Current;

                    if (token.Type == TokenType.Identifier)
                    {
                        string key = ExtractTokenString(token, data);

                        // Move to next token (should be '=')
                        tokenStream.Next();
                        if (tokenStream.Current.Type == TokenType.Equals)
                        {
                            tokenStream.Next(); // Move to value

                            if (tokenStream.Current.Type == TokenType.Number)
                            {
                                string numberStr = ExtractTokenString(tokenStream.Current, data);
                                int.TryParse(numberStr, out int value);

                                switch (key)
                                {
                                    case "width":
                                        width = value;
                                        fieldsFound++;
                                        Debug.Log($"Parsed width: {width}");
                                        break;
                                    case "height":
                                        height = value;
                                        fieldsFound++;
                                        Debug.Log($"Parsed height: {height}");
                                        break;
                                    case "max_provinces":
                                        maxProvinces = value;
                                        fieldsFound++;
                                        Debug.Log($"Parsed max_provinces: {maxProvinces}");
                                        break;
                                }
                            }
                        }
                    }

                    tokenStream.Next();
                }

                Assert.AreEqual(3, fieldsFound, "Should find all 3 basic fields");
                Assert.AreEqual(5632, width, "Width should be 5632");
                Assert.AreEqual(2048, height, "Height should be 2048");
                Assert.AreEqual(4942, maxProvinces, "Max provinces should be 4942");
            }
            finally
            {
                data.Dispose();
            }
        }

        private void LogTokens(TokenStream tokenStream, NativeArray<byte> sourceData, int maxTokens)
        {
            Debug.Log("=== First Few Tokens ===");
            tokenStream.Reset();

            int count = 0;
            while (tokenStream.Position < tokenStream.Count && !tokenStream.Current.IsEndOfFile && count < maxTokens)
            {
                var token = tokenStream.Current;
                string value = ExtractTokenString(token, sourceData);

                Debug.Log($"Token {count}: {token.Type} = '{value}' (pos:{token.StartPosition}, len:{token.Length})");

                tokenStream.Next();
                count++;
            }

            tokenStream.Reset();
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
    }
}