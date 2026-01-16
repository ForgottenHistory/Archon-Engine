using System;
using System.Collections.Generic;
using Core;
using MoonSharp.Interpreter;
using Scripting.Bindings;

namespace Scripting
{
    /// <summary>
    /// Central coordinator for Lua scripting using MoonSharp.
    /// Manages script execution, binding registration, and sandboxing.
    ///
    /// ARCHITECTURE: Scripts declare intent, C# executes deterministically.
    /// All Lua math uses LuaFixed (wrapping FixedPoint64) for multiplayer safety.
    /// Scripts query state read-only and submit commands for writes.
    /// </summary>
    public class ScriptEngine : IDisposable
    {
        private readonly Script luaScript;
        private readonly List<IScriptBinding> registeredBindings;
        private readonly ScriptSandbox sandbox;
        private bool isDisposed;

        /// <summary>
        /// Create a new script engine with sandboxed environment
        /// </summary>
        public ScriptEngine()
        {
            sandbox = new ScriptSandbox();
            registeredBindings = new List<IScriptBinding>();

            // Create sandboxed Lua script instance
            luaScript = new Script(sandbox.GetCoreModules());

            // Register built-in types
            RegisterBuiltInTypes();

            ArchonLogger.Log("ScriptEngine initialized with sandbox", "core_scripting");
        }

        /// <summary>
        /// Register built-in Lua types for deterministic math and IDs
        /// </summary>
        private void RegisterBuiltInTypes()
        {
            // Register custom types with MoonSharp
            UserData.RegisterType<Types.LuaFixed>();
            UserData.RegisterType<Types.LuaProvinceId>();
            UserData.RegisterType<Types.LuaCountryId>();

            // Create global Fixed table for factory methods
            var fixedTable = new Table(luaScript);
            fixedTable["FromInt"] = (Func<int, Types.LuaFixed>)Types.LuaFixed.FromInt;
            fixedTable["FromFraction"] = (Func<int, int, Types.LuaFixed>)Types.LuaFixed.FromFraction;
            fixedTable["Zero"] = Types.LuaFixed.Zero;
            fixedTable["One"] = Types.LuaFixed.One;
            fixedTable["Half"] = Types.LuaFixed.Half;
            luaScript.Globals["Fixed"] = fixedTable;

            // Create global ProvinceId table
            var provinceIdTable = new Table(luaScript);
            provinceIdTable["Create"] = (Func<int, Types.LuaProvinceId>)Types.LuaProvinceId.Create;
            provinceIdTable["None"] = Types.LuaProvinceId.None;
            luaScript.Globals["ProvinceId"] = provinceIdTable;

            // Create global CountryId table
            var countryIdTable = new Table(luaScript);
            countryIdTable["Create"] = (Func<int, Types.LuaCountryId>)Types.LuaCountryId.Create;
            countryIdTable["None"] = Types.LuaCountryId.None;
            luaScript.Globals["CountryId"] = countryIdTable;
        }

        /// <summary>
        /// Register a custom type with MoonSharp for Lua access
        /// </summary>
        public void RegisterType<T>()
        {
            UserData.RegisterType<T>();
            ArchonLogger.Log($"ScriptEngine: Registered type {typeof(T).Name}", "core_scripting");
        }

        /// <summary>
        /// Register a script binding that provides Lua functions
        /// </summary>
        public void RegisterBinding(IScriptBinding binding, ScriptContext context)
        {
            if (binding == null)
                throw new ArgumentNullException(nameof(binding));

            binding.Register(luaScript, context);
            registeredBindings.Add(binding);
            ArchonLogger.Log($"ScriptEngine: Registered binding {binding.BindingName}", "core_scripting");
        }

