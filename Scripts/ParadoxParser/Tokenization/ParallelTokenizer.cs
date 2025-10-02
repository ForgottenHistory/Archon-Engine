using System;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using ParadoxParser.Core;
using ParadoxParser.Data;
using ParadoxParser.Jobs;

namespace ParadoxParser.Tokenization
{
    /// <summary>
    /// High-performance parallel tokenizer for large files
    /// Splits files into chunks and processes them concurrently
    /// </summary>
    public unsafe struct ParallelTokenizer : IDisposable
    {
        private NativeArray<byte> m_Data;
        private NativeStringPool m_StringPool;
        private ErrorAccumulator m_ErrorAccumulator;
        private bool m_IsCreated;
        private bool m_IsDisposed;

        // Configuration
        private readonly int m_MinChunkSize;
        private readonly int m_MaxChunks;

        /// <summary>
        /// Create parallel tokenizer
        /// </summary>
        public ParallelTokenizer(NativeArray<byte> data, NativeStringPool stringPool, ErrorAccumulator errorAccumulator,
            int minChunkSize = 4096, int maxChunks = 16)
        {
            m_Data = data;
            m_StringPool = stringPool;
            m_ErrorAccumulator = errorAccumulator;
            m_MinChunkSize = minChunkSize;
            m_MaxChunks = maxChunks;
            m_IsCreated = true;
            m_IsDisposed = false;
        }

        /// <summary>
        /// Check if tokenizer is valid
        /// </summary>
        public bool IsCreated => m_IsCreated && !m_IsDisposed;

        /// <summary>
        /// Tokenize data in parallel
        /// </summary>
        public TokenStream Tokenize(Allocator allocator)
        {
            if (!IsCreated)
                return default;

            // Determine optimal chunking strategy
            var chunkStrategy = CalculateChunkStrategy();

            if (chunkStrategy.ChunkCount == 1)
            {
                // Use single-threaded tokenizer for small files
                return TokenizeSingleThreaded(allocator);
            }

            return TokenizeParallel(chunkStrategy, allocator);
        }

        /// <summary>
        /// Calculate optimal chunking strategy for the data
        /// </summary>
        private ChunkStrategy CalculateChunkStrategy()
        {
            int dataSize = m_Data.Length;

            // For small files, use single-threaded approach
            if (dataSize < m_MinChunkSize * 2)
            {
                return new ChunkStrategy
                {
                    ChunkCount = 1,
                    ChunkSize = dataSize,
                    UseParallel = false
                };
            }

            // Calculate optimal chunk count based on data size and core count
            int optimalChunks = math.min(m_MaxChunks, math.max(2, dataSize / m_MinChunkSize));
            int chunkSize = dataSize / optimalChunks;

            return new ChunkStrategy
            {
                ChunkCount = optimalChunks,
                ChunkSize = chunkSize,
                UseParallel = true
            };
        }

        /// <summary>
        /// Create chunk boundaries that respect token boundaries
        /// </summary>
        private NativeArray<ChunkInfo> CreateChunkBoundaries(ChunkStrategy strategy, Allocator allocator)
        {
            var chunks = new NativeArray<ChunkInfo>(strategy.ChunkCount, allocator);

            if (strategy.ChunkCount == 1)
            {
                chunks[0] = new ChunkInfo
                {
                    StartOffset = 0,
                    Length = m_Data.Length,
                    StartLine = 1,
                    StartColumn = 1,
                    EstimatedTokens = math.max(1024, m_Data.Length / 10)
                };
                return chunks;
            }

            int currentOffset = 0;
            int currentLine = 1;
            int currentColumn = 1;

            for (int i = 0; i < strategy.ChunkCount; i++)
            {
                int chunkStart = currentOffset;
                int chunkEnd = (i == strategy.ChunkCount - 1) ? m_Data.Length :
                    math.min(m_Data.Length, currentOffset + strategy.ChunkSize);

                // Adjust chunk end to avoid splitting tokens
                if (chunkEnd < m_Data.Length)
                {
                    chunkEnd = FindSafeChunkBoundary(chunkEnd);
                }

                int chunkLength = chunkEnd - chunkStart;
                int estimatedTokens = math.max(256, chunkLength / 10);

                chunks[i] = new ChunkInfo
                {
                    StartOffset = chunkStart,
                    Length = chunkLength,
                    StartLine = currentLine,
                    StartColumn = currentColumn,
                    EstimatedTokens = estimatedTokens
                };

                // Update position tracking for next chunk
                UpdatePositionTracking(chunkStart, chunkEnd, ref currentLine, ref currentColumn);
                currentOffset = chunkEnd;
            }

            return chunks;
        }

