using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Lua;
using Lua.Standard;

namespace Scripting
{
    /// <summary>A C# function scripts can call. Read the arguments from the call and set what it returns.</summary>
    public delegate void ScriptFunction(ref ScriptCall call);

    /// <summary>A script did something a function cannot accept (ScriptCall.Fail), or broke an engine rule.</summary>
    public sealed class ScriptException : Exception
    {
        public ScriptException(string message) : base(message) { }
    }

    /// <summary>
    /// ENGINE: runs Lua scripts (Lua 5.2, the Lua-CSharp interpreter). One engine is one Lua world: its globals, the
    /// C# functions and object types the GAME gives it, and the scripts loaded into it.
    ///
    /// The engine provides the mechanism; the GAME decides what scripts are for:
    /// - SetFunction: a global C# function. DefineType: a kind of object with methods (a country, a province);
    ///   the GAME's objects extend ScriptObject and scripts call their methods (c:add_gold(5)).
    /// - Run / RunFile: load a script. A script usually registers things by calling the GAME's functions with
    ///   tables that hold values and Lua functions; the GAME keeps those (ScriptTable, ScriptFunctionRef).
    /// - Call: call a kept Lua function now.
    ///
    /// Rules scripts run under:
    /// - Synchronous: a call returns when the script has finished. A script cannot wait.
    /// - Sandboxed: the base, string, table, math and bit32 libraries only. No files, no os, no loading code,
    ///   no math.random (the GAME provides its own deterministic random functions). print goes to the log.
    /// - Bounded: a top-level call that runs more than InstructionLimit instructions is stopped.
    /// - Deterministic: Lua numbers are doubles. Pass numbers to C# and do the simulation's arithmetic there, in
    ///   fixed point; keep the simulation's state in C#, not in Lua variables (they are not saved).
    ///
    /// A script failing (syntax, a runtime error, a C# function throwing) never throws out of the engine: Run and
    /// Call return false, LastError has the message with the script's file and line, and Error fires.
    /// A method a type lacks does not fail the script: it is reported once (MissingMember) and returns false.
    /// The same holds for globals when the engine is made with stubMissingGlobals.
    /// No type of the Lua library is in this assembly's public API.
    /// </summary>
    public sealed class ScriptEngine : IDisposable
    {
        const int HookInterval = 100_000;       // instructions between checks of the limit

        readonly LuaState state;
        readonly string channel;
        readonly Dictionary<string, ScriptType> types = new Dictionary<string, ScriptType>();
        readonly HashSet<(string type, string member)> missing = new HashSet<(string, string)>();
        readonly LuaFunction falseFunction;
        LuaState active;        // the Lua thread a C# function is being called from
        int depth;              // nested Call depth: only the outermost reports a failure
        long instructions;
        bool disposed;

        /// <summary>Instructions one top-level Run or Call may execute (checked every 100 000). 0: no limit.</summary>
        public long InstructionLimit = 50_000_000;

        /// <summary>Whether a missing method or global is also logged as a warning, once each (default). Turn it off
        /// when content is known to be far ahead of the game (a large converted set): MissingMember and Missing
        /// still report every one.</summary>
        public bool LogMissing = true;

        /// <summary>The last failure's message, with the script's file and line. Null after a success.</summary>
        public string LastError { get; private set; }

        /// <summary>A Run or Call failed (the message). Failures are also logged as warnings.</summary>
        public event Action<string> Error;

        /// <summary>A script used a method a type lacks, or a missing global: (type name, or null for a global;
        /// member name). Raised once per pair.</summary>
        public event Action<string, string> MissingMember;

        /// <summary>How many times scripts have called something missing, in all. Read it before and after a call
        /// to know whether that call's answer leaned on a missing name (its false is then "can't tell").</summary>
        public long MissingCalls { get; private set; }

        /// <summary>Every (type, member) scripts used that was not there. Type is null for a global.</summary>
        public IReadOnlyCollection<(string type, string member)> Missing => missing;

