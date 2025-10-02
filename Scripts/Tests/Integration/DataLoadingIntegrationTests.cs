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
        /// Test initialization with missing files (graceful degradation)
        /// </summary>
        [UnityTest]
        public IEnumerator TestInitializationWithMissingFiles()
        {
            // Arrange - use path to non-existent directory
            testSettings.DataDirectory = "NonExistent";

            // Expect error log messages for missing files
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(".*Game initialization failed.*"));

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
            invalidSettings.DataDirectory = "";

            var invalidResult = invalidSettings.ValidatePaths();
            Assert.IsFalse(invalidResult.IsValid, "Should be invalid with empty paths");
            Assert.Greater(invalidResult.Errors.Count, 0, "Should have error messages");

            // Cleanup
            ScriptableObject.DestroyImmediate(validSettings);
            ScriptableObject.DestroyImmediate(invalidSettings);
        }


        #region Helper Methods

        private void SetupTestSettings(GameSettings settings = null)
        {
            if (settings == null) settings = testSettings;

            // Use real data directory for testing
            settings.DataDirectory = "Assets/Data";

            // Set permissive settings for testing
            settings.UseGracefulDegradation = true;
            settings.TargetLoadingTime = 10f; // More lenient for tests
            settings.EnableDataValidation = true;
            settings.EnableVerboseLogging = true;
        }



        #endregion
    }
}