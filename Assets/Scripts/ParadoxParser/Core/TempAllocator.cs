using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace ParadoxParser.Core
{
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct TempParsingAllocator : IDisposable
    {
        private NativeList<IntPtr> m_TempAllocations;
        private int m_AllocationCount;
        private long m_TotalAllocated;
        private bool m_IsCreated;

        public bool IsCreated => m_IsCreated;
        public int AllocationCount => m_AllocationCount;
        public long TotalAllocated => m_TotalAllocated;

        public TempParsingAllocator(int maxAllocations)
        {
            m_TempAllocations = new NativeList<IntPtr>(maxAllocations, Allocator.Temp);
            m_AllocationCount = 0;
            m_TotalAllocated = 0;
            m_IsCreated = true;
        }

        public NativeArray<T> AllocateTemp<T>(int length) where T : unmanaged
        {
            var array = new NativeArray<T>(length, Allocator.Temp);
            TrackAllocation(array.GetUnsafePtr(), UnsafeUtility.SizeOf<T>() * length);
            return array;
        }

        public NativeList<T> AllocateTempList<T>(int initialCapacity) where T : unmanaged
        {
            var list = new NativeList<T>(initialCapacity, Allocator.Temp);
            TrackAllocation(list.GetUnsafePtr(), UnsafeUtility.SizeOf<T>() * initialCapacity);
            return list;
        }

        public NativeHashMap<TKey, TValue> AllocateTempHashMap<TKey, TValue>(int capacity)
            where TKey : unmanaged, IEquatable<TKey>
            where TValue : unmanaged
        {
            var hashMap = new NativeHashMap<TKey, TValue>(capacity, Allocator.Temp);
            var estimatedSize = (UnsafeUtility.SizeOf<TKey>() + UnsafeUtility.SizeOf<TValue>()) * capacity;
            unsafe
            {
                TrackAllocation(null, estimatedSize); // Approximate tracking for hashmap
            }
            return hashMap;
        }

        public StreamingBuffer AllocateTempBuffer(int capacity)
        {
            var buffer = new StreamingBuffer(capacity, Allocator.Temp);
            unsafe
            {
                TrackAllocation(null, capacity); // Approximate tracking for buffer
            }
            return buffer;
        }

        private void TrackAllocation(void* ptr, int size)
        {
            if (m_AllocationCount < m_TempAllocations.Capacity)
            {
                if (ptr != null)
                {
                    m_TempAllocations.Add((IntPtr)ptr);
                }
                else
                {
                    m_TempAllocations.Add(IntPtr.Zero); // Track size-only allocations
                }
                m_AllocationCount++;
                m_TotalAllocated += size;
            }
        }

        public void ClearAll()
        {
            // Temp allocations are automatically cleared by Unity's allocator
            // Reset our tracking
            m_TempAllocations.Clear();
            m_AllocationCount = 0;
            m_TotalAllocated = 0;
        }

        public TempAllocationStats GetStats()
        {
            return new TempAllocationStats
            {
                AllocationCount = m_AllocationCount,
                TotalBytes = m_TotalAllocated,
                TrackedAllocations = m_TempAllocations.Length
            };
        }

        public void Dispose()
        {
            if (m_IsCreated)
            {
                // Clear tracking (temp allocations are auto-disposed by Unity)
                if (m_TempAllocations.IsCreated)
                    m_TempAllocations.Dispose();

                m_IsCreated = false;
                m_AllocationCount = 0;
                m_TotalAllocated = 0;
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TempAllocationStats
    {
        public int AllocationCount;
        public long TotalBytes;
        public int TrackedAllocations;

        public override string ToString()
        {
            return $"Temp Allocations: {AllocationCount} count, {TotalBytes / 1024:F1}KB total, {TrackedAllocations} tracked";
        }
    }

    public static class TempAllocatorExtensions
    {
        public static void WithTempAllocator<T>(this T job, TempParsingAllocator allocator) where T : struct, IJob
        {
            // Extension for jobs to use temp allocator
            // Implementation would depend on job structure
        }

        public static bool IsTempAllocation(this Allocator allocator)
        {
            return allocator == Allocator.Temp || allocator == Allocator.TempJob;
        }

        public static void EnsureTempAllocator(this Allocator allocator)
        {
            if (!allocator.IsTempAllocation())
            {
                throw new InvalidOperationException("Expected temp allocator for this operation");
            }
        }
    }
}