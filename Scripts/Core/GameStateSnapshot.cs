using Unity.Collections;
using Core.Data;
using Utils;

namespace Core
{
    /// <summary>
    /// ENGINE - Double-buffer pattern for zero-blocking UI reads
    ///
    /// Problem: UI needs province data without blocking simulation
    /// Victoria 3 uses locks → "waiting" bars in profiler
    ///
    /// Solution: Double-buffer (two NativeArrays)
    /// - Simulation writes to buffer A
    /// - UI reads from buffer B
    /// - After tick: swap pointers (O(1), not memcpy!)
    ///
    /// Memory: 2x hot data (~160KB for 10k provinces at 8 bytes)
    /// Performance: O(1) pointer swap (no copying!)
    /// Staleness: Zero (UI reads completed tick)
    ///
    /// Pattern from Paradox analysis:
    /// - Victoria 3 locks = bad (waiting bars)
    /// - Snapshots = good (zero blocking)
    /// - Double-buffer = optimal (no memcpy overhead)
    /// </summary>
    public class GameStateSnapshot
    {
        // Double buffers for ProvinceState
        private NativeArray<ProvinceState> provinceBufferA;
        private NativeArray<ProvinceState> provinceBufferB;
        private int currentWriteBuffer; // 0 or 1

        private bool isInitialized;
        private int capacity;

        /// <summary>
        /// Initialize double buffers
        /// </summary>
        public void Initialize(int provinceCapacity)
        {
            if (isInitialized)
            {
                ArchonLogger.LogCoreSimulationWarning("GameStateSnapshot already initialized");
                return;
            }

            capacity = provinceCapacity;

            // Allocate both buffers
            provinceBufferA = new NativeArray<ProvinceState>(
                provinceCapacity,
                Allocator.Persistent,
                NativeArrayOptions.ClearMemory
            );
            provinceBufferB = new NativeArray<ProvinceState>(
                provinceCapacity,
                Allocator.Persistent,
                NativeArrayOptions.ClearMemory
            );

            currentWriteBuffer = 0; // Start with buffer A for writing

            isInitialized = true;
            ArchonLogger.LogCoreSimulation($"GameStateSnapshot initialized: {provinceCapacity} provinces, {provinceCapacity * 8 * 2} bytes (double-buffered)");
        }

        /// <summary>
        /// Get write buffer (simulation writes here)
        /// </summary>
        public NativeArray<ProvinceState> GetProvinceWriteBuffer()
        {
            ValidateInitialized();
            return currentWriteBuffer == 0 ? provinceBufferA : provinceBufferB;
        }

        /// <summary>
        /// Get read buffer (UI reads here)
        /// Thread-safe: UI can read while simulation writes to other buffer
        /// </summary>
        public NativeArray<ProvinceState> GetProvinceReadBuffer()
        {
            ValidateInitialized();
            return currentWriteBuffer == 0 ? provinceBufferB : provinceBufferA;
        }

        /// <summary>
        /// Swap buffers after simulation tick (O(1) pointer flip)
        ///
        /// Call from TimeManager after tick completes:
        /// - Simulation just finished writing to write buffer
        /// - Flip pointers so UI sees new data
        /// - Simulation starts writing to old read buffer next tick
        ///
        /// Performance: O(1) - just flips an int
        /// No memcpy! No allocations!
        /// </summary>
        public void SwapBuffers()
        {
            ValidateInitialized();
            currentWriteBuffer = 1 - currentWriteBuffer; // 0 → 1 or 1 → 0
        }

        /// <summary>
        /// Synchronize read buffer with write buffer (copy write → read)
        ///
        /// Used after scenario loading to ensure both buffers have the same initial data.
        /// This prevents the first tick from reading from an empty buffer after the first swap.
        ///
        /// Performance: O(n) memcpy - only call during initialization, not during gameplay
        /// </summary>
        public void SyncBuffersAfterLoad()
        {
            ValidateInitialized();

            var writeBuffer = GetProvinceWriteBuffer();
            var readBuffer = GetProvinceReadBuffer();

            // Copy all province states from write buffer to read buffer
            NativeArray<ProvinceState>.Copy(writeBuffer, readBuffer, writeBuffer.Length);

            ArchonLogger.LogCoreSimulation($"GameStateSnapshot: Synced buffers after scenario load ({writeBuffer.Length} provinces copied)");
        }

        /// <summary>
        /// Check if initialized
        /// </summary>
        public bool IsInitialized => isInitialized;

        /// <summary>
        /// Get buffer capacity
        /// </summary>
        public int Capacity => capacity;

        /// <summary>
        /// Get current write buffer index (0 or 1) - for debugging
        /// </summary>
        public int CurrentWriteBufferIndex => currentWriteBuffer;

        /// <summary>
        /// Dispose both buffers
        /// </summary>
        public void Dispose()
        {
            if (!isInitialized)
                return;

            if (provinceBufferA.IsCreated)
                provinceBufferA.Dispose();
            if (provinceBufferB.IsCreated)
                provinceBufferB.Dispose();

            isInitialized = false;
            ArchonLogger.LogCoreSimulation("GameStateSnapshot disposed");
        }

        private void ValidateInitialized()
        {
            if (!isInitialized)
            {
                throw new System.InvalidOperationException("GameStateSnapshot not initialized. Call Initialize() first.");
            }
        }
    }
}
