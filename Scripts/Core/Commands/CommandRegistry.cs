using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Utils;

namespace Core.Commands
{
    /// <summary>
    /// Registry for command factories with auto-discovery.
    /// Discovers commands via reflection and provides lookup by name/alias.
    ///
    /// Usage:
    /// 1. Create registry instance
    /// 2. Call DiscoverCommands() with assemblies to scan
    /// 3. Use TryGetCommand() to find commands by name/alias
    /// </summary>
    public class CommandRegistry
    {
        private readonly Dictionary<string, CommandRegistration> commandsByName;
        private readonly Dictionary<string, CommandRegistration> commandsByAlias;
        private readonly string logSubsystem;

        /// <summary>
        /// Create a command registry.
        /// </summary>
        /// <param name="logSubsystem">Subsystem name for logging (e.g., "game_hegemon", "starter_kit")</param>
        public CommandRegistry(string logSubsystem = "core_commands")
        {
            this.logSubsystem = logSubsystem;
            commandsByName = new Dictionary<string, CommandRegistration>();
            commandsByAlias = new Dictionary<string, CommandRegistration>();
        }

        /// <summary>
        /// Auto-discover and register all command factories in specified assemblies.
        /// </summary>
        /// <param name="assemblies">Assemblies to scan for ICommandFactory implementations</param>
        public void DiscoverCommands(params Assembly[] assemblies)
        {
            if (assemblies == null || assemblies.Length == 0)
            {
                // Default to calling assembly if none specified
                assemblies = new[] { Assembly.GetCallingAssembly() };
            }

            foreach (var assembly in assemblies)
            {
                DiscoverCommandsInAssembly(assembly);
            }

            ArchonLogger.Log($"CommandRegistry: Total {commandsByName.Count} commands registered", logSubsystem);
        }

        private void DiscoverCommandsInAssembly(Assembly assembly)
        {
            var factoryTypes = assembly.GetTypes()
                .Where(t => typeof(ICommandFactory).IsAssignableFrom(t)
                         && !t.IsInterface
                         && !t.IsAbstract);

            int count = 0;
            foreach (var factoryType in factoryTypes)
            {
                if (RegisterFactory(factoryType))
                    count++;
            }

            if (count > 0)
            {
                ArchonLogger.Log($"CommandRegistry: Discovered {count} commands in {assembly.GetName().Name}", logSubsystem);
            }
        }

        /// <summary>
        /// Manually register a command factory type.
        /// </summary>
        public bool RegisterFactory(Type factoryType)
        {
            var metadata = factoryType.GetCustomAttribute<CommandMetadataAttribute>();
            if (metadata == null)
            {
                ArchonLogger.LogWarning($"CommandRegistry: Factory {factoryType.Name} missing [CommandMetadata] attribute, skipping", logSubsystem);
                return false;
            }

            // Create factory instance
            ICommandFactory factory;
            try
            {
                factory = (ICommandFactory)Activator.CreateInstance(factoryType);
            }
            catch (Exception e)
            {
                ArchonLogger.LogError($"CommandRegistry: Failed to create factory {factoryType.Name}: {e.Message}", logSubsystem);
                return false;
            }

            return RegisterFactory(factory, metadata);
        }

        /// <summary>
        /// Manually register a command factory instance with metadata.
        /// </summary>
        public bool RegisterFactory(ICommandFactory factory, CommandMetadataAttribute metadata)
        {
            var registration = new CommandRegistration
            {
                Metadata = metadata,
                Factory = factory
            };

            // Register by primary name
            string nameLower = metadata.CommandName.ToLower();
            if (commandsByName.ContainsKey(nameLower))
            {
                ArchonLogger.LogWarning($"CommandRegistry: Command '{metadata.CommandName}' already registered, overwriting", logSubsystem);
            }
            commandsByName[nameLower] = registration;

            // Register by aliases
            if (metadata.Aliases != null)
            {
                foreach (var alias in metadata.Aliases)
                {
                    commandsByAlias[alias.ToLower()] = registration;
                }
            }

            ArchonLogger.Log($"CommandRegistry: Registered '{metadata.CommandName}'", logSubsystem);
            return true;
        }

        /// <summary>
        /// Try to get command registration by name or alias.
        /// </summary>
        public bool TryGetCommand(string nameOrAlias, out CommandRegistration registration)
        {
            string key = nameOrAlias.ToLower();

            if (commandsByName.TryGetValue(key, out registration))
                return true;

            if (commandsByAlias.TryGetValue(key, out registration))
                return true;

            registration = null;
            return false;
        }

        /// <summary>
        /// Get all registered commands.
        /// </summary>
        public IEnumerable<CommandRegistration> GetAllCommands()
        {
            return commandsByName.Values;
        }

        /// <summary>
        /// Get count of registered commands.
        /// </summary>
        public int Count => commandsByName.Count;

        /// <summary>
        /// Generate help text for all commands.
        /// </summary>
        public string GenerateHelpText()
        {
            var commands = GetAllCommands().OrderBy(c => c.Metadata.CommandName);
            var lines = new List<string>();

            lines.Add("Available Commands:");
            foreach (var cmd in commands)
            {
                var metadata = cmd.Metadata;
                string aliases = metadata.Aliases.Length > 0 ? $" (alias: {string.Join(", ", metadata.Aliases)})" : "";
                string description = !string.IsNullOrEmpty(metadata.Description) ? $" - {metadata.Description}" : "";
                lines.Add($"  {metadata.CommandName}{aliases}{description}");
            }

            lines.Add("");
            lines.Add("Examples:");
            foreach (var cmd in commands)
            {
                if (cmd.Metadata.Examples != null && cmd.Metadata.Examples.Length > 0)
                {
                    foreach (var example in cmd.Metadata.Examples)
                    {
                        lines.Add($"  {example}");
                    }
                }
            }

            return string.Join("\n", lines);
        }

        /// <summary>
        /// Registration entry for a command.
        /// </summary>
        public class CommandRegistration
        {
            public CommandMetadataAttribute Metadata { get; set; }
            public ICommandFactory Factory { get; set; }
        }
    }
}