        /// <summary>
        /// Execute Lua code and return the result
        /// </summary>
        public ScriptResult Execute(string code, ScriptContext context)
        {
            if (string.IsNullOrEmpty(code))
            {
                return ScriptResult.Failure("Empty code provided");
            }

            try
            {
                // Set context in globals for bindings to access
                luaScript.Globals["__context"] = context;

                DynValue result = luaScript.DoString(code);

                return ScriptResult.Success(result);
            }
            catch (ScriptRuntimeException e)
            {
                ArchonLogger.LogWarning($"Script runtime error: {e.DecoratedMessage}", "core_scripting");
                return ScriptResult.Failure($"Runtime error: {e.DecoratedMessage}");
            }
            catch (SyntaxErrorException e)
            {
                ArchonLogger.LogWarning($"Script syntax error: {e.DecoratedMessage}", "core_scripting");
                return ScriptResult.Failure($"Syntax error: {e.DecoratedMessage}");
            }
            catch (Exception e)
            {
                ArchonLogger.LogError($"Script execution failed: {e.Message}", "core_scripting");
                return ScriptResult.Failure($"Execution error: {e.Message}");
            }
        }

        /// <summary>
        /// Evaluate a condition expression and return a LuaFixed result
        /// Used for event conditions, AI weights, etc.
        /// </summary>
        public Types.LuaFixed EvaluateCondition(string condition, ScriptContext context)
        {
            var result = Execute($"return {condition}", context);

            if (!result.IsSuccess)
            {
                ArchonLogger.LogWarning($"Condition evaluation failed: {result.ErrorMessage}", "core_scripting");
                return Types.LuaFixed.Zero;
            }

            // Try to convert result to LuaFixed
            if (result.Value.Type == DataType.UserData && result.Value.UserData.Object is Types.LuaFixed luaFixed)
            {
                return luaFixed;
            }

            // Try numeric conversion
            if (result.Value.Type == DataType.Number)
            {
                return Types.LuaFixed.FromInt((int)result.Value.Number);
            }

            // Boolean to fixed: true = 1, false = 0
            if (result.Value.Type == DataType.Boolean)
            {
                return result.Value.Boolean ? Types.LuaFixed.One : Types.LuaFixed.Zero;
            }

            return Types.LuaFixed.Zero;
        }

        /// <summary>
        /// Evaluate a boolean condition
        /// </summary>
        public bool EvaluateBoolCondition(string condition, ScriptContext context)
        {
            var result = Execute($"return {condition}", context);

            if (!result.IsSuccess)
            {
                return false;
            }

            if (result.Value.Type == DataType.Boolean)
            {
                return result.Value.Boolean;
            }

            // Lua truthiness: nil and false are false, everything else is true
            return result.Value.Type != DataType.Nil && result.Value.Type != DataType.Void;
        }

        /// <summary>
        /// Call a Lua function by name
        /// </summary>
        public ScriptResult CallFunction(string functionName, ScriptContext context, params object[] args)
        {
            try
            {
                luaScript.Globals["__context"] = context;

                DynValue function = luaScript.Globals.Get(functionName);
                if (function.Type != DataType.Function)
                {
                    return ScriptResult.Failure($"Function '{functionName}' not found");
                }

                DynValue result = luaScript.Call(function, args);
                return ScriptResult.Success(result);
            }
            catch (Exception e)
            {
                ArchonLogger.LogError($"Function call '{functionName}' failed: {e.Message}", "core_scripting");
                return ScriptResult.Failure($"Function call error: {e.Message}");
            }
        }

        /// <summary>
        /// Set a global variable in the Lua environment
        /// </summary>
        public void SetGlobal(string name, object value)
        {
            luaScript.Globals[name] = value;
        }

        /// <summary>
        /// Get a global variable from the Lua environment
        /// </summary>
        public DynValue GetGlobal(string name)
        {
            return luaScript.Globals.Get(name);
        }

        public void Dispose()
        {
            if (isDisposed)
                return;

            registeredBindings.Clear();
            isDisposed = true;

            ArchonLogger.Log("ScriptEngine disposed", "core_scripting");
        }
    }

    /// <summary>
    /// Result of script execution
    /// </summary>
    public struct ScriptResult
    {
        public bool IsSuccess { get; private set; }
        public string ErrorMessage { get; private set; }
        public DynValue Value { get; private set; }

        public static ScriptResult Success(DynValue value)
        {
            return new ScriptResult
            {
                IsSuccess = true,
                Value = value,
                ErrorMessage = null
            };
        }

        public static ScriptResult Failure(string error)
        {
            return new ScriptResult
            {
                IsSuccess = false,
                ErrorMessage = error,
                Value = DynValue.Nil
            };
        }
    }
}
