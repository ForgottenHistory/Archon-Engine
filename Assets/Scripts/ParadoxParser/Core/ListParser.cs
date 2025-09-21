using System;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using ParadoxParser.Tokenization;
using ParadoxParser.Utilities;

namespace ParadoxParser.Core
{
    /// <summary>
    /// Specialized parser for list values in Paradox files
    /// Handles space-separated values: { item1 item2 item3 }
    /// </summary>
    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public static class ListParser
    {
        /// <summary>
        /// Parsed list item with type information
        /// </summary>
        public struct ListItem
        {
            public NativeSlice<byte> RawData;
            public ListItemType Type;
            public int IntValue;        // Cached if Type == Integer
            public float FloatValue;    // Cached if Type == Float
            public uint Hash;           // Hash for string items

            public bool IsString => Type == ListItemType.String;
            public bool IsInteger => Type == ListItemType.Integer;
            public bool IsFloat => Type == ListItemType.Float;
            public bool IsDate => Type == ListItemType.Date;
        }

        /// <summary>
        /// Types of list items
        /// </summary>
        public enum ListItemType : byte
        {
            String = 0,
            Integer = 1,
            Float = 2,
            Date = 3
        }

        /// <summary>
        /// Parse a list from tokens between braces
        /// Expects tokens starting after the opening brace
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryParseList(
            NativeSlice<Token> tokens,
            int startIndex,
            out int tokensConsumed,
            NativeList<ListItem> items,
            NativeSlice<byte> sourceData)
        {
            tokensConsumed = 0;
            items.Clear();

            int tokenIndex = startIndex;
            bool foundClosingBrace = false;

            while (tokenIndex < tokens.Length)
            {
                var token = tokens[tokenIndex];

                switch (token.Type)
                {
                    case TokenType.RightBrace:
                        foundClosingBrace = true;
                        tokenIndex++;
                        break;

                    case TokenType.Whitespace:
                    case TokenType.Newline:
                        tokenIndex++; // Skip whitespace
                        continue;

                    case TokenType.Hash:
                        // Skip comment line
                        tokenIndex = SkipToNextLine(tokens, tokenIndex);
                        continue;

                    case TokenType.Identifier:
                    case TokenType.String:
                        if (!ParseStringItem(token, sourceData, out var stringItem))
                            return false;
                        items.Add(stringItem);
                        tokenIndex++;
                        continue;

                    case TokenType.Number when (token.Flags & TokenFlags.IsFloat) == 0:
                        if (!ParseIntegerItem(token, sourceData, out var intItem))
                            return false;
                        items.Add(intItem);
                        tokenIndex++;
                        continue;

                    case TokenType.Number when (token.Flags & TokenFlags.IsFloat) != 0:
                        if (!ParseFloatItem(token, sourceData, out var floatItem))
                            return false;
                        items.Add(floatItem);
                        tokenIndex++;
                        continue;

                    case TokenType.Date:
                        if (!ParseDateItem(token, sourceData, out var dateItem))
                            return false;
                        items.Add(dateItem);
                        tokenIndex++;
                        continue;

                    default:
                        return false; // Invalid token in list
                }

                if (foundClosingBrace)
                    break;
            }

            tokensConsumed = tokenIndex - startIndex;
            return foundClosingBrace;
        }

        /// <summary>
        /// Parse a string/identifier list item
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool ParseStringItem(Token token, NativeSlice<byte> sourceData, out ListItem item)
        {
            var data = sourceData.Slice(token.StartPosition, token.Length);

            item = new ListItem
            {
                RawData = data,
                Type = ListItemType.String,
                IntValue = 0,
                FloatValue = 0f,
                Hash = FastHasher.HashFNV1a32(data)
            };

            return true;
        }

        /// <summary>
        /// Parse an integer list item
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool ParseIntegerItem(Token token, NativeSlice<byte> sourceData, out ListItem item)
        {
            var data = sourceData.Slice(token.StartPosition, token.Length);
            var parseResult = FastNumberParser.ParseInt32(data);

            if (!parseResult.Success)
            {
                item = default;
                return false;
            }

            item = new ListItem
            {
                RawData = data,
                Type = ListItemType.Integer,
                IntValue = parseResult.Value,
                FloatValue = parseResult.Value,
                Hash = 0
            };

            return true;
        }

        /// <summary>
        /// Parse a float list item
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool ParseFloatItem(Token token, NativeSlice<byte> sourceData, out ListItem item)
        {
            var data = sourceData.Slice(token.StartPosition, token.Length);
            var parseResult = FastNumberParser.ParseFloat(data);

            if (!parseResult.Success)
            {
                item = default;
                return false;
            }

            item = new ListItem
            {
                RawData = data,
                Type = ListItemType.Float,
                IntValue = (int)parseResult.Value,
                FloatValue = parseResult.Value,
                Hash = 0
            };

            return true;
        }

        /// <summary>
        /// Parse a date list item
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool ParseDateItem(Token token, NativeSlice<byte> sourceData, out ListItem item)
        {
            var data = sourceData.Slice(token.StartPosition, token.Length);

            item = new ListItem
            {
                RawData = data,
                Type = ListItemType.Date,
                IntValue = 0,
                FloatValue = 0f,
                Hash = 0
            };

            return true;
        }

        /// <summary>
        /// Skip tokens until next line (for comment handling)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int SkipToNextLine(NativeSlice<Token> tokens, int startIndex)
        {
            int index = startIndex;
            while (index < tokens.Length && tokens[index].Type != TokenType.Newline)
            {
                index++;
            }
            return index + 1; // Skip the newline too
        }

        /// <summary>
        /// Find all integer values in a list
        /// </summary>
        public static void GetIntegerValues(NativeSlice<ListItem> items, NativeList<int> values)
        {
            values.Clear();
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i].IsInteger)
                {
                    values.Add(items[i].IntValue);
                }
            }
        }

        /// <summary>
        /// Find all float values in a list
        /// </summary>
        public static void GetFloatValues(NativeSlice<ListItem> items, NativeList<float> values)
        {
            values.Clear();
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i].IsFloat)
                {
                    values.Add(items[i].FloatValue);
                }
                else if (items[i].IsInteger)
                {
                    values.Add(items[i].IntValue); // Include integers as floats
                }
            }
        }

        /// <summary>
        /// Find all string values in a list
        /// </summary>
        public static void GetStringValues(NativeSlice<ListItem> items, NativeList<NativeSlice<byte>> values)
        {
            values.Clear();
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i].IsString)
                {
                    values.Add(items[i].RawData);
                }
            }
        }

        /// <summary>
        /// Check if list contains a specific integer value
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool ContainsInteger(NativeSlice<ListItem> items, int value)
        {
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i].IsInteger && items[i].IntValue == value)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Check if list contains a specific string value (by hash)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool ContainsString(NativeSlice<ListItem> items, uint stringHash)
        {
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i].IsString && items[i].Hash == stringHash)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Check if list contains a specific string value
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool ContainsString(NativeSlice<ListItem> items, NativeSlice<byte> value)
        {
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i].IsString && KeyValueParser.KeyEquals(items[i].RawData, value))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Get the count of items of a specific type
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int CountItemsOfType(NativeSlice<ListItem> items, ListItemType type)
        {
            int count = 0;
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i].Type == type)
                    count++;
            }
            return count;
        }

        /// <summary>
        /// Parse a list of province IDs (common pattern in Paradox files)
        /// Optimized for integer lists
        /// </summary>
        public static bool TryParseProvinceList(
            NativeSlice<Token> tokens,
            int startIndex,
            out int tokensConsumed,
            NativeList<int> provinceIds,
            NativeSlice<byte> sourceData)
        {
            var tempItems = new NativeList<ListItem>(64, Allocator.Temp);

            try
            {
                if (!TryParseList(tokens, startIndex, out tokensConsumed, tempItems, sourceData))
                    return false;

                GetIntegerValues(tempItems.AsArray().Slice(0, tempItems.Length), provinceIds);
                return true;
            }
            finally
            {
                tempItems.Dispose();
            }
        }

        /// <summary>
        /// Parse a list of string identifiers (common pattern in Paradox files)
        /// </summary>
        public static bool TryParseStringList(
            NativeSlice<Token> tokens,
            int startIndex,
            out int tokensConsumed,
            NativeList<uint> stringHashes,
            NativeSlice<byte> sourceData)
        {
            var tempItems = new NativeList<ListItem>(64, Allocator.Temp);

            try
            {
                if (!TryParseList(tokens, startIndex, out tokensConsumed, tempItems, sourceData))
                    return false;

                stringHashes.Clear();
                for (int i = 0; i < tempItems.Length; i++)
                {
                    if (tempItems[i].IsString)
                    {
                        stringHashes.Add(tempItems[i].Hash);
                    }
                }

                return true;
            }
            finally
            {
                tempItems.Dispose();
            }
        }
    }
}