using System.Collections.Generic;
using UnityEngine;

namespace Core.Data
{
    /// <summary>
    /// Wrapper around CountryDataCollection to provide consistent result API
    /// Matches the expected interface for integration with CountrySystem
    /// </summary>
    public class CountryDataLoadResult
    {
        /// <summary>
        /// Whether the loading operation was successful
        /// </summary>
        public bool Success { get; private set; }

        /// <summary>
        /// Error message if loading failed
        /// </summary>
        public string ErrorMessage { get; private set; }

        /// <summary>
        /// The loaded country data collection
        /// </summary>
        public CountryDataCollection Countries { get; private set; }

        /// <summary>
        /// Additional loading statistics and metadata
        /// </summary>
        public LoadingStatistics Statistics { get; private set; }

        /// <summary>
        /// Create a successful result
        /// </summary>
        public static CountryDataLoadResult CreateSuccess(CountryDataCollection countries, LoadingStatistics stats = null)
        {
            if (countries == null)
            {
                return CreateFailure("Country data collection is null");
            }

            return new CountryDataLoadResult
            {
                Success = true,
                ErrorMessage = null,
                Countries = countries,
                Statistics = stats ?? new LoadingStatistics()
            };
        }

        /// <summary>
        /// Create a failed result
        /// </summary>
        public static CountryDataLoadResult CreateFailure(string errorMessage)
        {
            return new CountryDataLoadResult
            {
                Success = false,
                ErrorMessage = errorMessage ?? "Unknown error occurred",
                Countries = null,
                Statistics = new LoadingStatistics()
            };
        }

        /// <summary>
        /// Private constructor to enforce factory methods
        /// </summary>
        private CountryDataLoadResult() { }

        /// <summary>
        /// Get summary information about the loaded data
        /// </summary>
        public string GetSummary()
        {
            if (!Success)
            {
                return $"Failed: {ErrorMessage}";
            }

            int countryCount = Countries?.Count ?? 0;
            return $"Success: {countryCount} countries loaded in {Statistics.LoadingTimeMs}ms";
        }

        /// <summary>
        /// Validate that the result contains valid data
        /// </summary>
        public List<string> Validate()
        {
            var issues = new List<string>();

            if (!Success)
            {
                if (string.IsNullOrEmpty(ErrorMessage))
                    issues.Add("Failed result should have error message");
                if (Countries != null)
                    issues.Add("Failed result should not have country data");
            }
            else
            {
                if (Countries == null)
                    issues.Add("Successful result must have country data");
                else if (Countries.Count == 0)
                    issues.Add("Successful result has no countries loaded");

                if (!string.IsNullOrEmpty(ErrorMessage))
                    issues.Add("Successful result should not have error message");
            }

            return issues;
        }

        /// <summary>
        /// Clean up resources
        /// </summary>
        public void Dispose()
        {
            Countries?.Dispose();
            Countries = null;
        }
    }

    /// <summary>
    /// Loading statistics for performance monitoring
    /// </summary>
    public class LoadingStatistics
    {
        public long LoadingTimeMs { get; set; }
        public int FilesProcessed { get; set; }
        public int FilesSkipped { get; set; }
        public int ParseErrors { get; set; }
        public long MemoryUsedBytes { get; set; }
        public List<string> Warnings { get; set; }

        public LoadingStatistics()
        {
            Warnings = new List<string>();
        }

        public string GetPerformanceReport()
        {
            var memoryMB = MemoryUsedBytes / (1024.0 * 1024.0);
            return $"Loaded {FilesProcessed} files in {LoadingTimeMs}ms " +
                   $"({FilesSkipped} skipped, {ParseErrors} errors) " +
                   $"Memory: {memoryMB:F2}MB";
        }
    }
}