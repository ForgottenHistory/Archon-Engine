using UnityEngine;
using Unity.Collections;
using ParadoxParser.Jobs;
using ParadoxParser.Bitmap;
using System.Threading.Tasks;

namespace ParadoxParser.Tests
{
    /// <summary>
    /// Test script for JobifiedBMPLoader to validate Burst job architecture
    /// </summary>
    public class JobifiedBMPLoaderTests : MonoBehaviour
    {
        [Header("Test Configuration")]
        [SerializeField] private string bmpFilePath = "Assets/Data/map/provinces.bmp";
        [SerializeField] private string definitionCsvPath = "Assets/Data/map/definition.csv";
        [SerializeField] private bool autoRunOnStart = false;

        private JobifiedBMPLoader loader;

        void Start()
        {
            if (autoRunOnStart)
            {
                StartCoroutine(RunTestCoroutine());
            }
        }

        [ContextMenu("Run BMP Loading Test")]
        public void RunTest()
        {
            if (Application.isPlaying)
            {
                StartCoroutine(RunTestCoroutine());
            }
            else
            {
                Debug.LogWarning("Test can only be run in play mode");
            }
        }

        private System.Collections.IEnumerator RunTestCoroutine()
        {
            Debug.Log("=== JobifiedBMPLoader Test Starting ===");

            // Initialize loader
            loader = new JobifiedBMPLoader();
            loader.OnProgressUpdate += OnProgressUpdate;

            // Test variables
            BMPLoadResult result = default;
            bool testCompleted = false;
            string errorMessage = null;

            // Run async test
            Task.Run(async () =>
            {
                try
                {
                    result = await loader.LoadBMPAsync(bmpFilePath);
                }
                catch (System.Exception e)
                {
                    errorMessage = e.Message + "\n" + e.StackTrace;
                }
                finally
                {
                    testCompleted = true;
                }
            });

            // Wait for completion
            while (!testCompleted)
            {
                yield return new WaitForSeconds(0.1f);
            }

            // Check results
            if (!string.IsNullOrEmpty(errorMessage))
            {
                Debug.LogError($"❌ Test failed with exception: {errorMessage}");
                yield break;
            }

            if (!result.Success)
            {
                Debug.LogError($"❌ BMP loading failed: {result.ErrorMessage}");
                yield break;
            }

            // Log success
            Debug.Log($"✅ BMP Loading Success!");
            Debug.Log($"   Image Size: {result.Width}x{result.Height}");
            Debug.Log($"   Bits Per Pixel: {result.BitsPerPixel}");

            if (result.UniqueColors.IsCreated)
            {
                Debug.Log($"   Unique Colors Found: {result.UniqueColors.Count}");
            }

            // Test pixel search functionality
            yield return StartCoroutine(TestPixelSearch(result));

            // Cleanup
            result.Dispose();
            Debug.Log("=== JobifiedBMPLoader Test Completed ===");
        }

        private System.Collections.IEnumerator TestPixelSearch(BMPLoadResult result)
        {
            Debug.Log("--- Testing Pixel Search Functionality ---");

            if (result.UniqueColors.IsCreated && result.UniqueColors.Count > 0)
            {
                // Get first color from unique colors
                var colorEnumerator = result.UniqueColors.GetEnumerator();
                if (colorEnumerator.MoveNext())
                {
                    int testColor = colorEnumerator.Current;

                    Debug.Log($"   Testing pixel search for RGB: 0x{testColor:X6}");

                    // Test pixel search in background
                    NativeList<PixelCoord> searchResults = default;
                    bool searchCompleted = false;

                    Task.Run(() =>
                    {
                        try
                        {
                            // Convert PersistentBMPPixelData to BMPPixelData for the search
                            var pixelDataForSearch = new ParadoxParser.Bitmap.BMPParser.BMPPixelData
                            {
                                RawData = new Unity.Collections.NativeSlice<byte>(result.PixelData.RawData),
                                Header = result.PixelData.Header,
                                Success = result.PixelData.Success
                            };
                            searchResults = loader.FindPixelsWithColorJob(pixelDataForSearch, testColor);
                        }
                        finally
                        {
                            searchCompleted = true;
                        }
                    });

                    while (!searchCompleted)
                        yield return null;

                    if (searchResults.IsCreated && searchResults.Length > 0)
                    {
                        Debug.Log($"   ✅ Found {searchResults.Length} pixels with target color");

                        // Verify first pixel
                        var firstPixel = searchResults[0];
                        Debug.Log($"   First match at: ({firstPixel.x}, {firstPixel.y})");

                        searchResults.Dispose();
                    }
                    else
                    {
                        Debug.LogWarning("   ⚠️ No pixels found with target color");
                    }
                }
            }
            else
            {
                Debug.Log("   Skipping pixel search test (no unique colors found)");
            }
        }

        private void OnProgressUpdate(JobifiedBMPLoader.LoadingProgress progress)
        {
            Debug.Log($"Progress: {progress.ProgressPercentage:P1} - {progress.CurrentOperation}");
        }

        void OnDestroy()
        {
            if (loader != null)
            {
                loader.OnProgressUpdate -= OnProgressUpdate;
            }
        }
    }
}