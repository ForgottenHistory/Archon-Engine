using System.Collections.Generic;
using System.Text;

namespace Core.AI
{
    /// <summary>
    /// ENGINE: Statistics for AI system debugging and profiling.
    /// </summary>
    public class AIStatistics
    {
        // Processing stats
        private int totalProcessed;
        private int totalSkipped;
        private int totalTimeouts;
        private long totalProcessingTimeTicks;

        // Per-goal stats
        private Dictionary<ushort, GoalStats> goalStats;

        // Per-tier stats
        private int[] processedByTier;

        public AIStatistics(int maxTiers = 8)
        {
            goalStats = new Dictionary<ushort, GoalStats>(16);
            processedByTier = new int[maxTiers];
        }

        /// <summary>
        /// Record an AI processing event.
        /// </summary>
        public void RecordProcessing(byte tier, ushort goalID, long elapsedTicks, bool timedOut)
        {
            totalProcessed++;
            totalProcessingTimeTicks += elapsedTicks;

            if (timedOut)
                totalTimeouts++;

            if (tier < processedByTier.Length)
                processedByTier[tier]++;

            if (goalID != 0)
            {
                if (!goalStats.TryGetValue(goalID, out var stats))
                {
                    stats = new GoalStats();
                    goalStats[goalID] = stats;
                }
                stats.ExecutionCount++;
                stats.TotalTicks += elapsedTicks;
            }
        }

        /// <summary>
        /// Record a skipped AI (inactive).
        /// </summary>
        public void RecordSkipped()
        {
            totalSkipped++;
        }

        /// <summary>
        /// Reset all statistics.
        /// </summary>
        public void Reset()
        {
            totalProcessed = 0;
            totalSkipped = 0;
            totalTimeouts = 0;
            totalProcessingTimeTicks = 0;
            goalStats.Clear();
            for (int i = 0; i < processedByTier.Length; i++)
                processedByTier[i] = 0;
        }

        // === Properties ===

        public int TotalProcessed => totalProcessed;
        public int TotalSkipped => totalSkipped;
        public int TotalTimeouts => totalTimeouts;
        public double AverageProcessingTimeMs => totalProcessed > 0
            ? (totalProcessingTimeTicks / (double)System.Diagnostics.Stopwatch.Frequency * 1000.0) / totalProcessed
            : 0;

        public int GetProcessedByTier(int tier) =>
            tier < processedByTier.Length ? processedByTier[tier] : 0;

        /// <summary>
        /// Get statistics summary as string.
        /// </summary>
        public string GetSummary()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"AI Statistics:");
            sb.AppendLine($"  Processed: {totalProcessed}, Skipped: {totalSkipped}, Timeouts: {totalTimeouts}");
            sb.AppendLine($"  Avg Time: {AverageProcessingTimeMs:F3}ms");
            sb.Append("  By Tier: ");
            for (int i = 0; i < processedByTier.Length; i++)
            {
                if (processedByTier[i] > 0)
                    sb.Append($"T{i}={processedByTier[i]} ");
            }
            return sb.ToString();
        }

        private class GoalStats
        {
            public int ExecutionCount;
            public long TotalTicks;
        }
    }

    /// <summary>
    /// Debug info for a single country's AI state.
    /// </summary>
    public struct AIDebugInfo
    {
        public ushort CountryID;
        public byte Tier;
        public bool IsActive;
        public ushort ActiveGoalID;
        public string ActiveGoalName;
        public ushort LastProcessedHour;
        public int HoursSinceProcessed;
        public string[] FailedConstraints;
    }
}
