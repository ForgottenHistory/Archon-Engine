using System;
using Core.Data;
using MoonSharp.Interpreter;

namespace Scripting.Types
{
    /// <summary>
    /// Lua-compatible wrapper for FixedPoint64.
    /// Provides deterministic math for scripts that's multiplayer-safe.
    ///
    /// CRITICAL: ALL script math must use LuaFixed, never Lua numbers for gameplay.
    /// Lua numbers are floats and will cause desync in multiplayer.
    ///
    /// Usage in Lua:
    ///   local gold = Fixed.FromInt(100)
    ///   local tax = Fixed.FromFraction(1, 10)  -- 0.1
    ///   local result = gold * tax              -- 10
    /// </summary>
    [MoonSharpUserData]
    public struct LuaFixed : IEquatable<LuaFixed>, IComparable<LuaFixed>
    {
        private readonly FixedPoint64 value;

        // Constants exposed to Lua
        public static readonly LuaFixed Zero = new LuaFixed(FixedPoint64.Zero);
        public static readonly LuaFixed One = new LuaFixed(FixedPoint64.One);
        public static readonly LuaFixed Half = new LuaFixed(FixedPoint64.Half);
        public static readonly LuaFixed Two = new LuaFixed(FixedPoint64.Two);
        public static readonly LuaFixed Ten = new LuaFixed(FixedPoint64.Ten);
        public static readonly LuaFixed Hundred = new LuaFixed(FixedPoint64.Hundred);
        public static readonly LuaFixed NegativeOne = new LuaFixed(FixedPoint64.NegativeOne);

        /// <summary>
        /// Internal constructor from FixedPoint64
        /// </summary>
        internal LuaFixed(FixedPoint64 fp)
        {
            value = fp;
        }

        /// <summary>
        /// Get the underlying FixedPoint64 value
        /// </summary>
        public FixedPoint64 Value => value;

        // Factory methods (exposed to Lua)

        /// <summary>
        /// Create from integer value
        /// Lua: Fixed.FromInt(100)
        /// </summary>
        public static LuaFixed FromInt(int v)
        {
            return new LuaFixed(FixedPoint64.FromInt(v));
        }

        /// <summary>
        /// Create from fraction (numerator / denominator)
        /// Lua: Fixed.FromFraction(1, 10) -- creates 0.1
        /// </summary>
        public static LuaFixed FromFraction(int numerator, int denominator)
        {
            return new LuaFixed(FixedPoint64.FromFraction(numerator, denominator));
        }

        /// <summary>
        /// Create from raw FixedPoint64 value (for C# interop)
        /// </summary>
        public static LuaFixed FromFixedPoint(FixedPoint64 fp)
        {
            return new LuaFixed(fp);
        }

        // Arithmetic operators (MoonSharp maps these to Lua +, -, *, /, %)

        public static LuaFixed operator +(LuaFixed a, LuaFixed b)
        {
            return new LuaFixed(a.value + b.value);
        }

        public static LuaFixed operator -(LuaFixed a, LuaFixed b)
        {
            return new LuaFixed(a.value - b.value);
        }

        public static LuaFixed operator -(LuaFixed a)
        {
            return new LuaFixed(-a.value);
        }

        public static LuaFixed operator *(LuaFixed a, LuaFixed b)
        {
            return new LuaFixed(a.value * b.value);
        }

        public static LuaFixed operator /(LuaFixed a, LuaFixed b)
        {
            return new LuaFixed(a.value / b.value);
        }

        public static LuaFixed operator %(LuaFixed a, LuaFixed b)
        {
            return new LuaFixed(a.value % b.value);
        }

        // Integer operations (more efficient)

        public static LuaFixed operator *(LuaFixed a, int b)
        {
            return new LuaFixed(a.value * b);
        }

        public static LuaFixed operator /(LuaFixed a, int b)
        {
            return new LuaFixed(a.value / b);
        }

        // Comparison operators (MoonSharp maps these to Lua <, >, <=, >=, ==)

        public static bool operator ==(LuaFixed a, LuaFixed b)
        {
            return a.value == b.value;
        }

        public static bool operator !=(LuaFixed a, LuaFixed b)
        {
            return a.value != b.value;
        }

        public static bool operator <(LuaFixed a, LuaFixed b)
        {
            return a.value < b.value;
        }

        public static bool operator >(LuaFixed a, LuaFixed b)
        {
            return a.value > b.value;
        }

        public static bool operator <=(LuaFixed a, LuaFixed b)
        {
            return a.value <= b.value;
        }

        public static bool operator >=(LuaFixed a, LuaFixed b)
        {
            return a.value >= b.value;
        }

        // Math functions (exposed to Lua as Fixed.Abs(), Fixed.Min(), etc.)

        /// <summary>
        /// Absolute value
        /// Lua: Fixed.Abs(value)
        /// </summary>
        public static LuaFixed Abs(LuaFixed v)
        {
            return new LuaFixed(FixedPoint64.Abs(v.value));
        }

        /// <summary>
        /// Minimum of two values
        /// Lua: Fixed.Min(a, b)
        /// </summary>
        public static LuaFixed Min(LuaFixed a, LuaFixed b)
        {
            return new LuaFixed(FixedPoint64.Min(a.value, b.value));
        }

