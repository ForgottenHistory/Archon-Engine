using System.Collections.Generic;
using Core.Data;

namespace Core.AI
{
    /// <summary>
    /// ENGINE: Interface for custom goal selection strategies.
    ///
    /// Default behavior: Pick highest-scoring goal.
    /// Custom implementations can add:
    /// - Weighted random selection (personality variance)
    /// - Goal cooldowns (don't repeat same goal)
    /// - Multi-goal execution (do several things per tick)
    /// - Priority overrides (emergency goals always win)
    ///
    /// GAME layer implements to customize AI behavior.
    /// </summary>
    public interface IGoalSelector
    {
        /// <summary>
        /// Select goal(s) to execute for a country.
        ///
        /// Parameters:
        /// - countryID: The country making the decision
        /// - evaluatedGoals: Goals with their scores (pre-evaluated)
        /// - gameState: Current game state
        /// - aiState: Current AI state for this country
        ///
        /// Returns: The goal to execute (null if none).
        ///
        /// Note: Goals with score <= 0 are already filtered out.
        /// </summary>
        AIGoal SelectGoal(
            ushort countryID,
            IReadOnlyList<GoalEvaluation> evaluatedGoals,
            GameState gameState,
            AIState aiState);
    }

    /// <summary>
    /// Goal with its evaluated score.
    /// </summary>
    public readonly struct GoalEvaluation
    {
        public readonly AIGoal Goal;
        public readonly FixedPoint64 Score;

        public GoalEvaluation(AIGoal goal, FixedPoint64 score)
        {
            Goal = goal;
            Score = score;
        }
    }

    /// <summary>
    /// Default goal selector: Pick highest-scoring goal.
    /// </summary>
    public class HighestScoreSelector : IGoalSelector
    {
        public static readonly HighestScoreSelector Instance = new HighestScoreSelector();

        public AIGoal SelectGoal(
            ushort countryID,
            IReadOnlyList<GoalEvaluation> evaluatedGoals,
            GameState gameState,
            AIState aiState)
        {
            if (evaluatedGoals.Count == 0)
                return null;

            AIGoal bestGoal = null;
            FixedPoint64 bestScore = FixedPoint64.Zero;

            for (int i = 0; i < evaluatedGoals.Count; i++)
            {
                var eval = evaluatedGoals[i];
                if (eval.Score > bestScore)
                {
                    bestScore = eval.Score;
                    bestGoal = eval.Goal;
                }
            }

            return bestGoal;
        }
    }
}
