using System;

namespace Core.Data
{
    /// <summary>
    /// Simple 32-bit fixed-point number for deterministic calculations
    /// Format: 16.16 (16 integer bits, 16 fractional bits)
    /// </summary>
    public struct FixedPoint32
    {
        public readonly int RawValue;

        private const int FRACTIONAL_BITS = 16;
        private const int ONE_RAW = 1 << FRACTIONAL_BITS; // 65536

        public FixedPoint32(int rawValue)
        {
            RawValue = rawValue;
        }

        public static FixedPoint32 FromRaw(int raw) => new FixedPoint32(raw);
        public static FixedPoint32 FromInt(int value) => new FixedPoint32(value << FRACTIONAL_BITS);
        public static FixedPoint32 Zero => new FixedPoint32(0);
        public static FixedPoint32 One => new FixedPoint32(ONE_RAW);

        public static FixedPoint32 operator +(FixedPoint32 a, FixedPoint32 b) =>
            new FixedPoint32(a.RawValue + b.RawValue);

        public static FixedPoint32 operator -(FixedPoint32 a, FixedPoint32 b) =>
            new FixedPoint32(a.RawValue - b.RawValue);

        public static FixedPoint32 operator *(FixedPoint32 a, FixedPoint32 b) =>
            new FixedPoint32((int)(((long)a.RawValue * b.RawValue) >> FRACTIONAL_BITS));

        public static bool operator <(FixedPoint32 a, FixedPoint32 b) =>
            a.RawValue < b.RawValue;

        public static bool operator >(FixedPoint32 a, FixedPoint32 b) =>
            a.RawValue > b.RawValue;

        public static bool operator <=(FixedPoint32 a, FixedPoint32 b) =>
            a.RawValue <= b.RawValue;

        public static bool operator >=(FixedPoint32 a, FixedPoint32 b) =>
            a.RawValue >= b.RawValue;

        public float ToFloat() => (float)RawValue / ONE_RAW;

        public override string ToString() => ToFloat().ToString("F4");
    }

    /// <summary>
    /// Fixed-point 2D vector for deterministic calculations
    /// </summary>
    public struct FixedPoint2
    {
        public FixedPoint32 x;
        public FixedPoint32 y;

        public FixedPoint2(FixedPoint32 x, FixedPoint32 y)
        {
            this.x = x;
            this.y = y;
        }

        public static FixedPoint2 Zero => new FixedPoint2(FixedPoint32.Zero, FixedPoint32.Zero);

        public FixedPoint32 LengthSquared => x * x + y * y;

        public override string ToString()
        {
            return $"({x}, {y})";
        }
    }
}
