using UnityEngine;
using ProvinceSystem.Services;
using System.Linq;

/// <summary>
/// Minimal test script for ParadoxDataLib integration
/// Tests only the core ParadoxDataManager functionality
/// </summary>
public class ParadoxDataLibTest : MonoBehaviour
{
    [Header("Core Components")]
    public ParadoxDataManager paradoxDataManager;

    [Header("Test Settings")]
    public bool autoTest = true;
    public bool enableDetailedLogging = true;

    void Start()
    {
        if (autoTest)
        {
            TestCoreSystem();
        }
    }

    [ContextMenu("Test Core System")]
    public void TestCoreSystem()
    {
        Debug.Log("=== ParadoxDataLib Core System Test ===");

        // Test 1: Check if ParadoxDataManager exists
        if (paradoxDataManager == null)
        {
            paradoxDataManager = FindObjectOfType<ParadoxDataManager>();
        }

        if (paradoxDataManager == null)
        {
            Debug.LogError("❌ ParadoxDataManager not found! Please add it to the scene.");
            return;
        }

        Debug.Log("✅ ParadoxDataManager found");

        // Test 2: Check initialization state
        Debug.Log($"Initialization Status: {(paradoxDataManager.IsInitialized ? "✅ Initialized" : "❌ Not Initialized")}");
        Debug.Log($"Loading Status: {(paradoxDataManager.IsLoading ? "⏳ Loading" : "✅ Ready")}");
        Debug.Log($"Data Status: {(paradoxDataManager.IsLoaded ? "✅ Loaded" : "❌ Not Loaded")}");
        Debug.Log($"Current State: {paradoxDataManager.CurrentState}");

        // Test 3: Start loading if not loaded
        if (!paradoxDataManager.IsLoaded && !paradoxDataManager.IsLoading)
        {
            Debug.Log("🔄 Starting data loading...");
            StartCoroutine(paradoxDataManager.LoadAllDataCoroutine());
        }

        // Test 4: Memory usage
        Debug.Log($"Memory Usage: {paradoxDataManager.MemoryUsageBytes / 1024f / 1024f:F1} MB");
    }

    [ContextMenu("Test Data Access")]
    public void TestDataAccess()
    {
        if (paradoxDataManager == null || !paradoxDataManager.IsLoaded)
        {
            Debug.LogError("❌ ParadoxDataManager not loaded. Run TestCoreSystem first.");
            return;
        }

        Debug.Log("=== Data Access Test ===");

        // Test province definitions
        var definitions = paradoxDataManager.GetAllProvinceDefinitions();
        Debug.Log($"Province Definitions: {definitions.Count()} loaded");

        // Test a few specific provinces
        int[] testIds = { 1, 10, 100 };
        foreach (int id in testIds)
        {
            var definition = paradoxDataManager.GetProvinceDefinition(id);
            if (definition.HasValue)
            {
                var def = definition.Value;
                Debug.Log($"Province {id}: ✅ Found - RGB({def.Red},{def.Green},{def.Blue})");

                // Test color lookup
                var color = new Color32(def.Red, def.Green, def.Blue, 255);
                var lookupId = paradoxDataManager.GetProvinceIdFromColor(color);
                Debug.Log($"  Color Lookup: {(lookupId == id ? "✅ Consistent" : "❌ Mismatch")}");
            }
            else
            {
                Debug.Log($"Province {id}: ❌ Not found");
            }
        }

        // Test default map data
        var mapData = paradoxDataManager.GetDefaultMapData();
        Debug.Log($"Default Map Data: {(mapData != null ? "✅ Available" : "❌ Missing")}");
    }

    [ContextMenu("Performance Test")]
    public void PerformanceTest()
    {
        if (paradoxDataManager == null || !paradoxDataManager.IsLoaded)
        {
            Debug.LogError("❌ ParadoxDataManager not loaded. Run TestCoreSystem first.");
            return;
        }

        Debug.Log("=== Performance Test ===");

        // Test lookup performance
        var startTime = System.DateTime.Now;
        for (int i = 0; i < 1000; i++)
        {
            paradoxDataManager.GetProvinceDefinition(i % 100);
        }
        var duration = System.DateTime.Now - startTime;

        Debug.Log($"1000 province lookups: {duration.TotalMilliseconds:F2}ms");
        Debug.Log($"Average lookup time: {duration.TotalMilliseconds / 1000:F4}ms");

        if (duration.TotalMilliseconds < 10)
        {
            Debug.Log("✅ Performance: Excellent");
        }
        else if (duration.TotalMilliseconds < 50)
        {
            Debug.Log("✅ Performance: Good");
        }
        else
        {
            Debug.LogWarning("⚠️ Performance: Could be better");
        }
    }

    [ContextMenu("Error Handling Test")]
    public void ErrorHandlingTest()
    {
        Debug.Log("=== Error Handling Test ===");

        // Test invalid province ID
        var invalidProvince = paradoxDataManager?.GetProvinceDefinition(-1);
        Debug.Log($"Invalid ID Test: {(!invalidProvince.HasValue ? "✅ Handled correctly" : "❌ Should return null")}");

        // Test invalid color
        var invalidColor = new Color32(255, 255, 255, 255);
        var invalidId = paradoxDataManager?.GetProvinceIdFromColor(invalidColor);
        Debug.Log($"Invalid Color Test: {(invalidId == null ? "✅ Handled correctly" : "❌ Should return null")}");

        // Test error statistics
        var errorStats = ParadoxDataErrorHandler.GetErrorStatistics();
        Debug.Log($"Error Statistics: {errorStats.TotalErrors} total errors recorded");
    }

    [ContextMenu("Generate Test Report")]
    public void GenerateTestReport()
    {
        var report = $@"
=== ParadoxDataLib Test Report ===
Time: {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}

SYSTEM STATUS:
- ParadoxDataManager: {(paradoxDataManager != null ? "✅ Present" : "❌ Missing")}
- Initialized: {(paradoxDataManager?.IsInitialized == true ? "✅ Yes" : "❌ No")}
- Loaded: {(paradoxDataManager?.IsLoaded == true ? "✅ Yes" : "❌ No")}
- State: {paradoxDataManager?.CurrentState}

DATA STATISTICS:
- Provinces: {paradoxDataManager?.GetAllProvinceDefinitions().Count() ?? 0}
- Memory Usage: {(paradoxDataManager?.MemoryUsageBytes ?? 0) / 1024f / 1024f:F1} MB

RECOMMENDATIONS:
{(paradoxDataManager == null ? "- Add ParadoxDataManager to scene\n" : "")}
{(!paradoxDataManager?.IsLoaded == true ? "- Load data before testing\n" : "")}
- Use the context menu options to run individual tests
- Check console for detailed test results
";

        Debug.Log(report);
    }
}