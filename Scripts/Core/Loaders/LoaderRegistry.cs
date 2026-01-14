using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Utils;

namespace Core.Loaders
{
    /// <summary>
    /// Registry for data loaders with auto-discovery.
    /// Discovers loaders via reflection and executes them in priority order.
    ///
    /// Usage:
    /// 1. Create registry instance
    /// 2. Call DiscoverLoaders() with assemblies to scan
    /// 3. Call ExecuteAll() to run loaders in priority order
    /// </summary>
    public class LoaderRegistry
    {
        private readonly Dictionary<string, LoaderRegistration> loadersByName;
        private readonly List<LoaderRegistration> loadersOrdered;
        private readonly string logSubsystem;

        /// <summary>
        /// Create a loader registry.
        /// </summary>
        /// <param name="logSubsystem">Subsystem name for logging</param>
        public LoaderRegistry(string logSubsystem = "core_data_loading")
        {
            this.logSubsystem = logSubsystem;
            loadersByName = new Dictionary<string, LoaderRegistration>();
            loadersOrdered = new List<LoaderRegistration>();
        }

        /// <summary>
        /// Auto-discover and register all loader factories in specified assemblies.
        /// </summary>
        /// <param name="assemblies">Assemblies to scan for ILoaderFactory implementations</param>
        public void DiscoverLoaders(params Assembly[] assemblies)
        {
            if (assemblies == null || assemblies.Length == 0)
            {
                assemblies = new[] { Assembly.GetCallingAssembly() };
            }

            foreach (var assembly in assemblies)
            {
                DiscoverLoadersInAssembly(assembly);
            }

            // Sort by priority after discovery
            loadersOrdered.Sort((a, b) => a.Metadata.Priority.CompareTo(b.Metadata.Priority));

            ArchonLogger.Log($"LoaderRegistry: Total {loadersByName.Count} loaders registered", logSubsystem);
        }

        private void DiscoverLoadersInAssembly(Assembly assembly)
        {
            var factoryTypes = assembly.GetTypes()
                .Where(t => typeof(ILoaderFactory).IsAssignableFrom(t)
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
                ArchonLogger.Log($"LoaderRegistry: Discovered {count} loaders in {assembly.GetName().Name}", logSubsystem);
            }
        }

        /// <summary>
        /// Manually register a loader factory type.
        /// </summary>
        public bool RegisterFactory(Type factoryType)
        {
            var metadata = factoryType.GetCustomAttribute<LoaderMetadataAttribute>();
            if (metadata == null)
            {
                ArchonLogger.LogWarning($"LoaderRegistry: Factory {factoryType.Name} missing [LoaderMetadata] attribute, skipping", logSubsystem);
                return false;
            }

            ILoaderFactory factory;
            try
            {
                factory = (ILoaderFactory)Activator.CreateInstance(factoryType);
            }
            catch (Exception e)
            {
                ArchonLogger.LogError($"LoaderRegistry: Failed to create factory {factoryType.Name}: {e.Message}", logSubsystem);
                return false;
            }

            return RegisterFactory(factory, metadata);
        }

        /// <summary>
        /// Manually register a loader factory instance with metadata.
        /// </summary>
        public bool RegisterFactory(ILoaderFactory factory, LoaderMetadataAttribute metadata)
        {
            var registration = new LoaderRegistration
            {
                Metadata = metadata,
                Factory = factory
            };

            string nameLower = metadata.LoaderName.ToLower();
            if (loadersByName.ContainsKey(nameLower))
            {
                ArchonLogger.LogWarning($"LoaderRegistry: Loader '{metadata.LoaderName}' already registered, overwriting", logSubsystem);
                loadersOrdered.RemoveAll(r => r.Metadata.LoaderName.ToLower() == nameLower);
            }

            loadersByName[nameLower] = registration;
            loadersOrdered.Add(registration);

            ArchonLogger.Log($"LoaderRegistry: Registered '{metadata.LoaderName}' (priority: {metadata.Priority})", logSubsystem);
            return true;
        }

        /// <summary>
        /// Execute all registered loaders in priority order.
        /// </summary>
        /// <param name="context">Loading context with registries and settings</param>
        /// <returns>True if all required loaders succeeded</returns>
        public bool ExecuteAll(LoaderContext context)
        {
            ArchonLogger.Log($"LoaderRegistry: Executing {loadersOrdered.Count} loaders", logSubsystem);

            bool allSuccess = true;

            foreach (var registration in loadersOrdered)
            {
                var metadata = registration.Metadata;

                try
                {
                    if (context.EnableDetailedLogging)
                    {
                        ArchonLogger.Log($"LoaderRegistry: Loading '{metadata.LoaderName}'...", logSubsystem);
                    }

                    registration.Factory.Load(context);
                    registration.Executed = true;

                    if (context.EnableDetailedLogging)
                    {
                        ArchonLogger.Log($"LoaderRegistry: '{metadata.LoaderName}' completed", logSubsystem);
                    }
                }
                catch (Exception e)
                {
                    registration.Error = e.Message;

                    if (metadata.Required)
                    {
                        ArchonLogger.LogError($"LoaderRegistry: Required loader '{metadata.LoaderName}' failed: {e.Message}", logSubsystem);
                        allSuccess = false;
                    }
                    else
                    {
                        ArchonLogger.LogWarning($"LoaderRegistry: Optional loader '{metadata.LoaderName}' failed: {e.Message}", logSubsystem);
                    }
                }
            }

            ArchonLogger.Log($"LoaderRegistry: Execution complete - {(allSuccess ? "SUCCESS" : "FAILED")}", logSubsystem);
            return allSuccess;
        }

        /// <summary>
        /// Try to get loader registration by name.
        /// </summary>
        public bool TryGetLoader(string name, out LoaderRegistration registration)
        {
            return loadersByName.TryGetValue(name.ToLower(), out registration);
        }

        /// <summary>
        /// Get all registered loaders in priority order.
        /// </summary>
        public IEnumerable<LoaderRegistration> GetAllLoaders()
        {
            return loadersOrdered;
        }

        /// <summary>
        /// Get count of registered loaders.
        /// </summary>
        public int Count => loadersByName.Count;

        /// <summary>
        /// Get loading summary.
        /// </summary>
        public string GetLoadingSummary()
        {
            var lines = new List<string> { "Loader Summary:" };

            foreach (var reg in loadersOrdered)
            {
                string status = reg.Executed ? (reg.Error == null ? "OK" : "FAILED") : "PENDING";
                string required = reg.Metadata.Required ? " [REQUIRED]" : "";
                lines.Add($"  [{status}] {reg.Metadata.LoaderName} (priority: {reg.Metadata.Priority}){required}");

                if (reg.Error != null)
                {
                    lines.Add($"       Error: {reg.Error}");
                }
            }

            return string.Join("\n", lines);
        }

        /// <summary>
        /// Registration entry for a loader.
        /// </summary>
        public class LoaderRegistration
        {
            public LoaderMetadataAttribute Metadata { get; set; }
            public ILoaderFactory Factory { get; set; }
            public bool Executed { get; set; }
            public string Error { get; set; }
        }
    }
}
