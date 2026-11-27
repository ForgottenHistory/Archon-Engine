using System;
using System.Collections.Generic;
using Lua;

namespace Scripting
{
    /// <summary>
    /// A kind of object scripts can hold and call methods on (ScriptEngine.DefineType): a country, a province.
    /// A method is a C# function whose first argument is the object: c:add_gold(5) calls "add_gold" with (c, 5).
    /// A method the type lacks is reported once (ScriptEngine.MissingMember) and returns false.
    /// </summary>
    public sealed class ScriptType
    {
        readonly ScriptEngine engine;
        readonly HashSet<string> names = new HashSet<string>();
        internal readonly LuaTable Meta = new LuaTable();
        internal readonly LuaTable Methods = new LuaTable();

        public string Name { get; }

        /// <summary>The methods it has.</summary>
        public IReadOnlyCollection<string> MethodNames => names;

        internal ScriptType(ScriptEngine engine, string name)
        {
            this.engine = engine;
            Name = name;
            Meta["__index"] = Methods;
            Meta["__metatable"] = false;        // scripts cannot reach or replace it
            Methods.Metatable = engine.MissingMethods(this);
        }

        public void SetMethod(string name, ScriptFunction method)
        {
            Methods[name] = engine.Wrap(name, method);
            names.Add(name);
        }

        public bool HasMethod(string name) => names.Contains(name);
    }

    /// <summary>
    /// Base of the GAME's objects scripts can hold. Two values are the same object in Lua (==, table keys) when
    /// they are the same C# object, so keep one object per thing (one per country) and hand that out.
    /// </summary>
    public abstract class ScriptObject
    {
        internal readonly LuaValue Value;

        public ScriptType Type { get; }

        protected ScriptObject(ScriptType type)
        {
            Type = type ?? throw new ArgumentNullException(nameof(type));
            Value = LuaValue.FromUserData(new ScriptHandle(this, type.Meta));
        }
    }

    /// <summary>What Lua holds for a ScriptObject.</summary>
    internal sealed class ScriptHandle : ILuaUserData
    {
        public readonly ScriptObject Target;

        public LuaTable Metatable { get; set; }

        public Span<LuaValue> UserValues => default;

        public ScriptHandle(ScriptObject target, LuaTable metatable)
        {
            Target = target;
            Metatable = metatable;
        }
    }
}
