using System;
using Core;
using Core.Data.Ids;
using MoonSharp.Interpreter;

namespace Scripting.Types
{
    /// <summary>
    /// Lua-compatible wrapper for CountryId.
    /// Provides type-safe country identification in scripts.
    ///
    /// Usage in Lua:
    ///   local country = CountryId.Create(1)
    ///   if country:IsValid() then
    ///       local gold = country_gold(country)
    ///   end
    /// </summary>
    [MoonSharpUserData]
    public struct LuaCountryId : IEquatable<LuaCountryId>
    {
        private readonly CountryId value;

        /// <summary>
        /// Represents no country (invalid ID, unowned)
        /// </summary>
        public static readonly LuaCountryId None = new LuaCountryId(CountryId.None);

        /// <summary>
        /// Internal constructor from CountryId
        /// </summary>
        internal LuaCountryId(CountryId id)
        {
            value = id;
        }

        /// <summary>
        /// Get the underlying CountryId
        /// </summary>
        public CountryId Value => value;

        /// <summary>
        /// Get the raw ushort value
        /// </summary>
        public int RawValue => value.Value;

        /// <summary>
        /// Create a country ID from integer
        /// Lua: CountryId.Create(1)
        /// </summary>
        public static LuaCountryId Create(int id)
        {
            if (id < 0 || id > ushort.MaxValue)
            {
                ArchonLogger.LogWarning($"LuaCountryId.Create: Invalid ID {id}, returning None", "core_scripting");
                return None;
            }
            return new LuaCountryId(new CountryId((ushort)id));
        }

        /// <summary>
        /// Create from existing CountryId (for C# interop)
        /// </summary>
        internal static LuaCountryId FromCountryId(CountryId id)
        {
            return new LuaCountryId(id);
        }

        /// <summary>
        /// Check if this is a valid country ID
        /// Lua: country:IsValid()
        /// </summary>
        public bool IsValid()
        {
            return value.IsValid;
        }

        /// <summary>
        /// Check if this is the None value
        /// Lua: country:IsNone()
        /// </summary>
        public bool IsNone()
        {
            return !value.IsValid;
        }

        // Equality operators

        public static bool operator ==(LuaCountryId a, LuaCountryId b)
        {
            return a.value == b.value;
        }

        public static bool operator !=(LuaCountryId a, LuaCountryId b)
        {
            return a.value != b.value;
        }

        public bool Equals(LuaCountryId other)
        {
            return value == other.value;
        }

        public override bool Equals(object obj)
        {
            return obj is LuaCountryId other && Equals(other);
        }

        public override int GetHashCode()
        {
            return value.GetHashCode();
        }

        public override string ToString()
        {
            return value.ToString();
        }
    }
}
