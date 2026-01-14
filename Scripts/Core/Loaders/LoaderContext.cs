using Core.Registries;

namespace Core.Loaders
{
    /// <summary>
    /// Context provided to loaders during data loading.
    /// Contains registries and configuration needed for loading.
    /// </summary>
    public class LoaderContext
    {
        /// <summary>
        /// Game registries to populate with loaded data.
        /// </summary>
        public GameRegistries Registries { get; }

        /// <summary>
        /// Root data directory path.
        /// </summary>
        public string DataPath { get; }

        /// <summary>
        /// Whether to enable detailed logging.
        /// </summary>
        public bool EnableDetailedLogging { get; set; }

        public LoaderContext(GameRegistries registries, string dataPath)
        {
            Registries = registries;
            DataPath = dataPath;
        }
    }
}
