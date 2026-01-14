using System;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;

namespace ParadoxParser.CSV
{
    /// <summary>
    /// High-performance CSV parser for Paradox files
    /// Handles semicolon-delimited format with header detection
    /// </summary>
    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public static class CSVParser
    {
        /// <summary>
        /// CSV parsing result
        /// </summary>
        public struct CSVParseResult
        {
            public bool IsSuccess;
            public NativeArray<uint> HeaderHashes; // Hashed column names
            public NativeArray<CSVRow> Rows;
            public int RowCount;
            public int ColumnCount;
            public NativeSlice<byte> ErrorContext;

            public void Dispose()
            {
                if (HeaderHashes.IsCreated) HeaderHashes.Dispose();
                if (Rows.IsCreated)
                {
                    // Dispose each row's Fields array before disposing the Rows array
                    for (int i = 0; i < Rows.Length; i++)
                    {
                        Rows[i].Dispose();
                    }
                    Rows.Dispose();
                }
            }

            public static CSVParseResult Failure(NativeSlice<byte> errorContext)
            {
                return new CSVParseResult
                {
                    IsSuccess = false,
                    ErrorContext = errorContext
                };
            }
        }

        /// <summary>
        /// A single CSV row
        /// </summary>
        public struct CSVRow
        {
            public NativeArray<NativeSlice<byte>> Fields;
            public int FieldCount;

            public void Dispose()
            {
                if (Fields.IsCreated) Fields.Dispose();
            }

            public static CSVRow Create(int columnCount, Allocator allocator)
            {
                return new CSVRow
                {
                    Fields = new NativeArray<NativeSlice<byte>>(columnCount, allocator),
                    FieldCount = 0
                };
            }
        }

        /// <summary>
        /// Parse CSV data with automatic header detection
        /// Assumes UTF-8 encoding (preprocess with Python if needed)
        /// </summary>
        public static CSVParseResult Parse(
            NativeSlice<byte> csvData,
            Allocator allocator,
            bool hasHeader = true)
        {
            // Tokenize the CSV (assumes UTF-8)
            var tokens = new NativeList<CSVTokenizer.CSVToken>(1000, Allocator.Temp);
            var tokenizeResult = CSVTokenizer.Tokenize(csvData, tokens);

            if (!tokenizeResult.IsSuccess)
            {
                return CSVParseResult.Failure(tokenizeResult.ErrorContext);
            }

            // Parse tokens into structured data
            return ParseTokens(tokens.AsArray().Slice(), allocator, hasHeader);
        }

        /// <summary>
        /// Parse tokens into structured CSV data
        /// </summary>
        private static CSVParseResult ParseTokens(
            NativeSlice<CSVTokenizer.CSVToken> tokens,
            Allocator allocator,
            bool hasHeader)
        {
            if (tokens.Length == 0)
                return CSVParseResult.Failure(new NativeSlice<byte>());

            // First pass: count rows and columns
            int rowCount = 0;
            int maxColumns = 0;
            int currentColumns = 0;

            for (int i = 0; i < tokens.Length; i++)
            {
                var token = tokens[i];
                switch (token.Type)
                {
                    case CSVTokenizer.CSVTokenType.Field:
                    case CSVTokenizer.CSVTokenType.QuotedField:
                        currentColumns++;
                        break;

                    case CSVTokenizer.CSVTokenType.EndOfRow:
                        if (currentColumns > 0)
                        {
                            maxColumns = Math.Max(maxColumns, currentColumns);
                            rowCount++;
                            currentColumns = 0;
                        }
                        break;

                    case CSVTokenizer.CSVTokenType.EndOfFile:
                        if (currentColumns > 0)
                        {
                            maxColumns = Math.Max(maxColumns, currentColumns);
                            rowCount++;
                        }
                        break;
                }
            }

            if (rowCount == 0 || maxColumns == 0)
                return CSVParseResult.Failure(new NativeSlice<byte>());

            // Allocate result structures
            int dataRowCount = hasHeader ? rowCount - 1 : rowCount;
            var headerHashes = new NativeArray<uint>(maxColumns, allocator);
            var rows = new NativeArray<CSVRow>(dataRowCount, allocator);

            // Initialize rows
            for (int i = 0; i < dataRowCount; i++)
            {
                rows[i] = CSVRow.Create(maxColumns, allocator);
            }

            // Second pass: extract data
            int tokenIndex = 0;
            int currentRow = 0;
            int currentColumn = 0;

            // Parse header if present
            if (hasHeader)
            {
                for (int col = 0; col < maxColumns && tokenIndex < tokens.Length; col++)
                {
                    var token = tokens[tokenIndex];
                    if (token.Type == CSVTokenizer.CSVTokenType.Field ||
                        token.Type == CSVTokenizer.CSVTokenType.QuotedField)
                    {
                        headerHashes[col] = ComputeHash(token.Data);
                        tokenIndex++;
                    }
                    else if (token.Type == CSVTokenizer.CSVTokenType.EndOfRow)
                    {
                        tokenIndex++;
                        break;
                    }
                    else
                    {
                        tokenIndex++;
                    }
                }

                // Skip to next row
                while (tokenIndex < tokens.Length && tokens[tokenIndex].Type != CSVTokenizer.CSVTokenType.EndOfRow)
                    tokenIndex++;
                if (tokenIndex < tokens.Length && tokens[tokenIndex].Type == CSVTokenizer.CSVTokenType.EndOfRow)
                    tokenIndex++;
            }

            // Parse data rows
            currentRow = 0;
            currentColumn = 0;

            while (tokenIndex < tokens.Length && currentRow < dataRowCount)
            {
                var token = tokens[tokenIndex];

                switch (token.Type)
                {
                    case CSVTokenizer.CSVTokenType.Field:
                    case CSVTokenizer.CSVTokenType.QuotedField:
                        if (currentColumn < maxColumns)
                        {
                            var row = rows[currentRow];
                            row.Fields[currentColumn] = token.Data;
                            row.FieldCount = Math.Max(row.FieldCount, currentColumn + 1);
                            rows[currentRow] = row;
                            currentColumn++;
                        }
                        break;

                    case CSVTokenizer.CSVTokenType.EndOfRow:
                        currentRow++;
                        currentColumn = 0;
                        break;

                    case CSVTokenizer.CSVTokenType.EndOfFile:
                        if (currentColumn > 0)
                            currentRow++;
                        break;
                }

                tokenIndex++;
            }

            return new CSVParseResult
            {
                IsSuccess = true,
                HeaderHashes = headerHashes,
                Rows = rows,
                RowCount = Math.Min(currentRow, dataRowCount),
                ColumnCount = maxColumns
            };
        }

        /// <summary>
        /// Find column index by name hash
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int FindColumnIndex(NativeArray<uint> headerHashes, uint columnNameHash)
        {
            for (int i = 0; i < headerHashes.Length; i++)
            {
                if (headerHashes[i] == columnNameHash)
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// Get field data as string slice
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static NativeSlice<byte> GetField(CSVRow row, int columnIndex)
        {
            if (columnIndex >= 0 && columnIndex < row.FieldCount)
                return row.Fields[columnIndex];
            return new NativeSlice<byte>();
        }

        /// <summary>
        /// Try to parse field as integer
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryGetInt(CSVRow row, int columnIndex, out int value)
        {
            var field = GetField(row, columnIndex);
            if (field.Length > 0)
            {
                return TryParseInt(field, out value);
            }
            value = 0;
            return false;
        }

        /// <summary>
        /// Try to parse field as float
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryGetFloat(CSVRow row, int columnIndex, out float value)
        {
            var field = GetField(row, columnIndex);
            if (field.Length > 0)
            {
                return TryParseFloat(field, out value);
            }
            value = 0f;
            return false;
        }

        /// <summary>
        /// Compute FNV-1a hash of byte slice
        /// </summary>
        [BurstCompile]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint ComputeHash(in NativeSlice<byte> data)
        {
            const uint FNV_OFFSET_BASIS = 2166136261;
            const uint FNV_PRIME = 16777619;

            uint hash = FNV_OFFSET_BASIS;
            for (int i = 0; i < data.Length; i++)
            {
                hash ^= data[i];
                hash *= FNV_PRIME;
            }
            return hash;
        }

        /// <summary>
        /// Parse integer from byte slice
        /// </summary>
        [BurstCompile]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TryParseInt(in NativeSlice<byte> data, out int value)
        {
            value = 0;
            if (data.Length == 0) return false;

            int sign = 1;
            int start = 0;

            if (data[0] == '-')
            {
                sign = -1;
                start = 1;
            }

            for (int i = start; i < data.Length; i++)
            {
                byte c = data[i];
                if (c >= '0' && c <= '9')
                {
                    value = value * 10 + (c - '0');
                }
                else
                {
                    return false;
                }
            }

            value *= sign;
            return true;
        }

        /// <summary>
        /// Parse float from byte slice
        /// </summary>
        [BurstCompile]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TryParseFloat(in NativeSlice<byte> data, out float value)
        {
            value = 0f;
            if (data.Length == 0) return false;

            float sign = 1f;
            int start = 0;
            float result = 0f;
            float decimalPlace = 0f;

            if (data[0] == '-')
            {
                sign = -1f;
                start = 1;
            }

            for (int i = start; i < data.Length; i++)
            {
                byte c = data[i];
                if (c >= '0' && c <= '9')
                {
                    if (decimalPlace > 0)
                    {
                        result += (c - '0') / decimalPlace;
                        decimalPlace *= 10f;
                    }
                    else
                    {
                        result = result * 10f + (c - '0');
                    }
                }
                else if (c == '.' && decimalPlace == 0)
                {
                    decimalPlace = 10f;
                }
                else
                {
                    return false;
                }
            }

            value = result * sign;
            return true;
        }
    }
}