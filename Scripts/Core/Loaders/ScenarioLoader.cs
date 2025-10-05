using UnityEngine;
using System.Collections.Generic;
using Core.Commands;
using Core.Data;

namespace Core.Loaders
{
    /// <summary>
    /// Loads and applies scenario data for initial game state
    /// Handles start dates like 1444, 1836, or custom scenarios
    /// Applies province ownership, country treasuries, armies, etc.
    /// </summary>
    public class ScenarioLoader
    {
        /// <summary>
        /// Result of scenario loading operation
        /// </summary>
        public struct ScenarioLoadResult
        {
            public bool Success;
            public string ErrorMessage;
            public ScenarioData Data;
            public LoadingStatistics Statistics;

            public static ScenarioLoadResult CreateSuccess(ScenarioData data, LoadingStatistics stats = default)
            {
                return new ScenarioLoadResult
                {
                    Success = true,
                    Data = data,
                    Statistics = stats
                };
            }

            public static ScenarioLoadResult CreateFailure(string error)
            {
                return new ScenarioLoadResult
                {
                    Success = false,
                    ErrorMessage = error
                };
            }
        }

        /// <summary>
        /// Complete scenario data
        /// </summary>
        [System.Serializable]
        public class ScenarioData
        {
            public string Name;
            public string Description;
            public System.DateTime StartDate;
            public List<ProvinceSetup> ProvinceSetups;
            public List<CountrySetup> CountrySetups;
            public List<DiplomaticRelation> DiplomaticRelations;

            public ScenarioData()
            {
                ProvinceSetups = new List<ProvinceSetup>();
                CountrySetups = new List<CountrySetup>();
                DiplomaticRelations = new List<DiplomaticRelation>();
            }
        }

        /// <summary>
        /// Initial setup for a province
        /// </summary>
        [System.Serializable]
        public struct ProvinceSetup
        {
            public ushort ProvinceId;
            public ushort Owner;
            public ushort Controller;
            public byte Development;
            public byte Terrain;
            public bool HasFort;
            public string Religion;
            public string Culture;
        }

        /// <summary>
        /// Initial setup for a country
        /// </summary>
        [System.Serializable]
        public struct CountrySetup
        {
            public ushort CountryId;
            public string Tag;
            public string Name;
            public FixedPoint64 Treasury;  // Changed from float - must be deterministic
            public byte Technology;
            public string Government;
            public string Religion;
            public string PrimaryCulture;
            public ushort Capital;
        }

        /// <summary>
        /// Diplomatic relation between countries
        /// </summary>
        [System.Serializable]
        public struct DiplomaticRelation
        {
            public ushort Country1;
            public ushort Country2;
            public RelationType Type;
            public FixedPoint64 Value;  // Changed from float - must be deterministic
        }

        public enum RelationType
        {
            Alliance,
            War,
            Truce,
            Trade,
            Vassalage,
            PersonalUnion
        }

        /// <summary>
        /// Loading statistics
        /// </summary>
        public struct LoadingStatistics
        {
            public int ProvincesProcessed;
            public int CountriesProcessed;
            public int RelationsProcessed;
            public float LoadingTimeMs;
            public List<string> Warnings;
        }

        /// <summary>
        /// Load scenario from JSON file
        /// </summary>
        public static ScenarioLoadResult LoadFromFile(string filePath)
        {
            var startTime = Time.realtimeSinceStartup;

            try
            {
                if (!System.IO.File.Exists(filePath))
                {
                    return ScenarioLoadResult.CreateFailure($"Scenario file not found: {filePath}");
                }

                var json = System.IO.File.ReadAllText(filePath);
                var data = JsonUtility.FromJson<ScenarioData>(json);

                if (data == null)
                {
                    return ScenarioLoadResult.CreateFailure("Failed to parse scenario JSON");
                }

                var stats = new LoadingStatistics
                {
                    ProvincesProcessed = data.ProvinceSetups?.Count ?? 0,
                    CountriesProcessed = data.CountrySetups?.Count ?? 0,
                    RelationsProcessed = data.DiplomaticRelations?.Count ?? 0,
                    LoadingTimeMs = (Time.realtimeSinceStartup - startTime) * 1000f,
                    Warnings = new List<string>()
                };

                return ScenarioLoadResult.CreateSuccess(data, stats);
            }
            catch (System.Exception e)
            {
                return ScenarioLoadResult.CreateFailure($"Error loading scenario: {e.Message}");
            }
        }

        /// <summary>
        /// Create a default empty scenario
        /// </summary>
        public static ScenarioLoadResult CreateDefaultScenario()
        {
            var data = new ScenarioData
            {
                Name = "Default Empty",
                Description = "Empty scenario with all provinces unowned",
                StartDate = new System.DateTime(1444, 11, 11) // EU4 start date
            };

            var stats = new LoadingStatistics
            {
                LoadingTimeMs = 0f,
                Warnings = new List<string>()
            };

            return ScenarioLoadResult.CreateSuccess(data, stats);
        }

