using System;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;

namespace ParadoxParser.Utilities
{
    /// <summary>
    /// High-performance number parsing without allocations
    /// Optimized for Paradox file formats (integers, floats, dates)
    /// </summary>
    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public static class FastNumberParser
    {
        /// <summary>
        /// Parse result for number operations
        /// </summary>
        public struct ParseResult<T> where T : unmanaged
        {
            public T Value;
            public bool Success;
            public int BytesConsumed;

            public static ParseResult<T> Failed => new ParseResult<T> { Success = false, BytesConsumed = 0 };

            public static ParseResult<T> Successful(T value, int bytesConsumed)
            {
                return new ParseResult<T> { Value = value, Success = true, BytesConsumed = bytesConsumed };
            }
        }

        /// <summary>
        /// Parse integer from byte slice (supports negative numbers)
        /// Optimized for common Paradox integer ranges
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ParseResult<int> ParseInt32(NativeSlice<byte> data)
        {
            if (data.Length == 0)
                return ParseResult<int>.Failed;

            int index = 0;
            bool isNegative = false;

            // Handle negative sign
            if (data[0] == (byte)'-')
            {
                isNegative = true;
                index = 1;
                if (index >= data.Length)
                    return ParseResult<int>.Failed;
            }

            // Fast path for single digit (very common)
            if (index + 1 == data.Length || !IsDigit(data[index + 1]))
            {
                byte digit = data[index];
                if (IsDigit(digit))
                {
                    int value = digit - (byte)'0';
                    if (isNegative) value = -value;
                    return ParseResult<int>.Successful(value, index + 1);
                }
                return ParseResult<int>.Failed;
            }

            // Parse multiple digits with overflow checking
            long result = 0;
            int startIndex = index;

            while (index < data.Length && IsDigit(data[index]))
            {
                byte digit = data[index];
                long newResult = result * 10 + (digit - (byte)'0');

                // Check for overflow
                if (newResult > int.MaxValue)
                    return ParseResult<int>.Failed;

                result = newResult;
                index++;
            }

            if (index == startIndex) // No digits found
                return ParseResult<int>.Failed;

            int finalValue = (int)result;
            if (isNegative) finalValue = -finalValue;

            return ParseResult<int>.Successful(finalValue, index);
        }

        /// <summary>
        /// Parse long integer for larger numbers (province IDs, etc.)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ParseResult<long> ParseInt64(NativeSlice<byte> data)
        {
            if (data.Length == 0)
                return ParseResult<long>.Failed;

            int index = 0;
            bool isNegative = false;

            // Handle negative sign
            if (data[0] == (byte)'-')
            {
                isNegative = true;
                index = 1;
                if (index >= data.Length)
                    return ParseResult<long>.Failed;
            }

            long result = 0;
            int startIndex = index;

            while (index < data.Length && IsDigit(data[index]))
            {
                byte digit = data[index];

                // Check for overflow before multiplication
                if (result > (long.MaxValue - (digit - (byte)'0')) / 10)
                    return ParseResult<long>.Failed;

                result = result * 10 + (digit - (byte)'0');
                index++;
            }

            if (index == startIndex) // No digits found
                return ParseResult<long>.Failed;

            if (isNegative) result = -result;

            return ParseResult<long>.Successful(result, index);
        }

        /// <summary>
        /// Parse floating point number (for decimal values in Paradox files)
        /// Supports format: [-]digits[.digits]
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ParseResult<float> ParseFloat(NativeSlice<byte> data)
        {
            if (data.Length == 0)
                return ParseResult<float>.Failed;

            int index = 0;
            bool isNegative = false;

            // Handle negative sign
            if (data[0] == (byte)'-')
            {
                isNegative = true;
                index = 1;
                if (index >= data.Length)
                    return ParseResult<float>.Failed;
            }

            // Parse integer part
            long integerPart = 0;
            int startIndex = index;

            while (index < data.Length && IsDigit(data[index]))
            {
                byte digit = data[index];
                integerPart = integerPart * 10 + (digit - (byte)'0');
                index++;
            }

            if (index == startIndex) // No integer digits found
                return ParseResult<float>.Failed;

            float result = integerPart;

            // Parse fractional part if present
            if (index < data.Length && data[index] == (byte)'.')
            {
                index++; // Skip decimal point

                long fractionalPart = 0;
                int fractionalDigits = 0;
                int fractionalStart = index;

                while (index < data.Length && IsDigit(data[index]) && fractionalDigits < 7) // Limit precision to avoid overflow
                {
                    byte digit = data[index];
                    fractionalPart = fractionalPart * 10 + (digit - (byte)'0');
                    fractionalDigits++;
                    index++;
                }

                if (fractionalDigits > 0)
                {
                    // Convert fractional part to decimal
                    float divisor = math.pow(10, fractionalDigits);
                    result += fractionalPart / divisor;
                }
            }

            if (isNegative) result = -result;

            return ParseResult<float>.Successful(result, index);
        }

        /// <summary>
        /// Parse double precision number for high-precision values
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ParseResult<double> ParseDouble(NativeSlice<byte> data)
        {
            if (data.Length == 0)
                return ParseResult<double>.Failed;

            int index = 0;
            bool isNegative = false;

            // Handle negative sign
            if (data[0] == (byte)'-')
            {
                isNegative = true;
                index = 1;
                if (index >= data.Length)
                    return ParseResult<double>.Failed;
            }

            // Parse integer part
            long integerPart = 0;
            int startIndex = index;

            while (index < data.Length && IsDigit(data[index]))
            {
                byte digit = data[index];
                integerPart = integerPart * 10 + (digit - (byte)'0');
                index++;
            }

            if (index == startIndex) // No integer digits found
                return ParseResult<double>.Failed;

            double result = integerPart;

            // Parse fractional part if present
            if (index < data.Length && data[index] == (byte)'.')
            {
                index++; // Skip decimal point

                long fractionalPart = 0;
                int fractionalDigits = 0;

                while (index < data.Length && IsDigit(data[index]) && fractionalDigits < 15) // Double precision limit
                {
                    byte digit = data[index];
                    fractionalPart = fractionalPart * 10 + (digit - (byte)'0');
                    fractionalDigits++;
                    index++;
                }

                if (fractionalDigits > 0)
                {
                    // Convert fractional part to decimal
                    double divisor = math.pow(10, fractionalDigits);
                    result += fractionalPart / divisor;
                }
            }

            if (isNegative) result = -result;

            return ParseResult<double>.Successful(result, index);
        }

        /// <summary>
        /// Parse unsigned integer (for IDs, counts, etc.)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ParseResult<uint> ParseUInt32(NativeSlice<byte> data)
        {
            if (data.Length == 0)
                return ParseResult<uint>.Failed;

            // Fast path for single digit
            if (data.Length == 1 && IsDigit(data[0]))
            {
                uint value = (uint)(data[0] - (byte)'0');
                return ParseResult<uint>.Successful(value, 1);
            }

            uint result = 0;
            int index = 0;

            while (index < data.Length && IsDigit(data[index]))
            {
                byte digit = data[index];
                uint newResult = result * 10 + (uint)(digit - (byte)'0');

                // Check for overflow
                if (newResult < result) // Overflow occurred
                    return ParseResult<uint>.Failed;

                result = newResult;
                index++;
            }

            if (index == 0) // No digits found
                return ParseResult<uint>.Failed;

            return ParseResult<uint>.Successful(result, index);
        }

        /// <summary>
        /// Parse hexadecimal number (for RGB colors, etc.)
        /// Supports format: [0x]hexdigits
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ParseResult<uint> ParseHex(NativeSlice<byte> data)
        {
            if (data.Length == 0)
                return ParseResult<uint>.Failed;

            int index = 0;

            // Skip optional "0x" prefix
            if (data.Length >= 2 && data[0] == (byte)'0' && (data[1] == (byte)'x' || data[1] == (byte)'X'))
            {
                index = 2;
            }

            if (index >= data.Length)
                return ParseResult<uint>.Failed;

            uint result = 0;
            int startIndex = index;

            while (index < data.Length && IsHexDigit(data[index]))
            {
                byte digit = data[index];
                uint hexValue = GetHexValue(digit);

                // Check for overflow
                if (result > (uint.MaxValue - hexValue) / 16)
                    return ParseResult<uint>.Failed;

                result = result * 16 + hexValue;
                index++;
            }

            if (index == startIndex) // No hex digits found
                return ParseResult<uint>.Failed;

            return ParseResult<uint>.Successful(result, index);
        }

        /// <summary>
        /// Parse percentage value (returns value between 0 and 1)
        /// Supports format: digits[.digits]%
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ParseResult<float> ParsePercentage(NativeSlice<byte> data)
        {
            if (data.Length < 2) // Need at least "0%"
                return ParseResult<float>.Failed;

            // Check if it ends with %
            if (data[data.Length - 1] != (byte)'%')
                return ParseResult<float>.Failed;

            // Parse the number part (excluding the %)
            var numberSlice = data.Slice(0, data.Length - 1);
            var floatResult = ParseFloat(numberSlice);

            if (!floatResult.Success)
                return ParseResult<float>.Failed;

            // Convert percentage to decimal (divide by 100)
            float percentage = floatResult.Value / 100.0f;
            return ParseResult<float>.Successful(percentage, data.Length);
        }

        /// <summary>
        /// Check if byte is a decimal digit
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsDigit(byte b)
        {
            return b >= (byte)'0' && b <= (byte)'9';
        }

        /// <summary>
        /// Check if byte is a hexadecimal digit
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsHexDigit(byte b)
        {
            return (b >= (byte)'0' && b <= (byte)'9') ||
                   (b >= (byte)'a' && b <= (byte)'f') ||
                   (b >= (byte)'A' && b <= (byte)'F');
        }

        /// <summary>
        /// Get numeric value of hex digit
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint GetHexValue(byte b)
        {
            if (b >= (byte)'0' && b <= (byte)'9')
                return (uint)(b - (byte)'0');
            if (b >= (byte)'a' && b <= (byte)'f')
                return (uint)(b - (byte)'a' + 10);
            if (b >= (byte)'A' && b <= (byte)'F')
                return (uint)(b - (byte)'A' + 10);
            return 0;
        }

        /// <summary>
        /// Fast integer parsing with known bounds (for common cases like province IDs)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ParseResult<int> ParseBoundedInt(NativeSlice<byte> data, int minValue, int maxValue)
        {
            var result = ParseInt32(data);
            if (!result.Success)
                return result;

            if (result.Value < minValue || result.Value > maxValue)
                return ParseResult<int>.Failed;

            return result;
        }
    }
}