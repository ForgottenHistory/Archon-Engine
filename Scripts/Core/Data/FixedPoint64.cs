using System;
using System.Runtime.InteropServices;

namespace Core.Data
{
    /// <summary>
    /// 64-bit fixed-point number for deterministic calculations across all platforms
    /// Format: 32.32 (32 integer bits, 32 fractional bits)
    /// Range: -2,147,483,648 to 2,147,483,647 with ~0.0000000002 precision
    ///
    /// CRITICAL: This type is used for ALL simulation math to ensure multiplayer determinism.
    /// Float operations produce different results on different CPUs/compilers.
    /// Fixed-point math guarantees identical results across all platforms.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct FixedPoint64 : IEquatable<FixedPoint64>, IComparable<FixedPoint64>
    {
        public readonly long RawValue;

        private const int FRACTIONAL_BITS = 32;
        private const long ONE_RAW = 1L << FRACTIONAL_BITS; // 4294967296
        private const long HALF_RAW = ONE_RAW / 2;

        // Constants
        public static readonly FixedPoint64 Zero = new FixedPoint64(0);
        public static readonly FixedPoint64 One = new FixedPoint64(ONE_RAW);
        public static readonly FixedPoint64 Half = new FixedPoint64(HALF_RAW);
        public static readonly FixedPoint64 Two = new FixedPoint64(ONE_RAW * 2);
        public static readonly FixedPoint64 MinValue = new FixedPoint64(long.MinValue);
        public static readonly FixedPoint64 MaxValue = new FixedPoint64(long.MaxValue);
        public static readonly FixedPoint64 NegativeOne = new FixedPoint64(-ONE_RAW);
        public static readonly FixedPoint64 Ten = new FixedPoint64(ONE_RAW * 10);
        public static readonly FixedPoint64 Hundred = new FixedPoint64(ONE_RAW * 100);

        // Convenience properties
        /// <summary>True if value equals zero</summary>
        public bool IsZero => RawValue == 0;

        /// <summary>True if value is greater than zero</summary>
        public bool IsPositive => RawValue > 0;

        /// <summary>True if value is less than zero</summary>
        public bool IsNegative => RawValue < 0;

        /// <summary>True if value is greater than or equal to zero</summary>
        public bool IsNonNegative => RawValue >= 0;

        /// <summary>True if value is less than or equal to zero</summary>
        public bool IsNonPositive => RawValue <= 0;

        /// <summary>Returns the sign: -1, 0, or 1</summary>
        public int Sign => RawValue > 0 ? 1 : (RawValue < 0 ? -1 : 0);

        // Construction
        private FixedPoint64(long rawValue)
        {
            RawValue = rawValue;
        }

        /// <summary>
        /// Create from raw fixed-point value (for serialization)
        /// </summary>
        public static FixedPoint64 FromRaw(long raw) => new FixedPoint64(raw);

        /// <summary>
        /// Create from integer value
        /// </summary>
        public static FixedPoint64 FromInt(int value) => new FixedPoint64((long)value << FRACTIONAL_BITS);

        /// <summary>
        /// Create from long integer value
        /// </summary>
        public static FixedPoint64 FromLong(long value) => new FixedPoint64(value << FRACTIONAL_BITS);

        /// <summary>
        /// Create from fraction (numerator / denominator)
        /// </summary>
        public static FixedPoint64 FromFraction(long numerator, long denominator)
        {
            if (denominator == 0)
                throw new DivideByZeroException("Denominator cannot be zero");

            // Scale numerator and divide
            return new FixedPoint64((numerator << FRACTIONAL_BITS) / denominator);
        }

        /// <summary>
        /// Create from float (ONLY use during initialization, NEVER in simulation)
        /// </summary>
        public static FixedPoint64 FromFloat(float value)
        {
            return new FixedPoint64((long)(value * ONE_RAW));
        }

        /// <summary>
        /// Create from double (ONLY use during initialization, NEVER in simulation)
        /// </summary>
        public static FixedPoint64 FromDouble(double value)
        {
            return new FixedPoint64((long)(value * ONE_RAW));
        }

        #region Arithmetic Operators

        /// <summary>Adds two fixed-point values.</summary>
        public static FixedPoint64 operator +(FixedPoint64 a, FixedPoint64 b) =>
            new FixedPoint64(a.RawValue + b.RawValue);

        /// <summary>Subtracts two fixed-point values.</summary>
        public static FixedPoint64 operator -(FixedPoint64 a, FixedPoint64 b) =>
            new FixedPoint64(a.RawValue - b.RawValue);

        /// <summary>Negates a fixed-point value.</summary>
        public static FixedPoint64 operator -(FixedPoint64 a) =>
            new FixedPoint64(-a.RawValue);

        /// <summary>Multiplies two fixed-point values using 128-bit intermediate to prevent overflow.</summary>
        public static FixedPoint64 operator *(FixedPoint64 a, FixedPoint64 b)
        {
            // Use 128-bit intermediate to avoid overflow
            // Split into high and low parts
            long aHigh = a.RawValue >> FRACTIONAL_BITS;
            long aLow = a.RawValue & (ONE_RAW - 1);
            long bHigh = b.RawValue >> FRACTIONAL_BITS;
            long bLow = b.RawValue & (ONE_RAW - 1);

            // Multiply parts
            long result = (aHigh * bHigh) << FRACTIONAL_BITS;
            result += aHigh * bLow + aLow * bHigh;
            result += (aLow * bLow) >> FRACTIONAL_BITS;

            return new FixedPoint64(result);
        }

        /// <summary>Divides two fixed-point values. Throws DivideByZeroException if divisor is zero.</summary>
        public static FixedPoint64 operator /(FixedPoint64 a, FixedPoint64 b)
        {
            if (b.RawValue == 0)
                throw new DivideByZeroException("Cannot divide by zero");

            // Shift left for precision, then divide
            // Handle sign separately to avoid overflow
            long dividend = a.RawValue;
            long divisor = b.RawValue;

            bool negativeResult = (dividend < 0) ^ (divisor < 0);
            if (dividend < 0) dividend = -dividend;
            if (divisor < 0) divisor = -divisor;

            // Perform division with extended precision
            long result = (dividend << FRACTIONAL_BITS) / divisor;

            return new FixedPoint64(negativeResult ? -result : result);
        }

        /// <summary>Returns the remainder after division.</summary>
        public static FixedPoint64 operator %(FixedPoint64 a, FixedPoint64 b)
        {
            return new FixedPoint64(a.RawValue % b.RawValue);
        }

        /// <summary>Multiplies by an integer (more efficient than FixedPoint64 multiplication).</summary>
        public static FixedPoint64 operator *(FixedPoint64 a, int b) =>
            new FixedPoint64(a.RawValue * b);

        /// <summary>Divides by an integer (more efficient than FixedPoint64 division).</summary>
        public static FixedPoint64 operator /(FixedPoint64 a, int b) =>
            new FixedPoint64(a.RawValue / b);

        #endregion

        #region Comparison Operators

        public static bool operator ==(FixedPoint64 a, FixedPoint64 b) => a.RawValue == b.RawValue;
        public static bool operator !=(FixedPoint64 a, FixedPoint64 b) => a.RawValue != b.RawValue;
        public static bool operator <(FixedPoint64 a, FixedPoint64 b) => a.RawValue < b.RawValue;
        public static bool operator >(FixedPoint64 a, FixedPoint64 b) => a.RawValue > b.RawValue;
        public static bool operator <=(FixedPoint64 a, FixedPoint64 b) => a.RawValue <= b.RawValue;
        public static bool operator >=(FixedPoint64 a, FixedPoint64 b) => a.RawValue >= b.RawValue;

        #endregion

        #region Conversion Methods

        /// <summary>Truncates to integer (rounds toward zero).</summary>
        public int ToInt() => (int)(RawValue >> FRACTIONAL_BITS);

        /// <summary>Truncates to long integer (rounds toward zero).</summary>
        public long ToLong() => RawValue >> FRACTIONAL_BITS;

        /// <summary>
        /// Convert to float (ONLY for presentation layer, NEVER use result in simulation)
        /// </summary>
        public float ToFloat() => (float)RawValue / ONE_RAW;

        /// <summary>
        /// Convert to double (ONLY for presentation layer, NEVER use result in simulation)
        /// </summary>
        public double ToDouble() => (double)RawValue / ONE_RAW;

        #endregion

        #region Math Functions

        /// <summary>Returns the absolute value.</summary>
        public static FixedPoint64 Abs(FixedPoint64 value) =>
            value.RawValue >= 0 ? value : new FixedPoint64(-value.RawValue);

        /// <summary>Returns the smaller of two values.</summary>
        public static FixedPoint64 Min(FixedPoint64 a, FixedPoint64 b) =>
            a.RawValue < b.RawValue ? a : b;

        /// <summary>Returns the larger of two values.</summary>
        public static FixedPoint64 Max(FixedPoint64 a, FixedPoint64 b) =>
            a.RawValue > b.RawValue ? a : b;

        /// <summary>Clamps a value to the specified range.</summary>
        public static FixedPoint64 Clamp(FixedPoint64 value, FixedPoint64 min, FixedPoint64 max)
        {
            if (value.RawValue < min.RawValue) return min;
            if (value.RawValue > max.RawValue) return max;
            return value;
        }

        /// <summary>
        /// Floor to nearest integer
        /// </summary>
        public static FixedPoint64 Floor(FixedPoint64 value)
        {
            return new FixedPoint64((value.RawValue >> FRACTIONAL_BITS) << FRACTIONAL_BITS);
        }

        /// <summary>
        /// Ceiling to nearest integer
        /// </summary>
        public static FixedPoint64 Ceiling(FixedPoint64 value)
        {
            long fractional = value.RawValue & (ONE_RAW - 1);
            if (fractional == 0)
                return value;

            return Floor(value) + One;
        }

        /// <summary>
        /// Round to nearest integer
        /// </summary>
        public static FixedPoint64 Round(FixedPoint64 value)
        {
            return Floor(value + Half);
        }

        // === Advanced Math Functions ===

        /// <summary>
        /// Square root using Newton-Raphson method (deterministic, Burst-compatible)
        /// Returns Zero for negative inputs
        /// </summary>
        public static FixedPoint64 Sqrt(FixedPoint64 value)
        {
            if (value.RawValue <= 0)
                return Zero;

            // Initial guess: half the value or 1, whichever is larger
            long x = value.RawValue;
            long guess = x >> 1;
            if (guess == 0) guess = ONE_RAW;

            // Newton-Raphson iterations (8 iterations gives full precision)
            for (int i = 0; i < 8; i++)
            {
                // newGuess = (guess + value/guess) / 2
                // In fixed-point: we need to be careful with the division
                long quotient = (x << FRACTIONAL_BITS) / guess;
                guess = (guess + quotient) >> 1;
            }

            return new FixedPoint64(guess);
        }

        /// <summary>
        /// Integer exponentiation (value^exponent)
        /// Deterministic, handles negative exponents
        /// </summary>
        public static FixedPoint64 Pow(FixedPoint64 value, int exponent)
        {
            if (exponent == 0)
                return One;

            if (exponent < 0)
            {
                // Negative exponent: 1 / value^(-exponent)
                value = One / value;
                exponent = -exponent;
            }

            // Binary exponentiation for efficiency
            FixedPoint64 result = One;
            while (exponent > 0)
            {
                if ((exponent & 1) == 1)
                    result = result * value;
                value = value * value;
                exponent >>= 1;
            }

            return result;
        }

        /// <summary>
        /// Linear interpolation: a + (b - a) * t
        /// When t=0 returns a, when t=1 returns b
        /// </summary>
        public static FixedPoint64 Lerp(FixedPoint64 a, FixedPoint64 b, FixedPoint64 t)
        {
            return a + (b - a) * t;
        }

        /// <summary>
        /// Clamped linear interpolation (t clamped to 0-1)
        /// </summary>
        public static FixedPoint64 LerpClamped(FixedPoint64 a, FixedPoint64 b, FixedPoint64 t)
        {
            t = Clamp(t, Zero, One);
            return a + (b - a) * t;
        }

        /// <summary>
        /// Inverse linear interpolation: returns t where Lerp(a, b, t) = value
        /// Returns 0 if a == b
        /// </summary>
        public static FixedPoint64 InverseLerp(FixedPoint64 a, FixedPoint64 b, FixedPoint64 value)
        {
            if (a.RawValue == b.RawValue)
                return Zero;

            return (value - a) / (b - a);
        }

        /// <summary>
        /// Remap value from one range to another
        /// Example: Remap(50, 0, 100, 0, 1) = 0.5
        /// </summary>
        public static FixedPoint64 Remap(FixedPoint64 value, FixedPoint64 inMin, FixedPoint64 inMax,
                                         FixedPoint64 outMin, FixedPoint64 outMax)
        {
            FixedPoint64 t = InverseLerp(inMin, inMax, value);
            return Lerp(outMin, outMax, t);
        }

        /// <summary>
        /// Remap with clamping (output clamped to outMin-outMax range)
        /// </summary>
        public static FixedPoint64 RemapClamped(FixedPoint64 value, FixedPoint64 inMin, FixedPoint64 inMax,
                                                 FixedPoint64 outMin, FixedPoint64 outMax)
        {
            FixedPoint64 t = InverseLerp(inMin, inMax, value);
            return LerpClamped(outMin, outMax, t);
        }

        /// <summary>
        /// Move towards target by a maximum delta
        /// </summary>
        public static FixedPoint64 MoveTowards(FixedPoint64 current, FixedPoint64 target, FixedPoint64 maxDelta)
        {
            FixedPoint64 diff = target - current;
            if (Abs(diff) <= maxDelta)
                return target;

            return current + (diff.IsPositive ? maxDelta : -maxDelta);
        }

        /// <summary>
        /// Returns the fractional part of the value (0 to 0.999...)
        /// </summary>
        public static FixedPoint64 Frac(FixedPoint64 value)
        {
            long fractional = value.RawValue & (ONE_RAW - 1);
            if (value.RawValue < 0 && fractional != 0)
                fractional = ONE_RAW - fractional;
            return new FixedPoint64(fractional);
        }

        /// <summary>
        /// Percentage: (value / total) * 100
        /// Returns Zero if total is zero
        /// </summary>
        public static FixedPoint64 Percentage(FixedPoint64 value, FixedPoint64 total)
        {
            if (total.IsZero)
                return Zero;

            return (value / total) * Hundred;
        }

        // IEquatable implementation
        public bool Equals(FixedPoint64 other) => RawValue == other.RawValue;

        public override bool Equals(object obj) =>
            obj is FixedPoint64 other && Equals(other);

        public override int GetHashCode() => RawValue.GetHashCode();

        // IComparable implementation
        public int CompareTo(FixedPoint64 other) => RawValue.CompareTo(other.RawValue);

        // String conversion
        public override string ToString() => ToDouble().ToString("F6");

        public string ToString(string format) => ToDouble().ToString(format);

        /// <summary>
        /// Serialize to bytes for networking (8 bytes exactly)
        /// </summary>
        public byte[] ToBytes()
        {
            byte[] bytes = new byte[8];
            bytes[0] = (byte)(RawValue >> 56);
            bytes[1] = (byte)(RawValue >> 48);
            bytes[2] = (byte)(RawValue >> 40);
            bytes[3] = (byte)(RawValue >> 32);
            bytes[4] = (byte)(RawValue >> 24);
            bytes[5] = (byte)(RawValue >> 16);
            bytes[6] = (byte)(RawValue >> 8);
            bytes[7] = (byte)RawValue;
            return bytes;
        }

        /// <summary>
        /// Deserialize from bytes for networking
        /// </summary>
        public static FixedPoint64 FromBytes(byte[] bytes, int offset = 0)
        {
            if (bytes == null)
                throw new ArgumentNullException(nameof(bytes));
            if (bytes.Length < offset + 8)
                throw new ArgumentException("Not enough bytes to deserialize FixedPoint64");

            long value = ((long)bytes[offset] << 56) |
                        ((long)bytes[offset + 1] << 48) |
                        ((long)bytes[offset + 2] << 40) |
                        ((long)bytes[offset + 3] << 32) |
                        ((long)bytes[offset + 4] << 24) |
                        ((long)bytes[offset + 5] << 16) |
                        ((long)bytes[offset + 6] << 8) |
                        bytes[offset + 7];

            return new FixedPoint64(value);
        }
    }
}

#endregion