        /// <summary>
        /// Maximum of two values
        /// Lua: Fixed.Max(a, b)
        /// </summary>
        public static LuaFixed Max(LuaFixed a, LuaFixed b)
        {
            return new LuaFixed(FixedPoint64.Max(a.value, b.value));
        }

        /// <summary>
        /// Clamp value between min and max
        /// Lua: Fixed.Clamp(value, min, max)
        /// </summary>
        public static LuaFixed Clamp(LuaFixed v, LuaFixed min, LuaFixed max)
        {
            return new LuaFixed(FixedPoint64.Clamp(v.value, min.value, max.value));
        }

        /// <summary>
        /// Square root
        /// Lua: Fixed.Sqrt(value)
        /// </summary>
        public static LuaFixed Sqrt(LuaFixed v)
        {
            return new LuaFixed(FixedPoint64.Sqrt(v.value));
        }

        /// <summary>
        /// Power (value^exponent, integer exponent only)
        /// Lua: Fixed.Pow(value, 2)
        /// </summary>
        public static LuaFixed Pow(LuaFixed v, int exponent)
        {
            return new LuaFixed(FixedPoint64.Pow(v.value, exponent));
        }

        /// <summary>
        /// Linear interpolation
        /// Lua: Fixed.Lerp(a, b, t) -- returns a + (b - a) * t
        /// </summary>
        public static LuaFixed Lerp(LuaFixed a, LuaFixed b, LuaFixed t)
        {
            return new LuaFixed(FixedPoint64.Lerp(a.value, b.value, t.value));
        }

        /// <summary>
        /// Clamped linear interpolation (t clamped to 0-1)
        /// Lua: Fixed.LerpClamped(a, b, t)
        /// </summary>
        public static LuaFixed LerpClamped(LuaFixed a, LuaFixed b, LuaFixed t)
        {
            return new LuaFixed(FixedPoint64.LerpClamped(a.value, b.value, t.value));
        }

        /// <summary>
        /// Floor to nearest integer
        /// Lua: Fixed.Floor(value)
        /// </summary>
        public static LuaFixed Floor(LuaFixed v)
        {
            return new LuaFixed(FixedPoint64.Floor(v.value));
        }

        /// <summary>
        /// Ceiling to nearest integer
        /// Lua: Fixed.Ceiling(value)
        /// </summary>
        public static LuaFixed Ceiling(LuaFixed v)
        {
            return new LuaFixed(FixedPoint64.Ceiling(v.value));
        }

        /// <summary>
        /// Round to nearest integer
        /// Lua: Fixed.Round(value)
        /// </summary>
        public static LuaFixed Round(LuaFixed v)
        {
            return new LuaFixed(FixedPoint64.Round(v.value));
        }

        /// <summary>
        /// Get the fractional part (0 to 0.999...)
        /// Lua: Fixed.Frac(value)
        /// </summary>
        public static LuaFixed Frac(LuaFixed v)
        {
            return new LuaFixed(FixedPoint64.Frac(v.value));
        }

        /// <summary>
        /// Calculate percentage: (value / total) * 100
        /// Lua: Fixed.Percentage(value, total)
        /// </summary>
        public static LuaFixed Percentage(LuaFixed v, LuaFixed total)
        {
            return new LuaFixed(FixedPoint64.Percentage(v.value, total.value));
        }

        /// <summary>
        /// Move towards target by maximum delta
        /// Lua: Fixed.MoveTowards(current, target, maxDelta)
        /// </summary>
        public static LuaFixed MoveTowards(LuaFixed current, LuaFixed target, LuaFixed maxDelta)
        {
            return new LuaFixed(FixedPoint64.MoveTowards(current.value, target.value, maxDelta.value));
        }

        // Conversion methods

        /// <summary>
        /// Convert to integer (truncates)
        /// Lua: value:ToInt()
        /// </summary>
        public int ToInt()
        {
            return value.ToInt();
        }

        /// <summary>
        /// Convert to float (ONLY for display, never use in calculations)
        /// Lua: value:ToFloat()
        /// </summary>
        public float ToFloat()
        {
            return value.ToFloat();
        }

        // Properties for Lua access

        /// <summary>
        /// Check if value is zero
        /// Lua: value.IsZero
        /// </summary>
        public bool IsZero => value.IsZero;

        /// <summary>
        /// Check if value is positive
        /// Lua: value.IsPositive
        /// </summary>
        public bool IsPositive => value.IsPositive;

        /// <summary>
        /// Check if value is negative
        /// Lua: value.IsNegative
        /// </summary>
        public bool IsNegative => value.IsNegative;

        /// <summary>
        /// Get sign (-1, 0, or 1)
        /// Lua: value.Sign
        /// </summary>
        public int Sign => value.Sign;

        // IEquatable, IComparable implementation

        public bool Equals(LuaFixed other)
        {
            return value == other.value;
        }

        public override bool Equals(object obj)
        {
            return obj is LuaFixed other && Equals(other);
        }

        public override int GetHashCode()
        {
            return value.GetHashCode();
        }

        public int CompareTo(LuaFixed other)
        {
            return value.CompareTo(other.value);
        }

        public override string ToString()
        {
            return value.ToString();
        }
    }
}
