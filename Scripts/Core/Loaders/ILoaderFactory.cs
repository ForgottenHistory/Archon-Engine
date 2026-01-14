namespace Core.Loaders
{
    /// <summary>
    /// Interface for data loader factories.
    /// Implement this and add [LoaderMetadata] for auto-discovery.
    /// </summary>
    public interface ILoaderFactory
    {
        /// <summary>
        /// Load data into registries from the specified data path.
        /// </summary>
        /// <param name="context">Loading context with registries and settings</param>
        void Load(LoaderContext context);
    }
}
