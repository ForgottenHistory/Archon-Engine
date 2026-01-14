using Core;
using Core.Data;
using Core.Systems;
using Unity.Collections;
using UnityEngine;

namespace Core.AI
{
    /// <summary>
    /// Main AI system - manages AI state and coordinates AI processing.
    ///
    /// ENGINE LAYER - Provides AI mechanisms (tier-based scheduling, goal evaluation).
    /// GAME LAYER provides policy (which goals, formulas, tier configuration).
    ///
    /// Architecture:
    /// - Flat storage: NativeArray of AIState (all countries, O(1) access)
    /// - Tier-based: Countries assigned priority tiers based on distance from player
    /// - Interval-based: Each tier processes at configurable hourly intervals
    ///
    /// Memory:
    /// - AIState: 8 bytes × N countries (e.g., 300 × 8 = 2.4 KB)
    /// - Pre-allocated with Allocator.Persistent (zero gameplay allocations)
    ///
    /// Performance:
    /// - Near AI (tier 0): Processed every hour
    /// - Far AI (tier 3): Processed every 72 hours
    /// - Smooth frame times through interval-based distribution
    ///
    /// Responsibilities:
    /// - Initialize AI for all countries
    /// - Store AIState array
    /// - Calculate distances and assign tiers
    /// - Coordinate AIScheduler for hourly processing
    /// </summary>
    public class AISystem
    {
        private GameState gameState;
        private NativeArray<AIState> aiStates;
        private AIGoalRegistry goalRegistry;
        private AIScheduler scheduler;
        private AIDistanceCalculator distanceCalculator;
        private AISchedulingConfig config;
        private bool isInitialized;
        private ushort playerCountryID;

        public AISystem(GameState gameState)
        {
            this.gameState = gameState;
            this.isInitialized = false;
            this.playerCountryID = ushort.MaxValue;
        }

        /// <summary>
        /// Initialize AI system (called from HegemonInitializer).
        ///
        /// Initialization workflow:
        /// 1. Create goal registry
        /// 2. Create default scheduling config (GAME can override)
        /// 3. Create distance calculator
        ///
        /// Note: Goal registration happens externally (GAME layer calls RegisterGoal).
        /// </summary>
        public void Initialize()
        {
            if (isInitialized)
            {
                ArchonLogger.LogWarning("AISystem already initialized", "core_ai");
                return;
            }

            // Create goal registry (goals registered externally by GAME layer)
            // Pass EventBus so goals can subscribe to events (e.g., ProvinceOwnershipChangedEvent)
            goalRegistry = new AIGoalRegistry(gameState.EventBus);

            // Create default config (GAME layer can override via SetSchedulingConfig)
            config = AISchedulingConfig.CreateDefault();

            // Create distance calculator
            distanceCalculator = new AIDistanceCalculator();

            ArchonLogger.Log("AISystem initialized (goals pending registration)", "core_ai");
            isInitialized = true;
        }

        /// <summary>
        /// Set custom scheduling config (called by GAME layer).
        /// Must be called before InitializeCountryAI.
        /// </summary>
        public void SetSchedulingConfig(AISchedulingConfig customConfig)
        {
            config = customConfig;
            scheduler?.SetConfig(config);
            ArchonLogger.Log($"AI scheduling config updated ({config.TierCount} tiers)", "core_ai");
        }

