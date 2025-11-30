using System.Collections.Generic;
using Core.Systems;
using Core.Registries;
using Core.Loaders;
using Core.Linking;
using Core.Data;

namespace Core.Initialization
{
    /// <summary>
    /// Shared state container passed between initialization phases
    /// Contains all systems, registries, and loaded data
    /// </summary>
    public class InitializationContext
    {
        // Configuration
        public GameSettings Settings { get; set; }
        public bool EnableDetailedLogging { get; set; }

        // Core Systems
        public GameState GameState { get; set; }
        public ProvinceSystem ProvinceSystem { get; set; }
        public CountrySystem CountrySystem { get; set; }
        public TimeManager TimeManager { get; set; }
        public EventBus EventBus { get; set; }

        // Registries (Static Data)
        public GameRegistries Registries { get; set; }

        // Loaded Data
        public ProvinceInitialStateLoadResult ProvinceInitialStates { get; set; }
        public List<DefinitionLoader.DefinitionEntry> ProvinceDefinitions { get; set; }

        // Data Linking Systems
        public ReferenceResolver ReferenceResolver { get; set; }
        public CrossReferenceBuilder CrossReferenceBuilder { get; set; }
        public DataValidator DataValidator { get; set; }

        // Progress Tracking
        public System.Action<float, string> OnProgress { get; set; }

        // Error Tracking
        public bool HasError { get; set; }
        public string ErrorMessage { get; set; }

        /// <summary>
        /// Report progress update (percentage 0-100 and status message)
        /// </summary>
        public void ReportProgress(float percentage, string status)
        {
            OnProgress?.Invoke(percentage, status);
        }

        /// <summary>
        /// Report error and halt initialization
        /// </summary>
        public void ReportError(string error)
        {
            HasError = true;
            ErrorMessage = error;
            ArchonLogger.LogError($"Initialization Error: {error}", "core_data_loading");
        }
    }
}
