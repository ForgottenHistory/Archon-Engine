namespace Core.Validation
{
    /// <summary>
    /// Static entry point for fluent validation.
    ///
    /// Usage:
    /// Validate.For(gameState)
    ///         .Country(countryId)
    ///         .Province(provinceId)
    ///         .Result(out var reason);
    /// </summary>
    public static class Validate
    {
        /// <summary>
        /// Start a validation chain for the given GameState.
        /// </summary>
        public static ValidationBuilder For(GameState gameState)
        {
            return new ValidationBuilder(gameState);
        }
    }
}
