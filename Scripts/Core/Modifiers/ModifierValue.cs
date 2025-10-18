using System.Runtime.InteropServices;

namespace Core.Modifiers
{
    /// <summary>
    /// ENGINE: Modifier value with additive and multiplicative components
    /// Pattern used by: EU4, CK3, Stellaris, Victoria 3
    ///
    /// Formula: (base + additive) * (1 + multiplicative)
    /// Example: base=10, additive=+5, multiplicative=+0.5 → (10+5)*(1+0.5) = 22.5
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct ModifierValue
    {
        public float Additive;           // Flat bonus (e.g., +5 production)
        public float Multiplicative;     // Percentage bonus (e.g., +50% = 0.5)

        /// <summary>
        /// Apply this modifier to a base value
        /// Formula: (base + additive) * (1 + multiplicative)
        /// </summary>
        public float Apply(float baseValue)
        {
            return (baseValue + Additive) * (1.0f + Multiplicative);
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
            return $"[Add: {Additive:+0.0;-0.0}, Mult: {Multiplicative:+0.0%;-0.0%}]";
        }
    }
}
