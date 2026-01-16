using System;
using Core;
using Core.Data.Ids;
using MoonSharp.Interpreter;

namespace Scripting.Types
{
    /// <summary>
    /// Lua-compatible wrapper for ProvinceId.
    /// Provides type-safe province identification in scripts.
    ///
    /// Usage in Lua:
    ///   local province = ProvinceId.Create(42)
    ///   if province:IsValid() then
    ///       local owner = province_owner(province)
    ///   end
    /// </summary>
    [MoonSharpUserData]
    public struct LuaProvinceId : IEquatable<LuaProvinceId>
    {
        private readonly ProvinceId value;

        /// <summary>
        /// Represents no province (invalid ID)
        /// </summary>
        public static readonly LuaProvinceId None = new LuaProvinceId(ProvinceId.None);

        /// <summary>
        /// Internal constructor from ProvinceId
        /// </summary>
        internal LuaProvinceId(ProvinceId id)
        {
            value = id;
        }

        /// <summary>
        /// Get the underlying ProvinceId
        /// </summary>
        public ProvinceId Value => value;

        /// <summary>
        /// Get the raw ushort value
        /// </summary>
        public int RawValue => value.Value;

        /// <summary>
        /// Create a province ID from integer
        /// Lua: ProvinceId.Create(42)
        /// </summary>
        public static LuaProvinceId Create(int id)
        {
            if (id < 0 || id > ushort.MaxValue)
            {
                ArchonLogger.LogWarning($"LuaProvinceId.Create: Invalid ID {id}, returning None", "core_scripting");
                return None;
            }
            return new LuaProvinceId(new ProvinceId((ushort)id));
        }

        /// <summary>
        /// Create from existing ProvinceId (for C# interop)
        /// </summary>
        internal static LuaProvinceId FromProvinceId(ProvinceId id)
        {
            return new LuaProvinceId(id);
        }

        /// <summary>
        /// Check if this is a valid province ID
        /// Lua: province:IsValid()
        /// </summary>
        public bool IsValid()
        {
            return value.IsValid;
        }

        /// <summary>
        /// Check if this is the None value
        /// Lua: province:IsNone()
        /// </summary>
        public bool IsNone()
        {
            return !value.IsValid;
        }

        // Equality operators

        public static bool operator ==(LuaProvinceId a, LuaProvinceId b)
        {
            return a.value == b.value;
        }

        public static bool operator !=(LuaProvinceId a, LuaProvinceId b)
        {
            return a.value != b.value;
        }

        public bool Equals(LuaProvinceId other)
        {
            return value == other.value;
        }

        public override bool Equals(object obj)
        {
            return obj is LuaProvinceId other && Equals(other);
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
