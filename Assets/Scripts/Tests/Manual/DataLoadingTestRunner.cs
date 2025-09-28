using UnityEngine;
using UnityEngine.UI;
using Core;
using TMPro;

namespace Tests.Manual
{
    /// <summary>
    /// Manual test runner for data loading integration
    /// Provides a simple UI to test the complete loading pipeline
    /// </summary>
    public class DataLoadingTestRunner : MonoBehaviour
    {
        [Header("UI Components")]
        [SerializeField] private Button startButton;
        [SerializeField] private Button resetButton;
        [SerializeField] private Slider progressBar;
        [SerializeField] private TextMeshProUGUI statusText;
        [SerializeField] private TextMeshProUGUI resultsText;
        [SerializeField] private ScrollRect logScrollRect;
        [SerializeField] private TextMeshProUGUI logText;

        [Header("Test Configuration")]
        [SerializeField] private GameSettings testGameSettings;
        [SerializeField] private bool enableDetailedLogging = true;

        private GameInitializer gameInitializer;
        private System.Text.StringBuilder logBuilder;
        private float testStartTime;

        void Start()
        {
            SetupUI();
            InitializeLogging();
            CreateGameInitializer();
        }

        void SetupUI()
        {
            if (startButton != null)
                startButton.onClick.AddListener(StartLoadingTest);

            if (resetButton != null)
                resetButton.onClick.AddListener(ResetTest);

            if (progressBar != null)
                progressBar.value = 0f;

            UpdateStatus("Ready to test data loading integration");
            UpdateResults("Click 'Start Test' to begin");
        }

        void InitializeLogging()
        {
            logBuilder = new System.Text.StringBuilder();
            Application.logMessageReceived += OnLogMessage;
        }

