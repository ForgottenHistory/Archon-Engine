using System.Collections.Generic;
using System.Diagnostics;
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
    /// - Goal Evaluation: Evaluate goals, apply constraints, use IGoalSelector
    /// - Slow goals: a goal whose evaluation and execution take longer than a threshold is logged and counted
    ///   (AIStatistics.TotalSlowGoals). It always runs to completion: wall-clock time never changes what the AI
    ///   decides, so the same game state gives the same decisions.
    ///
    /// Performance Target:
    /// - &lt;5ms per hourly tick for typical load
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
        private IGoalSelector goalSelector;
        private AIStatistics statistics;

        // Pre-allocated buffer for goal evaluations (avoid allocations)
        private List<GoalEvaluation> evaluationBuffer;

        // Slow-goal threshold (diagnostics only; never cuts a goal short)
        private long slowGoalThresholdTicks;
        private const long DEFAULT_SLOW_GOAL_MS = 50;

        public AIScheduler(AIGoalRegistry goalRegistry, AISchedulingConfig config)
        {
            this.goalRegistry = goalRegistry;
            this.config = config;
            this.goalSelector = HighestScoreSelector.Instance;
            this.statistics = new AIStatistics(AISchedulingConfig.MAX_TIERS);
            this.evaluationBuffer = new List<GoalEvaluation>(16);
            this.slowGoalThresholdTicks = DEFAULT_SLOW_GOAL_MS * Stopwatch.Frequency / 1000;
        }

        /// <summary>
        /// Update scheduling config (e.g., when GAME layer provides custom config).
        /// </summary>
        public void SetConfig(AISchedulingConfig newConfig)
        {
            this.config = newConfig;
        }

        /// <summary>
        /// Set custom goal selector (default: HighestScoreSelector).
        /// </summary>
        public void SetGoalSelector(IGoalSelector selector)
        {
            this.goalSelector = selector ?? HighestScoreSelector.Instance;
        }

        /// <summary>
        /// Threshold (milliseconds) above which a goal's processing is logged and counted as slow.
        /// Diagnostics only: goals always run to completion.
        /// </summary>
        public void SetSlowGoalThreshold(long thresholdMs)
        {
            this.slowGoalThresholdTicks = thresholdMs * Stopwatch.Frequency / 1000;
        }

        /// <summary>
        /// Get statistics for debugging.
        /// </summary>
        public AIStatistics Statistics => statistics;

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
        /// 1. Check constraints for each goal
        /// 2. Evaluate passing goals
        /// 3. Use IGoalSelector to pick goal
        /// 4. Execute (to completion), then log it if it was slow
        /// 5. Update AIState.activeGoalID
        /// </summary>
        private void ProcessSingleAI(ref AIState state, GameState gameState)
        {
            ushort countryID = state.countryID;
            var stopwatch = Stopwatch.StartNew();

            // Clear evaluation buffer
            evaluationBuffer.Clear();

            // Evaluate all goals (with constraint checking)
            var allGoals = goalRegistry.GetAllGoals();
            for (int i = 0; i < allGoals.Count; i++)
            {
                var goal = allGoals[i];

                // Check constraints first (cheap, skips evaluation if failed)
                if (!goal.CheckConstraints(countryID, gameState))
                    continue;

                // Evaluate goal
                var score = goal.Evaluate(countryID, gameState);

                // Only consider positive scores
                if (score > FixedPoint64.Zero)
                {
                    evaluationBuffer.Add(new GoalEvaluation(goal, score));
                }
            }

            // Use goal selector to pick goal
            AIGoal selectedGoal = goalSelector.SelectGoal(countryID, evaluationBuffer, gameState, state);

            // Execute selected goal (always to completion)
            bool slow = false;
            if (selectedGoal != null)
            {
                selectedGoal.Execute(countryID, gameState);
                state.activeGoalID = selectedGoal.GoalID;

                // Diagnostics: a slow goal is reported, never interrupted (decisions must not depend on wall time)
                if (stopwatch.ElapsedTicks > slowGoalThresholdTicks)
                {
                    slow = true;
                    double ms = stopwatch.ElapsedTicks * 1000.0 / Stopwatch.Frequency;
                    ArchonLogger.LogWarning($"AI goal '{selectedGoal.GoalName}' for country {countryID} took {ms:F1}ms " +
                                            $"(slow-goal threshold {slowGoalThresholdTicks * 1000 / Stopwatch.Frequency}ms)", "core_ai");
                }
            }
            else
            {
                // No valid goals
                state.activeGoalID = 0;
            }

            // Record statistics
            statistics.RecordProcessing(state.tier, state.activeGoalID, stopwatch.ElapsedTicks, slow);
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
