using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace ParadoxParser.Data
{
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct NativeDynamicArray<T> : IDisposable where T : unmanaged
    {
        private NativeList<T> m_Data;
        private NativeList<ArraySegment> m_Segments;
        private int m_NextArrayId;

        public bool IsCreated => m_Data.IsCreated && m_Segments.IsCreated;

        public NativeDynamicArray(int initialCapacity, Allocator allocator)
        {
            m_Data = new NativeList<T>(initialCapacity, allocator);
            m_Segments = new NativeList<ArraySegment>(initialCapacity / 4, allocator);
            m_NextArrayId = 0;
        }

        public int AddArray(NativeArray<T> array)
        {
            var arrayId = m_NextArrayId++;
            var startIndex = m_Data.Length;

            for (int i = 0; i < array.Length; i++)
            {
                m_Data.Add(array[i]);
            }

            var segment = new ArraySegment
            {
                StartIndex = startIndex,
                Length = array.Length
            };

            m_Segments.Add(segment);
            return arrayId;
        }

        public int AddArray(T[] array)
        {
            var arrayId = m_NextArrayId++;
            var startIndex = m_Data.Length;

            for (int i = 0; i < array.Length; i++)
            {
                m_Data.Add(array[i]);
            }

            var segment = new ArraySegment
            {
                StartIndex = startIndex,
                Length = array.Length
            };

            m_Segments.Add(segment);
            return arrayId;
        }

        public NativeArray<T> GetArray(int arrayId, Allocator allocator)
        {
            if (arrayId < 0 || arrayId >= m_Segments.Length)
            {
                return new NativeArray<T>(0, allocator);
            }

            var segment = m_Segments[arrayId];
            var result = new NativeArray<T>(segment.Length, allocator);

            for (int i = 0; i < segment.Length; i++)
            {
                result[i] = m_Data[segment.StartIndex + i];
            }

            return result;
        }

        public bool TryGetArray(int arrayId, Allocator allocator, out NativeArray<T> result)
        {
            result = default;

            if (arrayId < 0 || arrayId >= m_Segments.Length)
            {
                return false;
            }

            result = GetArray(arrayId, allocator);
            return true;
        }

        public int GetArrayLength(int arrayId)
        {
            if (arrayId < 0 || arrayId >= m_Segments.Length)
            {
                return -1;
            }

            return m_Segments[arrayId].Length;
        }

        public T GetElement(int arrayId, int elementIndex)
        {
            if (arrayId < 0 || arrayId >= m_Segments.Length)
            {
                return default;
            }

            var segment = m_Segments[arrayId];
            if (elementIndex < 0 || elementIndex >= segment.Length)
            {
                return default;
            }

            return m_Data[segment.StartIndex + elementIndex];
        }

        public bool TryGetElement(int arrayId, int elementIndex, out T element)
        {
            element = default;

            if (arrayId < 0 || arrayId >= m_Segments.Length)
            {
                return false;
            }

            var segment = m_Segments[arrayId];
            if (elementIndex < 0 || elementIndex >= segment.Length)
            {
                return false;
            }

            element = m_Data[segment.StartIndex + elementIndex];
            return true;
        }

        public int ArrayCount => m_Segments.Length;
        public int TotalElementCount => m_Data.Length;

        public void Dispose()
        {
            if (m_Data.IsCreated)
                m_Data.Dispose();
            if (m_Segments.IsCreated)
                m_Segments.Dispose();
        }

        public JobHandle Dispose(JobHandle inputDeps)
        {
            var job1 = m_Data.Dispose(inputDeps);
            var job2 = m_Segments.Dispose(job1);
            return job2;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ArraySegment
    {
        public int StartIndex;
        public int Length;

        public ArraySegment(int startIndex, int length)
        {
            StartIndex = startIndex;
            Length = length;
        }

        public bool IsValid => Length >= 0 && StartIndex >= 0;
        public int EndIndex => StartIndex + Length - 1;
    }
}