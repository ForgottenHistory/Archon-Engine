using System.Runtime.InteropServices;
using Core.Data;

namespace Core.Modifiers
{
    /// <summary>
    /// ENGINE: Modifier value with additive and multiplicative components
    /// Pattern used by: EU4, CK3, Stellaris, Victoria 3
    ///
    /// Formula: (base + additive) * (1 + multiplicative)
    /// Example: base=10, additive=+5, multiplicative=+0.5 → (10+5)*(1+0.5) = 22.5
    ///
    /// DETERMINISM: Uses FixedPoint64 for cross-platform multiplayer compatibility
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct ModifierValue
    {
        public FixedPoint64 Additive;           // Flat bonus (e.g., +5 production)
        public FixedPoint64 Multiplicative;     // Percentage bonus (e.g., +50% = 0.5)

        /// <summary>
        /// Apply this modifier to a base value
        /// Formula: (base + additive) * (1 + multiplicative)
        /// </summary>
        public FixedPoint64 Apply(FixedPoint64 baseValue)
        {
            return (baseValue + Additive) * (FixedPoint64.One + Multiplicative);
        }

        /// <summary>
        /// Combine two modifier values (stacking)
        /// </summary>
        public static ModifierValue operator +(ModifierValue a, ModifierValue b)
        {
            return new ModifierValue
            {
                Additive = a.Additive + b.Additive,
                Multiplicative = a.Multiplicative + b.Multiplicative
            };
        }

        public override string ToString()
        {
            return $"[Add: {Additive.ToFloat():+0.0;-0.0}, Mult: {Multiplicative.ToFloat():+0.0%;-0.0%}]";
        }
    }
}
