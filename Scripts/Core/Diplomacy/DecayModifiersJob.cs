using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace Core.Diplomacy
{
    /// <summary>
    /// Burst-compiled job for marking decayed opinion modifiers
    ///
    /// ARCHITECTURE: Flat storage with relationship keys
    /// - All modifiers stored in single NativeList<ModifierWithKey>
    /// - Job marks decayed modifiers (parallel READ-ONLY operation)
    /// - Main thread compacts array SEQUENTIALLY (deterministic order preserved)
    ///
    /// DETERMINISM GUARANTEE:
    /// - Job is READ-ONLY (no race conditions, no order dependency)
    /// - Compaction is SEQUENTIAL on main thread
    /// - Insertion order preserved (append-only)
    /// - Result identical across all game clients
    ///
    /// Performance Target: <5ms for 610k modifiers (parallel SIMD processing)
    /// </summary>
    [BurstCompile(CompileSynchronously = true, OptimizeFor = OptimizeFor.Performance)]
    public struct DecayModifiersJob : IJobParallelFor
    {
        /// <summary>
        /// All modifiers with their relationship keys (flat contiguous array)
        /// READ-ONLY for determinism
        /// </summary>
        [ReadOnly]
        public NativeArray<ModifierWithKey> modifiers;

        /// <summary>
        /// Current game tick for decay calculations
        /// </summary>
        [ReadOnly]
        public int currentTick;

        /// <summary>
        /// Output: true if modifier at index i is fully decayed
        /// Each thread writes to unique index (no race conditions)
        /// </summary>
        [WriteOnly]
        public NativeArray<bool> isDecayed;

        /// <summary>
        /// Execute for each modifier in parallel
        /// Pure read operation - deterministic across all clients
        /// </summary>
        public void Execute(int index)
        {
            isDecayed[index] = modifiers[index].modifier.IsFullyDecayed(currentTick);
        }
    }
}


