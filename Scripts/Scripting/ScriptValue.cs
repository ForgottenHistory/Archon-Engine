using System;
using System.Collections.Generic;
using Lua;

namespace Scripting
{
    public enum ScriptValueKind : byte { Nil, Boolean, Number, String, Function, Table, Object, Other }

    /// <summary>A value passing between C# and a script: nil, a boolean, a number, a string, a Lua function,
    /// a Lua table or a GAME object (ScriptObject). C# booleans, numbers, strings and objects convert to it.</summary>
    public readonly struct ScriptValue
    {
        internal readonly LuaValue Raw;

        internal ScriptValue(LuaValue raw) { Raw = raw; }

        public static ScriptValue Nil => default;

        public static implicit operator ScriptValue(bool value) => new ScriptValue(value);
        public static implicit operator ScriptValue(double value) => new ScriptValue(value);
        public static implicit operator ScriptValue(string value) => value == null ? default : new ScriptValue(value);
        public static implicit operator ScriptValue(ScriptObject value) => value == null ? default : new ScriptValue(value.Value);

        public ScriptValueKind Kind
        {
            get
            {
                switch (Raw.Type)
                {
                    case LuaValueType.Nil: return ScriptValueKind.Nil;
                    case LuaValueType.Boolean: return ScriptValueKind.Boolean;
                    case LuaValueType.Number: return ScriptValueKind.Number;
                    case LuaValueType.String: return ScriptValueKind.String;
                    case LuaValueType.Function: return ScriptValueKind.Function;
                    case LuaValueType.Table: return ScriptValueKind.Table;
                    case LuaValueType.UserData: return Raw.TryRead(out ScriptHandle _) ? ScriptValueKind.Object : ScriptValueKind.Other;
                    default: return ScriptValueKind.Other;
                }
            }
        }

        public bool IsNil => Raw.Type == LuaValueType.Nil;

        /// <summary>Lua's truth: everything but nil and false.</summary>
        public bool Truthy => Raw.ToBoolean();

        public bool TryNumber(out double number)
        {
            if (Raw.Type == LuaValueType.Number) return Raw.TryRead(out number);
            number = 0;
            return false;
        }

        public double Number(double fallback = 0) => TryNumber(out double n) ? n : fallback;

        /// <summary>The string, or null when it is not one (a number is not converted).</summary>
        public string String => Raw.Type == LuaValueType.String && Raw.TryRead(out string s) ? s : null;

        /// <summary>The GAME object of that class, or null.</summary>
        public T Object<T>() where T : ScriptObject
        {
            return Raw.Type == LuaValueType.UserData && Raw.TryRead(out ScriptHandle handle) ? handle.Target as T : null;
        }

        /// <summary>The table (IsValid false when it is not one).</summary>
        public ScriptTable Table => Raw.Type == LuaValueType.Table && Raw.TryRead(out LuaTable t) ? new ScriptTable(t) : default;

        /// <summary>The Lua function (IsValid false when it is not one).</summary>
        public ScriptFunctionRef Function => Raw.Type == LuaValueType.Function ? new ScriptFunctionRef(Raw) : default;

        public override string ToString() => Raw.ToString();
    }

    /// <summary>A Lua function the GAME keeps to call later (ScriptEngine.Call). It stays valid while its engine does.</summary>
    public readonly struct ScriptFunctionRef
    {
        internal readonly LuaValue Raw;

        internal ScriptFunctionRef(LuaValue raw) { Raw = raw; }

        public bool IsValid => Raw.Type == LuaValueType.Function;
    }

    /// <summary>A Lua table, read from C#: fields by name ({ id = "x", cost = 10 }) and a list by position, from 1
    /// ({ "a", "b" }).</summary>
    public readonly struct ScriptTable
    {
        internal readonly LuaTable Raw;

        internal ScriptTable(LuaTable raw) { Raw = raw; }

        public bool IsValid => Raw != null;

        /// <summary>Items in the list part.</summary>
        public int Length => Raw == null ? 0 : Raw.ArrayLength;

        public ScriptValue this[string key] => Raw == null ? default : new ScriptValue(Raw[key]);

        /// <summary>The list's item at a position, from 1.</summary>
        public ScriptValue this[int index] => Raw == null ? default : new ScriptValue(Raw[index]);

        public bool Has(string key) => Raw != null && Raw[key].Type != LuaValueType.Nil;

        public double Number(string key, double fallback = 0) => this[key].Number(fallback);

        public string String(string key, string fallback = null) => this[key].String ?? fallback;

        /// <summary>A field's truth; fallback when the field is not there.</summary>
        public bool Bool(string key, bool fallback = false)
        {
            var value = this[key];
            return value.IsNil ? fallback : value.Truthy;
        }

        public ScriptTable Table(string key) => this[key].Table;

        public ScriptFunctionRef Function(string key) => this[key].Function;

        /// <summary>The names of its fields, added to the list in ordinal order (a Lua table has no order of its own,
        /// and the simulation needs one that every machine agrees on).</summary>
        public void FieldNames(List<string> into)
        {
            if (Raw == null) return;
            int start = into.Count;
            foreach (var pair in Raw)
                if (pair.Key.Type == LuaValueType.String && pair.Key.TryRead(out string name)) into.Add(name);
            into.Sort(start, into.Count - start, StringComparer.Ordinal);
        }
    }
}
