using System;
using Core;
using Core.Data.Ids;
using MoonSharp.Interpreter;
using Scripting.Types;

namespace Scripting.Bindings
{
    /// <summary>
    /// Core ENGINE bindings for province and country queries.
    /// Provides read-only access to basic game state.
    ///
    /// These bindings are engine-level - they work with any game built on Archon.
    /// Game-specific bindings (economy, buildings, etc.) are in the GAME layer.
    ///
    /// Available in Lua:
    ///   province_owner(province_id) -> CountryId
    ///   province_controller(province_id) -> CountryId
    ///   province_is_valid(province_id) -> bool
    ///   country_is_valid(country_id) -> bool
    ///   country_province_count(country_id) -> int
    ///   this_province -> ProvinceId (scope)
    ///   this_country -> CountryId (scope)
    /// </summary>
    public class CoreBindings : IScriptBinding
    {
        public string BindingName => "Core";

        public void Register(Script luaScript, ScriptContext context)
        {
            // Province queries
            luaScript.Globals["province_owner"] = (Func<LuaProvinceId, LuaCountryId>)(provinceId =>
                GetProvinceOwner(context, provinceId));

            luaScript.Globals["province_controller"] = (Func<LuaProvinceId, LuaCountryId>)(provinceId =>
                GetProvinceController(context, provinceId));

            luaScript.Globals["province_is_valid"] = (Func<LuaProvinceId, bool>)(provinceId =>
                IsProvinceValid(context, provinceId));

            // Country queries
            luaScript.Globals["country_is_valid"] = (Func<LuaCountryId, bool>)(countryId =>
                IsCountryValid(context, countryId));

            luaScript.Globals["country_province_count"] = (Func<LuaCountryId, int>)(countryId =>
                GetCountryProvinceCount(context, countryId));

            // Scope accessors (this_province, this_country)
            // These return the current scope from context
            luaScript.Globals["this_province"] = DynValue.NewCallback((ctx, args) =>
            {
                var scriptContext = luaScript.Globals["__context"] as ScriptContext;
                if (scriptContext?.ScopeProvinceId != null)
                {
                    return DynValue.FromObject(luaScript, LuaProvinceId.FromProvinceId(scriptContext.ScopeProvinceId.Value));
                }
                return DynValue.FromObject(luaScript, LuaProvinceId.None);
            });

            luaScript.Globals["this_country"] = DynValue.NewCallback((ctx, args) =>
            {
                var scriptContext = luaScript.Globals["__context"] as ScriptContext;
                if (scriptContext?.ScopeCountryId != null)
                {
                    return DynValue.FromObject(luaScript, LuaCountryId.FromCountryId(scriptContext.ScopeCountryId.Value));
                }
                return DynValue.FromObject(luaScript, LuaCountryId.None);
            });

            // Game time queries
            luaScript.Globals["current_tick"] = DynValue.NewCallback((ctx, args) =>
            {
                var scriptContext = luaScript.Globals["__context"] as ScriptContext;
                return DynValue.NewNumber(scriptContext?.CurrentTick ?? 0);
            });

            ArchonLogger.Log("CoreBindings registered", "core_scripting");
        }

        private static LuaCountryId GetProvinceOwner(ScriptContext context, LuaProvinceId provinceId)
        {
            if (context?.GameState?.Provinces == null)
            {
                return LuaCountryId.None;
            }

            if (!provinceId.IsValid())
            {
                return LuaCountryId.None;
            }

            ushort ownerId = context.GameState.GetProvinceOwner(provinceId.Value.Value);
            return LuaCountryId.FromCountryId(new CountryId(ownerId));
        }

        private static LuaCountryId GetProvinceController(ScriptContext context, LuaProvinceId provinceId)
        {
            if (context?.GameState?.Provinces == null)
            {
                return LuaCountryId.None;
            }

            if (!provinceId.IsValid())
            {
                return LuaCountryId.None;
            }

            // Get controller from province state
            var state = context.GameState.Provinces.GetProvinceState(provinceId.Value.Value);
            return LuaCountryId.FromCountryId(new CountryId(state.controllerID));
        }

        private static bool IsProvinceValid(ScriptContext context, LuaProvinceId provinceId)
        {
            if (context?.GameState?.Provinces == null)
            {
                return false;
            }

            if (!provinceId.IsValid())
            {
                return false;
            }

            return provinceId.Value.Value < context.GameState.Provinces.ProvinceCount;
        }

        private static bool IsCountryValid(ScriptContext context, LuaCountryId countryId)
        {
            if (context?.GameState?.Countries == null)
            {
                return false;
            }

            if (!countryId.IsValid())
            {
                return false;
            }

            return countryId.Value.Value < context.GameState.Countries.CountryCount;
        }

        private static int GetCountryProvinceCount(ScriptContext context, LuaCountryId countryId)
        {
            if (context?.GameState?.Provinces == null)
            {
                return 0;
            }

            if (!countryId.IsValid())
            {
                return 0;
            }

            // Count provinces owned by this country
            int count = 0;
            int provinceCount = context.GameState.Provinces.ProvinceCount;
            for (int i = 1; i < provinceCount; i++)
            {
                if (context.GameState.GetProvinceOwner((ushort)i) == countryId.Value.Value)
                {
                    count++;
                }
            }
            return count;
        }
    }
}
