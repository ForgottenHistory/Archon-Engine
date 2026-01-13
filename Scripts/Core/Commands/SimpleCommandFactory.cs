using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Core.Data;
using Utils;

namespace Core.Commands
{
    /// <summary>
    /// Auto-generates ICommandFactory wrappers for SimpleCommand classes.
    /// Handles argument parsing based on [Arg] attributes.
    /// </summary>
    public class SimpleCommandFactory : ICommandFactory
    {
        private readonly Type commandType;
        private readonly ArgInfo[] args;
        private readonly CommandAttribute metadata;

        public CommandAttribute Metadata => metadata;

        private SimpleCommandFactory(Type commandType, CommandAttribute metadata, ArgInfo[] args)
        {
            this.commandType = commandType;
            this.metadata = metadata;
            this.args = args;
        }

        /// <summary>
        /// Create a factory for a SimpleCommand type.
        /// </summary>
        public static SimpleCommandFactory Create(Type commandType)
        {
            if (!typeof(SimpleCommand).IsAssignableFrom(commandType))
                throw new ArgumentException($"{commandType.Name} must extend SimpleCommand");

            var metadata = commandType.GetCustomAttribute<CommandAttribute>();
            if (metadata == null)
                throw new ArgumentException($"{commandType.Name} must have [Command] attribute");

            // Get [Arg] properties sorted by position
            var args = commandType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(p => new { Property = p, Attr = p.GetCustomAttribute<ArgAttribute>() })
                .Where(x => x.Attr != null)
                .OrderBy(x => x.Attr.Position)
                .Select(x => new ArgInfo
                {
                    Property = x.Property,
                    Attribute = x.Attr,
                    Parser = GetParser(x.Property.PropertyType)
                })
                .ToArray();

            // Auto-generate usage if not provided
            if (string.IsNullOrEmpty(metadata.Usage))
            {
                var argNames = args.Select(a =>
                    a.Attribute.Optional
                        ? $"[{a.Attribute.Name ?? a.Property.Name}]"
                        : $"<{a.Attribute.Name ?? a.Property.Name}>");
                metadata.Usage = $"{metadata.Name} {string.Join(" ", argNames)}";
            }

            return new SimpleCommandFactory(commandType, metadata, args);
        }

        public bool TryCreateCommand(string[] inputArgs, GameState gameState, out ICommand command, out string errorMessage)
        {
            command = null;
            errorMessage = null;

            // Count required args
            int requiredCount = args.Count(a => !a.Attribute.Optional);
            if (inputArgs.Length < requiredCount)
            {
                errorMessage = $"Usage: {metadata.Usage}";
                return false;
            }

            // Create command instance
            SimpleCommand cmd;
            try
            {
                cmd = (SimpleCommand)Activator.CreateInstance(commandType);
            }
            catch (Exception e)
            {
                errorMessage = $"Failed to create command: {e.Message}";
                return false;
            }

            // Parse and set arguments
            for (int i = 0; i < args.Length; i++)
            {
                var arg = args[i];

                if (i >= inputArgs.Length)
                {
                    // Optional arg not provided - use default
                    if (!arg.Attribute.Optional)
                    {
                        errorMessage = $"Missing required argument: {arg.Attribute.Name ?? arg.Property.Name}";
                        return false;
                    }
                    continue;
                }

                // Parse argument
                if (!arg.Parser(inputArgs[i], out object value, out string parseError))
                {
                    errorMessage = $"Invalid {arg.Attribute.Name ?? arg.Property.Name}: {parseError}";
                    return false;
                }

                arg.Property.SetValue(cmd, value);
            }

            command = cmd;
            return true;
        }

