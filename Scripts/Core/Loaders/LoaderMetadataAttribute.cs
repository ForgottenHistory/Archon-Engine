using System;

namespace Core.Loaders
{
    /// <summary>
    /// Attribute to define loader metadata for auto-registration.
    /// Apply to ILoaderFactory implementations for automatic discovery.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class LoaderMetadataAttribute : Attribute
    {
        /// <summary>
        /// Unique loader name (e.g., "terrain", "water_provinces")
        /// </summary>
        public string LoaderName { get; }

        /// <summary>
        /// Description of what data this loader handles.
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Loading priority (lower = loaded first). Default is 100.
        /// Use for controlling load order when loaders have dependencies.
        /// </summary>
        public int Priority { get; set; } = 100;

        /// <summary>
        /// Whether this loader is required for the game to function.
        /// If true, failure to load will halt initialization.
        /// </summary>
        public bool Required { get; set; } = false;

        public LoaderMetadataAttribute(string loaderName)
        {
            LoaderName = loaderName;
        }
    }
}
