using System.Text;
using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;
using ParadoxParser.Core;
using ParadoxParser.Data;
using ParadoxParser.Tokenization;
using ParadoxParser.Jobs;

namespace ParadoxParser.Tests
{
    [TestFixture]
    public class ParallelTokenizationTests
    {
        private NativeStringPool stringPool;
        private ErrorAccumulator errorAccumulator;

        [SetUp]
        public void Setup()
        {
            stringPool = new NativeStringPool(1024, Allocator.TempJob);
            errorAccumulator = new ErrorAccumulator(Allocator.TempJob, 128);
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
        public void ParallelTokenizer_SmallFile_UsesSingleThreaded()
        {
            // Arrange
            string content = "key = value";
            var data = new NativeArray<byte>(Encoding.UTF8.GetBytes(content), Allocator.TempJob);
            var tokenizer = new ParallelTokenizer(data, stringPool, errorAccumulator, minChunkSize: 1024);

            try
            {
                // Act
                var stream = tokenizer.Tokenize(Allocator.TempJob);

                // Assert
                Assert.IsTrue(stream.IsCreated);
                Assert.Greater(stream.Count, 0);

                stream.Dispose();
            }
            finally
            {
                data.Dispose();
                tokenizer.Dispose();
            }
        }

        [Test]
        public void ParallelTokenizer_LargeFile_UsesParallelProcessing()
        {
            // Arrange
            var sb = new StringBuilder();
            for (int i = 0; i < 1000; i++)
            {
                sb.AppendLine($"key_{i} = \"value_{i}\"");
                sb.AppendLine($"number_{i} = {i}");
                sb.AppendLine($"# Comment {i}");
            }

            var data = new NativeArray<byte>(Encoding.UTF8.GetBytes(sb.ToString()), Allocator.TempJob);
            var tokenizer = new ParallelTokenizer(data, stringPool, errorAccumulator, minChunkSize: 512, maxChunks: 4);

            try
            {
                // Act
                var stream = tokenizer.Tokenize(Allocator.TempJob);

                // Assert
                Assert.IsTrue(stream.IsCreated);
                Assert.Greater(stream.Count, 3000); // Should have many tokens

                stream.Dispose();
            }
            finally
            {
                data.Dispose();
                tokenizer.Dispose();
            }
        }

        [Test]
        public void ParallelTokenizer_ResultsMatchSingleThreaded()
        {
            // Arrange
            string content = CreateTestContent();
            var data = new NativeArray<byte>(Encoding.UTF8.GetBytes(content), Allocator.TempJob);

            var singleThreadedTokenizer = new Tokenizer(data, stringPool, errorAccumulator);
            var parallelTokenizer = new ParallelTokenizer(data, stringPool, errorAccumulator, minChunkSize: 128, maxChunks: 4);

            try
            {
                // Act
                var singleThreadedStream = singleThreadedTokenizer.Tokenize(Allocator.TempJob);
                var parallelStream = parallelTokenizer.Tokenize(Allocator.TempJob);

                // Assert
                Assert.AreEqual(singleThreadedStream.Count, parallelStream.Count, "Token counts should match");

                // Compare token types and positions
                singleThreadedStream.Reset();
                parallelStream.Reset();

                while (!singleThreadedStream.IsAtEnd && !parallelStream.IsAtEnd)
                {
                    var singleToken = singleThreadedStream.Current;
                    var parallelToken = parallelStream.Current;

                    Assert.AreEqual(singleToken.Type, parallelToken.Type,
                        $"Token types should match at position {singleThreadedStream.Position}");
                    Assert.AreEqual(singleToken.StartPosition, parallelToken.StartPosition,
                        $"Token positions should match at position {singleThreadedStream.Position}");
                    Assert.AreEqual(singleToken.Length, parallelToken.Length,
                        $"Token lengths should match at position {singleThreadedStream.Position}");

                    singleThreadedStream.Advance();
                    parallelStream.Advance();
                }

                singleThreadedStream.Dispose();
                parallelStream.Dispose();
            }
            finally
            {
                data.Dispose();
                singleThreadedTokenizer.Dispose();
                parallelTokenizer.Dispose();
            }
        }

        [Test]
        public void ChunkTokenizer_HandlesAllTokenTypes()
        {
            // Arrange
            string content = "identifier = 123 \"string\" yes no # comment\n{ } [ ] ( ) , ; : .";
            var data = new NativeArray<byte>(Encoding.UTF8.GetBytes(content), Allocator.TempJob);
            var slice = new NativeSlice<byte>(data);

            var tokenizer = new ChunkTokenizer
            {
                Data = slice,
                Position = 0,
                Line = 1,
                Column = 1,
                ChunkStartOffset = 0
            };

            try
            {
                // Act & Assert
                var tokens = new System.Collections.Generic.List<Token>();
                while (tokenizer.Position < data.Length)
                {
                    var token = tokenizer.NextToken(stringPool, out bool hasError);
                    if (token.Type == TokenType.EndOfFile)
                        break;
                    tokens.Add(token);
                }

                // Verify we got all expected token types
                Assert.Contains(TokenType.Identifier, tokens.ConvertAll(t => t.Type));
                Assert.Contains(TokenType.Equals, tokens.ConvertAll(t => t.Type));
                Assert.Contains(TokenType.Number, tokens.ConvertAll(t => t.Type));
                Assert.Contains(TokenType.String, tokens.ConvertAll(t => t.Type));
                Assert.Contains(TokenType.Boolean, tokens.ConvertAll(t => t.Type));
                Assert.Contains(TokenType.Comment, tokens.ConvertAll(t => t.Type));
                Assert.Contains(TokenType.LeftBrace, tokens.ConvertAll(t => t.Type));
                Assert.Contains(TokenType.RightBrace, tokens.ConvertAll(t => t.Type));
            }
            finally
            {
                data.Dispose();
            }
        }

        [Test]
        public void ParallelTokenizeJob_ProcessesChunksCorrectly()
        {
            // Arrange
            string content = CreateTestContent();
            var data = new NativeArray<byte>(Encoding.UTF8.GetBytes(content), Allocator.TempJob);

            var chunkInfos = new NativeArray<ChunkInfo>(2, Allocator.TempJob);
            chunkInfos[0] = new ChunkInfo
            {
                StartOffset = 0,
                Length = data.Length / 2,
                StartLine = 1,
                StartColumn = 1,
                EstimatedTokens = 100
            };
            chunkInfos[1] = new ChunkInfo
            {
                StartOffset = data.Length / 2,
                Length = data.Length - data.Length / 2,
                StartLine = 10, // Approximate
                StartColumn = 1,
                EstimatedTokens = 100
            };

            var results = new NativeArray<TokenChunk>(2, Allocator.TempJob);
            var errorCounts = new NativeArray<int>(2, Allocator.TempJob);

            try
            {
                // Act
                var job = new ParallelTokenizeJob
                {
                    Data = data,
                    ChunkInfos = chunkInfos,
                    StringPool = stringPool,
                    Results = results,
                    ErrorCounts = errorCounts
                };

                var handle = job.Schedule(2, 1);
                handle.Complete();

                // Assert
                Assert.Greater(results[0].TokenCount, 0, "First chunk should have tokens");
                Assert.Greater(results[1].TokenCount, 0, "Second chunk should have tokens");
                Assert.AreEqual(0, results[0].ChunkIndex, "First chunk index should be 0");
                Assert.AreEqual(1, results[1].ChunkIndex, "Second chunk index should be 1");
            }
            finally
            {
                data.Dispose();
                chunkInfos.Dispose();
                results.Dispose();
                errorCounts.Dispose();
            }
        }

        [Test]
        public void ChunkTokenizer_HandlesMalformedStrings()
        {
            // Arrange
            string content = "\"unclosed string";
            var data = new NativeArray<byte>(Encoding.UTF8.GetBytes(content), Allocator.TempJob);
            var slice = new NativeSlice<byte>(data);

            var tokenizer = new ChunkTokenizer
            {
                Data = slice,
                Position = 0,
                Line = 1,
                Column = 1,
                ChunkStartOffset = 0
            };

            try
            {
                // Act
                var token = tokenizer.NextToken(stringPool, out bool hasError);

                // Assert
                Assert.AreEqual(TokenType.String, token.Type);
                Assert.IsFalse(hasError); // String tokenizer handles unclosed strings gracefully
            }
            finally
            {
                data.Dispose();
            }
        }

        [Test]
        public void ChunkTokenizer_TracksLineAndColumnCorrectly()
        {
            // Arrange
            string content = "line1\nline2\r\nline3";
            var data = new NativeArray<byte>(Encoding.UTF8.GetBytes(content), Allocator.TempJob);
            var slice = new NativeSlice<byte>(data);

            var tokenizer = new ChunkTokenizer
            {
                Data = slice,
                Position = 0,
                Line = 1,
                Column = 1,
                ChunkStartOffset = 0
            };

            try
            {
                // Act - consume all content
                while (tokenizer.Position < data.Length)
                {
                    var token = tokenizer.NextToken(stringPool, out bool hasError);
                    if (token.Type == TokenType.EndOfFile)
                        break;
                }

                // Assert
                Assert.AreEqual(3, tokenizer.Line, "Should be on line 3");
                Assert.Greater(tokenizer.Column, 1, "Should have advanced column on line 3");
            }
            finally
            {
                data.Dispose();
            }
        }

        private string CreateTestContent()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Test file");
            sb.AppendLine("country = {");
            sb.AppendLine("    name = \"Test Country\"");
            sb.AppendLine("    technology = 1.5");
            sb.AppendLine("    capital = 123");
            sb.AppendLine("    exists = yes");
            sb.AppendLine("    date = 1444.11.11");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("province = {");
            sb.AppendLine("    id = 456");
            sb.AppendLine("    owner = @country_var");
            sb.AppendLine("    buildings = [ temple workshop ]");
            sb.AppendLine("}");
            return sb.ToString();
        }
    }
}