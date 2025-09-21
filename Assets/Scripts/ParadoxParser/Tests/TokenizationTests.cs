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
    public class TokenizationTests
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

        #region Token Tests

        [Test]
        public void Token_CreateSimple_ShouldHaveCorrectProperties()
        {
            var token = Token.Create(TokenType.Identifier, 10, 5, 2, 3);

            Assert.AreEqual(TokenType.Identifier, token.Type);
            Assert.AreEqual(10, token.StartPosition);
            Assert.AreEqual(5, token.Length);
            Assert.AreEqual(15, token.EndPosition);
            Assert.AreEqual(2, token.Line);
            Assert.AreEqual(3, token.Column);
            Assert.IsTrue(token.IsValid);
            Assert.IsFalse(token.IsEndOfFile);
        }

        [Test]
        public void Token_CreateString_ShouldHaveStringProperties()
        {
            var token = Token.CreateString(0, 10, 1, 1, 12345u, 42);

            Assert.AreEqual(TokenType.String, token.Type);
            Assert.AreEqual(12345u, token.Hash);
            Assert.AreEqual(42, token.StringId);
            Assert.IsTrue(token.HasStringId);
        }

        [Test]
        public void Token_CreateNumber_ShouldHaveNumericProperties()
        {
            var token = Token.CreateNumber(0, 5, 1, 1, 123L, TokenFlags.IsFloat);

            Assert.AreEqual(TokenType.Number, token.Type);
            Assert.AreEqual(123L, token.NumericValue);
            Assert.AreEqual(TokenFlags.IsFloat, token.Flags);
        }

        [Test]
        public void Token_CreateEndOfFile_ShouldBeEOF()
        {
            var token = Token.CreateEndOfFile(100, 10, 5);

            Assert.AreEqual(TokenType.EndOfFile, token.Type);
            Assert.IsTrue(token.IsEndOfFile);
            Assert.AreEqual(100, token.StartPosition);
            Assert.AreEqual(0, token.Length);
        }

        #endregion

        #region TokenType Extension Tests

        [Test]
        public void TokenType_Extensions_ShouldClassifyCorrectly()
        {
            Assert.IsTrue(TokenType.Identifier.IsLiteral());
            Assert.IsTrue(TokenType.String.IsLiteral());
            Assert.IsTrue(TokenType.Number.IsLiteral());
            Assert.IsFalse(TokenType.LeftBrace.IsLiteral());

            Assert.IsTrue(TokenType.Equals.IsOperator());
            Assert.IsTrue(TokenType.GreaterThan.IsOperator());
            Assert.IsFalse(TokenType.Identifier.IsOperator());

            Assert.IsTrue(TokenType.LeftBrace.IsBracket());
            Assert.IsTrue(TokenType.RightBracket.IsBracket());
            Assert.IsFalse(TokenType.Identifier.IsBracket());

            Assert.IsTrue(TokenType.Whitespace.IsWhitespace());
            Assert.IsTrue(TokenType.Newline.IsWhitespace());
            Assert.IsFalse(TokenType.Identifier.IsWhitespace());
        }

        [Test]
        public void TokenType_ShouldSkip_ShouldWork()
        {
            Assert.IsTrue(TokenType.Whitespace.ShouldSkip());
            Assert.IsTrue(TokenType.Comment.ShouldSkip());
            Assert.IsFalse(TokenType.Identifier.ShouldSkip());
        }

        [Test]
        public void TokenType_BracketMatching_ShouldWork()
        {
            Assert.IsTrue(TokenType.LeftBrace.IsOpenBracket());
            Assert.IsTrue(TokenType.RightBrace.IsCloseBracket());
            Assert.AreEqual(TokenType.RightBrace, TokenType.LeftBrace.GetMatchingBracket());
            Assert.AreEqual(TokenType.RightBracket, TokenType.LeftBracket.GetMatchingBracket());
            Assert.AreEqual(TokenType.RightParen, TokenType.LeftParen.GetMatchingBracket());
        }

        #endregion

        #region TokenStream Tests

        [Test]
        public void TokenStream_Create_ShouldBeEmpty()
        {
            using var stream = new TokenStream(10, Allocator.Temp);

            Assert.IsTrue(stream.IsCreated);
            Assert.AreEqual(0, stream.Count);
            Assert.AreEqual(0, stream.Position);
            Assert.IsTrue(stream.IsAtEnd);
            Assert.AreEqual(0, stream.Remaining);
        }

        [Test]
        public void TokenStream_AddTokens_ShouldWork()
        {
            using var stream = new TokenStream(5, Allocator.Temp);

            var token1 = Token.Create(TokenType.Identifier, 0, 3, 1, 1);
            var token2 = Token.Create(TokenType.Equals, 3, 1, 1, 4);

            Assert.IsTrue(stream.TryAddToken(token1));
            Assert.IsTrue(stream.TryAddToken(token2));

            Assert.AreEqual(2, stream.Count);
            Assert.AreEqual(TokenType.Identifier, stream.Current.Type);
        }

        [Test]
        public void TokenStream_Navigation_ShouldWork()
        {
            using var stream = new TokenStream(5, Allocator.Temp);

            stream.TryAddToken(Token.Create(TokenType.Identifier, 0, 3, 1, 1));
            stream.TryAddToken(Token.Create(TokenType.Equals, 3, 1, 1, 4));
            stream.TryAddToken(Token.Create(TokenType.String, 4, 5, 1, 5));

            // Test Next()
            var first = stream.Next();
            Assert.AreEqual(TokenType.Identifier, first.Type);
            Assert.AreEqual(1, stream.Position);

            // Test Peek()
            var second = stream.Peek();
            Assert.AreEqual(TokenType.Equals, second.Type);
            Assert.AreEqual(1, stream.Position); // Position shouldn't change

            // Test Peek with offset
            var third = stream.Peek(1);
            Assert.AreEqual(TokenType.String, third.Type);

            // Test Advance()
            Assert.IsTrue(stream.Advance(2));
            Assert.AreEqual(3, stream.Position);
            Assert.IsTrue(stream.IsAtEnd);
        }

        [Test]
        public void TokenStream_Seek_ShouldWork()
        {
            using var stream = new TokenStream(3, Allocator.Temp);

            stream.TryAddToken(Token.Create(TokenType.Identifier, 0, 3, 1, 1));
            stream.TryAddToken(Token.Create(TokenType.Equals, 3, 1, 1, 4));

            Assert.IsTrue(stream.Seek(1));
            Assert.AreEqual(1, stream.Position);
            Assert.AreEqual(TokenType.Equals, stream.Current.Type);

            stream.Reset();
            Assert.AreEqual(0, stream.Position);
            Assert.AreEqual(TokenType.Identifier, stream.Current.Type);
        }

        [Test]
        public void TokenStream_Match_ShouldWork()
        {
            using var stream = new TokenStream(3, Allocator.Temp);

            stream.TryAddToken(Token.Create(TokenType.Identifier, 0, 3, 1, 1));
            stream.TryAddToken(Token.Create(TokenType.Equals, 3, 1, 1, 4));

            Assert.IsTrue(stream.Match(TokenType.Identifier));
            Assert.IsFalse(stream.Match(TokenType.String));

            Assert.IsTrue(stream.MatchAny(TokenType.String, TokenType.Identifier, TokenType.Number));
            Assert.IsFalse(stream.MatchAny(TokenType.String, TokenType.Number));
        }

        [Test]
        public void TokenStream_Consume_ShouldWork()
        {
            using var stream = new TokenStream(3, Allocator.Temp);

            stream.TryAddToken(Token.Create(TokenType.Identifier, 0, 3, 1, 1));
            stream.TryAddToken(Token.Create(TokenType.Equals, 3, 1, 1, 4));

            Assert.IsTrue(stream.Consume(TokenType.Identifier, out var token));
            Assert.AreEqual(TokenType.Identifier, token.Type);
            Assert.AreEqual(1, stream.Position);

            Assert.IsFalse(stream.Consume(TokenType.String, out var invalidToken));
            // When consume fails, token should be set to default (which has Type = EndOfFile)
            Assert.AreEqual(default(Token).Type, invalidToken.Type);
            Assert.AreEqual(1, stream.Position); // Position shouldn't change
        }

        #endregion

        #region Tokenizer Tests

        [Test]
        public void Tokenizer_TokenizeSimpleIdentifier_ShouldWork()
        {
            string input = "country_tag";
            var data = new NativeArray<byte>(Encoding.UTF8.GetBytes(input), Allocator.Temp);

            using var tokenizer = new Tokenizer(data, stringPool, errorAccumulator);
            using var stream = tokenizer.Tokenize(Allocator.Temp);

            Assert.AreEqual(2, stream.Count); // identifier + EOF
            Assert.AreEqual(TokenType.Identifier, stream.Current.Type);
            Assert.AreEqual(0, stream.Current.StartPosition);
            Assert.AreEqual(11, stream.Current.Length);
            Assert.AreEqual(1, stream.Current.Line);
            Assert.AreEqual(1, stream.Current.Column);

            data.Dispose();
        }

        [Test]
        public void Tokenizer_TokenizeString_ShouldWork()
        {
            string input = "\"quoted string\"";
            var data = new NativeArray<byte>(Encoding.UTF8.GetBytes(input), Allocator.Temp);

            using var tokenizer = new Tokenizer(data, stringPool, errorAccumulator);
            using var stream = tokenizer.Tokenize(Allocator.Temp);

            Assert.AreEqual(2, stream.Count); // string + EOF
            var token = stream.Current;
            Assert.AreEqual(TokenType.String, token.Type);
            Assert.AreEqual(0, token.StartPosition);
            Assert.AreEqual(15, token.Length);
            Assert.IsTrue((token.Flags & TokenFlags.IsQuoted) != 0);

            data.Dispose();
        }

        [Test]
        public void Tokenizer_TokenizeNumber_ShouldWork()
        {
            string input = "42 -123 45.67";
            var data = new NativeArray<byte>(Encoding.UTF8.GetBytes(input), Allocator.Temp);

            using var tokenizer = new Tokenizer(data, stringPool, errorAccumulator);
            using var stream = tokenizer.Tokenize(Allocator.Temp);

            Assert.GreaterOrEqual(stream.Count, 4); // 3 numbers + EOF

            // First number: 42
            var token1 = stream.Next();
            Assert.AreEqual(TokenType.Number, token1.Type);
            Assert.AreEqual(0, token1.StartPosition);
            Assert.AreEqual(2, token1.Length);
            Assert.IsFalse((token1.Flags & TokenFlags.IsNegative) != 0);
            Assert.IsFalse((token1.Flags & TokenFlags.IsFloat) != 0);

            // Skip to second number: -123
            stream.SkipWhitespaceAndComments();
            var token2 = stream.Next();
            Assert.AreEqual(TokenType.Number, token2.Type);
            Assert.IsTrue((token2.Flags & TokenFlags.IsNegative) != 0);

            // Skip to third number: 45.67
            stream.SkipWhitespaceAndComments();
            var token3 = stream.Next();
            Assert.AreEqual(TokenType.Number, token3.Type);
            Assert.IsTrue((token3.Flags & TokenFlags.IsFloat) != 0);

            data.Dispose();
        }

        [Test]
        public void Tokenizer_TokenizeOperators_ShouldWork()
        {
            string input = "= > < >= <= != += -=";
            var data = new NativeArray<byte>(Encoding.UTF8.GetBytes(input), Allocator.Temp);

            using var tokenizer = new Tokenizer(data, stringPool, errorAccumulator);
            using var stream = tokenizer.Tokenize(Allocator.Temp);

            stream.SkipWhitespaceAndComments();
            Assert.AreEqual(TokenType.Equals, stream.Next().Type);

            stream.SkipWhitespaceAndComments();
            Assert.AreEqual(TokenType.GreaterThan, stream.Next().Type);

            stream.SkipWhitespaceAndComments();
            Assert.AreEqual(TokenType.LessThan, stream.Next().Type);

            stream.SkipWhitespaceAndComments();
            Assert.AreEqual(TokenType.GreaterEquals, stream.Next().Type);

            stream.SkipWhitespaceAndComments();
            Assert.AreEqual(TokenType.LessEquals, stream.Next().Type);

            stream.SkipWhitespaceAndComments();
            Assert.AreEqual(TokenType.NotEquals, stream.Next().Type);

            stream.SkipWhitespaceAndComments();
            Assert.AreEqual(TokenType.Add, stream.Next().Type);

            stream.SkipWhitespaceAndComments();
            Assert.AreEqual(TokenType.Subtract, stream.Next().Type);

            data.Dispose();
        }

        [Test]
        public void Tokenizer_TokenizeBrackets_ShouldWork()
        {
            string input = "{ } [ ] ( )";
            var data = new NativeArray<byte>(Encoding.UTF8.GetBytes(input), Allocator.Temp);

            using var tokenizer = new Tokenizer(data, stringPool, errorAccumulator);
            using var stream = tokenizer.Tokenize(Allocator.Temp);

            stream.SkipWhitespaceAndComments();
            Assert.AreEqual(TokenType.LeftBrace, stream.Next().Type);

            stream.SkipWhitespaceAndComments();
            Assert.AreEqual(TokenType.RightBrace, stream.Next().Type);

            stream.SkipWhitespaceAndComments();
            Assert.AreEqual(TokenType.LeftBracket, stream.Next().Type);

            stream.SkipWhitespaceAndComments();
            Assert.AreEqual(TokenType.RightBracket, stream.Next().Type);

            stream.SkipWhitespaceAndComments();
            Assert.AreEqual(TokenType.LeftParen, stream.Next().Type);

            stream.SkipWhitespaceAndComments();
            Assert.AreEqual(TokenType.RightParen, stream.Next().Type);

            data.Dispose();
        }

        [Test]
        public void Tokenizer_TokenizeComment_ShouldWork()
        {
            string input = "# This is a comment\nidentifier";
            var data = new NativeArray<byte>(Encoding.UTF8.GetBytes(input), Allocator.Temp);

            using var tokenizer = new Tokenizer(data, stringPool, errorAccumulator);
            using var stream = tokenizer.Tokenize(Allocator.Temp);

            Assert.GreaterOrEqual(stream.Count, 3); // comment + identifier + EOF

            var commentToken = stream.Next();
            Assert.AreEqual(TokenType.Comment, commentToken.Type);
            Assert.AreEqual(0, commentToken.StartPosition);
            Assert.AreEqual(19, commentToken.Length); // "# This is a comment"

            stream.SkipWhitespaceAndComments();
            var identifierToken = stream.Current;
            Assert.AreEqual(TokenType.Identifier, identifierToken.Type);
            Assert.AreEqual(2, identifierToken.Line); // Should be on line 2

            data.Dispose();
        }

        [Test]
        public void Tokenizer_TokenizeBooleans_ShouldWork()
        {
            string input = "yes no";
            var data = new NativeArray<byte>(Encoding.UTF8.GetBytes(input), Allocator.Temp);

            using var tokenizer = new Tokenizer(data, stringPool, errorAccumulator);
            using var stream = tokenizer.Tokenize(Allocator.Temp);

            var yesToken = stream.Next();
            Assert.AreEqual(TokenType.Boolean, yesToken.Type);
            Assert.AreEqual(1, yesToken.NumericValue); // yes = 1

            stream.SkipWhitespaceAndComments();
            var noToken = stream.Next();
            Assert.AreEqual(TokenType.Boolean, noToken.Type);
            Assert.AreEqual(0, noToken.NumericValue); // no = 0

            data.Dispose();
        }

        [Test]
        public void Tokenizer_TokenizeComplexExpression_ShouldWork()
        {
            string input = "country = {\n    tag = \"ENG\"\n    name = \"England\"\n}";
            var data = new NativeArray<byte>(Encoding.UTF8.GetBytes(input), Allocator.Temp);

            using var tokenizer = new Tokenizer(data, stringPool, errorAccumulator);
            using var stream = tokenizer.Tokenize(Allocator.Temp);

            // Skip whitespace and comments, check tokens
            stream.SkipWhitespaceAndComments();
            Assert.AreEqual(TokenType.Identifier, stream.Next().Type); // country

            stream.SkipWhitespaceAndComments();
            Assert.AreEqual(TokenType.Equals, stream.Next().Type); // =

            stream.SkipWhitespaceAndComments();
            Assert.AreEqual(TokenType.LeftBrace, stream.Next().Type); // {

            stream.SkipWhitespaceAndComments();
            Assert.AreEqual(TokenType.Identifier, stream.Next().Type); // tag

            stream.SkipWhitespaceAndComments();
            Assert.AreEqual(TokenType.Equals, stream.Next().Type); // =

            stream.SkipWhitespaceAndComments();
            Assert.AreEqual(TokenType.String, stream.Next().Type); // "ENG"

            data.Dispose();
        }

        #endregion

        #region Error Handling Tests

        [Test]
        public void Tokenizer_InvalidCharacter_ShouldReportError()
        {
            string input = "test\x01invalid";
            var data = new NativeArray<byte>(Encoding.UTF8.GetBytes(input), Allocator.Temp);

            using var tokenizer = new Tokenizer(data, stringPool, errorAccumulator);
            using var stream = tokenizer.Tokenize(Allocator.Temp);

            Assert.Greater(errorAccumulator.TotalCount, 0);
            Assert.Greater(errorAccumulator.WarningCount, 0);

            data.Dispose();
        }

        #endregion
    }
}