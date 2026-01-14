using Core;
using Core.Data;
using Unity.Collections;

namespace Core.AI
{
    /// <summary>
    /// AI scheduler - handles tier-based processing and goal evaluation.
    ///
    /// Design:
    /// - Tier-based: Countries assigned tiers based on distance from player
    /// - Interval-based: Each tier has configurable processing interval (hours)
    /// - Goal Evaluation: Pick highest-scoring goal, execute it
    ///
    /// Performance Target:
    /// - <5ms per hourly tick for typical load
    /// - Near AI processed frequently, far AI processed rarely
    ///
    /// Scheduling Strategy:
    /// - Tiers defined by AISchedulingConfig (GAME provides policy)
    /// - Each hour: Process AI whose interval has elapsed
    /// - Example: Tier 0 (neighbors) every hour, Tier 3 (far) every 72 hours
    ///
    /// Hour-of-year tracking:
    /// - 365 days × 24 hours = 8760 hours per year
    /// - lastProcessedHour wraps at HOURS_PER_YEAR
    /// - Handles year wrap correctly
    /// </summary>
    public class AIScheduler
    {

        private AIGoalRegistry goalRegistry;
        private AISchedulingConfig config;

        public AIScheduler(AIGoalRegistry goalRegistry, AISchedulingConfig config)
        {
            this.goalRegistry = goalRegistry;
            this.config = config;
        }

        /// <summary>
        /// Update scheduling config (e.g., when GAME layer provides custom config).
        /// </summary>
        public void SetConfig(AISchedulingConfig newConfig)
        {
            this.config = newConfig;
        }

        /// <summary>
        /// Process AI for the current hour based on tier intervals.
        ///
        /// Called once per game hour from AISystem.ProcessHourlyAI().
        /// </summary>
        public void ProcessHourlyTick(ushort currentHourOfYear, NativeArray<AIState> aiStates, GameState gameState)
        {
            int processedCount = 0;

            for (int i = 0; i < aiStates.Length; i++)
            {
                var state = aiStates[i];

                // Skip inactive AI
                if (!state.IsActive)
                    continue;

                // Check if enough time has passed based on tier interval
                if (!ShouldProcess(state, currentHourOfYear))
                    continue;

                // Process this AI
                ProcessSingleAI(ref state, gameState);

                // Update last processed hour
                state.lastProcessedHour = currentHourOfYear;
                aiStates[i] = state;

                processedCount++;
            }
        }

        /// <summary>
        /// Check if AI should be processed based on tier interval.
        /// Handles year wrap correctly.
        /// </summary>
        private bool ShouldProcess(AIState state, ushort currentHourOfYear)
        {
            ushort interval = config.GetIntervalForTier(state.tier);

            // Calculate hours elapsed (handle year wrap)
            int elapsed;
            if (currentHourOfYear >= state.lastProcessedHour)
            {
                elapsed = currentHourOfYear - state.lastProcessedHour;
            }
            else
            {
                // Year wrapped
                elapsed = (CalendarConstants.HOURS_PER_YEAR - state.lastProcessedHour) + currentHourOfYear;
            }

            return elapsed >= interval;
        }

        /// <summary>
        /// Process a single AI country (evaluate goals, pick best, execute).
        ///
        /// Algorithm:
        /// 1. Evaluate all goals
        /// 2. Pick highest-scoring goal
        /// 3. Execute best goal
        /// 4. Update AIState.activeGoalID
        /// </summary>
        private void ProcessSingleAI(ref AIState state, GameState gameState)
        {
            ushort countryID = state.countryID;

            // Evaluate all goals
            AIGoal bestGoal = null;
            FixedPoint64 bestScore = FixedPoint64.Zero;

            var allGoals = goalRegistry.GetAllGoals();
            for (int i = 0; i < allGoals.Count; i++)
            {
                var goal = allGoals[i];
                var score = goal.Evaluate(countryID, gameState);

                if (score > bestScore)
                {
                    bestScore = score;
                    bestGoal = goal;
                }
            }

            // Execute best goal (if any)
            if (bestGoal != null && bestScore > FixedPoint64.Zero)
            {
                bestGoal.Execute(countryID, gameState);
                state.activeGoalID = bestGoal.GoalID;
            }
            else
            {
                // No valid goals
                state.activeGoalID = 0;
            }
        }

        /// <summary>
        /// Calculate hour-of-year from month, day, and hour.
        /// Uses real month lengths via CalendarConstants.
        /// </summary>
        public static ushort CalculateHourOfYear(int month, int day, int hour)
        {
            // Use DAYS_BEFORE_MONTH for proper month offsets (handles variable month lengths)
            int dayOfYear = CalendarConstants.DAYS_BEFORE_MONTH[month] + (day - 1);
            int totalHours = dayOfYear * CalendarConstants.HOURS_PER_DAY + hour;
            return (ushort)(totalHours % CalendarConstants.HOURS_PER_YEAR);
        }
    }
}
