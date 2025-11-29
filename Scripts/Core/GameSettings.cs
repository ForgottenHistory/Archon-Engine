using UnityEngine;

namespace Core
{
    /// <summary>
    /// ScriptableObject configuration for game initialization and data loading
    /// </summary>
    [CreateAssetMenu(fileName = "GameSettings", menuName = "Archon/Game Settings", order = 1)]
    public class GameSettings : ScriptableObject
    {
        [Header("Data File Paths")]
        [Tooltip("Root data directory containing all game data")]
        public string DataDirectory = "Assets/Data";

        [Header("Loading Configuration")]
        [Tooltip("Enable parallel loading where possible")]
        public bool EnableParallelLoading = true;

        [Tooltip("Enable detailed validation of loaded data")]
        public bool EnableDataValidation = true;

        [Header("Error Handling")]
        [Tooltip("Continue loading with defaults if non-critical files are missing")]
        public bool UseGracefulDegradation = true;

        [Tooltip("Show detailed error messages to user")]
        public bool ShowDetailedErrors = true;

        [Tooltip("Retry failed operations this many times")]
        [Range(0, 5)]
        public int RetryAttempts = 1;

        [Header("Development Options")]
        [Tooltip("Enable verbose logging during development")]
        public bool EnableVerboseLogging = false;

        [Tooltip("Enable memory leak detection")]
        public bool EnableMemoryLeakDetection = false;

        [Tooltip("Skip cache warming for faster iteration")]
        public bool SkipCacheWarming = false;

        /// <summary>
        /// Validate that all required paths exist and are accessible
        /// </summary>
        public ValidationResult ValidatePaths()
        {
            var result = new ValidationResult();

            // Check data directory (critical)
            if (string.IsNullOrEmpty(DataDirectory))
            {
                result.AddError("Data directory is required");
                return result; // Can't check anything else without base directory
            }
            else if (!System.IO.Directory.Exists(DataDirectory))
            {
                result.AddError($"Data directory not found: {DataDirectory}");
                return result; // Can't check anything else without base directory
            }

            // Check province bitmap (critical)
            var provinceBitmapPath = System.IO.Path.Combine(DataDirectory, "map", "provinces.bmp");
            if (!System.IO.File.Exists(provinceBitmapPath))
            {
                result.AddError($"Province bitmap not found: {provinceBitmapPath}");
            }

            // Check countries directory (critical)
            var countriesDirectory = System.IO.Path.Combine(DataDirectory, "common", "countries");
            if (!System.IO.Directory.Exists(countriesDirectory))
            {
                result.AddError($"Countries directory not found: {countriesDirectory}");
            }
            else
            {
                var countryFiles = System.IO.Directory.GetFiles(countriesDirectory, "*.txt");
                if (countryFiles.Length == 0)
                {
                    result.AddWarning($"No country files found in: {countriesDirectory}");
                }
            }

            // Check province definitions (optional)
            var provinceDefinitionsPath = System.IO.Path.Combine(DataDirectory, "map", "definition.csv");
            if (!System.IO.File.Exists(provinceDefinitionsPath))
            {
                result.AddWarning($"Province definitions file not found: {provinceDefinitionsPath}");
            }

            // Check scenarios directory (optional)
            var scenariosDirectory = System.IO.Path.Combine(DataDirectory, "history", "countries");
            if (!System.IO.Directory.Exists(scenariosDirectory))
            {
                result.AddWarning($"Scenarios directory not found: {scenariosDirectory}");
            }

            return result;
        }

        /// <summary>
        /// Result of path validation
        /// </summary>
        public struct ValidationResult
        {
            public System.Collections.Generic.List<string> Errors;
            public System.Collections.Generic.List<string> Warnings;

            public bool IsValid => Errors == null || Errors.Count == 0;
            public bool HasWarnings => Warnings != null && Warnings.Count > 0;

            public void AddError(string error)
            {
                if (Errors == null) Errors = new System.Collections.Generic.List<string>();
                Errors.Add(error);
            }

            public void AddWarning(string warning)
            {
                if (Warnings == null) Warnings = new System.Collections.Generic.List<string>();
                Warnings.Add(warning);
            }

            public string GetSummary()
            {
                var summary = "";
                if (Errors != null && Errors.Count > 0)
                {
                    summary += $"Errors: {string.Join(", ", Errors)}";
                }
                if (Warnings != null && Warnings.Count > 0)
                {
                    if (!string.IsNullOrEmpty(summary)) summary += " | ";
                    summary += $"Warnings: {string.Join(", ", Warnings)}";
                }
                return string.IsNullOrEmpty(summary) ? "All paths valid" : summary;
            }
        }

        /// <summary>
        /// Create default settings for testing
        /// </summary>
        public static GameSettings CreateDefault()
        {
            var settings = CreateInstance<GameSettings>();
            // Default values are already set in the field declarations
            return settings;
        }

        /// <summary>
        /// Log current configuration
        /// </summary>
        public void LogConfiguration()
        {
            ArchonLogger.Log($"GameSettings Configuration:", "core_simulation");
            ArchonLogger.Log($"  Data Directory: {DataDirectory}", "core_simulation");
            ArchonLogger.Log($"  Parallel Loading: {EnableParallelLoading}", "core_simulation");
            ArchonLogger.Log($"  Data Validation: {EnableDataValidation}", "core_simulation");
        }

        #if UNITY_EDITOR
        /// <summary>
        /// Validate paths in the editor
        /// </summary>
        void OnValidate()
        {
            // To be implemented
        }
        #endif
    }
}