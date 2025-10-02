using NUnit.Framework;
using Unity.Collections;
using ParadoxParser.Core;
using ParadoxParser.Tokenization;
using ParadoxParser.Utilities;

namespace ParadoxParser.Tests
{
    [TestFixture]
    public class ParserStateMachineTests
    {
        private NativeArray<byte> sourceData;
        private NativeList<Token> tokens;
        private NativeList<ParadoxParser.Core.ParadoxParser.ParsedKeyValue> keyValues;
        private NativeList<ParadoxParser.Core.ParadoxParser.ParsedKeyValue> childBlocks;

        [SetUp]
        public void Setup()
        {
            sourceData = new NativeArray<byte>(1024, Allocator.Persistent);
            tokens = new NativeList<Token>(64, Allocator.Persistent);
            keyValues = new NativeList<ParadoxParser.Core.ParadoxParser.ParsedKeyValue>(32, Allocator.Persistent);
            childBlocks = new NativeList<ParadoxParser.Core.ParadoxParser.ParsedKeyValue>(32, Allocator.Persistent);
        }

        [TearDown]
        public void Teardown()
        {
            sourceData.Dispose();
            tokens.Dispose();
            keyValues.Dispose();
            childBlocks.Dispose();
        }

        [Test]
        public void TestStateTransitions()
        {
            var state = new ParserStateInfo();

            // Test initial state
            Assert.AreEqual(ParserState.Initial, state.State);
            Assert.AreEqual(0, state.BlockDepth);
            Assert.IsTrue(state.IsAtRootLevel);

            // Test entering block
            state.EnterBlock(false);
            Assert.AreEqual(ParserState.InBlock, state.State);
            Assert.AreEqual(1, state.BlockDepth);
            Assert.IsTrue(state.IsInContainer);

            // Test entering nested block
            state.EnterBlock(true);
            Assert.AreEqual(ParserState.InList, state.State);
            Assert.AreEqual(2, state.BlockDepth);
            Assert.IsTrue(state.IsInList);

            // Test exiting blocks
            state.ExitBlock();
            Assert.AreEqual(1, state.BlockDepth);
            Assert.IsFalse(state.IsInList);

            state.ExitBlock();
            Assert.AreEqual(0, state.BlockDepth);
            Assert.IsTrue(state.IsAtRootLevel);
        }

        [Test]
        public void TestSimpleKeyValueParsing()
        {
            // Test data: "key = value"
            string testData = "key = value";
            CopyStringToNative(testData, sourceData);

            // Create tokens
            tokens.Clear();
            tokens.Add(new Token { Type = TokenType.Identifier, StartPosition = 0, Length = 3, Line = 1 });
            tokens.Add(new Token { Type = TokenType.Equals, StartPosition = 4, Length = 1, Line = 1 });
            tokens.Add(new Token { Type = TokenType.Identifier, StartPosition = 6, Length = 5, Line = 1 });

            var kvp = new ParadoxParser.Core.ParadoxParser.ParsedKeyValue();
            int consumed;
            bool success = KeyValueParser.TryParseKeyValue(
                tokens.AsArray().Slice(0, tokens.Length), 0, out kvp, out consumed, sourceData.GetSubArray(0, sourceData.Length));

            Assert.IsTrue(success);
            Assert.AreEqual(3, consumed);
            Assert.AreEqual("key", GetStringFromSlice(kvp.Key));
            Assert.AreEqual("value", GetStringFromSlice(kvp.Value.RawData));
        }

