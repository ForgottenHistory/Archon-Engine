using System;

namespace Core.Commands
{
    /// <summary>
    /// Marks a property as a command argument for auto-parsing and serialization.
    ///
    /// Supported types:
    /// - Primitives: int, float, double, bool, byte, ushort, uint, long
    /// - Strings: string
    /// - Fixed-point: FixedPoint64 (parsed from float)
    ///
    /// Usage:
    /// [Arg(0, "amount")]
    /// public int Amount { get; set; }
    ///
    /// [Arg(1, "target", Optional = true)]
    /// public ushort TargetId { get; set; }
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class ArgAttribute : Attribute
    {
        /// <summary>
        /// Positional index in the argument list (0-based)
        /// </summary>
        public int Position { get; }

        /// <summary>
        /// Display name for help text (e.g., "amount", "provinceId")
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// If true, argument can be omitted. Property keeps default value.
        /// </summary>
        public bool Optional { get; set; }

        /// <summary>
        /// Description for help text
        /// </summary>
        public string Description { get; set; }

        public ArgAttribute(int position, string name = null)
        {
            Position = position;
            Name = name;
            Optional = false;
        }
    }
}
