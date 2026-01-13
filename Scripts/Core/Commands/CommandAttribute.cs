using System;

namespace Core.Commands
{
    /// <summary>
    /// Marks a SimpleCommand class for auto-discovery and factory generation.
    /// Replaces the need for separate ICommandFactory + CommandMetadataAttribute.
    ///
    /// Usage:
    /// [Command("my_command", Description = "Does something")]
    /// public class MyCommand : SimpleCommand { ... }
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class CommandAttribute : Attribute
    {
        /// <summary>
        /// Primary command name (e.g., "add_gold")
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Aliases for the command (e.g., ["gold", "ag"])
        /// </summary>
        public string[] Aliases { get; set; }

        /// <summary>
        /// Description of what the command does
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Usage example. Auto-generated from [Arg] attributes if not provided.
        /// </summary>
        public string Usage { get; set; }

        /// <summary>
        /// Example invocations
        /// </summary>
        public string[] Examples { get; set; }

        public CommandAttribute(string name)
        {
            Name = name;
            Aliases = Array.Empty<string>();
            Examples = Array.Empty<string>();
        }
    }
}