        [Test]
        public void TestListParsing()
        {
            // Test data: "{ 1 2 3 }"
            string testData = "{ 1 2 3 }";
            CopyStringToNative(testData, sourceData);

            tokens.Clear();
            tokens.Add(new Token { Type = TokenType.LeftBrace, StartPosition = 0, Length = 1, Line = 1 });
            tokens.Add(new Token { Type = TokenType.Number, StartPosition = 2, Length = 1, Line = 1 });
            tokens.Add(new Token { Type = TokenType.Number, StartPosition = 4, Length = 1, Line = 1 });
            tokens.Add(new Token { Type = TokenType.Number, StartPosition = 6, Length = 1, Line = 1 });
            tokens.Add(new Token { Type = TokenType.RightBrace, StartPosition = 8, Length = 1, Line = 1 });

            var listItems = new NativeList<ListParser.ListItem>(16, Allocator.Temp);
            int consumed;
            bool success = ListParser.TryParseList(
                tokens.AsArray().Slice(0, tokens.Length), 1, out consumed, listItems, sourceData.GetSubArray(0, sourceData.Length));

            Assert.IsTrue(success);
            Assert.AreEqual(4, consumed); // Should consume everything except opening brace
            Assert.AreEqual(3, listItems.Length);
            Assert.IsTrue(listItems[0].IsInteger);
            Assert.AreEqual(1, listItems[0].IntValue);
            Assert.IsTrue(listItems[1].IsInteger);
            Assert.AreEqual(2, listItems[1].IntValue);
            Assert.IsTrue(listItems[2].IsInteger);
            Assert.AreEqual(3, listItems[2].IntValue);

            listItems.Dispose();
        }

        [Test]
        public void TestQuotedStringParsing()
        {
            // Test data: "\"hello world\""
            string testData = "\"hello world\"";
            CopyStringToNative(testData, sourceData);

            var outputBuffer = new NativeArray<byte>(64, Allocator.Temp);
            var result = QuotedStringParser.ParseQuotedString(sourceData.GetSubArray(0, sourceData.Length), outputBuffer);

            Assert.IsTrue(result.Success);
            Assert.AreEqual("hello world", GetStringFromSlice(result.Content));
            Assert.AreEqual(13, result.BytesConsumed); // Including quotes
            Assert.IsFalse(result.HasEscapes);

            outputBuffer.Dispose();
        }

        [Test]
        public void TestQuotedStringWithEscapes()
        {
            // Test data: "\"hello\\nworld\""
            string testData = "\"hello\\nworld\"";
            CopyStringToNative(testData, sourceData);

            var outputBuffer = new NativeArray<byte>(64, Allocator.Temp);
            var result = QuotedStringParser.ParseQuotedString(sourceData.GetSubArray(0, sourceData.Length), outputBuffer);

            Assert.IsTrue(result.Success);
            Assert.AreEqual("hello\nworld", GetStringFromSlice(result.Content));
            Assert.IsTrue(result.HasEscapes);

            outputBuffer.Dispose();
        }

        [Test]
        public void TestNestedBlockParsing()
        {
            // Test data: "country = { tag = GER capital = 50 }"
            string testData = "country = { tag = GER capital = 50 }";
            CopyStringToNative(testData, sourceData);

            tokens.Clear();
            // country
            tokens.Add(new Token { Type = TokenType.Identifier, StartPosition = 0, Length = 7, Line = 1 });
            // =
            tokens.Add(new Token { Type = TokenType.Equals, StartPosition = 8, Length = 1, Line = 1 });
            // {
            tokens.Add(new Token { Type = TokenType.LeftBrace, StartPosition = 10, Length = 1, Line = 1 });
            // tag
            tokens.Add(new Token { Type = TokenType.Identifier, StartPosition = 12, Length = 3, Line = 1 });
            // =
            tokens.Add(new Token { Type = TokenType.Equals, StartPosition = 16, Length = 1, Line = 1 });
            // GER
            tokens.Add(new Token { Type = TokenType.Identifier, StartPosition = 18, Length = 3, Line = 1 });
            // capital
            tokens.Add(new Token { Type = TokenType.Identifier, StartPosition = 22, Length = 7, Line = 1 });
            // =
            tokens.Add(new Token { Type = TokenType.Equals, StartPosition = 30, Length = 1, Line = 1 });
            // 50
            tokens.Add(new Token { Type = TokenType.Number, StartPosition = 32, Length = 2, Line = 1 });
            // }
            tokens.Add(new Token { Type = TokenType.RightBrace, StartPosition = 35, Length = 1, Line = 1 });

            var childKeyValues = new NativeList<ParadoxParser.Core.ParadoxParser.ParsedKeyValue>(16, Allocator.Temp);
            var kvp = new ParadoxParser.Core.ParadoxParser.ParsedKeyValue();
            int consumed;

            bool success = KeyValueParser.TryParseBlockValue(
                tokens.AsArray().Slice(0, tokens.Length), 0, out kvp, out consumed, sourceData.GetSubArray(0, sourceData.Length), childKeyValues);

            Assert.IsTrue(success);
            Assert.AreEqual("country", GetStringFromSlice(kvp.Key));
            Assert.AreEqual(ParadoxParser.Core.ParadoxParser.ParsedValueType.Block, kvp.Value.Type);
            Assert.AreEqual(2, childKeyValues.Length); // tag and capital

            childKeyValues.Dispose();
        }

