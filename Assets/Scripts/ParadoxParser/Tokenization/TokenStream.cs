using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace ParadoxParser.Tokenization
{
    /// <summary>
    /// A stream of tokens that supports efficient iteration and lookahead
    /// Optimized for Unity's Job System and Native Collections
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct TokenStream : IDisposable
    {
        private NativeArray<Token> m_Tokens;
        private int m_Position;
        private int m_Count;
        private bool m_IsCreated;
        private bool m_IsDisposed;

        /// <summary>
        /// Current position in the token stream
        /// </summary>
        public int Position => m_Position;

        /// <summary>
        /// Total number of tokens in the stream
        /// </summary>
        public int Count => m_Count;

        /// <summary>
        /// Check if stream is at end
        /// </summary>
        public bool IsAtEnd => m_Position >= m_Count;

        /// <summary>
        /// Check if stream is created and valid
        /// </summary>
        public bool IsCreated => m_IsCreated && !m_IsDisposed;

        /// <summary>
        /// Remaining tokens from current position
        /// </summary>
        public int Remaining => Math.Max(0, m_Count - m_Position);

        /// <summary>
        /// Current token at position
        /// </summary>
        public Token Current
        {
            get
            {
                if (!IsCreated || IsAtEnd)
                    return Token.CreateEndOfFile(0, 0, 0);
                return m_Tokens[m_Position];
            }
        }

        /// <summary>
        /// Create a new token stream
        /// </summary>
        public TokenStream(int capacity, Allocator allocator)
        {
            m_Tokens = new NativeArray<Token>(capacity, allocator);
            m_Position = 0;
            m_Count = 0;
            m_IsCreated = true;
            m_IsDisposed = false;
        }

        /// <summary>
        /// Create token stream from existing token array
        /// </summary>
        public TokenStream(NativeArray<Token> tokens)
        {
            m_Tokens = tokens;
            m_Position = 0;
            m_Count = tokens.Length;
            m_IsCreated = true;
            m_IsDisposed = false;
        }

        /// <summary>
        /// Add a token to the stream
        /// </summary>
        public bool TryAddToken(Token token)
        {
            if (!IsCreated || m_Count >= m_Tokens.Length)
                return false;

            m_Tokens[m_Count] = token;
            m_Count++;
            return true;
        }

        /// <summary>
        /// Peek at token at relative offset from current position
        /// </summary>
        public Token Peek(int offset = 0)
        {
            int targetPosition = m_Position + offset;
            if (!IsCreated || targetPosition < 0 || targetPosition >= m_Count)
                return Token.CreateEndOfFile(0, 0, 0);

            return m_Tokens[targetPosition];
        }

        /// <summary>
        /// Advance to next token and return it
        /// </summary>
        public Token Next()
        {
            if (!IsCreated || IsAtEnd)
                return Token.CreateEndOfFile(0, 0, 0);

            var token = m_Tokens[m_Position];
            m_Position++;
            return token;
        }

        /// <summary>
        /// Advance position without returning token
        /// </summary>
        public bool Advance(int count = 1)
        {
            if (!IsCreated)
                return false;

            int newPosition = m_Position + count;
            if (newPosition < 0 || newPosition > m_Count)
                return false;

            m_Position = newPosition;
            return true;
        }

        /// <summary>
        /// Reset position to beginning
        /// </summary>
        public void Reset()
        {
            if (IsCreated)
                m_Position = 0;
        }

        /// <summary>
        /// Set position to specific index
        /// </summary>
        public bool Seek(int position)
        {
            if (!IsCreated || position < 0 || position > m_Count)
                return false;

            m_Position = position;
            return true;
        }

        /// <summary>
        /// Skip tokens of specified types
        /// </summary>
        public void SkipTokenTypes(params TokenType[] typesToSkip)
        {
            if (!IsCreated)
                return;

            while (!IsAtEnd)
            {
                var current = Current;
                bool shouldSkip = false;

                for (int i = 0; i < typesToSkip.Length; i++)
                {
                    if (current.Type == typesToSkip[i])
                    {
                        shouldSkip = true;
                        break;
                    }
                }

                if (!shouldSkip)
                    break;

                Advance();
            }
        }

        /// <summary>
        /// Skip whitespace and comments
        /// </summary>
        public void SkipWhitespaceAndComments()
        {
            SkipTokenTypes(TokenType.Whitespace, TokenType.Newline, TokenType.Comment);
        }

        /// <summary>
        /// Check if current token matches expected type
        /// </summary>
        public bool Match(TokenType expectedType)
        {
            return IsCreated && !IsAtEnd && Current.Type == expectedType;
        }

        /// <summary>
        /// Check if current token matches any of the expected types
        /// </summary>
        public bool MatchAny(params TokenType[] expectedTypes)
        {
            if (!IsCreated || IsAtEnd)
                return false;

            var currentType = Current.Type;
            for (int i = 0; i < expectedTypes.Length; i++)
            {
                if (currentType == expectedTypes[i])
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Consume token if it matches expected type
        /// </summary>
        public bool Consume(TokenType expectedType, out Token token)
        {
            token = default;
            if (!Match(expectedType))
                return false;

            token = Next();
            return true;
        }

        /// <summary>
        /// Get slice of tokens from current position
        /// </summary>
        public NativeSlice<Token> GetSlice(int length)
        {
            if (!IsCreated || IsAtEnd)
                return default;

            int actualLength = Math.Min(length, Remaining);
            return new NativeSlice<Token>(m_Tokens, m_Position, actualLength);
        }

        /// <summary>
        /// Get all remaining tokens as a slice
        /// </summary>
        public NativeSlice<Token> GetRemainingSlice()
        {
            return GetSlice(Remaining);
        }

        /// <summary>
        /// Copy tokens to target array
        /// </summary>
        public void CopyTo(NativeArray<Token> target, int sourceOffset = 0, int targetOffset = 0, int count = -1)
        {
            if (!IsCreated || !target.IsCreated)
                return;

            if (count < 0)
                count = Math.Min(m_Count - sourceOffset, target.Length - targetOffset);

            count = Math.Min(count, m_Count - sourceOffset);
            count = Math.Min(count, target.Length - targetOffset);

            if (count > 0)
            {
                NativeArray<Token>.Copy(m_Tokens, sourceOffset, target, targetOffset, count);
            }
        }

        /// <summary>
        /// Complete the stream after all tokens are added
        /// </summary>
        public void Complete()
        {
            if (!IsCreated)
                return;

            // Ensure there's always an EOF token at the end
            if (m_Count == 0 || (m_Count > 0 && m_Tokens[m_Count - 1].Type != TokenType.EndOfFile))
            {
                if (m_Count < m_Tokens.Length)
                {
                    var lastToken = m_Count > 0 ? m_Tokens[m_Count - 1] : Token.Create(TokenType.EndOfFile, 0, 0, 1, 1);
                    var eofToken = Token.CreateEndOfFile(
                        lastToken.EndPosition,
                        lastToken.Line,
                        lastToken.Column + lastToken.Length
                    );
                    m_Tokens[m_Count] = eofToken;
                    m_Count++;
                }
            }
        }

        public void Dispose()
        {
            if (m_IsDisposed)
                return;

            m_IsDisposed = true;
            m_IsCreated = false;

            try
            {
                if (m_Tokens.IsCreated)
                {
                    m_Tokens.Dispose();
                }
            }
            catch (ObjectDisposedException) { /* Already disposed */ }
        }

        /// <summary>
        /// Job-safe disposal
        /// </summary>
        public JobHandle Dispose(JobHandle inputDeps)
        {
            if (m_IsDisposed)
                return inputDeps;

            m_IsDisposed = true;
            m_IsCreated = false;

            if (m_Tokens.IsCreated)
            {
                return m_Tokens.Dispose(inputDeps);
            }

            return inputDeps;
        }
    }
}