        /// <summary>
        /// Initialize AI states for all countries (called after goals are registered).
        ///
        /// This is a separate step because:
        /// 1. Goals must be registered first (GAME layer responsibility)
        /// 2. Province/country count may not be ready at Initialize()
        /// 3. Player country must be known for distance calculation
        ///
        /// Performance: Calculates distances for ALL countries (one-time cost).
        /// </summary>
        public void InitializeCountryAI(int countryCount, int provinceCount, ushort playerCountryID, ushort currentHourOfYear = 0)
        {
            if (aiStates.IsCreated)
            {
                ArchonLogger.LogWarning("AIState array already created", "core_ai");
                return;
            }

            this.playerCountryID = playerCountryID;

            // Allocate AIState array for all countries (Allocator.Persistent = no gameplay allocations)
            aiStates = new NativeArray<AIState>(countryCount, Allocator.Persistent);

            // Initialize AI for ALL countries (tier will be set by distance calculator)
            // Stagger lastProcessedHour to prevent all AI processing at once
            // Each country gets a different "last processed" time within the max interval (72h)
            const int MAX_INTERVAL = 72;

            for (ushort i = 0; i < countryCount; i++)
            {
                // Country 0 is "unowned/null" country - never enable AI for it
                bool isActive = (i != 0);

                // Create AI state
                // GAME layer can disable AI for player-controlled countries later
                var state = AIState.Create(i, isActive: isActive);

                // Stagger: pretend each country was last processed at a different time
                // This spreads out when they next become eligible
                // Use modulo to distribute evenly across the max interval
                int stagger = i % MAX_INTERVAL;
                int staggeredHour = currentHourOfYear - stagger;
                if (staggeredHour < 0) staggeredHour += 8640; // Wrap to previous year
                state.lastProcessedHour = (ushort)staggeredHour;

                aiStates[i] = state;
            }

            // Initialize distance calculator
            distanceCalculator.Initialize(provinceCount, countryCount);

            // Calculate distances and assign tiers
            RecalculateDistances();

            // Create scheduler (needs goal registry and config)
            scheduler = new AIScheduler(goalRegistry, config);

            ArchonLogger.Log($"Initialized AI for {countryCount} countries ({goalRegistry.GoalCount} goals, {config.TierCount} tiers)", "core_ai");
        }

        /// <summary>
        /// Recalculate distances from player and reassign tiers.
        /// Call monthly or when major border changes occur.
        /// </summary>
        public void RecalculateDistances()
        {
            if (!isInitialized || !aiStates.IsCreated || playerCountryID == ushort.MaxValue)
            {
                ArchonLogger.LogWarning("Cannot recalculate distances before full initialization", "core_ai");
                return;
            }

            var provinceSystem = gameState.Provinces;
            var adjacencySystem = gameState.Adjacencies;

            if (provinceSystem == null || adjacencySystem == null)
            {
                ArchonLogger.LogWarning("Cannot recalculate distances: missing systems", "core_ai");
                return;
            }

            // Calculate distances via BFS
            distanceCalculator.CalculateDistances(playerCountryID, provinceSystem, adjacencySystem);

            // Assign tiers based on distances
            distanceCalculator.AssignTiers(aiStates, config);
        }

        /// <summary>
        /// Register a goal (called by GAME layer during initialization).
        /// </summary>
        public void RegisterGoal(AIGoal goal)
        {
            if (goalRegistry == null)
            {
                ArchonLogger.LogWarning("Cannot register goal before AISystem.Initialize()", "core_ai");
                return;
            }

            goalRegistry.Register(goal);
        }

        /// <summary>
        /// Process AI for the current hour (called once per game hour).
        ///
        /// Runtime: Processes AI based on tier intervals.
        /// Near AI processed frequently, far AI processed rarely.
        /// </summary>
        public void ProcessHourlyAI(int month, int day, int hour)
        {
            if (!isInitialized || !aiStates.IsCreated)
            {
                ArchonLogger.LogWarning("Cannot process AI before initialization", "core_ai");
                return;
            }

            ushort currentHourOfYear = AIScheduler.CalculateHourOfYear(month, day, hour);
            scheduler.ProcessHourlyTick(currentHourOfYear, aiStates, gameState);
        }

        /// <summary>
        /// Set AI active/inactive for a country (e.g., disable for player).
        /// </summary>
        public void SetAIActive(ushort countryID, bool isActive)
        {
            if (countryID >= aiStates.Length)
            {
                ArchonLogger.LogWarning($"Invalid country ID: {countryID}", "core_ai");
                return;
            }

            var state = aiStates[countryID];
            state.IsActive = isActive;
            aiStates[countryID] = state;

            ArchonLogger.Log($"AI for country {countryID} set to: {(isActive ? "active" : "inactive")}", "core_ai");
        }

        /// <summary>
        /// Alias for SetAIActive (naming consistency).
        /// </summary>
        public void SetAIEnabled(ushort countryID, bool enabled) => SetAIActive(countryID, enabled);

        /// <summary>
        /// Get AI state for a country (readonly, for debugging).
        /// </summary>
        public AIState GetAIState(ushort countryID)
        {
            if (countryID >= aiStates.Length)
            {
                ArchonLogger.LogWarning($"Invalid country ID: {countryID}", "core_ai");
                return default;
            }

            return aiStates[countryID];
        }

