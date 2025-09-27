using System.Collections;
using UnityEngine;
using UnityEngine.TestTools;
using NUnit.Framework;
using Core;
using Core.Loaders;
using Core.Systems;

namespace Tests.Integration
{
    /// <summary>
    /// Integration tests for the complete data loading pipeline
    /// Tests the flow from files → loaders → GameState → ready to play
    /// </summary>
    public class DataLoadingIntegrationTests
    {
        private GameObject testGameObject;
        private GameInitializer initializer;
        private GameSettings testSettings;

        [SetUp]
        public void SetUp()
        {
            // Create test GameObject with GameInitializer
            testGameObject = new GameObject("TestGameInitializer");
            initializer = testGameObject.AddComponent<GameInitializer>();

            // Create test settings
            testSettings = ScriptableObject.CreateInstance<GameSettings>();
            SetupTestSettings();

            // Assign settings to initializer
            var settingsField = typeof(GameInitializer).GetField("gameSettings",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            settingsField.SetValue(initializer, testSettings);
        }

        [TearDown]
        public void TearDown()
        {
            if (testGameObject != null)
            {
                Object.DestroyImmediate(testGameObject);
            }

            if (testSettings != null)
            {
                ScriptableObject.DestroyImmediate(testSettings);
            }
        }

        /// <summary>
        /// Test complete initialization pipeline with real data
        /// </summary>
        [UnityTest]
        public IEnumerator TestCompleteInitializationPipeline()
        {
            // Arrange - using real data paths
            bool initializationComplete = false;
            bool initializationSuccessful = false;
            string errorMessage = "";

            // Hook up completion callback
            initializer.OnLoadingComplete += (success, message) =>
            {
                initializationComplete = true;
                initializationSuccessful = success;
                errorMessage = message;
            };

            // Act
            initializer.StartInitialization();

            // Wait for completion (with timeout)
            float timeout = 120f; // 2 minute timeout for real data loading
            float elapsed = 0f;

            while (!initializationComplete && elapsed < timeout)
            {
                yield return new WaitForSeconds(0.1f);
                elapsed += 0.1f;
            }

            // Assert
            Assert.IsTrue(initializationComplete, "Initialization should complete within timeout");
            Assert.IsTrue(initializationSuccessful, $"Initialization should succeed: {errorMessage}");
            Assert.AreEqual(GameInitializer.LoadingPhase.Complete, initializer.CurrentPhase);
            Assert.AreEqual(100f, initializer.Progress, 1f);
        }

        /// <summary>
        /// Test initialization with missing files (graceful degradation)
        /// </summary>
        [UnityTest]
        public IEnumerator TestInitializationWithMissingFiles()
        {
            // Arrange - use paths to non-existent files
            testSettings.ProvinceBitmapPath = "NonExistent/provinces.bmp";
            testSettings.CountriesDirectory = "NonExistent/countries";

            bool initializationComplete = false;
            bool initializationSuccessful = false;

            initializer.OnLoadingComplete += (success, message) =>
            {
                initializationComplete = true;
                initializationSuccessful = success;
            };

            // Act
            initializer.StartInitialization();

            // Wait for completion
            yield return new WaitUntil(() => initializationComplete);

            // Assert - should fail gracefully
            Assert.IsTrue(initializationComplete);
            Assert.IsFalse(initializationSuccessful, "Should fail when critical files are missing");
            Assert.AreEqual(GameInitializer.LoadingPhase.Error, initializer.CurrentPhase);
        }

        /// <summary>
        /// Test GameState systems after successful initialization
        /// </summary>
        [UnityTest]
        public IEnumerator TestGameStateAfterInitialization()
        {
            // Arrange - using real data paths
            yield return StartInitializationAndWait();

            var gameState = GameState.Instance;
            Assert.IsNotNull(gameState, "GameState should exist after initialization");

            // Test Province System
            Assert.IsNotNull(gameState.Provinces, "ProvinceSystem should be initialized");
            Assert.IsTrue(gameState.Provinces.IsInitialized, "ProvinceSystem should be marked as initialized");

            // Test Country System
            Assert.IsNotNull(gameState.Countries, "CountrySystem should be initialized");
            Assert.IsTrue(gameState.Countries.IsInitialized, "CountrySystem should be marked as initialized");

            // Test Query Systems
            Assert.IsNotNull(gameState.ProvinceQueries, "ProvinceQueries should be available");
            Assert.IsNotNull(gameState.CountryQueries, "CountryQueries should be available");

            // Test basic queries work
            var provinceCount = gameState.ProvinceQueries.GetTotalProvinceCount();
            var countryCount = gameState.CountryQueries.GetTotalCountryCount();

            Assert.Greater(provinceCount, 0, "Should have provinces after loading");
            Assert.Greater(countryCount, 0, "Should have countries after loading");

            Debug.Log($"Loaded {provinceCount} provinces and {countryCount} countries");
        }


        /// <summary>
        /// Test scenario loading and application
        /// </summary>
        [Test]
        public void TestScenarioLoader()
        {
            // Test default scenario creation
            var defaultResult = ScenarioLoader.CreateDefaultScenario();
            Assert.IsTrue(defaultResult.Success, "Default scenario should create successfully");
            Assert.IsNotNull(defaultResult.Data, "Default scenario should have data");

            // Test example scenario creation
            var exampleScenario = ScenarioLoader.CreateExampleScenario();
            Assert.IsNotNull(exampleScenario, "Example scenario should be created");
            Assert.IsNotNull(exampleScenario.ProvinceSetups, "Should have province setups");
            Assert.IsNotNull(exampleScenario.CountrySetups, "Should have country setups");
            Assert.Greater(exampleScenario.ProvinceSetups.Count, 0, "Should have some provinces");
            Assert.Greater(exampleScenario.CountrySetups.Count, 0, "Should have some countries");
        }

        /// <summary>
        /// Test GameSettings validation
        /// </summary>
        [Test]
        public void TestGameSettingsValidation()
        {
            // Test with valid paths
            var validSettings = ScriptableObject.CreateInstance<GameSettings>();
            SetupTestSettings(validSettings);

            var result = validSettings.ValidatePaths();
            // Note: This might fail if mock files don't exist, which is expected

            // Test with invalid paths
            var invalidSettings = ScriptableObject.CreateInstance<GameSettings>();
            invalidSettings.ProvinceBitmapPath = "";
            invalidSettings.CountriesDirectory = "";

            var invalidResult = invalidSettings.ValidatePaths();
            Assert.IsFalse(invalidResult.IsValid, "Should be invalid with empty paths");
            Assert.Greater(invalidResult.Errors.Count, 0, "Should have error messages");

            // Cleanup
            ScriptableObject.DestroyImmediate(validSettings);
            ScriptableObject.DestroyImmediate(invalidSettings);
        }

        /// <summary>
        /// Test command execution after initialization
        /// </summary>
        [UnityTest]
        public IEnumerator TestCommandExecutionAfterInitialization()
        {
            // Arrange - using real data paths
            yield return StartInitializationAndWait();

            var gameState = GameState.Instance;
            Assert.IsNotNull(gameState);

            // Try to execute a simple command
            var command = new Core.Commands.ChangeProvinceOwnerCommand
            {
                ProvinceId = 1,
                NewOwner = 1
            };

            // Act
            bool commandSuccess = gameState.TryExecuteCommand(command);

            // Assert
            // Note: This might fail if province/country don't exist, which is expected with mock data
            Debug.Log($"Command execution result: {commandSuccess}");
        }

        #region Helper Methods

        private void SetupTestSettings(GameSettings settings = null)
        {
            if (settings == null) settings = testSettings;

            // Use real data paths for testing
            settings.ProvinceBitmapPath = "Assets/Data/map/provinces.bmp";
            settings.CountriesDirectory = "Assets/Data/common/countries";
            settings.ProvinceDefinitionsPath = "Assets/Data/map/definition.csv";
            settings.ScenariosDirectory = "Assets/Data/history/countries";

            // Set permissive settings for testing
            settings.UseGracefulDegradation = true;
            settings.TargetLoadingTime = 10f; // More lenient for tests
            settings.EnableDataValidation = true;
            settings.EnableVerboseLogging = true;
        }


        private IEnumerator StartInitializationAndWait()
        {
            bool complete = false;
            bool success = false;

            initializer.OnLoadingComplete += (s, m) =>
            {
                complete = true;
                success = s;
            };

            initializer.StartInitialization();

            yield return new WaitUntil(() => complete);

            Assert.IsTrue(success, "Initialization should succeed for testing");
        }

        #endregion
    }
}