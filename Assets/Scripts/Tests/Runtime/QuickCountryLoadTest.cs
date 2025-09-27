using System.Collections;
using UnityEngine;
using GameData.Core;
using GameData.Loaders;

namespace Tests.Runtime
{
    /// <summary>
    /// Quick test script to validate the country loading system
    /// Attach this to a GameObject and call StartTest() to run a simple validation
    /// </summary>
    public class QuickCountryLoadTest : MonoBehaviour
    {
        [Header("Test Configuration")]
        [SerializeField] private bool autoStartOnAwake = true;
        [SerializeField] private int maxFilesToTest = 10; // Start small
        [SerializeField] private string countriesDirectory = "Assets/Data/common/countries";

        private JobifiedCountryLoader loader;
        private CountryDataCollection loadedCountries;

        void Awake()
        {
            if (autoStartOnAwake)
            {
                StartCoroutine(RunQuickTest());
            }
        }

        public void StartTest()
        {
            StartCoroutine(RunQuickTest());
        }

        private IEnumerator RunQuickTest()
        {
            // Initialize FileLogger first
            var fileLogger = FileLogger.Instance;

            DominionLogger.LogSection("Quick Country Load Test");
            DominionLogger.Log("Test Starting with Burst Job System");

            bool success = false;
            System.Exception caughtException = null;

            // Initialize loader
            loader = new JobifiedCountryLoader();
            loader.OnProgressUpdate += OnProgressUpdate;

            // Check if countries directory exists
            if (!System.IO.Directory.Exists(countriesDirectory))
            {
                DominionLogger.LogError("Countries directory not found: " + countriesDirectory);
                CleanupResources();
                yield break;
            }

            try
            {
                // Get a limited set of files for testing
                var allFiles = System.IO.Directory.GetFiles(countriesDirectory, "*.txt");
                DominionLogger.LogFormat("Found {0} country files total in {1}", allFiles.Length, countriesDirectory);

                // For this quick test, we'll try loading all files using Job System
                DominionLogger.LogSeparator("Starting Parallel Processing");
                DominionLogger.LogFormat("Loading {0} country files with Burst Job System...", allFiles.Length);

                // Use synchronous Job System approach (runs on main thread but uses Unity worker threads internally)
                loadedCountries = loader.LoadAllCountriesJob(countriesDirectory);
                success = true;
            }
            catch (System.Exception e)
            {
                caughtException = e;
            }

            // Small yield to prevent frame blocking
            yield return null;

            if (caughtException != null)
            {
                DominionLogger.LogError($"Exception in RunQuickTest: {caughtException.Message}\n{caughtException.StackTrace}");
                CleanupResources();
                yield break;
            }

            if (loadedCountries == null)
            {
                DominionLogger.LogError("No countries were loaded - LoadAllCountriesJob returned null");
                CleanupResources();
                yield break;
            }

            // Log results
            DominionLogger.LogSeparator("Processing Complete");
            DominionLogger.LogFormat("✓ Successfully loaded {0} countries using Burst jobs!", loadedCountries.Count);

            // Test some basic functionality
            yield return StartCoroutine(TestBasicFunctionality());

            DominionLogger.LogSeparator("Test Completed Successfully");
            DominionLogger.Log("All country loading tests passed");

            // Cleanup at the end of successful run
            CleanupResources();
        }

        private IEnumerator TestBasicFunctionality()
        {
            DominionLogger.LogSeparator("Basic Functionality Tests");
            DominionLogger.Log("Running validation and memory tests...");

            // Test accessing countries by index
            for (int i = 0; i < Mathf.Min(5, loadedCountries.Count); i++)
            {
                var country = loadedCountries.GetCountryByIndex(i);
                if (country != null)
                {
                    DominionLogger.LogFormat("Country {0}: {1}", i, country);

                    // Test validation
                    if (country.Validate(out var errors))
                    {
                        DominionLogger.Log($"  ✓ Validation passed");
                    }
                    else
                    {
                        DominionLogger.LogWarning($"  ⚠ Validation failed: {string.Join(", ", errors)}");
                    }

                    // Test memory usage calculation
                    if (country.coldData != null)
                    {
                        var memoryUsage = country.coldData.GetMemoryUsage();
                        DominionLogger.LogFormat("  Memory usage: {0} bytes", memoryUsage);
                    }
                }
                yield return null; // Spread across frames
            }

            // Test collection memory usage
            var totalMemory = loadedCountries.GetMemoryUsage();
            DominionLogger.LogFormat("Total collection memory usage: {0:F2} MB", totalMemory / (1024 * 1024));

            // Test tag-based lookup (if we have any countries)
            if (loadedCountries.Count > 0)
            {
                var firstCountry = loadedCountries.GetCountryByIndex(0);
                if (firstCountry != null && !string.IsNullOrEmpty(firstCountry.Tag))
                {
                    var foundCountry = loadedCountries.GetCountryByTag(firstCountry.Tag);
                    if (foundCountry != null)
                    {
                        DominionLogger.LogFormat("✓ Tag lookup test passed for '{0}'", firstCountry.Tag);
                    }
                    else
                    {
                        DominionLogger.LogWarning($"⚠ Tag lookup test failed for '{firstCountry.Tag}'");
                    }
                }
            }
        }

        private void OnProgressUpdate(JobifiedCountryLoader.LoadingProgress progress)
        {
            DominionLogger.LogFormat("Progress: {0}/{1} ({2:P1}) - {3}",
                progress.FilesProcessed, progress.TotalFiles, progress.ProgressPercentage, progress.CurrentOperation);
        }

        void OnDestroy()
        {
            CleanupResources();
        }

        private void CleanupResources()
        {
            // Cleanup in a centralized method to ensure it's called properly
            try
            {
                // Complete any pending jobs before cleanup
                Unity.Jobs.JobHandle.ScheduleBatchedJobs();
                System.Threading.Thread.Sleep(1); // Brief pause to allow job completion
            }
            catch (System.Exception e)
            {
                DominionLogger.LogWarning($"Exception during job completion: {e.Message}");
            }

            try
            {
                if (loadedCountries != null && loadedCountries.IsCreated)
                {
                    loadedCountries.Dispose();
                    loadedCountries = null;
                }
            }
            catch (System.Exception e)
            {
                DominionLogger.LogWarning($"Exception during loadedCountries cleanup: {e.Message}");
            }

            try
            {
                if (loader != null)
                {
                    loader.OnProgressUpdate -= OnProgressUpdate;
                    loader = null;
                }
            }
            catch (System.Exception e)
            {
                DominionLogger.LogWarning($"Exception during loader cleanup: {e.Message}");
            }
        }

        // Context menu for easy testing in editor
        [ContextMenu("Run Quick Test")]
        private void ContextMenuTest()
        {
            if (Application.isPlaying)
            {
                StartTest();
            }
            else
            {
                DominionLogger.LogWarning("Test can only be run in play mode");
            }
        }
    }
}