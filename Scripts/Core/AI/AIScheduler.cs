using Core;
using Core.Data;
using Unity.Collections;

namespace Core.AI
{
    /// <summary>
    /// AI scheduler - handles bucketing and goal evaluation.
    ///
    /// Design:
    /// - Bucketing: Spread ALL countries across 30 days (N/30 AI per day)
    /// - Goal Evaluation: Pick highest-scoring goal, execute it
    /// - Stateless: No persistent state (operates on AIState array)
    ///
    /// Performance Target:
    /// - <5ms per frame for typical bucket size
    /// - Simple heuristics over perfect optimization
    ///
    /// Bucketing Strategy:
    /// - 30 buckets (one per day of month)
    /// - Each day: Process all AI in current bucket
    /// - Distribution: country.bucket = countryID % 30 (deterministic, even spread)
    ///
    /// Future Extensions:
    /// - Crisis override (process out-of-bucket for wars, bankruptcy)
    /// - Tactical/Operational layers (weekly/daily processing)
    /// - Priority system (important AI process more frequently)
    /// </summary>
    public class AIScheduler
    {
        private const int BUCKETS_PER_MONTH = 30;

        private AIGoalRegistry goalRegistry;

        public AIScheduler(AIGoalRegistry goalRegistry)
        {
            this.goalRegistry = goalRegistry;
        }

        /// <summary>
        /// Process AI for the current day's bucket.
        ///
        /// Called once per game day from AISystem.ProcessDailyAI().
        /// </summary>
        public void ProcessDailyBucket(int currentDay, NativeArray<AIState> aiStates, GameState gameState)
        {
            // Determine which bucket to process (0-29)
            int bucketToProcess = currentDay % BUCKETS_PER_MONTH;

            // Process all AI in this bucket
            for (int i = 0; i < aiStates.Length; i++)
            {
                var state = aiStates[i];

                // Skip if wrong bucket, inactive, or player-controlled
                if (state.bucket != bucketToProcess || !state.IsActive)
                    continue;

                // Process this AI
                ProcessSingleAI(ref state, gameState);

                // Write back modified state
                aiStates[i] = state;
            }

            ArchonLogger.LogCoreAI($"Processed AI bucket {bucketToProcess} (day {currentDay})");
        }

        /// <summary>
        /// Process a single AI country (evaluate goals, pick best, execute).
        ///
        /// Algorithm:
        /// 1. Evaluate all goals
        /// 2. Pick highest-scoring goal
        /// 3. Execute best goal
        /// 4. Update AIState.activeGoalID
        ///
        /// Design:
        /// - Stateless evaluation (pure function of GameState)
        /// - Simple iteration (no fancy optimization)
        /// - Early exit if no valid goals (score > 0)
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

                ArchonLogger.LogCoreAI($"Country {countryID}: Executing goal '{bestGoal.GoalName}' (score: {bestScore})");
            }
            else
            {
                // No valid goals
                state.activeGoalID = 0;
                ArchonLogger.LogCoreAI($"Country {countryID}: No valid goals");
            }
        }

        /// <summary>
        /// Get bucket assignment for a country (deterministic distribution).
        ///
        /// Uses modulo to distribute countries evenly across 30 buckets.
        /// All countries get AI, bucket determines which day they process.
        /// </summary>
        public static byte GetBucketForCountry(ushort countryID)
        {
            return (byte)(countryID % BUCKETS_PER_MONTH);
        }
    }
}
