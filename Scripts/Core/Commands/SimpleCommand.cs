using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Core.Data;

namespace Core.Commands
{
    /// <summary>
    /// Simplified command base class with auto-serialization.
    /// Use [Command] attribute on class and [Arg] attributes on properties.
    ///
    /// Benefits over BaseCommand:
    /// - No separate factory class needed
    /// - Auto-serialization of [Arg] properties
    /// - Auto-generated usage/help text
    /// - Optional Undo (default no-op)
    ///
    /// Use BaseCommand instead when you need:
    /// - Custom serialization format
    /// - Complex undo logic
    /// - Maximum performance (no reflection)
    /// </summary>
    public abstract class SimpleCommand : BaseCommand
    {
        // Cached property info for serialization (per type)
        private static readonly Dictionary<Type, ArgPropertyInfo[]> PropertyCache = new();

        /// <summary>
        /// Override to implement validation logic.
        /// </summary>
        public abstract override bool Validate(GameState gameState);

        /// <summary>
        /// Override to implement execution logic.
        /// </summary>
        public abstract override void Execute(GameState gameState);

        /// <summary>
        /// Override to implement undo logic. Default is no-op.
        /// </summary>
        public override void Undo(GameState gameState)
        {
            // Default: no undo support
            // Override if undo is needed
        }

        /// <summary>
        /// Auto-serializes all [Arg] properties.
        /// </summary>
        public sealed override void Serialize(BinaryWriter writer)
        {
            var props = GetArgProperties();
            foreach (var prop in props)
            {
                WriteProperty(writer, prop.Property, prop.Property.GetValue(this));
            }
        }

        /// <summary>
        /// Auto-deserializes all [Arg] properties.
        /// </summary>
        public sealed override void Deserialize(BinaryReader reader)
        {
            var props = GetArgProperties();
            foreach (var prop in props)
            {
                var value = ReadProperty(reader, prop.Property.PropertyType);
                prop.Property.SetValue(this, value);
            }
        }

        private ArgPropertyInfo[] GetArgProperties()
        {
            var type = GetType();
            if (!PropertyCache.TryGetValue(type, out var props))
            {
                props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Select(p => new { Property = p, Attr = p.GetCustomAttribute<ArgAttribute>() })
                    .Where(x => x.Attr != null)
                    .OrderBy(x => x.Attr.Position)
                    .Select(x => new ArgPropertyInfo { Property = x.Property, Attribute = x.Attr })
                    .ToArray();
                PropertyCache[type] = props;
            }
            return props;
        }

        private void WriteProperty(BinaryWriter writer, PropertyInfo prop, object value)
        {
            var type = prop.PropertyType;

            if (type == typeof(int)) writer.Write((int)value);
            else if (type == typeof(uint)) writer.Write((uint)value);
            else if (type == typeof(short)) writer.Write((short)value);
            else if (type == typeof(ushort)) writer.Write((ushort)value);
            else if (type == typeof(byte)) writer.Write((byte)value);
            else if (type == typeof(sbyte)) writer.Write((sbyte)value);
            else if (type == typeof(long)) writer.Write((long)value);
            else if (type == typeof(ulong)) writer.Write((ulong)value);
            else if (type == typeof(float)) writer.Write((float)value);
            else if (type == typeof(double)) writer.Write((double)value);
            else if (type == typeof(bool)) writer.Write((bool)value);
            else if (type == typeof(string)) writer.Write((string)value ?? "");
            else if (type == typeof(FixedPoint64))
            {
                var fp = (FixedPoint64)value;
                writer.Write(fp.RawValue);
            }
            else
            {
                throw new NotSupportedException($"SimpleCommand: Unsupported property type {type.Name} on {prop.Name}");
            }
        }

        private object ReadProperty(BinaryReader reader, Type type)
        {
            if (type == typeof(int)) return reader.ReadInt32();
            if (type == typeof(uint)) return reader.ReadUInt32();
            if (type == typeof(short)) return reader.ReadInt16();
            if (type == typeof(ushort)) return reader.ReadUInt16();
            if (type == typeof(byte)) return reader.ReadByte();
            if (type == typeof(sbyte)) return reader.ReadSByte();
            if (type == typeof(long)) return reader.ReadInt64();
            if (type == typeof(ulong)) return reader.ReadUInt64();
            if (type == typeof(float)) return reader.ReadSingle();
            if (type == typeof(double)) return reader.ReadDouble();
            if (type == typeof(bool)) return reader.ReadBoolean();
            if (type == typeof(string)) return reader.ReadString();
            if (type == typeof(FixedPoint64)) return FixedPoint64.FromRaw(reader.ReadInt64());

            throw new NotSupportedException($"SimpleCommand: Unsupported property type {type.Name}");
        }

        private struct ArgPropertyInfo
        {
            public PropertyInfo Property;
            public ArgAttribute Attribute;
        }
    }
}