        [Test]
        public void TestComplexListWithMixedTypes()
        {
            // Test data: "{ \"string\" 123 45.6 }"
            string testData = "{ \"string\" 123 45.6 }";
            CopyStringToNative(testData, sourceData);

            tokens.Clear();
            tokens.Add(new Token { Type = TokenType.LeftBrace, StartPosition = 0, Length = 1, Line = 1 });
            tokens.Add(new Token { Type = TokenType.String, StartPosition = 2, Length = 8, Line = 1 });
            tokens.Add(new Token { Type = TokenType.Number, StartPosition = 11, Length = 3, Line = 1 });
            tokens.Add(new Token { Type = TokenType.Number, StartPosition = 15, Length = 4, Line = 1, Flags = TokenFlags.IsFloat });
            tokens.Add(new Token { Type = TokenType.RightBrace, StartPosition = 20, Length = 1, Line = 1 });

            var listItems = new NativeList<ListParser.ListItem>(16, Allocator.Temp);
            int consumed;
            bool success = ListParser.TryParseList(
                tokens.AsArray().Slice(0, tokens.Length), 1, out consumed, listItems, sourceData.GetSubArray(0, sourceData.Length));

            Assert.IsTrue(success);
            Assert.AreEqual(3, listItems.Length);
            Assert.IsTrue(listItems[0].IsString);
            Assert.IsTrue(listItems[1].IsInteger);
            Assert.AreEqual(123, listItems[1].IntValue);
            Assert.IsTrue(listItems[2].IsFloat);
            Assert.AreEqual(45.6f, listItems[2].FloatValue, 0.001f);

            listItems.Dispose();
        }

        [Test]
        public void TestKeyHashing()
        {
            string key1 = "culture";
            string key2 = "culture";
            string key3 = "religion";

            uint hash1 = KeyValueParser.HashKey(key1);
            uint hash2 = KeyValueParser.HashKey(key2);
            uint hash3 = KeyValueParser.HashKey(key3);

            Assert.AreEqual(hash1, hash2); // Same strings should have same hash
            Assert.AreNotEqual(hash1, hash3); // Different strings should have different hashes
        }

        [Test]
        public void TestBooleanParsing()
        {
            var testCases = new[]
            {
                ("\"yes\"", true),
                ("\"no\"", false),
                ("\"true\"", true),
                ("\"false\"", false),
                ("\"1\"", true),
                ("\"0\"", false)
            };

            var outputBuffer = new NativeArray<byte>(64, Allocator.Temp);

            foreach (var (input, expected) in testCases)
            {
                CopyStringToNative(input, sourceData);
                var result = QuotedStringParser.ParseQuotedString(sourceData.GetSubArray(0, sourceData.Length), outputBuffer);

                Assert.IsTrue(result.Success);
                bool boolResult;
                bool canParse = QuotedStringParser.TryParseBool(result, out boolResult);
                Assert.IsTrue(canParse, $"Failed to parse boolean from: {input}");
                Assert.AreEqual(expected, boolResult, $"Wrong boolean value for: {input}");
            }

            outputBuffer.Dispose();
        }

        private void CopyStringToNative(string str, NativeArray<byte> target)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(str);
            for (int i = 0; i < bytes.Length && i < target.Length; i++)
            {
                target[i] = bytes[i];
            }
        }

        private string GetStringFromSlice(NativeSlice<byte> slice)
        {
            var bytes = new byte[slice.Length];
            for (int i = 0; i < slice.Length; i++)
            {
                bytes[i] = slice[i];
            }
            return System.Text.Encoding.UTF8.GetString(bytes);
        }
    }
}