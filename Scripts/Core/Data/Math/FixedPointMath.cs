using System;
using System.Runtime.InteropServices;

namespace Core.Data
{
    /// <summary>
    /// 32-bit fixed-point number for deterministic calculations
    /// Format: 16.16 (16 integer bits, 16 fractional bits)
    /// Range: -32,768 to 32,767 with ~0.00002 precision
    ///
    /// Use when memory is constrained or smaller range is acceptable.
    /// For most simulation math, prefer FixedPoint64 for its larger range.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct FixedPoint32 : IEquatable<FixedPoint32>, IComparable<FixedPoint32>
    {
        public readonly int RawValue;

        private const int FRACTIONAL_BITS = 16;
        private const int ONE_RAW = 1 << FRACTIONAL_BITS; // 65536
        private const int HALF_RAW = ONE_RAW / 2;

        // Constants
        public static readonly FixedPoint32 Zero = new FixedPoint32(0);
        public static readonly FixedPoint32 One = new FixedPoint32(ONE_RAW);
        public static readonly FixedPoint32 Half = new FixedPoint32(HALF_RAW);
        public static readonly FixedPoint32 Two = new FixedPoint32(ONE_RAW * 2);
        public static readonly FixedPoint32 MinValue = new FixedPoint32(int.MinValue);
        public static readonly FixedPoint32 MaxValue = new FixedPoint32(int.MaxValue);
        public static readonly FixedPoint32 NegativeOne = new FixedPoint32(-ONE_RAW);
        public static readonly FixedPoint32 Ten = new FixedPoint32(ONE_RAW * 10);
        public static readonly FixedPoint32 Hundred = new FixedPoint32(ONE_RAW * 100);

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
        public FixedPoint32(int rawValue)
        {
            RawValue = rawValue;
        }

        /// <summary>
        /// Create from raw fixed-point value (for serialization)
        /// </summary>
        public static FixedPoint32 FromRaw(int raw) => new FixedPoint32(raw);

        /// <summary>
        /// Create from integer value
        /// </summary>
        public static FixedPoint32 FromInt(int value) => new FixedPoint32(value << FRACTIONAL_BITS);

        /// <summary>
        /// Create from fraction (numerator / denominator)
        /// </summary>
        public static FixedPoint32 FromFraction(int numerator, int denominator)
        {
            if (denominator == 0)
                throw new DivideByZeroException("Denominator cannot be zero");

            return new FixedPoint32((numerator << FRACTIONAL_BITS) / denominator);
        }

        /// <summary>
        /// Create from float (ONLY use during initialization, NEVER in simulation)
        /// </summary>
        public static FixedPoint32 FromFloat(float value)
        {
            return new FixedPoint32((int)(value * ONE_RAW));
        }

        /// <summary>
        /// Create from FixedPoint64 (truncates to lower precision)
        /// </summary>
        public static FixedPoint32 FromFixed64(FixedPoint64 value)
        {
            // FixedPoint64 is 32.32, FixedPoint32 is 16.16
            // Shift right by 16 to convert fractional bits
            return new FixedPoint32((int)(value.RawValue >> 16));
        }

        // Arithmetic operators
        public static FixedPoint32 operator +(FixedPoint32 a, FixedPoint32 b) =>
            new FixedPoint32(a.RawValue + b.RawValue);

        public static FixedPoint32 operator -(FixedPoint32 a, FixedPoint32 b) =>
            new FixedPoint32(a.RawValue - b.RawValue);

        public static FixedPoint32 operator -(FixedPoint32 a) =>
            new FixedPoint32(-a.RawValue);

        public static FixedPoint32 operator *(FixedPoint32 a, FixedPoint32 b) =>
            new FixedPoint32((int)(((long)a.RawValue * b.RawValue) >> FRACTIONAL_BITS));

        public static FixedPoint32 operator /(FixedPoint32 a, FixedPoint32 b)
        {
            if (b.RawValue == 0)
                throw new DivideByZeroException("Cannot divide by zero");

            return new FixedPoint32((int)(((long)a.RawValue << FRACTIONAL_BITS) / b.RawValue));
        }

        public static FixedPoint32 operator %(FixedPoint32 a, FixedPoint32 b)
        {
            return new FixedPoint32(a.RawValue % b.RawValue);
        }

        // Integer multiplication/division (more efficient)
        public static FixedPoint32 operator *(FixedPoint32 a, int b) =>
            new FixedPoint32(a.RawValue * b);

        public static FixedPoint32 operator /(FixedPoint32 a, int b) =>
            new FixedPoint32(a.RawValue / b);

        // Comparison operators
        public static bool operator ==(FixedPoint32 a, FixedPoint32 b) => a.RawValue == b.RawValue;
        public static bool operator !=(FixedPoint32 a, FixedPoint32 b) => a.RawValue != b.RawValue;
        public static bool operator <(FixedPoint32 a, FixedPoint32 b) => a.RawValue < b.RawValue;
        public static bool operator >(FixedPoint32 a, FixedPoint32 b) => a.RawValue > b.RawValue;
        public static bool operator <=(FixedPoint32 a, FixedPoint32 b) => a.RawValue <= b.RawValue;
        public static bool operator >=(FixedPoint32 a, FixedPoint32 b) => a.RawValue >= b.RawValue;

        // Conversion to primitives
        public int ToInt() => RawValue >> FRACTIONAL_BITS;

        /// <summary>
        /// Convert to float (ONLY for presentation layer, NEVER use result in simulation)
        /// </summary>
        public float ToFloat() => (float)RawValue / ONE_RAW;

        /// <summary>
        /// Convert to FixedPoint64 (lossless upcast)
        /// </summary>
        public FixedPoint64 ToFixed64()
        {
            // FixedPoint32 is 16.16, FixedPoint64 is 32.32
            // Shift left by 16 to convert fractional bits
            return FixedPoint64.FromRaw((long)RawValue << 16);
        }

        // Math functions
        public static FixedPoint32 Abs(FixedPoint32 value) =>
            value.RawValue >= 0 ? value : new FixedPoint32(-value.RawValue);

        public static FixedPoint32 Min(FixedPoint32 a, FixedPoint32 b) =>
            a.RawValue < b.RawValue ? a : b;

        public static FixedPoint32 Max(FixedPoint32 a, FixedPoint32 b) =>
            a.RawValue > b.RawValue ? a : b;

        public static FixedPoint32 Clamp(FixedPoint32 value, FixedPoint32 min, FixedPoint32 max)
        {
            if (value.RawValue < min.RawValue) return min;
            if (value.RawValue > max.RawValue) return max;
            return value;
        }

        /// <summary>
        /// Floor to nearest integer
        /// </summary>
        public static FixedPoint32 Floor(FixedPoint32 value)
        {
            return new FixedPoint32((value.RawValue >> FRACTIONAL_BITS) << FRACTIONAL_BITS);
        }

        /// <summary>
        /// Ceiling to nearest integer
        /// </summary>
        public static FixedPoint32 Ceiling(FixedPoint32 value)
        {
            int fractional = value.RawValue & (ONE_RAW - 1);
            if (fractional == 0)
                return value;

            return Floor(value) + One;
        }

        /// <summary>
        /// Round to nearest integer
        /// </summary>
        public static FixedPoint32 Round(FixedPoint32 value)
        {
            return Floor(value + Half);
        }

        // === Advanced Math Functions ===

        /// <summary>
        /// Square root using Newton-Raphson method (deterministic, Burst-compatible)
        /// Returns Zero for negative inputs
        /// </summary>
        public static FixedPoint32 Sqrt(FixedPoint32 value)
        {
            if (value.RawValue <= 0)
                return Zero;

            // Initial guess
            int x = value.RawValue;
            int guess = x >> 1;
            if (guess == 0) guess = ONE_RAW;

            // Newton-Raphson iterations (6 iterations sufficient for 32-bit)
            for (int i = 0; i < 6; i++)
            {
                int quotient = (int)(((long)x << FRACTIONAL_BITS) / guess);
                guess = (guess + quotient) >> 1;
            }

            return new FixedPoint32(guess);
        }

        /// <summary>
        /// Integer exponentiation (value^exponent)
        /// </summary>
        public static FixedPoint32 Pow(FixedPoint32 value, int exponent)
        {
            if (exponent == 0)
                return One;

            if (exponent < 0)
            {
                value = One / value;
                exponent = -exponent;
            }

            // Binary exponentiation
            FixedPoint32 result = One;
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
        /// </summary>
        public static FixedPoint32 Lerp(FixedPoint32 a, FixedPoint32 b, FixedPoint32 t)
        {
            return a + (b - a) * t;
        }

        /// <summary>
        /// Clamped linear interpolation (t clamped to 0-1)
        /// </summary>
        public static FixedPoint32 LerpClamped(FixedPoint32 a, FixedPoint32 b, FixedPoint32 t)
        {
            t = Clamp(t, Zero, One);
            return a + (b - a) * t;
        }

        /// <summary>
        /// Inverse linear interpolation
        /// </summary>
        public static FixedPoint32 InverseLerp(FixedPoint32 a, FixedPoint32 b, FixedPoint32 value)
        {
            if (a.RawValue == b.RawValue)
                return Zero;

            return (value - a) / (b - a);
        }

        /// <summary>
        /// Remap value from one range to another
        /// </summary>
        public static FixedPoint32 Remap(FixedPoint32 value, FixedPoint32 inMin, FixedPoint32 inMax,
                                         FixedPoint32 outMin, FixedPoint32 outMax)
        {
            FixedPoint32 t = InverseLerp(inMin, inMax, value);
            return Lerp(outMin, outMax, t);
        }

        /// <summary>
        /// Move towards target by a maximum delta
        /// </summary>
        public static FixedPoint32 MoveTowards(FixedPoint32 current, FixedPoint32 target, FixedPoint32 maxDelta)
        {
            FixedPoint32 diff = target - current;
            if (Abs(diff) <= maxDelta)
                return target;

            return current + (diff.IsPositive ? maxDelta : -maxDelta);
        }

        /// <summary>
        /// Returns the fractional part of the value
        /// </summary>
        public static FixedPoint32 Frac(FixedPoint32 value)
        {
            int fractional = value.RawValue & (ONE_RAW - 1);
            if (value.RawValue < 0 && fractional != 0)
                fractional = ONE_RAW - fractional;
            return new FixedPoint32(fractional);
        }

        /// <summary>
        /// Percentage: (value / total) * 100
        /// </summary>
        public static FixedPoint32 Percentage(FixedPoint32 value, FixedPoint32 total)
        {
            if (total.IsZero)
                return Zero;

            return (value / total) * Hundred;
        }

        // IEquatable implementation
        public bool Equals(FixedPoint32 other) => RawValue == other.RawValue;

        public override bool Equals(object obj) =>
            obj is FixedPoint32 other && Equals(other);

        public override int GetHashCode() => RawValue.GetHashCode();

        // IComparable implementation
        public int CompareTo(FixedPoint32 other) => RawValue.CompareTo(other.RawValue);

        // String conversion
        public override string ToString() => ToFloat().ToString("F4");

        public string ToString(string format) => ToFloat().ToString(format);

        /// <summary>
        /// Serialize to bytes for networking (4 bytes exactly)
        /// </summary>
        public byte[] ToBytes()
        {
            byte[] bytes = new byte[4];
            bytes[0] = (byte)(RawValue >> 24);
            bytes[1] = (byte)(RawValue >> 16);
            bytes[2] = (byte)(RawValue >> 8);
            bytes[3] = (byte)RawValue;
            return bytes;
        }

        /// <summary>
        /// Deserialize from bytes for networking
        /// </summary>
        public static FixedPoint32 FromBytes(byte[] bytes, int offset = 0)
        {
            if (bytes == null)
                throw new ArgumentNullException(nameof(bytes));
            if (bytes.Length < offset + 4)
                throw new ArgumentException("Not enough bytes to deserialize FixedPoint32");

            int value = (bytes[offset] << 24) |
                        (bytes[offset + 1] << 16) |
                        (bytes[offset + 2] << 8) |
                        bytes[offset + 3];

            return new FixedPoint32(value);
        }
    }

    /// <summary>
    /// Fixed-point 2D vector for deterministic calculations
    /// </summary>
    public struct FixedPoint2 : IEquatable<FixedPoint2>
    {
        public FixedPoint32 x;
        public FixedPoint32 y;

        public FixedPoint2(FixedPoint32 x, FixedPoint32 y)
        {
            this.x = x;
            this.y = y;
        }

        public static FixedPoint2 Zero => new FixedPoint2(FixedPoint32.Zero, FixedPoint32.Zero);
        public static FixedPoint2 One => new FixedPoint2(FixedPoint32.One, FixedPoint32.One);

        public FixedPoint32 LengthSquared => x * x + y * y;

        public FixedPoint32 Length => FixedPoint32.Sqrt(LengthSquared);

        public static FixedPoint2 operator +(FixedPoint2 a, FixedPoint2 b) =>
            new FixedPoint2(a.x + b.x, a.y + b.y);

        public static FixedPoint2 operator -(FixedPoint2 a, FixedPoint2 b) =>
            new FixedPoint2(a.x - b.x, a.y - b.y);

        public static FixedPoint2 operator *(FixedPoint2 a, FixedPoint32 scalar) =>
            new FixedPoint2(a.x * scalar, a.y * scalar);

        public static FixedPoint2 operator /(FixedPoint2 a, FixedPoint32 scalar) =>
            new FixedPoint2(a.x / scalar, a.y / scalar);

        public static bool operator ==(FixedPoint2 a, FixedPoint2 b) =>
            a.x == b.x && a.y == b.y;

        public static bool operator !=(FixedPoint2 a, FixedPoint2 b) =>
            !(a == b);

        public static FixedPoint32 Dot(FixedPoint2 a, FixedPoint2 b) =>
            a.x * b.x + a.y * b.y;

        public static FixedPoint32 Distance(FixedPoint2 a, FixedPoint2 b) =>
            (b - a).Length;

        public static FixedPoint32 DistanceSquared(FixedPoint2 a, FixedPoint2 b) =>
            (b - a).LengthSquared;

        public static FixedPoint2 Lerp(FixedPoint2 a, FixedPoint2 b, FixedPoint32 t) =>
            new FixedPoint2(
                FixedPoint32.Lerp(a.x, b.x, t),
                FixedPoint32.Lerp(a.y, b.y, t));

        public bool Equals(FixedPoint2 other) => x == other.x && y == other.y;

        public override bool Equals(object obj) =>
            obj is FixedPoint2 other && Equals(other);

        public override int GetHashCode() => (x.RawValue, y.RawValue).GetHashCode();

        public override string ToString() => $"({x}, {y})";
    }
}