        /// <summary>
        /// Discover all SimpleCommand types in assemblies and register with CommandRegistry.
        /// </summary>
        public static void DiscoverAndRegister(CommandRegistry registry, params Assembly[] assemblies)
        {
            if (assemblies == null || assemblies.Length == 0)
                assemblies = new[] { Assembly.GetCallingAssembly() };

            int count = 0;
            foreach (var assembly in assemblies)
            {
                var commandTypes = assembly.GetTypes()
                    .Where(t => typeof(SimpleCommand).IsAssignableFrom(t)
                             && !t.IsAbstract
                             && t.GetCustomAttribute<CommandAttribute>() != null);

                foreach (var type in commandTypes)
                {
                    try
                    {
                        var factory = Create(type);
                        var cmdMetadata = new CommandMetadataAttribute(factory.Metadata.Name)
                        {
                            Aliases = factory.Metadata.Aliases,
                            Description = factory.Metadata.Description,
                            Usage = factory.Metadata.Usage,
                            Examples = factory.Metadata.Examples
                        };
                        registry.RegisterFactory(factory, cmdMetadata);
                        count++;
                    }
                    catch (Exception e)
                    {
                        ArchonLogger.LogError($"SimpleCommandFactory: Failed to register {type.Name}: {e.Message}", "core_commands");
                    }
                }
            }

            if (count > 0)
            {
                ArchonLogger.Log($"SimpleCommandFactory: Registered {count} simple commands", "core_commands");
            }
        }

        // Delegate for argument parsers
        private delegate bool ArgParser(string input, out object value, out string error);

        private static ArgParser GetParser(Type type)
        {
            if (type == typeof(int)) return ParseInt;
            if (type == typeof(uint)) return ParseUInt;
            if (type == typeof(short)) return ParseShort;
            if (type == typeof(ushort)) return ParseUShort;
            if (type == typeof(byte)) return ParseByte;
            if (type == typeof(long)) return ParseLong;
            if (type == typeof(float)) return ParseFloat;
            if (type == typeof(double)) return ParseDouble;
            if (type == typeof(bool)) return ParseBool;
            if (type == typeof(string)) return ParseString;
            if (type == typeof(FixedPoint64)) return ParseFixedPoint64;

            throw new NotSupportedException($"SimpleCommand: Unsupported argument type {type.Name}");
        }

        private static bool ParseInt(string input, out object value, out string error)
        {
            error = null;
            if (int.TryParse(input, out int result)) { value = result; return true; }
            value = null; error = $"'{input}' is not a valid integer"; return false;
        }

        private static bool ParseUInt(string input, out object value, out string error)
        {
            error = null;
            if (uint.TryParse(input, out uint result)) { value = result; return true; }
            value = null; error = $"'{input}' is not a valid unsigned integer"; return false;
        }

        private static bool ParseShort(string input, out object value, out string error)
        {
            error = null;
            if (short.TryParse(input, out short result)) { value = result; return true; }
            value = null; error = $"'{input}' is not a valid short"; return false;
        }

        private static bool ParseUShort(string input, out object value, out string error)
        {
            error = null;
            if (ushort.TryParse(input, out ushort result)) { value = result; return true; }
            value = null; error = $"'{input}' is not a valid ushort"; return false;
        }

        private static bool ParseByte(string input, out object value, out string error)
        {
            error = null;
            if (byte.TryParse(input, out byte result)) { value = result; return true; }
            value = null; error = $"'{input}' is not a valid byte"; return false;
        }

        private static bool ParseLong(string input, out object value, out string error)
        {
            error = null;
            if (long.TryParse(input, out long result)) { value = result; return true; }
            value = null; error = $"'{input}' is not a valid long"; return false;
        }

        private static bool ParseFloat(string input, out object value, out string error)
        {
            error = null;
            if (float.TryParse(input, out float result)) { value = result; return true; }
            value = null; error = $"'{input}' is not a valid float"; return false;
        }

        private static bool ParseDouble(string input, out object value, out string error)
        {
            error = null;
            if (double.TryParse(input, out double result)) { value = result; return true; }
            value = null; error = $"'{input}' is not a valid double"; return false;
        }

        private static bool ParseBool(string input, out object value, out string error)
        {
            error = null;
            string lower = input.ToLower();
            if (lower == "true" || lower == "1" || lower == "yes") { value = true; return true; }
            if (lower == "false" || lower == "0" || lower == "no") { value = false; return true; }
            value = null; error = $"'{input}' is not a valid boolean (use true/false/1/0)"; return false;
        }

        private static bool ParseString(string input, out object value, out string error)
        {
            error = null;
            value = input;
            return true;
        }

        private static bool ParseFixedPoint64(string input, out object value, out string error)
        {
            error = null;
            if (float.TryParse(input, out float result))
            {
                value = FixedPoint64.FromFloat(result);
                return true;
            }
            value = null; error = $"'{input}' is not a valid number"; return false;
        }

        private struct ArgInfo
        {
            public PropertyInfo Property;
            public ArgAttribute Attribute;
            public ArgParser Parser;
        }
    }
}