        /// <param name="stubMissingGlobals">A global that is not defined reads as a function returning false (and is
        /// reported), where plain Lua reads nil. For scripts converted from another format, which call things the
        /// GAME may not have yet.</param>
        public ScriptEngine(bool stubMissingGlobals = false, string logChannel = "core_scripting")
        {
            channel = logChannel;
            state = LuaState.Create();
            state.OpenBasicLibrary();
            state.OpenStringLibrary();
            state.OpenTableLibrary();
            state.OpenMathLibrary();
            state.OpenBitwiseLibrary();
            foreach (string name in new[] { "dofile", "loadfile", "load", "loadstring", "require", "collectgarbage" })
                state.Environment[name] = LuaValue.Nil;
            if (state.Environment["math"].TryRead(out LuaTable math))
            {
                math["random"] = LuaValue.Nil;
                math["randomseed"] = LuaValue.Nil;
            }
            falseFunction = new LuaFunction("missing", (ctx, ct) =>
            {
                MissingCalls++;
                return new ValueTask<int>(ctx.Return(false));
            });
            SetFunction("print", Print);
            state.SetHook(new LuaFunction("instruction_limit", (ctx, ct) =>
            {
                instructions += HookInterval;
                if (InstructionLimit > 0 && instructions > InstructionLimit)
                    throw new ScriptException($"stopped after {InstructionLimit} instructions (an endless loop?)");
                return new ValueTask<int>(0);
            }), "", HookInterval);
            if (stubMissingGlobals)
            {
                var meta = new LuaTable();
                meta["__index"] = new LuaFunction("missing_global", (ctx, ct) =>
                {
                    string name = ctx.GetArgument(1).TryRead(out string s) ? s : null;
                    if (name == null) return new ValueTask<int>(ctx.Return(LuaValue.Nil));
                    ReportMissing(null, name);
                    state.Environment[name] = falseFunction;    // found from now on
                    return new ValueTask<int>(ctx.Return(falseFunction));
                });
                state.Environment.Metatable = meta;
            }
        }

        // ---- what scripts can use --------------------------------------------------------------------------

        /// <summary>A global C# function.</summary>
        public void SetFunction(string name, ScriptFunction function)
        {
            state.Environment[name] = Wrap(name, function);
        }

        /// <summary>A global value (a number, a boolean, a string, an object).</summary>
        public void SetGlobal(string name, ScriptValue value)
        {
            state.Environment[name] = value.Raw;
        }

        /// <summary>A global as it is (nil when not defined, also with stubMissingGlobals).</summary>
        public ScriptValue GetGlobal(string name)
        {
            return new ScriptValue(state.Environment[name]);
        }

        /// <summary>A new kind of object. Give it methods with ScriptType.SetMethod; its objects extend
        /// ScriptObject.</summary>
        public ScriptType DefineType(string name)
        {
            if (types.ContainsKey(name)) throw new ArgumentException($"Script type '{name}' is already defined");
            var type = new ScriptType(this, name);
            types[name] = type;
            return type;
        }

        // ---- running ---------------------------------------------------------------------------------------

        /// <summary>Run a script file. Errors name the file (its name, without the directory).</summary>
        public bool RunFile(string path)
        {
            string source;
            try { source = File.ReadAllText(path); }
            catch (Exception e) { return Fail($"{path}: {e.Message}"); }
            return Run(source, Path.GetFileName(path));
        }

        /// <summary>Run script source. chunkName is what errors call it ("events/harvest.lua").</summary>
        public bool Run(string source, string chunkName)
        {
            LuaValue chunk;
            try { chunk = state.Load(source, "@" + chunkName); }
            catch (Exception e) { return Fail(e.Message); }
            return Invoke(chunk, default, default, 0, out _);
        }

        /// <summary>Call a Lua function. False when it failed (LastError); result is then nil.</summary>
        public bool Call(ScriptFunctionRef function, out ScriptValue result)
        {
            return Invoke(function.Raw, default, default, 0, out result);
        }

        public bool Call(ScriptFunctionRef function, ScriptValue argument, out ScriptValue result)
        {
            return Invoke(function.Raw, argument.Raw, default, 1, out result);
        }

        public bool Call(ScriptFunctionRef function, ScriptValue first, ScriptValue second, out ScriptValue result)
        {
            return Invoke(function.Raw, first.Raw, second.Raw, 2, out result);
        }