        /// <summary>
        /// Apply scenario data to game state
        /// </summary>
        public static bool ApplyScenario(ScenarioData scenario, GameState gameState)
        {
            if (scenario == null)
            {
                ArchonLogger.LogError("Cannot apply null scenario");
                return false;
            }

            if (gameState == null)
            {
                ArchonLogger.LogError("Cannot apply scenario to null game state");
                return false;
            }

            ArchonLogger.Log($"Applying scenario: {scenario.Name}");

            try
            {
                // Apply province setups
                ApplyProvinceSetups(scenario.ProvinceSetups, gameState);

                // Apply country setups
                ApplyCountrySetups(scenario.CountrySetups, gameState);

                // Apply diplomatic relations
                ApplyDiplomaticRelations(scenario.DiplomaticRelations, gameState);

                ArchonLogger.Log($"Scenario '{scenario.Name}' applied successfully");
                return true;
            }
            catch (System.Exception e)
            {
                ArchonLogger.LogError($"Failed to apply scenario: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Apply province setups using commands
        /// </summary>
        private static void ApplyProvinceSetups(List<ProvinceSetup> setups, GameState gameState)
        {
            if (setups == null) return;

            foreach (var setup in setups)
            {
                // Validate province exists
                if (!gameState.ProvinceQueries.Exists(setup.ProvinceId))
                {
                    ArchonLogger.LogWarning($"Scenario references non-existent province: {setup.ProvinceId}");
                    continue;
                }

                // Set ownership
                if (setup.Owner != 0)
                {
                    var ownerCommand = ProvinceCommandFactory.TransferProvince(setup.ProvinceId, setup.Owner);
                    gameState.TryExecuteCommand(ownerCommand);
                }

                // Set development
                if (setup.Development > 0)
                {
                    var devCommand = ProvinceCommandFactory.DevelopProvince(setup.ProvinceId, setup.Development);
                    gameState.TryExecuteCommand(devCommand);
                }

                // TODO: Set terrain, religion, culture when those systems exist
            }

            ArchonLogger.Log($"Applied {setups.Count} province setups");
        }

        /// <summary>
        /// Apply country setups
        /// </summary>
        private static void ApplyCountrySetups(List<CountrySetup> setups, GameState gameState)
        {
            if (setups == null) return;

            foreach (var setup in setups)
            {
                // Validate country exists
                if (!gameState.CountryQueries.Exists(setup.CountryId))
                {
                    ArchonLogger.LogWarning($"Scenario references non-existent country: {setup.CountryId} ({setup.Tag})");
                    continue;
                }

                // TODO: Set treasury, technology, government when those systems exist
                ArchonLogger.Log($"Country setup for {setup.Tag}: Treasury={setup.Treasury}, Capital={setup.Capital}");
            }

            ArchonLogger.Log($"Applied {setups.Count} country setups");
        }

        /// <summary>
        /// Apply diplomatic relations
        /// </summary>
        private static void ApplyDiplomaticRelations(List<DiplomaticRelation> relations, GameState gameState)
        {
            if (relations == null) return;

            foreach (var relation in relations)
            {
                // TODO: Apply diplomatic relations when diplomacy system exists
                ArchonLogger.Log($"Diplomatic relation: {relation.Country1} {relation.Type} {relation.Country2} ({relation.Value})");
            }

            ArchonLogger.Log($"Applied {relations.Count} diplomatic relations");
        }

        /// <summary>
        /// Validate scenario data
        /// </summary>
        public static List<string> ValidateScenario(ScenarioData scenario, GameState gameState)
        {
            var issues = new List<string>();

            if (scenario == null)
            {
                issues.Add("Scenario data is null");
                return issues;
            }

            // Validate province setups
            if (scenario.ProvinceSetups != null)
            {
                foreach (var setup in scenario.ProvinceSetups)
                {
                    if (!gameState.ProvinceQueries.Exists(setup.ProvinceId))
                    {
                        issues.Add($"Province {setup.ProvinceId} does not exist");
                    }

                    if (setup.Owner != 0 && !gameState.CountryQueries.Exists(setup.Owner))
                    {
                        issues.Add($"Owner country {setup.Owner} does not exist for province {setup.ProvinceId}");
                    }
                }
            }

            // Validate country setups
            if (scenario.CountrySetups != null)
            {
                foreach (var setup in scenario.CountrySetups)
                {
                    if (!gameState.CountryQueries.Exists(setup.CountryId))
                    {
                        issues.Add($"Country {setup.CountryId} ({setup.Tag}) does not exist");
                    }

                    if (setup.Capital != 0 && !gameState.ProvinceQueries.Exists(setup.Capital))
                    {
                        issues.Add($"Capital province {setup.Capital} does not exist for country {setup.Tag}");
                    }
                }
            }

            return issues;
        }

        /// <summary>
        /// Create example scenario for testing
        /// </summary>
        public static ScenarioData CreateExampleScenario()
        {
            var scenario = new ScenarioData
            {
                Name = "Test Scenario",
                Description = "Example scenario for testing",
                StartDate = new System.DateTime(1444, 11, 11)
            };

            // Add some example province setups
            scenario.ProvinceSetups.Add(new ProvinceSetup
            {
                ProvinceId = 1,
                Owner = 1,
                Controller = 1,
                Development = 10,
                Terrain = 1
            });

            scenario.ProvinceSetups.Add(new ProvinceSetup
            {
                ProvinceId = 2,
                Owner = 2,
                Controller = 2,
                Development = 8,
                Terrain = 1
            });

            // Add example country setups
            scenario.CountrySetups.Add(new CountrySetup
            {
                CountryId = 1,
                Tag = "TST",
                Name = "Test Country 1",
                Treasury = FixedPoint64.FromInt(1000),
                Technology = 3,
                Capital = 1
            });

            scenario.CountrySetups.Add(new CountrySetup
            {
                CountryId = 2,
                Tag = "TS2",
                Name = "Test Country 2",
                Treasury = FixedPoint64.FromInt(800),
                Technology = 3,
                Capital = 2
            });

            return scenario;
        }
    }
}