using Unity.Jobs;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Core.Jobs
{
    /// <summary>
    /// ENGINE - Generic load balancing for heterogeneous workloads
    ///
    /// Problem: Unity's IJobParallelFor assumes uniform cost per item.
    /// Reality: Some provinces take 10ms, others take 0.1ms (100x difference).
    /// Result: Thread imbalance (thread 0 works, threads 1-7 idle).
    ///
    /// Solution (Victoria 3 pattern): Split into "expensive" and "affordable" batches
    /// - Expensive items: Spread across threads (balanced naturally)
    /// - Affordable items: Separate job (also balanced)
    ///
    /// Usage:
    /// 1. GAME estimates cost per item (building count, pop count, etc.)
    /// 2. Create WorkItem array with indices and costs
    /// 3. Call SplitByThreshold() or ScheduleBalanced()
    /// 4. ENGINE handles thread distribution
    ///
    /// Architecture: MECHANISM (engine) vs POLICY (game)
    /// - Engine: Provides splitting/scheduling infrastructure
    /// - Game: Defines what makes an item "expensive"
    /// </summary>
    public static class LoadBalancedScheduler
    {
        /// <summary>
        /// Work item with cost estimation
        /// Generic - can represent provinces, countries, fleets, anything
        /// </summary>
        public struct WorkItem
        {
            public int index;           // Entity ID (province, country, etc.)
            public int estimatedCost;   // Cost heuristic (provided by caller)
        }

        /// <summary>
        /// Split work items into expensive and affordable batches by threshold
        ///
        /// Pattern: Items above threshold go to "expensive" batch, others to "affordable"
        /// Result: Two balanced batches that can be scheduled separately
        ///
        /// Performance: O(n) split, no sorting (assumes caller pre-sorts if needed)
        /// Memory: Allocates two NativeArrays (caller must dispose)
        /// </summary>
        /// <param name="items">All work items with costs</param>
        /// <param name="costThreshold">Items >= threshold are "expensive"</param>
        /// <param name="allocator">Memory allocator (TempJob, Persistent, etc.)</param>
        /// <returns>Tuple of (expensive indices, affordable indices)</returns>
        public static (NativeArray<int> expensive, NativeArray<int> affordable)
            SplitByThreshold(
                NativeArray<WorkItem> items,
                int costThreshold,
                Allocator allocator)
        {
            // Count expensive items
            int expensiveCount = 0;
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i].estimatedCost >= costThreshold)
                    expensiveCount++;
            }

            int affordableCount = items.Length - expensiveCount;

            // Allocate result arrays
            var expensive = new NativeArray<int>(expensiveCount, allocator);
            var affordable = new NativeArray<int>(affordableCount, allocator);

            // Populate arrays
            int expensiveIndex = 0;
            int affordableIndex = 0;

            for (int i = 0; i < items.Length; i++)
            {
                if (items[i].estimatedCost >= costThreshold)
                {
                    expensive[expensiveIndex++] = items[i].index;
                }
                else
                {
                    affordable[affordableIndex++] = items[i].index;
                }
            }

            return (expensive, affordable);
        }

        /// <summary>
        /// Split work items using percentile-based threshold (adaptive)
        ///
        /// Instead of fixed threshold, use Nth percentile as split point
        /// Example: percentile=0.9 → top 10% are "expensive"
        ///
        /// Benefit: Automatically adapts to data distribution
        /// Cost: Requires sorting (O(n log n))
        /// </summary>
        /// <param name="items">All work items with costs</param>
        /// <param name="percentile">Percentile threshold (0.0-1.0, e.g., 0.9 = top 10%)</param>
        /// <param name="allocator">Memory allocator</param>
        /// <returns>Tuple of (expensive indices, affordable indices)</returns>
        public static (NativeArray<int> expensive, NativeArray<int> affordable)
            SplitByPercentile(
                NativeArray<WorkItem> items,
                float percentile,
                Allocator allocator)
        {
            // Sort by cost (descending) - requires temp copy
            var sortedItems = new NativeArray<WorkItem>(items.Length, Allocator.Temp);
            items.CopyTo(sortedItems);

            // Insertion sort (simple, good for small arrays, Burst-compatible)
            // TODO: Replace with radix sort if profiling shows bottleneck
            for (int i = 1; i < sortedItems.Length; i++)
            {
                var key = sortedItems[i];
                int j = i - 1;
                while (j >= 0 && sortedItems[j].estimatedCost < key.estimatedCost)
                {
                    sortedItems[j + 1] = sortedItems[j];
                    j--;
                }
                sortedItems[j + 1] = key;
            }

            // Find threshold at percentile
            int thresholdIndex = (int)(sortedItems.Length * percentile);
            if (thresholdIndex >= sortedItems.Length)
                thresholdIndex = sortedItems.Length - 1;

            int threshold = sortedItems[thresholdIndex].estimatedCost;
            sortedItems.Dispose();

            // Split by computed threshold
            return SplitByThreshold(items, threshold, allocator);
        }

        /// <summary>
        /// Calculate recommended threshold from work items (helper)
        ///
        /// Heuristic: Use median cost as threshold
        /// Reasoning: Splits work roughly 50/50, natural balance point
        ///
        /// Alternative: Use mean + 1 std dev for "outlier detection"
        /// </summary>
        public static int CalculateMedianThreshold(NativeArray<WorkItem> items)
        {
            if (items.Length == 0)
                return 0;

            // Create temp array for sorting
            var costs = new NativeArray<int>(items.Length, Allocator.Temp);
            for (int i = 0; i < items.Length; i++)
            {
                costs[i] = items[i].estimatedCost;
            }

            // Simple insertion sort (Burst-compatible)
            for (int i = 1; i < costs.Length; i++)
            {
                int key = costs[i];
                int j = i - 1;
                while (j >= 0 && costs[j] > key)
                {
                    costs[j + 1] = costs[j];
                    j--;
                }
                costs[j + 1] = key;
            }

            int median = costs[costs.Length / 2];
            costs.Dispose();
            return median;
        }
    }
}