        void CreateGameInitializer()
        {
            // Find existing or create new GameInitializer
            gameInitializer = FindObjectOfType<GameInitializer>();

            if (gameInitializer == null)
            {
                var initializerGO = new GameObject("GameInitializer");
                gameInitializer = initializerGO.AddComponent<GameInitializer>();
            }

            // Assign test settings if available
            if (testGameSettings != null)
            {
                var settingsField = typeof(GameInitializer).GetField("gameSettings",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                settingsField?.SetValue(gameInitializer, testGameSettings);
            }

            // Hook up events
            gameInitializer.OnLoadingProgress += OnLoadingProgress;
            gameInitializer.OnLoadingComplete += OnLoadingComplete;
        }

        public void StartLoadingTest()
        {
            if (gameInitializer == null)
            {
                UpdateStatus("ERROR: GameInitializer not found!");
                return;
            }

            if (gameInitializer.IsLoading)
            {
                UpdateStatus("Loading already in progress...");
                return;
            }

            // Clear previous results
            ClearLog();
            UpdateResults("Test in progress...");
            testStartTime = Time.realtimeSinceStartup;

            // Disable start button
            if (startButton != null)
                startButton.interactable = false;

            // Start the test
            LogMessage("=== Starting Data Loading Integration Test ===");

            if (testGameSettings != null)
            {
                LogMessage($"Using GameSettings: {testGameSettings.name}");
                LogMessage($"Province BMP: {testGameSettings.ProvinceBitmapPath}");
                LogMessage($"Countries Dir: {testGameSettings.CountriesDirectory}");

                // Validate paths
                var validation = testGameSettings.ValidatePaths();
                if (!validation.IsValid)
                {
                    LogMessage($"⚠️  Path validation issues: {validation.GetSummary()}");
                }
                else
                {
                    LogMessage("✅ All paths validated successfully");
                }
            }
            else
            {
                LogMessage("⚠️  No GameSettings assigned - using defaults");
            }

            // Start initialization
            gameInitializer.StartInitialization();
        }

        public void ResetTest()
        {
            // Destroy existing GameState and GameInitializer
            if (GameState.Instance != null)
            {
                DestroyImmediate(GameState.Instance.gameObject);
            }

            if (gameInitializer != null && gameInitializer.gameObject.name == "GameInitializer")
            {
                DestroyImmediate(gameInitializer.gameObject);
            }

            // Create fresh GameInitializer
            CreateGameInitializer();

            // Reset UI
            if (progressBar != null)
                progressBar.value = 0f;

            if (startButton != null)
                startButton.interactable = true;

            UpdateStatus("Reset complete - ready for new test");
            UpdateResults("Click 'Start Test' to begin");
            ClearLog();

            LogMessage("=== Test Environment Reset ===");
        }

        void OnLoadingProgress(GameInitializer.LoadingPhase phase, float progress, string status)
        {
            UpdateStatus($"[{phase}] {status}");

            if (progressBar != null)
                progressBar.value = progress / 100f;

            if (enableDetailedLogging)
            {
                LogMessage($"Progress: {progress:F1}% - {status}");
            }
        }

        void OnLoadingComplete(bool success, string message)
        {
            var totalTime = Time.realtimeSinceStartup - testStartTime;

            if (success)
            {
                UpdateStatus("✅ Loading completed successfully!");
                LogMessage($"=== TEST COMPLETED SUCCESSFULLY in {totalTime:F2}s ===");

                // Display results
                DisplayTestResults();
            }
            else
            {
                UpdateStatus($"❌ Loading failed: {message}");
                LogMessage($"=== TEST FAILED after {totalTime:F2}s ===");
                LogMessage($"Error: {message}");
            }

            // Re-enable start button
            if (startButton != null)
                startButton.interactable = true;
        }

        void DisplayTestResults()
        {
            var results = new System.Text.StringBuilder();
            var gameState = GameState.Instance;

            if (gameState != null)
            {
                results.AppendLine("🎮 GAME STATE ANALYSIS");
                results.AppendLine($"GameState Instance: ✅ Created");
                results.AppendLine($"Is Initialized: {gameState.IsInitialized}");
                results.AppendLine();

                // Province System Results
                results.AppendLine("🗺️ PROVINCE SYSTEM");
                if (gameState.Provinces != null)
                {
                    results.AppendLine($"Province Count: {gameState.Provinces.ProvinceCount}");
                    results.AppendLine($"Capacity: {gameState.Provinces.Capacity}");
                    results.AppendLine($"Initialized: {gameState.Provinces.IsInitialized}");
                }
                else
                {
                    results.AppendLine("❌ ProvinceSystem is null");
                }
                results.AppendLine();

                // Country System Results
                results.AppendLine("🏰 COUNTRY SYSTEM");
                if (gameState.Countries != null)
                {
                    results.AppendLine($"Country Count: {gameState.Countries.CountryCount}");
                    results.AppendLine($"Initialized: {gameState.Countries.IsInitialized}");
                }
                else
                {
                    results.AppendLine("❌ CountrySystem is null");
                }
                results.AppendLine();

                // Query System Results
                results.AppendLine("🔍 QUERY SYSTEMS");
                if (gameState.ProvinceQueries != null)
                {
                    var totalProvinces = gameState.ProvinceQueries.GetTotalProvinceCount();
                    results.AppendLine($"Province Queries: ✅ ({totalProvinces} provinces)");
                }
                else
                {
                    results.AppendLine("❌ ProvinceQueries is null");
                }

                if (gameState.CountryQueries != null)
                {
                    var totalCountries = gameState.CountryQueries.GetTotalCountryCount();
                    results.AppendLine($"Country Queries: ✅ ({totalCountries} countries)");
                }
                else
                {
                    results.AppendLine("❌ CountryQueries is null");
                }
            }
            else
            {
                results.AppendLine("❌ GameState Instance is null!");
            }

            UpdateResults(results.ToString());
        }

        void UpdateStatus(string status)
        {
            if (statusText != null)
                statusText.text = status;

            DominionLogger.Log($"[DataLoadingTest] {status}");
        }

        void UpdateResults(string results)
        {
            if (resultsText != null)
                resultsText.text = results;
        }

        void LogMessage(string message)
        {
            logBuilder.AppendLine($"[{Time.realtimeSinceStartup:F2}s] {message}");

            if (logText != null)
            {
                logText.text = logBuilder.ToString();

                // Auto-scroll to bottom
                if (logScrollRect != null)
                {
                    Canvas.ForceUpdateCanvases();
                    logScrollRect.normalizedPosition = new Vector2(0, 0);
                }
            }
        }

        void ClearLog()
        {
            logBuilder.Clear();
            if (logText != null)
                logText.text = "";
        }

        void OnLogMessage(string logString, string stackTrace, LogType type)
        {
            if (enableDetailedLogging)
            {
                var icon = type switch
                {
                    LogType.Error => "❌",
                    LogType.Warning => "⚠️",
                    LogType.Log => "ℹ️",
                    _ => "📝"
                };

                LogMessage($"{icon} {logString}");
            }
        }

        void OnDestroy()
        {
            Application.logMessageReceived -= OnLogMessage;

            if (gameInitializer != null)
            {
                gameInitializer.OnLoadingProgress -= OnLoadingProgress;
                gameInitializer.OnLoadingComplete -= OnLoadingComplete;
            }
        }

        #if UNITY_EDITOR
        [UnityEditor.MenuItem("Dominion/Create Data Loading Test Scene")]
        public static void CreateTestScene()
        {
            // Create a simple test scene with UI
            var canvas = new GameObject("Canvas").AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var testRunner = new GameObject("DataLoadingTestRunner");
            testRunner.AddComponent<DataLoadingTestRunner>();

            DominionLogger.Log("Created Data Loading Test Scene");
        }
        #endif
    }
}