        /// <summary>
        /// Get the active goal for a country.
        /// </summary>
        public AIGoal GetActiveGoal(ushort countryID)
        {
            if (countryID >= aiStates.Length)
                return null;

            var state = aiStates[countryID];
            if (state.activeGoalID == 0)
                return null;

            return goalRegistry.GetGoal(state.activeGoalID);
        }

        /// <summary>
        /// Get all countries in a specific tier.
        /// </summary>
        public System.Collections.Generic.List<ushort> GetCountriesByTier(byte tier)
        {
            var result = new System.Collections.Generic.List<ushort>();

            if (!aiStates.IsCreated)
                return result;

            for (int i = 0; i < aiStates.Length; i++)
            {
                if (aiStates[i].tier == tier)
                    result.Add((ushort)i);
            }

            return result;
        }

        /// <summary>
        /// Get count of countries in a specific tier (no allocation).
        /// </summary>
        public int GetCountryCountByTier(byte tier)
        {
            if (!aiStates.IsCreated)
                return 0;

            int count = 0;
            for (int i = 0; i < aiStates.Length; i++)
            {
                if (aiStates[i].tier == tier)
                    count++;
            }

            return count;
        }

        /// <summary>
        /// Get count of active AI countries.
        /// </summary>
        public int GetActiveAICount()
        {
            if (!aiStates.IsCreated)
                return 0;

            int count = 0;
            for (int i = 0; i < aiStates.Length; i++)
            {
                if (aiStates[i].IsActive)
                    count++;
            }

            return count;
        }

        /// <summary>
        /// Set custom goal selector for AI processing.
        /// </summary>
        public void SetGoalSelector(IGoalSelector selector)
        {
            scheduler?.SetGoalSelector(selector);
        }

        /// <summary>
        /// Set execution timeout for goal processing (milliseconds).
        /// </summary>
        public void SetExecutionTimeout(long timeoutMs)
        {
            scheduler?.SetExecutionTimeout(timeoutMs);
        }

        /// <summary>
        /// Get debug info for a country's AI.
        /// </summary>
        public AIDebugInfo GetDebugInfo(ushort countryID, int currentHourOfYear)
        {
            var info = new AIDebugInfo
            {
                CountryID = countryID,
                Tier = 255,
                IsActive = false,
                ActiveGoalID = 0,
                ActiveGoalName = "None",
                LastProcessedHour = 0,
                HoursSinceProcessed = 0,
                FailedConstraints = null
            };

            if (countryID >= aiStates.Length)
                return info;

            var state = aiStates[countryID];
            info.Tier = state.tier;
            info.IsActive = state.IsActive;
            info.ActiveGoalID = state.activeGoalID;
            info.LastProcessedHour = state.lastProcessedHour;

            // Calculate hours since processed
            if (currentHourOfYear >= state.lastProcessedHour)
                info.HoursSinceProcessed = currentHourOfYear - state.lastProcessedHour;
            else
                info.HoursSinceProcessed = (CalendarConstants.HOURS_PER_YEAR - state.lastProcessedHour) + currentHourOfYear;

            // Get goal name
            if (state.activeGoalID != 0)
            {
                var goal = goalRegistry.GetGoal(state.activeGoalID);
                info.ActiveGoalName = goal?.GoalName ?? "Unknown";
            }

            return info;
        }

        /// <summary>
        /// Goal registry for auto-discovery and debugging.
        /// </summary>
        public AIGoalRegistry GoalRegistry => goalRegistry;

        /// <summary>
        /// Get scheduling config (for debugging/testing).
        /// </summary>
        public AISchedulingConfig GetSchedulingConfig()
        {
            return config;
        }

        /// <summary>
        /// Get AI statistics.
        /// </summary>
        public AIStatistics GetStatistics()
        {
            return scheduler?.Statistics;
        }

        /// <summary>
        /// Reset AI statistics.
        /// </summary>
        public void ResetStatistics()
        {
            scheduler?.Statistics?.Reset();
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
            distanceCalculator?.Dispose();

            ArchonLogger.Log("AISystem disposed", "core_ai");
        }
    }
}