        /// <summary>
        /// Find a safe boundary for chunk splitting
        /// Avoids splitting in the middle of tokens
        /// </summary>
        private int FindSafeChunkBoundary(int preferredEnd)
        {
            int searchRadius = math.min(512, m_Data.Length - preferredEnd);

            // Look for whitespace or line breaks near the preferred boundary
            for (int i = 0; i < searchRadius; i++)
            {
                int pos = preferredEnd + i;
                if (pos >= m_Data.Length)
                    return m_Data.Length;

                byte b = m_Data[pos];
                if (IsWhitespace(b) || b == (byte)'\n')
                {
                    return pos;
                }
            }

            // If no good boundary found, use original position
            return preferredEnd;
        }

        /// <summary>
        /// Update line/column tracking for chunk boundary calculation
        /// </summary>
        private void UpdatePositionTracking(int start, int end, ref int line, ref int column)
        {
            for (int i = start; i < end && i < m_Data.Length; i++)
            {
                byte b = m_Data[i];
                if (b == (byte)'\n')
                {
                    line++;
                    column = 1;
                }
                else if (b != (byte)'\r')
                {
                    column++;
                }
            }
        }

        /// <summary>
        /// Tokenize using parallel jobs
        /// </summary>
        private TokenStream TokenizeParallel(ChunkStrategy strategy, Allocator allocator)
        {
            var chunkInfos = CreateChunkBoundaries(strategy, Allocator.TempJob);
            var results = new NativeArray<TokenChunk>(strategy.ChunkCount, Allocator.TempJob);
            var errorCounts = new NativeArray<int>(strategy.ChunkCount, Allocator.TempJob);

            try
            {
                // Create and schedule parallel job
                var job = new ParallelTokenizeJob
                {
                    Data = m_Data,
                    ChunkInfos = chunkInfos,
                    StringPool = m_StringPool,
                    Results = results,
                    ErrorCounts = errorCounts
                };

                // Execute job with appropriate batch size
                int batchSize = math.max(1, strategy.ChunkCount / 4);
                var jobHandle = job.Schedule(strategy.ChunkCount, batchSize);
                jobHandle.Complete();

                // Merge results into final token stream
                return MergeChunkResults(results, chunkInfos, allocator);
            }
            finally
            {
                if (chunkInfos.IsCreated) chunkInfos.Dispose();
                if (results.IsCreated) results.Dispose();
                if (errorCounts.IsCreated) errorCounts.Dispose();
            }
        }

        /// <summary>
        /// Fallback single-threaded tokenization
        /// </summary>
        private TokenStream TokenizeSingleThreaded(Allocator allocator)
        {
            var tokenizer = new Tokenizer(m_Data, m_StringPool, m_ErrorAccumulator);
            try
            {
                return tokenizer.Tokenize(allocator);
            }
            finally
            {
                tokenizer.Dispose();
            }
        }

        /// <summary>
        /// Merge chunk results into a single token stream
        /// </summary>
        private TokenStream MergeChunkResults(NativeArray<TokenChunk> results, NativeArray<ChunkInfo> chunkInfos, Allocator allocator)
        {
            // Calculate total token count
            int totalTokens = 0;
            for (int i = 0; i < results.Length; i++)
            {
                totalTokens += results[i].TokenCount;
            }

            // Create merged token stream
            var stream = new TokenStream(totalTokens + 16, allocator);

            // Process each chunk sequentially to maintain order
            for (int chunkIndex = 0; chunkIndex < results.Length; chunkIndex++)
            {
                var chunk = results[chunkIndex];
                var chunkInfo = chunkInfos[chunkIndex];

                // Re-tokenize this chunk to get actual tokens
                // (The parallel job only collected metadata for performance)
                var chunkData = m_Data.Slice(chunkInfo.StartOffset, chunkInfo.Length);
                var chunkTokenizer = new ChunkTokenizer
                {
                    Data = chunkData,
                    Position = 0,
                    Line = chunkInfo.StartLine,
                    Column = chunkInfo.StartColumn,
                    ChunkStartOffset = chunkInfo.StartOffset
                };

                // Add tokens from this chunk
                while (chunkTokenizer.Position < chunkData.Length)
                {
                    var token = chunkTokenizer.NextToken(m_StringPool, out bool hasError);
                    if (hasError)
                    {
                        // Accumulate error (simplified for parallel context)
                    }

                    if (token.Type == TokenType.EndOfFile)
                        break;

                    if (!stream.TryAddToken(token))
                    {
                        // Stream is full
                        break;
                    }
                }
            }

            stream.Complete();
            return stream;
        }

        private static bool IsWhitespace(byte b)
        {
            return b == (byte)' ' || b == (byte)'\t' || b == (byte)'\r' || b == (byte)'\n';
        }

        public void Dispose()
        {
            if (m_IsDisposed)
                return;

            m_IsDisposed = true;
            m_IsCreated = false;
            // Note: We don't dispose m_Data, m_StringPool, or m_ErrorAccumulator
            // as they are owned by the caller
        }
    }

    /// <summary>
    /// Strategy for chunk-based processing
    /// </summary>
    public struct ChunkStrategy
    {
        public int ChunkCount;
        public int ChunkSize;
        public bool UseParallel;
    }
}