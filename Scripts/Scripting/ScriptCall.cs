using System.Collections.Generic;
using Lua;

namespace Scripting
{
    /// <summary>
    /// One call of a C# function from a script: its arguments, and what it returns (nothing unless Return is
    /// called; the last Return counts). Arguments count from 0. In a method, argument 0 is the object:
    /// c:add_gold(5) has Self at 0 and 5 at 1. An argument that is missing or of another kind reads as the
    /// fallback (or null), so a function decides itself what it requires; Fail stops the script with a message.
    /// </summary>
    public ref struct ScriptCall
    {
        readonly LuaFunctionExecutionContext context;
        int results;

        public ScriptEngine Engine { get; }

        internal ScriptCall(ScriptEngine engine, LuaFunctionExecutionContext context)
        {
            Engine = engine;
            this.context = context;
            results = -1;
        }

        /// <summary>Arguments passed.</summary>
        public int Count => context.ArgumentCount;

        public ScriptValue this[int index] => index >= 0 && index < context.ArgumentCount ? new ScriptValue(context.Arguments[index]) : default;

        /// <summary>Whether the argument is there and not nil.</summary>
        public bool Has(int index) => !this[index].IsNil;

        public double Number(int index, double fallback = 0) => this[index].Number(fallback);

        public string String(int index, string fallback = null) => this[index].String ?? fallback;

        /// <summary>The argument's truth; fallback when it is not there. (c:is_at_war() and c:is_at_war(false).)</summary>
        public bool Bool(int index, bool fallback = true)
        {
            var value = this[index];
            return value.IsNil ? fallback : value.Truthy;
        }

        public T Object<T>(int index) where T : ScriptObject => this[index].Object<T>();

        /// <summary>The object a method was called on (argument 0).</summary>
        public T Self<T>() where T : ScriptObject => this[0].Object<T>();

        public ScriptTable Table(int index) => this[index].Table;

        public ScriptFunctionRef Function(int index) => this[index].Function;

        public void Return() { results = context.Return(); }

        public void Return(ScriptValue value) { results = context.Return(value.Raw); }

        public void Return(bool value) { results = context.Return(value); }

        public void Return(double value) { results = context.Return(value); }

        public void Return(string value) { results = context.Return(value == null ? LuaValue.Nil : value); }

        /// <summary>The object, or nil for null.</summary>
        public void Return(ScriptObject value) { results = context.Return(value == null ? LuaValue.Nil : value.Value); }

        /// <summary>A Lua list of the objects, in their order (for scripts to loop over with ipairs).</summary>
        public void ReturnList<T>(IReadOnlyList<T> items) where T : ScriptObject
        {
            var table = new LuaTable(items.Count, 0);
            for (int i = 0; i < items.Count; i++) table[i + 1] = items[i].Value;
            results = context.Return(table);
        }

        /// <summary>Stop the script with an error (the script's file and line are added).</summary>
        public void Fail(string message) => throw new ScriptException(message);

        internal int Finish() => results >= 0 ? results : context.Return();
    }
}