        /// <summary>Call a Lua function for a yes or no (Lua's truth: everything but nil and false).
        /// onError is the answer when it failed.</summary>
        public bool CallBool(ScriptFunctionRef function, ScriptValue argument, bool onError = false)
        {
            return Invoke(function.Raw, argument.Raw, default, 1, out var result) ? result.Truthy : onError;
        }

        /// <summary>Call a Lua function for a number. onError is the answer when it failed or returned no number.</summary>
        public double CallNumber(ScriptFunctionRef function, ScriptValue argument, double onError = 0)
        {
            return Invoke(function.Raw, argument.Raw, default, 1, out var result) && result.TryNumber(out double n) ? n : onError;
        }

        bool Invoke(LuaValue function, LuaValue first, LuaValue second, int count, out ScriptValue result)
        {
            result = default;
            if (disposed) return Fail("the script engine is disposed");
            if (function.Type != LuaValueType.Function) return depth == 0 ? Fail("not a function") : throw new ScriptException("not a function");
            var thread = active ?? state;
            int stackBase = thread.Stack.Count;
            bool outermost = depth == 0;
            if (outermost) instructions = 0;
            depth++;
            try
            {
                thread.Push(function);
                if (count > 0) thread.Push(first);
                if (count > 1) thread.Push(second);
                var task = thread.CallAsync(stackBase, stackBase);
                if (!task.IsCompleted) throw new ScriptException("the script waited on something: scripts run synchronously");
                int results = task.Result;
                if (results > 0) result = new ScriptValue(thread.Stack[stackBase]);
                thread.Stack.PopUntil(stackBase);
                if (outermost) LastError = null;
                return true;
            }
            catch (Exception e) when (outermost)    // an inner call's failure goes on up to the outermost
            {
                thread.Stack.PopUntil(stackBase);
                active = null;
                result = default;
                return Fail(Describe(e));
            }
            finally
            {
                depth--;
            }
        }

        // ---- inside ----------------------------------------------------------------------------------------

        internal LuaFunction Wrap(string name, ScriptFunction function)
        {
            if (function == null) throw new ArgumentNullException(nameof(function));
            return new LuaFunction(name, (ctx, ct) =>
            {
                var call = new ScriptCall(this, ctx);
                var previous = active;
                active = ctx.State;
                try { function(ref call); }
                finally { active = previous; }
                return new ValueTask<int>(call.Finish());
            });
        }

        /// <summary>The metatable that makes a missing key of a type's methods a reported function returning false.</summary>
        internal LuaTable MissingMethods(ScriptType type)
        {
            var meta = new LuaTable();
            meta["__index"] = new LuaFunction("missing_method", (ctx, ct) =>
            {
                string name = ctx.GetArgument(1).TryRead(out string s) ? s : null;
                if (name == null) return new ValueTask<int>(ctx.Return(LuaValue.Nil));
                ReportMissing(type.Name, name);
                type.Methods[name] = falseFunction;    // found from now on
                return new ValueTask<int>(ctx.Return(falseFunction));
            });
            return meta;
        }

        void ReportMissing(string type, string member)
        {
            if (!missing.Add((type, member))) return;
            if (LogMissing) ArchonLogger.LogWarning(type == null ? $"Script: no global '{member}' (reads as a function returning false)"
                                                 : $"Script: {type} has no method '{member}' (returns false)", channel);
            MissingMember?.Invoke(type, member);
        }

        /// <summary>A failure's message. A Lua error already names its file and line; a C# function's exception
        /// gets the script's traceback added.</summary>
        static string Describe(Exception e)
        {
            if (e is LuaRuntimeException runtime && runtime.InnerException != null && runtime.LuaTraceback != null)
                return e.Message + "\n" + runtime.LuaTraceback;
            return e.Message;
        }

        bool Fail(string message)
        {
            LastError = message;
            ArchonLogger.LogWarning($"Script error: {message}", channel);
            Error?.Invoke(message);
            return false;
        }

        void Print(ref ScriptCall call)
        {
            var text = new StringBuilder();
            for (int i = 0; i < call.Count; i++)
            {
                if (i > 0) text.Append('\t');
                text.Append(call[i].ToString());
            }
            ArchonLogger.Log(text.ToString(), channel);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            state.Dispose();
        }
    }
}
