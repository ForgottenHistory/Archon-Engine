using System;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;

namespace Core.Loaders
{
    /// <summary>
    /// High-performance CSV tokenizer for Paradox format (semicolon-delimited).
    /// Supports UTF-8 encoding and quoted fields.
    /// </summary>
    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public static class CSVTokenizer
    {
        /// <summary>
        /// CSV token types
        /// </summary>
        public enum CSVTokenType : byte
        {
            Field = 0,
            QuotedField = 1,
            EndOfRow = 2,
            EndOfFile = 3
        }

        /// <summary>
        /// A single CSV token
        /// </summary>
        public struct CSVToken
        {
            public CSVTokenType Type;
            public NativeSlice<byte> Data;
            public int Row;
            public int Column;

            public static CSVToken Create(CSVTokenType type, NativeSlice<byte> data, int row, int column)
            {
                return new CSVToken
                {
                    Type = type,
                    Data = data,
                    Row = row,
                    Column = column
                };
            }
        }

        /// <summary>
        /// CSV tokenization result
        /// </summary>
        public struct CSVTokenizeResult
        {
            public bool IsSuccess;
            public int TokensGenerated;
            public int BytesProcessed;
            public int ErrorRow;
            public int ErrorColumn;
            public NativeSlice<byte> ErrorContext;

            public static CSVTokenizeResult Success(int tokensGenerated, int bytesProcessed)
            {
                return new CSVTokenizeResult
                {
                    IsSuccess = true,
                    TokensGenerated = tokensGenerated,
                    BytesProcessed = bytesProcessed,
                    ErrorRow = 0,
                    ErrorColumn = 0
                };
            }

            public static CSVTokenizeResult Failure(int errorRow, int errorColumn, NativeSlice<byte> errorContext)
            {
                return new CSVTokenizeResult
                {
                    IsSuccess = false,
                    TokensGenerated = 0,
                    BytesProcessed = 0,
                    ErrorRow = errorRow,
                    ErrorColumn = errorColumn,
                    ErrorContext = errorContext
                };
            }
        }

        /// <summary>
        /// Tokenize CSV data with semicolon delimiters
        /// </summary>
        public static CSVTokenizeResult Tokenize(
            NativeSlice<byte> csvData,
            NativeList<CSVToken> tokens)
        {
            if (csvData.Length == 0)
                return CSVTokenizeResult.Success(0, 0);

            int position = 0;
            int row = 1;
            int column = 1;
            int tokenCount = 0;

            while (position < csvData.Length)
            {
                // Skip any leading whitespace (except within quotes)
                while (position < csvData.Length && IsWhitespace(csvData[position]) && csvData[position] != (byte)'\n' && csvData[position] != (byte)'\r')
                {
                    position++;
                    column++;
                }

                if (position >= csvData.Length)
                    break;

                // Check for end of line
                if (csvData[position] == (byte)'\n' || csvData[position] == (byte)'\r')
                {
                    var endOfRowToken = CSVToken.Create(CSVTokenType.EndOfRow, new NativeSlice<byte>(), row, column);
                    tokens.Add(endOfRowToken);
                    tokenCount++;

                    // Handle \r\n properly
                    if (csvData[position] == (byte)'\r' && position + 1 < csvData.Length && csvData[position + 1] == (byte)'\n')
                    {
                        position += 2;
                    }
                    else
                    {
                        position++;
                    }

                    row++;
                    column = 1;
                    continue;
                }

                // Parse field
                var fieldResult = ParseField(csvData, position, row, column);
                if (!fieldResult.IsSuccess)
                {
                    var errorContext = csvData.Slice(Math.Max(0, position - 10), Math.Min(20, csvData.Length - Math.Max(0, position - 10)));
                    return CSVTokenizeResult.Failure(row, column, errorContext);
                }

                tokens.Add(fieldResult.Token);
                tokenCount++;
                position = fieldResult.NextPosition;
                column = fieldResult.NextColumn;

                // Skip the semicolon delimiter if present
                if (position < csvData.Length && csvData[position] == (byte)';')
                {
                    position++;
                    column++;
                }
            }

            // Add final EOF token
            var eofToken = CSVToken.Create(CSVTokenType.EndOfFile, new NativeSlice<byte>(), row, column);
            tokens.Add(eofToken);
            tokenCount++;

            return CSVTokenizeResult.Success(tokenCount, position);
        }

        /// <summary>
        /// Field parsing result
        /// </summary>
        private struct FieldParseResult
        {
            public bool IsSuccess;
            public CSVToken Token;
            public int NextPosition;
            public int NextColumn;

            public static FieldParseResult Success(CSVToken token, int nextPosition, int nextColumn)
            {
                return new FieldParseResult
                {
                    IsSuccess = true,
                    Token = token,
                    NextPosition = nextPosition,
                    NextColumn = nextColumn
                };
            }

            public static FieldParseResult Failure()
            {
                return new FieldParseResult { IsSuccess = false };
            }
        }

        /// <summary>
        /// Parse a single CSV field (quoted or unquoted)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static FieldParseResult ParseField(NativeSlice<byte> csvData, int startPosition, int row, int startColumn)
        {
            int position = startPosition;
            int column = startColumn;

            // Check if this is a quoted field
            if (position < csvData.Length && csvData[position] == (byte)'"')
            {
                return ParseQuotedField(csvData, position, row, column);
            }
            else
            {
                return ParseUnquotedField(csvData, position, row, column);
            }
        }

        /// <summary>
        /// Parse a quoted CSV field
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static FieldParseResult ParseQuotedField(NativeSlice<byte> csvData, int startPosition, int row, int startColumn)
        {
            int position = startPosition + 1; // Skip opening quote
            int column = startColumn + 1;
            int fieldStart = position;

            while (position < csvData.Length)
            {
                if (csvData[position] == (byte)'"')
                {
                    // Check for escaped quote (double quote)
                    if (position + 1 < csvData.Length && csvData[position + 1] == (byte)'"')
                    {
                        position += 2; // Skip both quotes
                        column += 2;
                        continue;
                    }
                    else
                    {
                        // End of quoted field
                        var fieldData = csvData.Slice(fieldStart, position - fieldStart);
                        var token = CSVToken.Create(CSVTokenType.QuotedField, fieldData, row, startColumn);
                        return FieldParseResult.Success(token, position + 1, column + 1);
                    }
                }
                else if (csvData[position] == (byte)'\n')
                {
                    // Newline within quoted field is allowed
                    position++;
                    column = 1; // Reset column for new line
                }
                else if (csvData[position] == (byte)'\r')
                {
                    position++;
                    if (position < csvData.Length && csvData[position] == (byte)'\n')
                    {
                        position++;
                    }
                    column = 1;
                }
                else
                {
                    position++;
                    column++;
                }
            }

            // Unclosed quoted field
            return FieldParseResult.Failure();
        }

        /// <summary>
        /// Parse an unquoted CSV field
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static FieldParseResult ParseUnquotedField(NativeSlice<byte> csvData, int startPosition, int row, int startColumn)
        {
            int position = startPosition;
            int column = startColumn;
            int fieldStart = position;

            // Find end of field (semicolon, newline, or EOF)
            while (position < csvData.Length)
            {
                byte b = csvData[position];
                if (b == (byte)';' || b == (byte)'\n' || b == (byte)'\r')
                {
                    break;
                }
                position++;
                column++;
            }

            // Trim trailing whitespace
            int fieldEnd = position;
            while (fieldEnd > fieldStart && IsWhitespace(csvData[fieldEnd - 1]))
            {
                fieldEnd--;
            }

            var fieldData = csvData.Slice(fieldStart, fieldEnd - fieldStart);
            var token = CSVToken.Create(CSVTokenType.Field, fieldData, row, startColumn);
            return FieldParseResult.Success(token, position, column);
        }

        /// <summary>
        /// Check if byte is whitespace (space or tab)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsWhitespace(byte b)
        {
            return b == (byte)' ' || b == (byte)'\t';
        }
    }
}
