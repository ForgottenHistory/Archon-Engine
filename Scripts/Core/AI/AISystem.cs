using Core;
using Unity.Collections;
using UnityEngine;

namespace Core.AI
{
    /// <summary>
    /// Main AI system - manages AI state and coordinates AI processing.
    ///
    /// ENGINE LAYER - Provides AI mechanisms (bucketing, scheduling, goal evaluation).
    /// GAME LAYER provides policy (which goals, formulas, thresholds).
    ///
    /// Architecture:
    /// - Flat storage: NativeArray of AIState (all countries, O(1) access)
    /// - Bucketed runtime: Process 1/30th of AI per day (smooth frame times)
    /// - Initialization: Process ALL countries at once (no bucketing)
    ///
    /// Memory:
    /// - AIState: 8 bytes × N countries (e.g., 300 × 8 = 2.4 KB)
    /// - Pre-allocated with Allocator.Persistent (zero gameplay allocations)
    ///
    /// Performance:
    /// - Initialization: Can process all AI at once
    /// - Runtime: ~1/30th of AI per day (amortized cost)
    /// - Target: <5ms per frame for typical bucket size
    ///
    /// Responsibilities:
    /// - Initialize AI for all countries
    /// - Store AIState array
    /// - Coordinate AIScheduler for daily processing
    /// - Expose query API for debugging
    /// </summary>
    public class AISystem
    {
        private GameState gameState;
        private NativeArray<AIState> aiStates;
        private AIGoalRegistry goalRegistry;
        private AIScheduler scheduler;
        private bool isInitialized;

        public AISystem(GameState gameState)
        {
            this.gameState = gameState;
            this.isInitialized = false;
        }

        /// <summary>
        /// Initialize AI system (called from HegemonInitializer).
        ///
        /// Initialization workflow:
        /// 1. Create goal registry
        /// 2. Register goals (GAME layer responsibility via AITickHandler)
        /// 3. Allocate AIState array for all countries
        /// 4. Initialize AI for all non-player countries
        /// 5. Create scheduler
        ///
        /// Note: Goal registration happens externally (GAME layer calls RegisterGoal).
        /// </summary>
        public void Initialize()
        {
            if (isInitialized)
            {
                ArchonLogger.LogCoreAIWarning("AISystem already initialized");
                return;
            }

            // Create goal registry (goals registered externally by GAME layer)
            goalRegistry = new AIGoalRegistry();

            ArchonLogger.LogCoreAI("AISystem initialized (goals pending registration)");
            isInitialized = true;
        }

        /// <summary>
        /// Initialize AI states for all countries (called after goals are registered).
        ///
        /// This is a separate step because:
        /// 1. Goals must be registered first (GAME layer responsibility)
        /// 2. Country count comes from CountrySystem (may not be ready at Initialize())
        /// 3. Allows flexible initialization order
        ///
        /// Performance: Processes ALL countries at once (initialization, not runtime).
        /// </summary>
        public void InitializeCountryAI(int countryCount)
        {
            if (aiStates.IsCreated)
            {
                ArchonLogger.LogCoreAIWarning("AIState array already created");
                return;
            }

            // Allocate AIState array for all countries (Allocator.Persistent = no gameplay allocations)
            aiStates = new NativeArray<AIState>(countryCount, Allocator.Persistent);

            // Initialize AI for ALL countries
            for (ushort i = 0; i < countryCount; i++)
            {
                // Determine bucket assignment (deterministic distribution)
                byte bucket = AIScheduler.GetBucketForCountry(i);

                // Create AI state (default: active for all countries)
                // GAME layer can disable AI for player-controlled countries later
                aiStates[i] = AIState.Create(i, bucket, isActive: true);
            }

            // Create scheduler (needs goal registry)
            scheduler = new AIScheduler(goalRegistry);

            ArchonLogger.LogCoreAI($"Initialized AI for {countryCount} countries ({goalRegistry.GoalCount} goals registered)");
        }

        /// <summary>
        /// Register a goal (called by GAME layer during initialization).
        /// </summary>
        public void RegisterGoal(AIGoal goal)
        {
            if (goalRegistry == null)
            {
                ArchonLogger.LogCoreAIWarning("Cannot register goal before AISystem.Initialize()");
                return;
            }

            goalRegistry.Register(goal);
        }

        /// <summary>
        /// Process AI for the current day's bucket (called once per game day).
        ///
        /// Runtime: Processes only ~1/30th of AI (bucketed for smooth frame times).
        /// </summary>
        public void ProcessDailyAI(int currentDay)
        {
            if (!isInitialized || !aiStates.IsCreated)
            {
                ArchonLogger.LogCoreAIWarning("Cannot process AI before initialization");
                return;
            }

            scheduler.ProcessDailyBucket(currentDay, aiStates, gameState);
        }

        /// <summary>
        /// Set AI active/inactive for a country (e.g., disable for player).
        /// </summary>
        public void SetAIActive(ushort countryID, bool isActive)
        {
            if (countryID >= aiStates.Length)
            {
                ArchonLogger.LogCoreAIWarning($"Invalid country ID: {countryID}");
                return;
            }

            var state = aiStates[countryID];
            state.IsActive = isActive;
            aiStates[countryID] = state;

            ArchonLogger.LogCoreAI($"AI for country {countryID} set to: {(isActive ? "active" : "inactive")}");
        }

        /// <summary>
        /// Get AI state for a country (readonly, for debugging).
        /// </summary>
        public AIState GetAIState(ushort countryID)
        {
            if (countryID >= aiStates.Length)
            {
                ArchonLogger.LogCoreAIWarning($"Invalid country ID: {countryID}");
                return default;
            }

            return aiStates[countryID];
        }

        /// <summary>
        /// Get goal registry (for debugging/testing).
        /// </summary>
        public AIGoalRegistry GetGoalRegistry()
        {
            return goalRegistry;
        }

        /// <summary>
        /// Dispose AI system (cleanup native allocations).
        /// </summary>
        public void Dispose()
        {
            if (aiStates.IsCreated)
            {
                aiStates.Dispose();
            }

            goalRegistry?.Dispose();

            ArchonLogger.LogCoreAI("AISystem disposed");
        }
    }
}
