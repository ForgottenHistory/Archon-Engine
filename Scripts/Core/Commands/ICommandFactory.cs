namespace Core.Commands
{
    /// <summary>
    /// Factory for creating commands from string arguments.
    /// Parses debug console input and creates ICommand instances.
    ///
    /// Implement this interface and add [CommandMetadata] attribute
    /// for automatic discovery by CommandRegistry.
    /// </summary>
    public interface ICommandFactory
    {
        /// <summary>
        /// Try to create a command from parsed arguments.
        /// </summary>
        /// <param name="args">Command arguments (excludes command name itself)</param>
        /// <param name="gameState">GameState for validation/lookups</param>
        /// <param name="command">Created command if successful</param>
        /// <param name="errorMessage">Error message if failed</param>
        /// <returns>True if command created successfully</returns>
        bool TryCreateCommand(
            string[] args,
            GameState gameState,
            out ICommand command,
            out string errorMessage);
    }
}
