using System;

namespace Core.Commands
{
    /// <summary>
    /// Attribute to define command metadata for auto-registration and help generation.
    /// Apply to ICommandFactory implementations for automatic discovery.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class CommandMetadataAttribute : Attribute
    {
        /// <summary>
        /// Primary command name (e.g., "add_gold")
        /// </summary>
        public string CommandName { get; }

        /// <summary>
        /// Aliases for the command (e.g., ["gold", "ag"])
        /// </summary>
        public string[] Aliases { get; set; }

        /// <summary>
        /// Description of what the command does
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Usage example showing command syntax.
        /// </summary>
        public string Usage { get; set; }

        /// <summary>
        /// Example invocations (e.g., "add_gold 100", "add_gold -50 5")
        /// </summary>
        public string[] Examples { get; set; }

        public CommandMetadataAttribute(string commandName)
        {
            CommandName = commandName;
            Aliases = Array.Empty<string>();
            Examples = Array.Empty<string>();
        }
    }
}
